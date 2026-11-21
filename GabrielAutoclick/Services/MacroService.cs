using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GabrielAutoclick.Helpers;
using GabrielAutoclick.Models;

namespace GabrielAutoclick.Services
{
    /// <summary>
    /// Records mouse (buttons, movement, wheel) and keyboard input through low-level hooks
    /// and replays it with SendInput (absolute mouse moves + hardware scan codes, which
    /// games such as Roblox/Minecraft accept).
    /// </summary>
    public class MacroService : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private const int MoveSampleIntervalMs = 15;
        private const int MaxFirstDelayMs = 400;

        private readonly MouseInputService _mouseInput;
        private readonly SafeZoneService _safeZoneService;
        private readonly WindowRestrictionService _windowRestrictionService;
        private readonly StatisticsService _statisticsService;

        // Recording state
        private IntPtr _mouseHookId = IntPtr.Zero;
        private IntPtr _keyboardHookId = IntPtr.Zero;
        private NativeMethods.HookProc? _mouseHookProc;
        private NativeMethods.HookProc? _keyboardHookProc;
        private Stopwatch? _recordStopwatch;
        private long _lastActionTimeMs;
        private long _lastMoveTimeMs;
        private int _lastMoveX = int.MinValue, _lastMoveY = int.MinValue;
        private readonly HashSet<int> _keysDown = new HashSet<int>();
        private readonly HashSet<MouseButtonType> _ignoredButtons = new HashSet<MouseButtonType>();
        private readonly uint _ownProcessId = (uint)Environment.ProcessId;

        // Playback state
        private readonly object _lock = new object();

        public List<MacroAction> RecordedActions { get; set; } = new List<MacroAction>();
        public bool IsRecording { get; private set; }
        public bool IsPlaying { get; private set; }
        public double PlaybackSpeed { get; set; } = 1.0;
        public int LoopCount { get; set; } = 1; // 0 = infinite

        public bool RecordMouseMovement { get; set; } = true;
        public bool RecordKeyboard { get; set; } = true;

        /// <summary>Returns true for virtual-key codes that are app hotkeys and must not be recorded.</summary>
        public Func<int, bool>? IsReservedKey { get; set; }

        public event Action<bool>? RecordingStateChanged;
        public event Action<bool>? PlaybackStateChanged;
        public event Action<MacroAction>? ActionRecorded;
        public event Action<int, int>? PlaybackProgress;
        public event Action<int>? PlaybackCycleCompleted;
        public event Action<string>? StatusUpdated;

        public MacroService(
            MouseInputService mouseInput,
            SafeZoneService safeZoneService,
            WindowRestrictionService windowRestrictionService,
            StatisticsService statisticsService)
        {
            _mouseInput = mouseInput;
            _safeZoneService = safeZoneService;
            _windowRestrictionService = windowRestrictionService;
            _statisticsService = statisticsService;
            _playRunner.StateChanged += OnPlayRunnerStateChanged;
        }

        #region Recording

        /// <summary>Must be called from the UI thread (hooks need its message loop).</summary>
        public void StartRecording()
        {
            lock (_lock)
            {
                if (IsRecording || IsPlaying) return;

                RecordedActions = new List<MacroAction>();
                _keysDown.Clear();
                _ignoredButtons.Clear();
                _lastActionTimeMs = 0;
                _lastMoveTimeMs = 0;
                _lastMoveX = _lastMoveY = int.MinValue;
                _recordStopwatch = Stopwatch.StartNew();
                IsRecording = true;

                StartHooks();
            }

            LoggerService.Log("[MACRO] Gravação iniciada");
            RecordingStateChanged?.Invoke(true);
            StatusUpdated?.Invoke("Gravando");
            SoundService.PlayStartSound();
        }

        public void StopRecording()
        {
            lock (_lock)
            {
                if (!IsRecording) return;

                StopHooks();
                _recordStopwatch?.Stop();
                IsRecording = false;

                // Drop trailing cursor movement (usually the trip back to the "Stop" button)
                while (RecordedActions.Count > 0 && RecordedActions[^1].EventType == MacroEventType.MouseMove)
                {
                    RecordedActions.RemoveAt(RecordedActions.Count - 1);
                }

                // Close any key still held so playback never leaves a key stuck
                foreach (int vk in _keysDown.ToList())
                {
                    RecordedActions.Add(new MacroAction
                    {
                        EventType = MacroEventType.KeyUp,
                        VkCode = vk,
                        ScanCode = (int)NativeMethods.MapVirtualKey((uint)vk, NativeMethods.MAPVK_VK_TO_VSC),
                        DelayMs = 0
                    });
                }
                _keysDown.Clear();

                Renumber(RecordedActions);
            }

            LoggerService.Log($"[MACRO] Gravação finalizada com {RecordedActions.Count} ações");
            RecordingStateChanged?.Invoke(false);
            StatusUpdated?.Invoke("Parado");
            SoundService.PlayStopSound();
        }

