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
        Paused = 2,
        /// <summary>Completed the configured number of cycles.</summary>
        Finished = 3
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
        private readonly RunController _runner = new RunController("Background");
        private static readonly uint OwnProcessId = (uint)Environment.ProcessId;

        /// <summary>How long every target window may stay closed before the run gives up.</summary>
        private const int ClosedWindowGraceMs = 15000;

        public BackgroundRunState State => _runner.State switch
        {
            RunState.Running => BackgroundRunState.Running,
            RunState.Paused => BackgroundRunState.Paused,
            RunState.Finished => BackgroundRunState.Finished,
            _ => BackgroundRunState.Stopped
        };

        public bool IsActive => _runner.IsActive;
        public long ClicksSent { get; private set; }
        public int CompletedCycles { get; private set; }

        public event Action<BackgroundRunState>? StateChanged;
        public event Action<string>? StatusUpdated;
        /// <summary>Raised when automation stops because of a real, unrecoverable failure.</summary>
        public event Action<string>? ErrorOccurred;
        public event Action<long, int>? ProgressUpdated;

        public BackgroundClickService(StatisticsService statisticsService)
        {
            _statisticsService = statisticsService;
            _runner.StateChanged += _ => StateChanged?.Invoke(State);
            _runner.Failed += msg =>
            {
                StatusUpdated?.Invoke("Parado: " + msg);
                ErrorOccurred?.Invoke(msg);
                SoundService.PlayPanicSound();
            };
        }

        #region Run control

        public void Start(IList<BackgroundClickPoint> points, BackgroundClickSettings settings)
        {
            if (_runner.IsActive || points.Count == 0) return;

            var snapshot = points.ToList();
            var cfg = new BackgroundClickSettings
            {
                IntervalMs = Math.Max(10, settings.IntervalMs),
                Cycles = Math.Max(0, settings.Cycles),
                Method = settings.Method,
                SendMouseMove = settings.SendMouseMove
            };

            ClicksSent = 0;
            CompletedCycles = 0;
            ProgressUpdated?.Invoke(0, 0);

            if (_runner.TryStart(ctx => RunLoop(ctx, snapshot, cfg)))
            {
                LoggerService.Log($"[BG] Início: {snapshot.Count} ponto(s), método={cfg.Method}, intervalo={cfg.IntervalMs}ms, ciclos={cfg.Cycles}");
                StatusUpdated?.Invoke("Clicando em segundo plano");
                SoundService.PlayStartSound();
            }
        }

        public void Stop()
        {
            if (!_runner.IsActive) return;
            _runner.Stop();
            StatusUpdated?.Invoke("Parado");
            SoundService.PlayStopSound();
        }

        public void TogglePause()
        {
            if (_runner.State == RunState.Paused)
            {
                if (_runner.Resume()) StatusUpdated?.Invoke("Clicando em segundo plano");
            }
            else if (_runner.Pause())
            {
                StatusUpdated?.Invoke("Pausado");
            }
        }

        public void Panic() => _runner.Stop();

        /// <summary>
        /// Failure policy:
        ///  - temporary problems (window covered, minimized, busy, element not found) never stop the
        ///    run: the point is marked and retried on the next cycle;
        ///  - a closed window is searched again every cycle (the program may be reopened); the run
        ///    only stops if every target stays closed for ClosedWindowGraceMs;
        ///  - permanent errors (target runs as administrator, method can't do that click type) stop
        ///    the run with an explanation, since retrying can never succeed.
        /// </summary>
        private void RunLoop(RunContext ctx, List<BackgroundClickPoint> points, BackgroundClickSettings cfg)
        {
            var allClosedSince = new Stopwatch();
            string? lastNotice = null;

            void Notice(string msg)
            {
                if (msg == lastNotice) return;
                lastNotice = msg;
                StatusUpdated?.Invoke(msg);
            }

            while (ctx.WaitIfPaused())
            {
                int delivered = 0, closed = 0;

                for (int i = 0; i < points.Count; i++)
                {
                    var p = points[i];
                    if (!ctx.Sleep(p.DelayMs)) return;

                    if (!EnsureWindow(p, out _))
                    {
                        closed++;
                        p.Status = "Janela fechada - aguardando reabrir";
                        continue;
                    }

                    if (SendClick(p, cfg.Method, cfg.SendMouseMove, out string error, out bool permanent))
                    {
                        delivered++;
                        ClicksSent++;
                        _statisticsService.RegisterClick();
                        string status = IsPointCovered(p)
                            ? "Enviado, mas o ponto está coberto por outra janela (alguns programas ignoram)"
                            : "OK";
                        if (p.Status != status) p.Status = status;
                    }
                    else if (permanent)
                    {
                        p.Status = error;
                        ctx.Fail($"Ponto {p.Index}: {error}");
                        return;
                    }
                    else
                    {
                        p.Status = error + " (tentando de novo)";
                        Notice($"Ponto {p.Index}: {error} Continuando a tentar.");
                    }
                }

                if (closed == points.Count)
                {
                    if (!allClosedSince.IsRunning)
                    {
                        allClosedSince.Restart();
                        Notice("A janela alvo foi fechada. Aguardando ela ser aberta novamente...");
                    }
                    else if (allClosedSince.ElapsedMilliseconds >= ClosedWindowGraceMs)
                    {
                        ctx.Fail("A janela alvo foi fechada e não foi reaberta.");
                        return;
                    }
                }
                else
                {
                    if (allClosedSince.IsRunning)
                    {
                        allClosedSince.Reset();
                        Notice("Janela encontrada novamente. Clicando em segundo plano");
                    }
                    else if (delivered == points.Count)
                    {
                        Notice("Clicando em segundo plano");
                    }
                }

                CompletedCycles++;
                ProgressUpdated?.Invoke(ClicksSent, CompletedCycles);

                if (cfg.Cycles > 0 && CompletedCycles >= cfg.Cycles)
                {
                    StatusUpdated?.Invoke($"Concluído: {CompletedCycles} ciclo(s)");
                    SoundService.PlayStopSound();
                    return;
                }

                if (!ctx.Sleep(cfg.IntervalMs)) return;
            }
        }

        #endregion

        #region Target capture & validation

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
        private const int DWMWA_CLOAKED = 14;

        /// <summary>
        /// Builds a click point from a screen coordinate picked by the user.
        /// Gabriel Autoclick's own windows (main window, compact mode, picker overlay, highlight)
        /// are never chosen: the topmost window of another program under the point is used instead.
        /// </summary>
        public static BackgroundClickPoint? CaptureAt(int screenX, int screenY, out string error)
        {
            error = string.Empty;
            var pt = new NativeMethods.POINT(screenX, screenY);

            IntPtr root = IntPtr.Zero;
            IntPtr hwnd = NativeMethods.WindowFromPoint(pt);
            if (hwnd != IntPtr.Zero)
            {
                root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
                if (root == IntPtr.Zero) root = hwnd;
                NativeMethods.GetWindowThreadProcessId(root, out uint hitPid);
                if (hitPid == OwnProcessId) root = IntPtr.Zero;
            }

            // Fallback: walk the top-level windows in z-order, skipping our own
            if (root == IntPtr.Zero) root = FindExternalWindowAt(pt);

            if (root == IntPtr.Zero)
            {
                error = "Nenhum programa encontrado nesse ponto. Clique sobre a janela do programa que deve receber os cliques.";
                return null;
            }

            NativeMethods.GetWindowThreadProcessId(root, out uint pid);
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
        /// True when another window is on top of the point. Messages are still delivered, but some
        /// frameworks (e.g. WinForms buttons) only accept a click when the point is visible on screen.
        /// </summary>
        public static bool IsPointCovered(BackgroundClickPoint p)
        {
            if (NativeMethods.IsIconic(p.Hwnd)) return false;
            var screen = new NativeMethods.POINT(p.ClientX, p.ClientY);
            NativeMethods.ClientToScreen(p.Hwnd, ref screen);
            IntPtr at = NativeMethods.WindowFromPoint(screen);
            if (at == IntPtr.Zero) return false;
            return NativeMethods.GetAncestor(at, NativeMethods.GA_ROOT) != p.Hwnd;
        }

        /// <summary>Topmost visible window of another process that contains the screen point.</summary>
        private static IntPtr FindExternalWindowAt(NativeMethods.POINT pt)
        {
            IntPtr result = IntPtr.Zero;
            NativeMethods.EnumWindows((h, _) =>
            {
                if (!NativeMethods.IsWindowVisible(h) || NativeMethods.IsIconic(h)) return true;
                if (((long)NativeMethods.GetWindowLongPtr(h, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TRANSPARENT) != 0) return true;
                if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
                if (!NativeMethods.GetWindowRect(h, out var r)) return true;
                if (pt.X < r.Left || pt.X >= r.Right || pt.Y < r.Top || pt.Y >= r.Bottom) return true;

                NativeMethods.GetWindowThreadProcessId(h, out uint pid);
                if (pid == OwnProcessId) return true;

                result = h; // EnumWindows walks from the top of the z-order
                return false;
            }, IntPtr.Zero);
            return result;
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
            => SendClick(p, method, sendMove, out error, out _);

        /// <param name="permanent">True when retrying can never succeed (configuration or permission problem).</param>
        public static bool SendClick(BackgroundClickPoint p, BackgroundClickMethod method, bool sendMove, out string error, out bool permanent)
        {
            error = string.Empty;
            permanent = false;

            if (method == BackgroundClickMethod.UIAutomation && p.Kind is BackgroundClickKind.Right or BackgroundClickKind.Middle)
            {
                error = "UI Automation só aciona o controle (equivale ao clique esquerdo). Troque o método ou o tipo de clique.";
                permanent = true;
                return false;
            }

            try
            {
                bool ok = method switch
                {
                    BackgroundClickMethod.SendMessage => ClickWithMessages(p, sendMove, synchronous: true, out error),
                    BackgroundClickMethod.UIAutomation => ClickWithUIAutomation(p, out error),
                    BackgroundClickMethod.CompatibilityCursor => ClickWithCursor(p, out error),
                    _ => ClickWithMessages(p, sendMove, synchronous: false, out error)
                };
                if (!ok && error.StartsWith("Acesso negado")) permanent = true;
                return ok;
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
                u = new NativeMethods.INPUTUNION { mi = new NativeMethods.MOUSEINPUT { dwFlags = flags, dwExtraInfo = SelfClickGuard.Signature } }
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
