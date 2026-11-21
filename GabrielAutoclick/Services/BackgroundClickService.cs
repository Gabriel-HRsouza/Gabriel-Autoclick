using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using GabrielAutoclick.Helpers;
using GabrielAutoclick.Models;

namespace GabrielAutoclick.Services
{
    public enum BackgroundRunState
    {
        Stopped = 0,
        Running = 1,
        Paused = 2
    }

    /// <summary>
    /// Clicks inside a chosen window without using the physical cursor.
    ///
    /// Delivery methods, in order of preference:
    ///  - PostMessage: queues WM_xBUTTONDOWN/UP to the deepest child control under the point.
    ///    Cursor untouched, works with most classic Win32/WinForms/WPF/Chromium apps.
    ///  - SendMessage: same messages, synchronous (with a timeout so a hung app can't freeze us).
    ///  - UI Automation: invokes the accessible element at the point (buttons, menu items, list items).
    ///    Independent of coordinates and mouse state, but only "activates" — no right click.
    ///  - Compatibility cursor: last resort for apps that read the hardware mouse (games, DirectX,
    ///    raw input). Moves the real cursor for ~20 ms and puts it back; requires the point to be visible.
    ///
    /// None of these is universal: apps reading raw input / DirectInput ignore window messages,
    /// and Windows blocks messages to programs running as administrator (UIPI) unless we are elevated too.
    /// </summary>
    public class BackgroundClickService
    {
        private readonly StatisticsService _statisticsService;
        private readonly object _lock = new object();
        private readonly ManualResetEventSlim _resumeSignal = new ManualResetEventSlim(true);
        private CancellationTokenSource? _cts;
        private static readonly uint OwnProcessId = (uint)Environment.ProcessId;

        public BackgroundRunState State { get; private set; } = BackgroundRunState.Stopped;
        public bool IsActive => State != BackgroundRunState.Stopped;
        public long ClicksSent { get; private set; }
        public int CompletedCycles { get; private set; }

        public event Action<BackgroundRunState>? StateChanged;
        public event Action<string>? StatusUpdated;
        /// <summary>Raised when automation stops because of an error (e.g. every target window was closed).</summary>
        public event Action<string>? ErrorOccurred;
        public event Action<long, int>? ProgressUpdated;

        public BackgroundClickService(StatisticsService statisticsService)
        {
            _statisticsService = statisticsService;
        }

        #region Run control