        public static void Renumber(IList<MacroAction> actions)
        {
            for (int i = 0; i < actions.Count; i++) actions[i].SequenceNumber = i + 1;
        }

        private void StartHooks()
        {
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            IntPtr hMod = NativeMethods.GetModuleHandle(curModule?.ModuleName);

            if (_mouseHookId == IntPtr.Zero)
            {
                _mouseHookProc = MouseHookCallback;
                _mouseHookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseHookProc, hMod, 0);
            }

            if (_keyboardHookId == IntPtr.Zero)
            {
                _keyboardHookProc = KeyboardHookCallback;
                _keyboardHookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardHookProc, hMod, 0);
            }

            LoggerService.Log($"[MACRO] Hooks instalados (mouse={_mouseHookId}, teclado={_keyboardHookId})");
        }

        private void StopHooks()
        {
            if (_mouseHookId != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_mouseHookId);
                _mouseHookId = IntPtr.Zero;
            }
            if (_keyboardHookId != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_keyboardHookId);
                _keyboardHookId = IntPtr.Zero;
            }
        }

        private bool IsPointOverOwnWindow(NativeMethods.POINT pt)
        {
            IntPtr hwnd = NativeMethods.WindowFromPoint(pt);
            if (hwnd == IntPtr.Zero) return false;
            IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            NativeMethods.GetWindowThreadProcessId(root != IntPtr.Zero ? root : hwnd, out uint pid);
            return pid == _ownProcessId;
        }

        private bool IsOwnWindowForeground()
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            NativeMethods.GetWindowThreadProcessId(fg, out uint pid);
            return pid == _ownProcessId;
        }

        private void AddRecordedAction(MacroAction action, long nowMs)
        {
            int delay = (int)(nowMs - _lastActionTimeMs);
            if (RecordedActions.Count == 0) delay = Math.Min(delay, MaxFirstDelayMs);
            _lastActionTimeMs = nowMs;

            action.DelayMs = Math.Max(0, delay);
            action.TimestampMs = nowMs;
            action.SequenceNumber = RecordedActions.Count + 1;

            RecordedActions.Add(action);
            ActionRecorded?.Invoke(action);
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && IsRecording && _recordStopwatch != null)
            {
                try
                {
                    var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                    if ((data.flags & NativeMethods.LLMHF_INJECTED) == 0)
                    {
                        HandleMouseEvent((int)wParam, data);
                    }
                }
                catch (Exception ex)
                {
                    LoggerService.LogException("[MACRO] Erro no hook de mouse", ex);
                }
            }

            return NativeMethods.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        private void HandleMouseEvent(int msg, NativeMethods.MSLLHOOKSTRUCT data)
        {
            long now = _recordStopwatch!.ElapsedMilliseconds;
            int x = data.pt.X, y = data.pt.Y;

            switch (msg)
            {
                case NativeMethods.WM_MOUSEMOVE:
                    if (!RecordMouseMovement) return;
                    if (now - _lastMoveTimeMs < MoveSampleIntervalMs) return;
                    if (x == _lastMoveX && y == _lastMoveY) return;
                    _lastMoveTimeMs = now;
                    _lastMoveX = x;
                    _lastMoveY = y;
                    AddRecordedAction(new MacroAction { EventType = MacroEventType.MouseMove, X = x, Y = y }, now);
                    return;

                case NativeMethods.WM_MOUSEWHEEL:
                case NativeMethods.WM_MOUSEHWHEEL:
                    if (IsPointOverOwnWindow(data.pt)) return;
                    AddRecordedAction(new MacroAction
                    {
                        EventType = MacroEventType.Wheel,
                        X = x,
                        Y = y,
                        WheelDelta = (short)((data.mouseData >> 16) & 0xFFFF),
                        IsHorizontalWheel = msg == NativeMethods.WM_MOUSEHWHEEL
                    }, now);
                    return;
            }

            MacroEventType? type = null;
            MouseButtonType button = MouseButtonType.Left;
            switch (msg)
            {
                case NativeMethods.WM_LBUTTONDOWN: type = MacroEventType.MouseDown; button = MouseButtonType.Left; break;
                case NativeMethods.WM_LBUTTONUP: type = MacroEventType.MouseUp; button = MouseButtonType.Left; break;
                case NativeMethods.WM_RBUTTONDOWN: type = MacroEventType.MouseDown; button = MouseButtonType.Right; break;
                case NativeMethods.WM_RBUTTONUP: type = MacroEventType.MouseUp; button = MouseButtonType.Right; break;
                case NativeMethods.WM_MBUTTONDOWN: type = MacroEventType.MouseDown; button = MouseButtonType.Middle; break;
                case NativeMethods.WM_MBUTTONUP: type = MacroEventType.MouseUp; button = MouseButtonType.Middle; break;
            }
            if (type == null) return;

            if (type == MacroEventType.MouseDown)
            {
                // Clicks on Gabriel Autoclick itself (e.g. the Stop button) are not part of the macro
                if (IsPointOverOwnWindow(data.pt))
                {
                    _ignoredButtons.Add(button);
                    return;
                }
            }
            else if (_ignoredButtons.Remove(button))
            {
                return;
            }

            AddRecordedAction(new MacroAction { EventType = type.Value, MouseButton = button, X = x, Y = y }, now);
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && IsRecording && RecordKeyboard && _recordStopwatch != null)
            {
                try
                {
                    var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                    if ((data.flags & NativeMethods.LLKHF_INJECTED) == 0)
                    {
                        HandleKeyEvent((int)wParam, data);
                    }
                }
                catch (Exception ex)
                {
                    LoggerService.LogException("[MACRO] Erro no hook de teclado", ex);
                }
            }

            return NativeMethods.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        private void HandleKeyEvent(int msg, NativeMethods.KBDLLHOOKSTRUCT data)
        {
            int vk = (int)data.vkCode;
            bool isDown = msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN;
            bool isUp = msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP;
            if (!isDown && !isUp) return;

            if (IsReservedKey?.Invoke(vk) == true) return;

            if (isDown)
            {
                if (_keysDown.Contains(vk)) return;          // Windows auto-repeat
                if (IsOwnWindowForeground()) return;          // typing inside this app
                _keysDown.Add(vk);
            }
            else
            {
                if (!_keysDown.Remove(vk)) return;            // key was pressed before recording / ignored
            }

            AddRecordedAction(new MacroAction
            {
                EventType = isDown ? MacroEventType.KeyDown : MacroEventType.KeyUp,
                VkCode = vk,
                ScanCode = (int)data.scanCode,
                IsExtendedKey = (data.flags & NativeMethods.LLKHF_EXTENDED) != 0
            }, _recordStopwatch!.ElapsedMilliseconds);
        }

        #endregion

        #region Playback

        private readonly RunController _playRunner = new RunController("Macro");

        /// <summary>Playback lifecycle: Stopped / Running / Paused / Finished.</summary>
        public event Action<RunState>? PlaybackRunStateChanged;
        public bool IsPlaybackPaused => _playRunner.State == RunState.Paused;
        public RunState PlaybackState => _playRunner.State;

        private void OnPlayRunnerStateChanged(RunState s)
        {
            IsPlaying = s is RunState.Running or RunState.Paused;
            PlaybackRunStateChanged?.Invoke(s);
            if (s is RunState.Running && !_playbackWasActive) { _playbackWasActive = true; PlaybackStateChanged?.Invoke(true); }
            else if (s is RunState.Stopped or RunState.Finished && _playbackWasActive) { _playbackWasActive = false; PlaybackStateChanged?.Invoke(false); }
        }
        private volatile bool _playbackWasActive;

        public void StartPlayback(double speed = 1.0, int loops = 1)
        {
            List<MacroAction> actionsCopy;
            lock (_lock)
            {
                if (_playRunner.IsActive || IsRecording || RecordedActions.Count == 0) return;
                PlaybackSpeed = speed <= 0 ? 1.0 : speed;
                LoopCount = Math.Max(0, loops);
                actionsCopy = new List<MacroAction>(RecordedActions);
            }

            double sp = PlaybackSpeed;
            int lp = LoopCount;
            if (_playRunner.TryStart(ctx => PlaybackLoop(ctx, actionsCopy, sp, lp)))
            {
                LoggerService.Log($"[MACRO] Reprodução iniciada: {actionsCopy.Count} ações, {sp}x, ciclos={lp}");
                StatusUpdated?.Invoke("Executando macro");
                SoundService.PlayStartSound();
            }
        }

        public void StopPlayback()
        {
            if (!_playRunner.IsActive) return;
            _playRunner.Stop();
            StatusUpdated?.Invoke("Parado");
            SoundService.PlayStopSound();
        }

        public void TogglePlaybackPause()
        {
            if (_playRunner.State == RunState.Paused)
            {
                if (_playRunner.Resume()) StatusUpdated?.Invoke("Executando macro");
            }
            else if (_playRunner.Pause())
            {
                StatusUpdated?.Invoke("Macro pausada");
            }
        }

        public void Panic()
        {
            if (IsRecording) StopRecording();
            _playRunner.Stop();
        }

        private void PlaybackLoop(RunContext ctx, List<MacroAction> actions, double speed, int maxLoops)
        {
            var heldKeys = new Dictionary<int, MacroAction>();
            var heldButtons = new HashSet<MouseButtonType>();
            var clock = new Stopwatch();

            // While paused nothing may stay pressed, and the macro clock must not advance
            bool HonorPause()
            {
                if (!ctx.IsPaused) return !ctx.IsCancelled;
                clock.Stop();
                foreach (var key in heldKeys.Values) SendKey(key, keyUp: true);
                foreach (var btn in heldButtons) _mouseInput.SendMouseUp(btn);
                if (!ctx.WaitIfPaused()) return false;
                foreach (var key in heldKeys.Values) SendKey(key, keyUp: false);
                foreach (var btn in heldButtons) _mouseInput.SendMouseDown(btn);
                clock.Start();
                return true;
            }

            try
            {
                int completedCycles = 0;
                while (!ctx.IsCancelled)
                {
                    clock.Restart();
                    double targetMs = 0;

                    for (int i = 0; i < actions.Count; i++)
                    {
                        if (!HonorPause()) return;

                        // Optional focus restriction: wait (clock frozen), never stop
                        if (!_windowRestrictionService.IsTargetWindowActive())
                        {
                            StatusUpdated?.Invoke("Macro aguardando a janela alvo voltar ao foco");
                            clock.Stop();
                            while (!_windowRestrictionService.IsTargetWindowActive())
                            {
                                if (!ctx.Sleep(150)) return;
                            }
                            clock.Start();
                            StatusUpdated?.Invoke("Executando macro");
                        }

                        var act = actions[i];
                        targetMs += act.DelayMs / speed;

                        // Wait in small slices so a pause takes effect immediately
                        while (true)
                        {
                            if (!HonorPause()) return;
                            int wait = (int)Math.Round(targetMs - clock.Elapsed.TotalMilliseconds);
                            if (wait <= 0) break;
                            MouseInputService.PreciseWaitMilliseconds(Math.Min(wait, 50), ctx.Token);
                            if (ctx.IsCancelled) return;
                        }

                        ExecuteAction(act, heldKeys, heldButtons);

                        if (act.EventType != MacroEventType.MouseMove || i % 20 == 0)
                        {
                            PlaybackProgress?.Invoke(i + 1, actions.Count);
                        }
                    }

                    completedCycles++;
                    PlaybackCycleCompleted?.Invoke(completedCycles);
                    _statisticsService.RegisterMacroExecution();

                    if (maxLoops > 0 && completedCycles >= maxLoops)
                    {
                        StatusUpdated?.Invoke("Macro concluída");
                        SoundService.PlayStopSound();
                        return;
                    }
                }
            }
            finally
            {
                // Never leave keys or buttons stuck down
                foreach (var key in heldKeys.Values) SendKey(key, keyUp: true);
                foreach (var btn in heldButtons) _mouseInput.SendMouseUp(btn);
                LoggerService.Log($"[MACRO] Reprodução finalizada (cancelada={ctx.IsCancelled})");
            }
        }

        private void ExecuteAction(MacroAction act, Dictionary<int, MacroAction> heldKeys, HashSet<MouseButtonType> heldButtons)
        {
            switch (act.EventType)
            {
                case MacroEventType.MouseMove:
                    SendMouse(act.X, act.Y, 0);
                    break;

                case MacroEventType.MouseDown:
                    if (_safeZoneService.IsPointInSafeZone(act.X, act.Y, out _)) break;
                    SendMouse(act.X, act.Y, ButtonFlag(act.MouseButton, down: true));
                    heldButtons.Add(act.MouseButton);
                    break;

                case MacroEventType.MouseUp:
                    SendMouse(act.X, act.Y, ButtonFlag(act.MouseButton, down: false));
                    if (heldButtons.Remove(act.MouseButton))
                    {
                        _statisticsService.RegisterClick();
                    }
                    break;

                case MacroEventType.Click:
                    if (_safeZoneService.IsPointInSafeZone(act.X, act.Y, out _)) break;
                    SendMouse(act.X, act.Y, ButtonFlag(act.MouseButton, down: true));
                    MouseInputService.PreciseWaitMilliseconds(8);
                    SendMouse(act.X, act.Y, ButtonFlag(act.MouseButton, down: false));
                    _statisticsService.RegisterClick();
                    break;

                case MacroEventType.Wheel:
                    SendWheel(act);
                    break;

                case MacroEventType.KeyDown:
                    SendKey(act, keyUp: false);
                    heldKeys[act.VkCode] = act;
                    break;

                case MacroEventType.KeyUp:
                    SendKey(act, keyUp: true);
                    heldKeys.Remove(act.VkCode);
                    break;
            }
        }

        private static uint ButtonFlag(MouseButtonType button, bool down) => button switch
        {
            MouseButtonType.Right => down ? NativeMethods.MOUSEEVENTF_RIGHTDOWN : NativeMethods.MOUSEEVENTF_RIGHTUP,
            MouseButtonType.Middle => down ? NativeMethods.MOUSEEVENTF_MIDDLEDOWN : NativeMethods.MOUSEEVENTF_MIDDLEUP,
            _ => down ? NativeMethods.MOUSEEVENTF_LEFTDOWN : NativeMethods.MOUSEEVENTF_LEFTUP
        };

        /// <summary>Moves to (x, y) in virtual-desktop coordinates and optionally presses/releases a button.</summary>
        private static void SendMouse(int x, int y, uint buttonFlags)
        {
            int vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            int vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            int vw = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN) - 1);
            int vh = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN) - 1);

            int ax = (int)Math.Round((x - vx) * 65535.0 / vw);
            int ay = (int)Math.Round((y - vy) * 65535.0 / vh);

            var input = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                u = new NativeMethods.INPUTUNION
                {
                    mi = new NativeMethods.MOUSEINPUT
                    {
                        dx = ax,
                        dy = ay,
                        dwFlags = NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE |
                                  NativeMethods.MOUSEEVENTF_VIRTUALDESK | buttonFlags,
                        dwExtraInfo = SelfClickGuard.Signature
                    }
                }
            };

            if (NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>()) == 0)
            {
                // Fallback for sessions where SendInput is blocked
                NativeMethods.SetCursorPos(x, y);
                if (buttonFlags != 0) NativeMethods.mouse_event(buttonFlags, 0, 0, 0, (UIntPtr)(ulong)SelfClickGuard.Signature);
            }
        }

        private static void SendWheel(MacroAction act)
        {
            var input = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                u = new NativeMethods.INPUTUNION
                {
                    mi = new NativeMethods.MOUSEINPUT
                    {
                        mouseData = unchecked((uint)act.WheelDelta),
                        dwFlags = act.IsHorizontalWheel ? NativeMethods.MOUSEEVENTF_HWHEEL : NativeMethods.MOUSEEVENTF_WHEEL,
                        dwExtraInfo = SelfClickGuard.Signature
                    }
                }
            };
            NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
        }

        private static void SendKey(MacroAction act, bool keyUp)
        {
            int scan = act.ScanCode != 0
                ? act.ScanCode
                : (int)NativeMethods.MapVirtualKey((uint)act.VkCode, NativeMethods.MAPVK_VK_TO_VSC);

            uint flags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0;
            if (act.IsExtendedKey) flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
            if (scan != 0) flags |= NativeMethods.KEYEVENTF_SCANCODE;

            var input = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.INPUTUNION
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = scan != 0 ? (ushort)0 : (ushort)act.VkCode,
                        wScan = (ushort)scan,
                        dwFlags = flags,
                        dwExtraInfo = SelfClickGuard.Signature
                    }
                }
            };
            NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
        }

        #endregion

        #region Save / Load Macro File

        public void SaveMacroToFile(string filePath)
        {
            string json = JsonSerializer.Serialize(RecordedActions, JsonOptions);
            File.WriteAllText(filePath, json);
        }

        public bool LoadMacroFromFile(string filePath)
        {
            if (!File.Exists(filePath)) return false;
            try
            {
                string json = File.ReadAllText(filePath);
                var list = JsonSerializer.Deserialize<List<MacroAction>>(json, JsonOptions);
                if (list != null)
                {
                    Renumber(list);
                    RecordedActions = list;
                    return true;
                }
            }
            catch (Exception ex)
            {
                LoggerService.LogException("[MACRO] Falha ao carregar arquivo", ex);
            }
            return false;
        }

        #endregion

        public void Dispose()
        {
            StopHooks();
            _playRunner.Stop();
            GC.SuppressFinalize(this);
        }
    }
}
