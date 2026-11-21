using System;
using System.Text;
using System.Windows.Input;

namespace GabrielAutoclick.Models
{
    public class HotkeyItem
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Key Key { get; set; } = Key.None;
        public ModifierKeys Modifiers { get; set; } = ModifierKeys.None;

        public override string ToString()
        {
            return ToDisplayString();
        }

        public string ToDisplayString()
        {
            if (Key == Key.None) return "Nenhum";

            var sb = new StringBuilder();
            if ((Modifiers & ModifierKeys.Control) != 0) sb.Append("Ctrl + ");
            if ((Modifiers & ModifierKeys.Alt) != 0) sb.Append("Alt + ");
            if ((Modifiers & ModifierKeys.Shift) != 0) sb.Append("Shift + ");
            if ((Modifiers & ModifierKeys.Windows) != 0) sb.Append("Win + ");

            sb.Append(KeyToString(Key));
            return sb.ToString();
        }

        public static string KeyToString(Key key)
        {
            return key switch
            {
                Key.None => "Nenhum",
                Key.Escape => "ESC",
                Key.Return => "Enter",
                Key.Back => "Backspace",
                Key.Capital => "Caps Lock",
                Key.Next => "Page Down",
                Key.Prior => "Page Up",
                Key.OemTilde => "~",
                Key.OemMinus => "-",
                Key.OemPlus => "+",
                Key.OemOpenBrackets => "[",
                Key.Oem6 => "]",
                Key.Oem5 => "\\",
                Key.Oem1 => ";",
                Key.OemQuotes => "'",
                Key.OemComma => ",",
                Key.OemPeriod => ".",
                Key.OemQuestion => "/",
                Key.D0 => "0",
                Key.D1 => "1",
                Key.D2 => "2",
                Key.D3 => "3",
                Key.D4 => "4",
                Key.D5 => "5",
                Key.D6 => "6",
                Key.D7 => "7",
                Key.D8 => "8",
                Key.D9 => "9",
                _ => key.ToString()
            };
        }

        public static bool IsModifierKey(Key key)
        {
            return key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin
                or Key.System;
        }
    }

    public class HotkeyConfig
    {
        public HotkeyItem ToggleAutoclick { get; set; } = new HotkeyItem
        {
            Name = "Iniciar / Parar Autoclick",
            Description = "Alterna a execução do autoclicker",
            Key = Key.F6,
            Modifiers = ModifierKeys.None
        };

        public HotkeyItem SelectPosition { get; set; } = new HotkeyItem
        {
            Name = "Selecionar Posição",
            Description = "Permite selecionar uma coordenada na tela",
            Key = Key.F7,
            Modifiers = ModifierKeys.None
        };

        public HotkeyItem StartMacro { get; set; } = new HotkeyItem
        {
            Name = "Iniciar Macro",
            Description = "Executa a macro carregada",
            Key = Key.F8,
            Modifiers = ModifierKeys.None
        };

        public HotkeyItem StopMacro { get; set; } = new HotkeyItem
        {
            Name = "Parar Macro",
            Description = "Interrompe a execução da macro",
            Key = Key.F9,
            Modifiers = ModifierKeys.None
        };

        public HotkeyItem BackgroundToggle { get; set; } = new HotkeyItem
        {
            Name = "Iniciar / parar clique em segundo plano",
            Description = "Liga ou desliga o clique em segundo plano",
            Key = Key.F10,
            Modifiers = ModifierKeys.None
        };

        public HotkeyItem BackgroundPause { get; set; } = new HotkeyItem
        {
            Name = "Pausar / continuar clique em segundo plano",
            Description = "Pausa ou retoma o clique em segundo plano",
            Key = Key.F11,
            Modifiers = ModifierKeys.None
        };

        public HotkeyItem Panic { get; set; } = new HotkeyItem
        {
            Name = "Panic Button",
            Description = "Interrompe imediatamente cliques, macros e sequências",
            Key = Key.F12,
            Modifiers = ModifierKeys.None
        };

        public void RestoreDefaults()
        {
            ToggleAutoclick.Key = Key.F6;
            ToggleAutoclick.Modifiers = ModifierKeys.None;

            SelectPosition.Key = Key.F7;
            SelectPosition.Modifiers = ModifierKeys.None;

            StartMacro.Key = Key.F8;
            StartMacro.Modifiers = ModifierKeys.None;

            StopMacro.Key = Key.F9;
            StopMacro.Modifiers = ModifierKeys.None;

            BackgroundToggle.Key = Key.F10;
            BackgroundToggle.Modifiers = ModifierKeys.None;

            BackgroundPause.Key = Key.F11;
            BackgroundPause.Modifiers = ModifierKeys.None;

            Panic.Key = Key.F12;
            Panic.Modifiers = ModifierKeys.None;
        }
    }
}
