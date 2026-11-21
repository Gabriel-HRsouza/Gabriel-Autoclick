using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;
using GabrielAutoclick.Helpers;
using GabrielAutoclick.Models;

namespace GabrielAutoclick.Services
{
    public class HotkeyService : IDisposable
    {
        private IntPtr _hookId = IntPtr.Zero;
        private NativeMethods.HookProc? _proc;
        private readonly HashSet<int> _pressedVkCodes = new HashSet<int>();

        public HotkeyConfig Config { get; set; } = new HotkeyConfig();
        public bool IsRunningAutoclickOrMacro { get; set; } = false;
        public bool IsCapturingHotkey { get; set; } = false;

        // Events
        public event Action? ToggleAutoclickTriggered;
        public event Action? SelectPositionTriggered;
        public event Action? StartMacroTriggered;
        public event Action? StopMacroTriggered;
        public event Action? PanicTriggered;

        // Live capture event
        public event Action<Key, ModifierKeys>? HotkeyCaptured;

        public HotkeyService(HotkeyConfig? initialConfig = null)
        {
            if (initialConfig != null)
            {
                Config = initialConfig;
            }
            StartHook();
        }

        public void StartHook()
        {
            if (_hookId != IntPtr.Zero) return;

            _proc = HookCallback;
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;

            if (curModule?.ModuleName != null)
            {
                IntPtr hMod = NativeMethods.GetModuleHandle(curModule.ModuleName);
                _hookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, hMod, 0);
                LoggerService.Log($"[HOTKEY] Global Low-Level Keyboard Hook started (HookID={_hookId})");
            }
        }

        public void StopHook()
        {
            if (_hookId != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
                _pressedVkCodes.Clear();
                LoggerService.Log("[HOTKEY] Global Low-Level Keyboard Hook stopped");
            }
        }