        public void Start(IList<BackgroundClickPoint> points, BackgroundClickSettings settings)
        {
            CancellationTokenSource cts;
            lock (_lock)
            {
                if (IsActive || points.Count == 0) return;
                cts = new CancellationTokenSource();
                _cts = cts;
                _resumeSignal.Set();
                ClicksSent = 0;
                CompletedCycles = 0;
                State = BackgroundRunState.Running;
            }

            var snapshot = points.ToList();
            var cfg = new BackgroundClickSettings
            {
                IntervalMs = Math.Max(10, settings.IntervalMs),
                Cycles = Math.Max(0, settings.Cycles),
                Method = settings.Method,
                SendMouseMove = settings.SendMouseMove
            };

            LoggerService.Log($"[BG] Início: {snapshot.Count} ponto(s), método={cfg.Method}, intervalo={cfg.IntervalMs}ms, ciclos={cfg.Cycles}");
            StateChanged?.Invoke(State);
            StatusUpdated?.Invoke("Clicando em segundo plano");
            SoundService.PlayStartSound();

            var thread = new Thread(() => RunLoop(snapshot, cfg, cts)) { IsBackground = true, Name = "BackgroundClick" };
            thread.Start();
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!IsActive) return;
                _cts?.Cancel();
                _resumeSignal.Set();
            }
            SoundService.PlayStopSound();
        }

        public void TogglePause()
        {
            lock (_lock)
            {
                if (State == BackgroundRunState.Running)
                {
                    State = BackgroundRunState.Paused;
                    _resumeSignal.Reset();
                }
                else if (State == BackgroundRunState.Paused)
                {
                    State = BackgroundRunState.Running;
                    _resumeSignal.Set();
                }
                else return;
            }

            StateChanged?.Invoke(State);
            StatusUpdated?.Invoke(State == BackgroundRunState.Paused ? "Pausado" : "Clicando em segundo plano");
        }

        public void Panic()
        {
            lock (_lock)
            {
                _cts?.Cancel();
                _resumeSignal.Set();
            }
        }

        private void RunLoop(List<BackgroundClickPoint> points, BackgroundClickSettings cfg, CancellationTokenSource cts)
        {
            var ct = cts.Token;
            string? stopError = null;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int okThisCycle = 0;
                    string? lastError = null;

                    for (int i = 0; i < points.Count; i++)
                    {
                        var p = points[i];
                        if (!WaitInterruptible(p.DelayMs, ct)) return;

                        if (!EnsureWindow(p, out string resolveError))
                        {
                            p.Status = resolveError;
                            lastError = $"Ponto {p.Index}: {resolveError}";
                            continue;
                        }

                        if (SendClick(p, cfg.Method, cfg.SendMouseMove, out string error))
                        {
                            okThisCycle++;
                            ClicksSent++;
                            _statisticsService.RegisterClick();
                            if (p.Status != "OK") p.Status = "OK";
                        }
                        else
                        {
                            p.Status = error;
                            lastError = $"Ponto {p.Index}: {error}";
                        }
                    }

                    // A full cycle without a single delivered click means nothing can work anymore
                    if (okThisCycle == 0)
                    {
                        stopError = lastError ?? "Nenhum clique pôde ser entregue.";
                        return;
                    }

                    CompletedCycles++;
                    ProgressUpdated?.Invoke(ClicksSent, CompletedCycles);
                    if (lastError != null) StatusUpdated?.Invoke(lastError);

                    if (cfg.Cycles > 0 && CompletedCycles >= cfg.Cycles) return;
                    if (!WaitInterruptible(cfg.IntervalMs, ct)) return;
                }
            }
            catch (Exception ex)
            {
                LoggerService.LogException("[BG] Erro no loop de clique em segundo plano", ex);
                stopError = "Erro inesperado: " + ex.Message;
            }
            finally
            {
                lock (_lock)
                {
                    State = BackgroundRunState.Stopped;
                    if (ReferenceEquals(_cts, cts)) _cts = null;
                    _resumeSignal.Set();
                }
                cts.Dispose();

                LoggerService.Log($"[BG] Fim. Cliques={ClicksSent}, ciclos={CompletedCycles}, erro={stopError ?? "nenhum"}");
                ProgressUpdated?.Invoke(ClicksSent, CompletedCycles);
                StateChanged?.Invoke(BackgroundRunState.Stopped);

                if (stopError != null)
                {
                    StatusUpdated?.Invoke("Parado: " + stopError);
                    ErrorOccurred?.Invoke(stopError);
                    SoundService.PlayPanicSound();
                }
                else
                {
                    StatusUpdated?.Invoke(cfg.Cycles > 0 && CompletedCycles >= cfg.Cycles ? "Concluído" : "Parado");
                }
            }
        }

        /// <summary>Waits while honoring pause and cancellation. Returns false when cancelled.</summary>
        private bool WaitInterruptible(int ms, CancellationToken ct)
        {
            try
            {
                _resumeSignal.Wait(ct);
                if (ms > 0 && ct.WaitHandle.WaitOne(ms)) return false;
                _resumeSignal.Wait(ct);
                return !ct.IsCancellationRequested;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        #endregion

        #region Target capture & validation

        /// <summary>Builds a click point from a screen coordinate picked by the user.</summary>
        public static BackgroundClickPoint? CaptureAt(int screenX, int screenY, out string error)
        {
            error = string.Empty;
            var pt = new NativeMethods.POINT(screenX, screenY);
            IntPtr hwnd = NativeMethods.WindowFromPoint(pt);
            if (hwnd == IntPtr.Zero)
            {
                error = "Nenhuma janela encontrada nesse ponto.";
                return null;
            }

            IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            if (root == IntPtr.Zero) root = hwnd;

            NativeMethods.GetWindowThreadProcessId(root, out uint pid);
            if (pid == OwnProcessId)
            {
                error = "Esse ponto é do próprio Gabriel Autoclick. Escolha um ponto em outra janela.";
                return null;
            }

            string className = GetClassName(root);
            if (className is "Progman" or "WorkerW" or "Shell_TrayWnd")
            {
                error = "Não é possível usar a área de trabalho ou a barra de tarefas como alvo.";
                return null;
            }

            var client = pt;
            NativeMethods.ScreenToClient(root, ref client);

            return new BackgroundClickPoint
            {
                Hwnd = root,
                WindowTitle = GetWindowTitle(root),
                ProcessName = GetProcessName(pid),
                ClassName = className,
                ClientX = client.X,
                ClientY = client.Y
            };
        }

        /// <summary>
        /// Makes sure the point's window still exists. If it was closed, tries to find a new window
        /// of the same program (same process name and window class), e.g. after the app was reopened.
        /// </summary>
        public static bool EnsureWindow(BackgroundClickPoint p, out string error)
        {
            error = string.Empty;
            if (p.Hwnd != IntPtr.Zero && NativeMethods.IsWindow(p.Hwnd)) return true;

            IntPtr found = FindReplacementWindow(p);
            if (found != IntPtr.Zero)
            {
                p.Hwnd = found;
                p.WindowTitle = GetWindowTitle(found);
                LoggerService.Log($"[BG] Ponto {p.Index} reconectado à janela 0x{found.ToInt64():X} ({p.ProcessName})");
                return true;
            }

            error = "Janela fechada";
            return false;
        }

        private static IntPtr FindReplacementWindow(BackgroundClickPoint p)
        {
            if (string.IsNullOrEmpty(p.ProcessName)) return IntPtr.Zero;

            IntPtr best = IntPtr.Zero;
            NativeMethods.EnumWindows((h, _) =>
            {
                if (!NativeMethods.IsWindowVisible(h)) return true;
                if (!string.IsNullOrEmpty(p.ClassName) && GetClassName(h) != p.ClassName) return true;
                NativeMethods.GetWindowThreadProcessId(h, out uint pid);
                if (pid == OwnProcessId || !string.Equals(GetProcessName(pid), p.ProcessName, StringComparison.OrdinalIgnoreCase)) return true;

                best = h;
                // Prefer an exact title match and stop searching
                return GetWindowTitle(h) != p.WindowTitle;
            }, IntPtr.Zero);

            return best;
        }

        public enum WindowCondition { Normal, Minimized, Closed }

        public static WindowCondition GetCondition(BackgroundClickPoint p)
        {
            if (p.Hwnd == IntPtr.Zero || !NativeMethods.IsWindow(p.Hwnd)) return WindowCondition.Closed;
            return NativeMethods.IsIconic(p.Hwnd) ? WindowCondition.Minimized : WindowCondition.Normal;
        }

        /// <summary>
        /// Returns a warning when the target is a kind of app known to ignore window messages
        /// (game engines / DirectX / raw input), or null when no problem is expected.
        /// </summary>
        public static string? GetCompatibilityWarning(BackgroundClickPoint p)
        {
            string cls = p.ClassName ?? string.Empty;
            string proc = (p.ProcessName ?? string.Empty).ToLowerInvariant();

            bool gameEngine = cls is "UnityWndClass" or "UnrealWindow" or "SDL_app" or "GLFW30" or "WINDOWSCLIENT" or "LWJGL" or "GLUT"
                || cls.StartsWith("SFML", StringComparison.OrdinalIgnoreCase)
                || proc.Contains("roblox") || proc is "javaw" or "minecraft";

            if (gameEngine)
            {
                return "Esse programa parece ser um jogo. Jogos costumam ler o mouse direto do hardware e ignorar cliques " +
                       "enviados em segundo plano. Se não funcionar, use o método \"Modo compatível\".";
            }

            if (cls == "ApplicationFrameWindow")
            {
                return "Apps da Microsoft Store (UWP) costumam ignorar mensagens de mouse. Se não funcionar, tente \"UI Automation\" ou o \"Modo compatível\".";
            }

            return null;
        }

        #endregion

        #region Click delivery

        public static bool SendClick(BackgroundClickPoint p, BackgroundClickMethod method, bool sendMove, out string error)
        {
            error = string.Empty;
            try
            {
                return method switch
                {
                    BackgroundClickMethod.SendMessage => ClickWithMessages(p, sendMove, synchronous: true, out error),
                    BackgroundClickMethod.UIAutomation => ClickWithUIAutomation(p, out error),
                    BackgroundClickMethod.CompatibilityCursor => ClickWithCursor(p, out error),
                    _ => ClickWithMessages(p, sendMove, synchronous: false, out error)
                };
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Finds the deepest visible child control under a client-area point of the root window.
        /// Walks the child list manually instead of ChildWindowFromPointEx, because that API fails
        /// for minimized windows (their client area is reported as empty).
        /// </summary>
        private static (IntPtr hwnd, NativeMethods.POINT pt) ResolveChild(IntPtr root, int clientX, int clientY)
        {
            IntPtr parent = root;
            var pt = new NativeMethods.POINT(clientX, clientY);

            for (int depth = 0; depth < 32; depth++)
            {
                IntPtr found = IntPtr.Zero;
                // GW_CHILD returns children in z-order, topmost first
                for (IntPtr c = NativeMethods.GetWindow(parent, NativeMethods.GW_CHILD); c != IntPtr.Zero;
                     c = NativeMethods.GetWindow(c, NativeMethods.GW_HWNDNEXT))
                {
                    if (!NativeMethods.IsWindowVisible(c)) continue;
                    if (((long)NativeMethods.GetWindowLongPtr(c, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TRANSPARENT) != 0) continue;
                    if (!NativeMethods.GetWindowRect(c, out var r)) continue;

                    var tl = new NativeMethods.POINT(r.Left, r.Top);
                    var br = new NativeMethods.POINT(r.Right, r.Bottom);
                    NativeMethods.MapWindowPoints(IntPtr.Zero, parent, ref tl, 1);
                    NativeMethods.MapWindowPoints(IntPtr.Zero, parent, ref br, 1);

                    if (pt.X >= tl.X && pt.X < br.X && pt.Y >= tl.Y && pt.Y < br.Y)
                    {
                        found = c;
                        pt = new NativeMethods.POINT(pt.X - tl.X, pt.Y - tl.Y); // window origin; adjusted to client below
                        break;
                    }
                }

                if (found == IntPtr.Zero) break;

                // Convert from the child's window origin to its client origin (accounts for borders)
                var clientOrigin = new NativeMethods.POINT(0, 0);
                NativeMethods.MapWindowPoints(found, IntPtr.Zero, ref clientOrigin, 1);
                NativeMethods.GetWindowRect(found, out var wr);
                pt = new NativeMethods.POINT(pt.X - (clientOrigin.X - wr.Left), pt.Y - (clientOrigin.Y - wr.Top));
                parent = found;
            }

            return (parent, pt);
        }

        private static bool ClickWithMessages(BackgroundClickPoint p, bool sendMove, bool synchronous, out string error)
        {
            error = string.Empty;
            var (target, pt) = ResolveChild(p.Hwnd, p.ClientX, p.ClientY);
            IntPtr lParam = (IntPtr)((pt.Y << 16) | (pt.X & 0xFFFF));

            (uint down, uint up, int mk) = p.Kind switch
            {
                BackgroundClickKind.Right => (NativeMethods.WM_RBUTTONDOWN_U, NativeMethods.WM_RBUTTONUP_U, NativeMethods.MK_RBUTTON),
                BackgroundClickKind.Middle => (NativeMethods.WM_MBUTTONDOWN_U, NativeMethods.WM_MBUTTONUP_U, NativeMethods.MK_MBUTTON),
                _ => (NativeMethods.WM_LBUTTONDOWN_U, NativeMethods.WM_LBUTTONUP_U, NativeMethods.MK_LBUTTON)
            };

            bool Deliver(uint msg, int wParam)
            {
                if (synchronous)
                {
                    return NativeMethods.SendMessageTimeout(target, msg, (IntPtr)wParam, lParam,
                        NativeMethods.SMTO_ABORTIFHUNG, 1000, out _) != IntPtr.Zero;
                }
                return NativeMethods.PostMessage(target, msg, (IntPtr)wParam, lParam);
            }

            if (sendMove) Deliver(NativeMethods.WM_MOUSEMOVE_U, 0);

            bool ok = Deliver(down, mk);
            Thread.Sleep(15);
            ok &= Deliver(up, 0);

            if (ok && p.Kind == BackgroundClickKind.DoubleLeft)
            {
                // Windows sends WM_LBUTTONDBLCLK instead of the second DOWN only to classes with CS_DBLCLKS
                bool dblClass = ((long)NativeMethods.GetClassLongPtr(target, NativeMethods.GCL_STYLE) & NativeMethods.CS_DBLCLKS) != 0;
                Thread.Sleep(40);
                ok &= Deliver(dblClass ? NativeMethods.WM_LBUTTONDBLCLK_U : down, mk);
                Thread.Sleep(15);
                ok &= Deliver(up, 0);
            }

            if (!ok)
            {
                int code = Marshal.GetLastWin32Error();
                error = code == 5
                    ? "Acesso negado: o programa alvo roda como administrador. Abra o Gabriel Autoclick como administrador."
                    : synchronous ? "O programa não respondeu à mensagem." : $"Falha ao enviar a mensagem (erro {code}).";
            }
            return ok;
        }

        private static bool ClickWithUIAutomation(BackgroundClickPoint p, out string error)
        {
            error = string.Empty;
            if (p.Kind is BackgroundClickKind.Right or BackgroundClickKind.Middle)
            {
                error = "UI Automation só aciona o controle (equivale ao clique esquerdo).";
                return false;
            }
            if (NativeMethods.IsIconic(p.Hwnd))
            {
                error = "Janela minimizada: UI Automation precisa dela restaurada.";
                return false;
            }

            var screen = new NativeMethods.POINT(p.ClientX, p.ClientY);
            NativeMethods.ClientToScreen(p.Hwnd, ref screen);
            var point = new System.Windows.Point(screen.X, screen.Y);

            // Walk down from the window (not from the screen) so covered windows still work
            AutomationElement element = AutomationElement.FromHandle(p.Hwnd);
            var walker = TreeWalker.ControlViewWalker;
            for (int depth = 0; depth < 40; depth++)
            {
                AutomationElement? next = null;
                double bestArea = double.MaxValue;
                var child = walker.GetFirstChild(element);
                while (child != null)
                {
                    Rect r = child.Current.BoundingRectangle;
                    if (!r.IsEmpty && r.Contains(point) && r.Width * r.Height < bestArea)
                    {
                        bestArea = r.Width * r.Height;
                        next = child;
                    }
                    child = walker.GetNextSibling(child);
                }
                if (next == null) break;
                element = next;
            }

            int times = p.Kind == BackgroundClickKind.DoubleLeft ? 2 : 1;
            for (int i = 0; i < times; i++)
            {
                if (!InvokeElement(element))
                {
                    error = $"O elemento \"{element.Current.Name}\" não aceita acionamento por UI Automation.";
                    return false;
                }
            }
            return true;
        }

        private static bool InvokeElement(AutomationElement e)
        {
            // Climb to the nearest ancestor that can be activated (e.g. text inside a button)
            var walker = TreeWalker.ControlViewWalker;
            for (var cur = e; cur != null; cur = walker.GetParent(cur))
            {
                if (cur.TryGetCurrentPattern(InvokePattern.Pattern, out object inv)) { ((InvokePattern)inv).Invoke(); return true; }
                if (cur.TryGetCurrentPattern(TogglePattern.Pattern, out object tog)) { ((TogglePattern)tog).Toggle(); return true; }
                if (cur.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object sel)) { ((SelectionItemPattern)sel).Select(); return true; }
                if (cur.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out object exp))
                {
                    var ec = (ExpandCollapsePattern)exp;
                    if (ec.Current.ExpandCollapseState == ExpandCollapseState.Collapsed) ec.Expand(); else ec.Collapse();
                    return true;
                }
                if (cur.Current.NativeWindowHandle != 0 && cur != e) break; // don't escape the control's window
            }
            return false;
        }

        /// <summary>
        /// Fallback for apps that ignore window messages. Briefly moves the real cursor, clicks with
        /// SendInput and restores the cursor. Only clicks if the target window is actually visible at the point.
        /// </summary>
        private static bool ClickWithCursor(BackgroundClickPoint p, out string error)
        {
            error = string.Empty;
            if (NativeMethods.IsIconic(p.Hwnd))
            {
                error = "Janela minimizada: o modo compatível precisa dela visível.";
                return false;
            }

            var screen = new NativeMethods.POINT(p.ClientX, p.ClientY);
            NativeMethods.ClientToScreen(p.Hwnd, ref screen);

            IntPtr atPoint = NativeMethods.WindowFromPoint(screen);
            IntPtr rootAtPoint = NativeMethods.GetAncestor(atPoint, NativeMethods.GA_ROOT);
            if (rootAtPoint != p.Hwnd)
            {
                error = "Outra janela está cobrindo o ponto (o modo compatível precisa dele visível).";
                return false;
            }

            (uint down, uint up) = p.Kind switch
            {
                BackgroundClickKind.Right => (NativeMethods.MOUSEEVENTF_RIGHTDOWN, NativeMethods.MOUSEEVENTF_RIGHTUP),
                BackgroundClickKind.Middle => (NativeMethods.MOUSEEVENTF_MIDDLEDOWN, NativeMethods.MOUSEEVENTF_MIDDLEUP),
                _ => (NativeMethods.MOUSEEVENTF_LEFTDOWN, NativeMethods.MOUSEEVENTF_LEFTUP)
            };

            NativeMethods.GetCursorPos(out var original);
            try
            {
                NativeMethods.SetCursorPos(screen.X, screen.Y);
                int count = p.Kind == BackgroundClickKind.DoubleLeft ? 2 : 1;
                for (int i = 0; i < count; i++)
                {
                    SendMouseFlag(down);
                    Thread.Sleep(10);
                    SendMouseFlag(up);
                    if (i + 1 < count) Thread.Sleep(40);
                }
                Thread.Sleep(5);
            }
            finally
            {
                NativeMethods.SetCursorPos(original.X, original.Y);
            }
            return true;
        }

        private static void SendMouseFlag(uint flags)
        {
            var input = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                u = new NativeMethods.INPUTUNION { mi = new NativeMethods.MOUSEINPUT { dwFlags = flags } }
            };
            NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
        }

        #endregion

        #region Helpers

        public static string GetClassName(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string GetWindowTitle(IntPtr hwnd)
        {
            int len = NativeMethods.GetWindowTextLength(hwnd);
            if (len <= 0) return string.Empty;
            var sb = new StringBuilder(len + 1);
            NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string GetProcessName(uint pid)
        {
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                return proc.ProcessName;
            }
            catch
            {
                return "desconhecido";
            }
        }

        #endregion
    }
}
