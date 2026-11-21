using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace GabrielAutoclick.Models
{
    public enum BackgroundClickKind
    {
        Left = 0,
        Right = 1,
        Middle = 2,
        DoubleLeft = 3
    }

    public enum BackgroundClickMethod
    {
        /// <summary>Posts WM_*BUTTON* messages to the control under the point. Does not touch the cursor.</summary>
        PostMessage = 0,
        /// <summary>Same messages, delivered synchronously (waits for the target to process them).</summary>
        SendMessage = 1,
        /// <summary>Invokes the UI element at the point through UI Automation (buttons, menus, lists).</summary>
        UIAutomation = 2,
        /// <summary>Fallback: moves the real cursor for a few milliseconds, clicks and puts it back.</summary>
        CompatibilityCursor = 3
    }

    /// <summary>
    /// A click target bound to a specific window. Coordinates are stored relative to the
    /// window's client area so the click stays correct when the window is moved.
    /// </summary>
    public class BackgroundClickPoint : INotifyPropertyChanged
    {
        private int _index = 1;
        private int _delayMs;
        private BackgroundClickKind _kind = BackgroundClickKind.Left;
        private string _status = "Pronto";
        private string _windowTitle = string.Empty;

        public string Id { get; set; } = Guid.NewGuid().ToString();

        public int Index { get => _index; set => Set(ref _index, value); }

        /// <summary>Window handle for this session. Re-resolved by process/class when stale.</summary>
        [JsonIgnore]
        public IntPtr Hwnd { get; set; } = IntPtr.Zero;

        public string WindowTitle
        {
            get => _windowTitle;
            set { Set(ref _windowTitle, value); OnChanged(nameof(WindowDisplay)); }
        }

        public string ProcessName { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;

        /// <summary>Position inside the window's client area (pixels).</summary>
        public int ClientX { get; set; }
        public int ClientY { get; set; }

        public BackgroundClickKind Kind
        {
            get => _kind;
            set { Set(ref _kind, value); OnChanged(nameof(KindName)); }
        }

        /// <summary>Wait before this point is clicked (inside each cycle).</summary>
        public int DelayMs { get => _delayMs; set => Set(ref _delayMs, Math.Max(0, value)); }

        [JsonIgnore]
        public string Status { get => _status; set => Set(ref _status, value); }

        [JsonIgnore]
        public string KindName => KindToText(Kind);

        [JsonIgnore]
        public string WindowDisplay => string.IsNullOrWhiteSpace(WindowTitle) ? ProcessName : WindowTitle;

        [JsonIgnore]
        public string PositionText => $"{ClientX}, {ClientY}";

        public static string KindToText(BackgroundClickKind kind) => kind switch
        {
            BackgroundClickKind.Right => "Direito",
            BackgroundClickKind.Middle => "Meio",
            BackgroundClickKind.DoubleLeft => "Duplo clique",
            _ => "Esquerdo"
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            OnChanged(name);
        }

        private void OnChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class BackgroundClickSettings
    {
        public List<BackgroundClickPoint> Points { get; set; } = new List<BackgroundClickPoint>();
        public int IntervalMs { get; set; } = 1000;
        public int Cycles { get; set; } = 0; // 0 = until stopped
        public BackgroundClickMethod Method { get; set; } = BackgroundClickMethod.PostMessage;
        public bool SendMouseMove { get; set; } = true;
        public BackgroundClickKind DefaultKind { get; set; } = BackgroundClickKind.Left;
    }
}