        public void UpdateConfig(HotkeyConfig newConfig)
        {
            Config = newConfig;
            LoggerService.Log($"[HOTKEY] Configuração de atalhos atualizada: Toggle={Config.ToggleAutoclick}, SelectPos={Config.SelectPosition}, StartMacro={Config.StartMacro}, StopMacro={Config.StopMacro}");
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = (int)wParam;

                // Handle Key Up: release from tracking set
                if (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP)
                {
                    int vkCode = Marshal.ReadInt32(lParam);
                    _pressedVkCodes.Remove(vkCode);
                    return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                }

                // Handle Key Down
                if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
                {
                    int vkCode = Marshal.ReadInt32(lParam);
                    Key key = KeyInterop.KeyFromVirtualKey(vkCode);

                    // Prevent double-toggle / Windows auto-repeat when key is held down
                    if (_pressedVkCodes.Contains(vkCode))
                    {
                        LoggerService.Log($"[HOTKEY IGNORED] Key={key} (VK=0x{vkCode:X2}) Reason=KeyAlreadyPressed (AutoRepeat)");
                        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                    }

                    _pressedVkCodes.Add(vkCode);

                    bool ctrl = (NativeMethods.GetKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0;
                    bool alt = (NativeMethods.GetKeyState(NativeMethods.VK_MENU) & 0x8000) != 0;
                    bool shift = (NativeMethods.GetKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0;
                    bool win = ((NativeMethods.GetKeyState(NativeMethods.VK_LWIN) & 0x8000) != 0) ||
                               ((NativeMethods.GetKeyState(NativeMethods.VK_RWIN) & 0x8000) != 0);

                    ModifierKeys currentMods = ModifierKeys.None;
                    if (ctrl) currentMods |= ModifierKeys.Control;
                    if (alt) currentMods |= ModifierKeys.Alt;
                    if (shift) currentMods |= ModifierKeys.Shift;
                    if (win) currentMods |= ModifierKeys.Windows;

                    // Handle Hotkey Capture mode if active
                    if (IsCapturingHotkey)
                    {
                        if (!HotkeyItem.IsModifierKey(key) && key != Key.None)
                        {
                            LoggerService.Log($"[HOTKEY CAPTURE] Key={key}, Mods={currentMods}");
                            HotkeyCaptured?.Invoke(key, currentMods);
                            return (IntPtr)1; // Consume key during capture
                        }
                        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                    }

                    string fgWindow = GetForegroundWindowTitle();

                    // 1. Check EXCLUSIVELY F12 for Panic Button (ESC is NOT Panic)
                    if (vkCode == NativeMethods.VK_F12 && currentMods == ModifierKeys.None)
                    {
                        LoggerService.Log($"[HOTKEY] Action=Panic Key=F12 Modifiers=None Source=KeyboardHook ForegroundWindow='{fgWindow}' Timestamp={DateTime.Now:HH:mm:ss.fff}");
                        PanicTriggered?.Invoke();
                    }
                    // 2. Dynamic Toggle Autoclick (e.g. F6)
                    else if (MatchesHotkey(Config.ToggleAutoclick, key, currentMods, vkCode))
                    {
                        LoggerService.Log($"[HOTKEY] Action=ToggleAutoclick Key={key} Modifiers={currentMods} Source=KeyboardHook ForegroundWindow='{fgWindow}' Timestamp={DateTime.Now:HH:mm:ss.fff}");
                        ToggleAutoclickTriggered?.Invoke();
                    }
                    // 3. Dynamic Select Position (e.g. F7)
                    else if (MatchesHotkey(Config.SelectPosition, key, currentMods, vkCode))
                    {
                        LoggerService.Log($"[HOTKEY] Action=SelectPosition Key={key} Modifiers={currentMods} Source=KeyboardHook ForegroundWindow='{fgWindow}' Timestamp={DateTime.Now:HH:mm:ss.fff}");
                        SelectPositionTriggered?.Invoke();
                    }
                    // 4. Dynamic Start Macro (e.g. F8)
                    else if (MatchesHotkey(Config.StartMacro, key, currentMods, vkCode))
                    {
                        LoggerService.Log($"[HOTKEY] Action=StartMacro Key={key} Modifiers={currentMods} Source=KeyboardHook ForegroundWindow='{fgWindow}' Timestamp={DateTime.Now:HH:mm:ss.fff}");
                        StartMacroTriggered?.Invoke();
                    }
                    // 5. Dynamic Stop Macro (e.g. F9)
                    else if (MatchesHotkey(Config.StopMacro, key, currentMods, vkCode))
                    {
                        LoggerService.Log($"[HOTKEY] Action=StopMacro Key={key} Modifiers={currentMods} Source=KeyboardHook ForegroundWindow='{fgWindow}' Timestamp={DateTime.Now:HH:mm:ss.fff}");
                        StopMacroTriggered?.Invoke();
                    }
                }
            }

            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private static string GetForegroundWindowTitle()
        {
            try
            {
                IntPtr hwnd = NativeMethods.GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return "None";
                int len = NativeMethods.GetWindowTextLength(hwnd);
                if (len == 0) return "Unknown";
                var sb = new StringBuilder(len + 1);
                NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
                return sb.ToString();
            }
            catch
            {
                return "Unknown";
            }
        }

        private static bool MatchesHotkey(HotkeyItem item, Key key, ModifierKeys mods, int vkCode)
        {
            if (item == null || item.Key == Key.None) return false;

            // Check modifier match
            if (item.Modifiers != mods) return false;

            // Direct Key match or Virtual Key match
            if (item.Key == key) return true;

            int targetVk = KeyInterop.VirtualKeyFromKey(item.Key);
            return targetVk == vkCode;
        }

        public bool CheckConflict(Key key, ModifierKeys mods, string actionTag, out string conflictName)
        {
            conflictName = string.Empty;

            if (key == Key.F12 && mods == ModifierKeys.None)
            {
                conflictName = "Panic Button (F12 é fixo)";
                return true;
            }

            if (actionTag != "ToggleAutoclick" && Config.ToggleAutoclick.Key == key && Config.ToggleAutoclick.Modifiers == mods)
            {
                conflictName = Config.ToggleAutoclick.Name;
                return true;
            }

            if (actionTag != "SelectPosition" && Config.SelectPosition.Key == key && Config.SelectPosition.Modifiers == mods)
            {
                conflictName = Config.SelectPosition.Name;
                return true;
            }

            if (actionTag != "StartMacro" && Config.StartMacro.Key == key && Config.StartMacro.Modifiers == mods)
            {
                conflictName = Config.StartMacro.Name;
                return true;
            }

            if (actionTag != "StopMacro" && Config.StopMacro.Key == key && Config.StopMacro.Modifiers == mods)
            {
                conflictName = Config.StopMacro.Name;
                return true;
            }

            return false;
        }

        public void Dispose()
        {
            StopHook();
            GC.SuppressFinalize(this);
        }
    }
}
