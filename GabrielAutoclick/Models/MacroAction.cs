using System;
using System.Text.Json.Serialization;
using System.Windows.Input;
using GabrielAutoclick.Helpers;

namespace GabrielAutoclick.Models
{
    public enum MacroEventType
    {
        MouseDown = 0,
        MouseUp = 1,
        Click = 2,
        MouseMove = 3,
        Delay = 4,
        KeyDown = 5,
        KeyUp = 6,
        Wheel = 7
    }

    public class MacroAction
    {
        public int SequenceNumber { get; set; } = 1;
        public MacroEventType EventType { get; set; } = MacroEventType.Click;
        public MouseButtonType MouseButton { get; set; } = MouseButtonType.Left;
        public int X { get; set; } = 0;
        public int Y { get; set; } = 0;
        public int DelayMs { get; set; } = 50;
        public long TimestampMs { get; set; } = 0;

        // Keyboard data
        public int VkCode { get; set; } = 0;
        public int ScanCode { get; set; } = 0;
        public bool IsExtendedKey { get; set; } = false;

        // Wheel data (positive = up / right)
        public int WheelDelta { get; set; } = 0;
        public bool IsHorizontalWheel { get; set; } = false;

        [JsonIgnore]
        public bool IsKeyboard => EventType is MacroEventType.KeyDown or MacroEventType.KeyUp;

        [JsonIgnore]
        public string ButtonName => MouseButton switch
        {
            MouseButtonType.Right => "direito",
            MouseButtonType.Middle => "do meio",
            _ => "esquerdo"
        };

        [JsonIgnore]
        public string KeyName
        {
            get
            {
                if (VkCode == 0) return "?";
                var key = KeyInterop.KeyFromVirtualKey(VkCode);
                return HotkeyItem.KeyToString(key);
            }
        }

        [JsonIgnore]
        public string Description => EventType switch
        {
            MacroEventType.MouseDown => $"Pressionar botão {ButtonName}",
            MacroEventType.MouseUp => $"Soltar botão {ButtonName}",
            MacroEventType.Click => $"Clique {ButtonName}",
            MacroEventType.MouseMove => "Mover cursor",
            MacroEventType.Delay => "Aguardar",
            MacroEventType.KeyDown => "Pressionar tecla",
            MacroEventType.KeyUp => "Soltar tecla",
            MacroEventType.Wheel => WheelDelta > 0 ? "Rolar para cima" : "Rolar para baixo",
            _ => "Ação"
        };

        [JsonIgnore]
        public string Detail => EventType switch
        {
            MacroEventType.KeyDown or MacroEventType.KeyUp => KeyName,
            MacroEventType.Delay => $"{DelayMs} ms",
            _ => $"{X}, {Y}"
        };
    }
}
