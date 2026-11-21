using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using GabrielAutoclick.Helpers;
using GabrielAutoclick.Models;

namespace GabrielAutoclick.Services
{
    public class TrayIconService : IDisposable
    {
        private NotifyIcon? _notifyIcon;
        private ContextMenuStrip? _contextMenu;
        private bool _shownMinimizeTip = false;
        private bool _isDisposed = false;

        public event Action? OpenMainWindowRequested;
        public event Action? ToggleMiniModeRequested;
        public event Action? StartAutoClickRequested;
        public event Action? StopAutoClickRequested;
        public event Action? TogglePauseRequested;
        public event Action<ClickPositionMode>? PositionModeChanged;
        public event Action<MouseButtonType>? MouseButtonChanged;
        public event Action<double>? CpsPresetSelected;
        public event Action? StartMacroRequested;
        public event Action? StopMacroRequested;
        public event Action? StartMultiClickRequested;
        public event Action? StopMultiClickRequested;
        public event Action? OpenSettingsRequested;
        public event Action? PanicRequested;
        public event Action? ExitRequested;

        public void Initialize()
        {
            if (_notifyIcon != null) return;

            try
            {
                // Create context menu
                _contextMenu = new ContextMenuStrip();
                _contextMenu.RenderMode = ToolStripRenderMode.System;

                // 1. Header
                var headerItem = new ToolStripMenuItem("Gabriel Autoclick")
                {
                    Enabled = false,
                    Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
                };
                _contextMenu.Items.Add(headerItem);
                _contextMenu.Items.Add(new ToolStripSeparator());

                // 2. Start / Stop / Pause
                var itemStart = new ToolStripMenuItem("Iniciar autoclick", null, (s, e) => StartAutoClickRequested?.Invoke());
                var itemStop = new ToolStripMenuItem("Parar autoclick", null, (s, e) => StopAutoClickRequested?.Invoke());
                var itemPause = new ToolStripMenuItem("Pausar / continuar", null, (s, e) => TogglePauseRequested?.Invoke());
                _contextMenu.Items.Add(itemStart);
                _contextMenu.Items.Add(itemStop);
                _contextMenu.Items.Add(itemPause);
                _contextMenu.Items.Add(new ToolStripSeparator());

                // 3. Submenu: Mode
                var menuMode = new ToolStripMenuItem("Posição do clique");
                menuMode.DropDownItems.Add("Seguir cursor", null, (s, e) => PositionModeChanged?.Invoke(ClickPositionMode.FollowCursor));
                menuMode.DropDownItems.Add("Posição atual", null, (s, e) => PositionModeChanged?.Invoke(ClickPositionMode.CurrentPosition));
                menuMode.DropDownItems.Add("Ponto fixo", null, (s, e) => PositionModeChanged?.Invoke(ClickPositionMode.FixedPosition));
                _contextMenu.Items.Add(menuMode);

                // 4. Submenu: Mouse Button
                var menuButton = new ToolStripMenuItem("Botão do mouse");
                menuButton.DropDownItems.Add("Esquerdo", null, (s, e) => MouseButtonChanged?.Invoke(MouseButtonType.Left));
                menuButton.DropDownItems.Add("Direito", null, (s, e) => MouseButtonChanged?.Invoke(MouseButtonType.Right));
                menuButton.DropDownItems.Add("Meio", null, (s, e) => MouseButtonChanged?.Invoke(MouseButtonType.Middle));
                _contextMenu.Items.Add(menuButton);

                // 5. Submenu: CPS Presets
                var menuCps = new ToolStripMenuItem("Cliques por segundo");
                menuCps.DropDownItems.Add("1 CPS", null, (s, e) => CpsPresetSelected?.Invoke(1));
                menuCps.DropDownItems.Add("5 CPS", null, (s, e) => CpsPresetSelected?.Invoke(5));
                menuCps.DropDownItems.Add("10 CPS", null, (s, e) => CpsPresetSelected?.Invoke(10));
                menuCps.DropDownItems.Add("15 CPS", null, (s, e) => CpsPresetSelected?.Invoke(15));
                menuCps.DropDownItems.Add("20 CPS", null, (s, e) => CpsPresetSelected?.Invoke(20));
                menuCps.DropDownItems.Add("Personalizado...", null, (s, e) => OpenMainWindowRequested?.Invoke());
                _contextMenu.Items.Add(menuCps);
                _contextMenu.Items.Add(new ToolStripSeparator());

                // 6. Submenu: Macro
                var menuMacro = new ToolStripMenuItem("Macro");
                menuMacro.DropDownItems.Add("Reproduzir macro", null, (s, e) => StartMacroRequested?.Invoke());
                menuMacro.DropDownItems.Add("Parar macro", null, (s, e) => StopMacroRequested?.Invoke());
                _contextMenu.Items.Add(menuMacro);

                // 7. Submenu: Multi Click
                var menuMulti = new ToolStripMenuItem("Múltiplos pontos");
                menuMulti.DropDownItems.Add("Iniciar sequência", null, (s, e) => StartMultiClickRequested?.Invoke());
                menuMulti.DropDownItems.Add("Parar sequência", null, (s, e) => StopMultiClickRequested?.Invoke());
                _contextMenu.Items.Add(menuMulti);
                _contextMenu.Items.Add(new ToolStripSeparator());

                // 8. Open, Mini Mode, Settings
                var itemOpen = new ToolStripMenuItem("Abrir Gabriel Autoclick", null, (s, e) => OpenMainWindowRequested?.Invoke())
                {
                    Font = new Font("Segoe UI", 9f, FontStyle.Bold)
                };
                var itemMini = new ToolStripMenuItem("Modo compacto", null, (s, e) => ToggleMiniModeRequested?.Invoke());
                var itemSettings = new ToolStripMenuItem("Configurações", null, (s, e) => OpenSettingsRequested?.Invoke());
                _contextMenu.Items.Add(itemOpen);
                _contextMenu.Items.Add(itemMini);
                _contextMenu.Items.Add(itemSettings);
                _contextMenu.Items.Add(new ToolStripSeparator());

                // 9. Panic & Exit
                var itemPanic = new ToolStripMenuItem("Parar tudo (F12)", null, (s, e) => PanicRequested?.Invoke())
                {
                    ForeColor = Color.DarkRed,
                    Font = new Font("Segoe UI", 9f, FontStyle.Bold)
                };
                var itemExit = new ToolStripMenuItem("Sair", null, (s, e) => ExitRequested?.Invoke());
                _contextMenu.Items.Add(itemPanic);
                _contextMenu.Items.Add(itemExit);

                // Load application icon
                Icon appIcon = SystemIcons.Application;
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "icon.ico");
                if (File.Exists(iconPath))
                {
                    try
                    {
                        appIcon = new Icon(iconPath);
                    }
                    catch { }
                }

                _notifyIcon = new NotifyIcon
                {
                    Icon = appIcon,
                    Text = "Gabriel Autoclick — Desligado",
                    Visible = true,
                    ContextMenuStrip = _contextMenu
                };

                _notifyIcon.MouseClick += (s, e) =>
                {
                    if (e.Button == MouseButtons.Left)
                    {
                        OpenMainWindowRequested?.Invoke();
                    }
                };

                _notifyIcon.DoubleClick += (s, e) =>
                {
                    OpenMainWindowRequested?.Invoke();
                };

                LoggerService.Log("System Tray icon successfully initialized");
            }
            catch (Exception ex)
            {
                LoggerService.LogException("Failed to initialize Tray Icon", ex);
            }
        }

        public void UpdateTooltip(string statusText)
        {
            if (_notifyIcon != null)
            {
                try
                {
                    string text = $"{Constants.AppName} — {statusText}";
                    if (text.Length > 63) text = text.Substring(0, 63);
                    _notifyIcon.Text = text;
                }
                catch { }
            }
        }

        public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
        {
            if (_notifyIcon != null)
            {
                try
                {
                    _notifyIcon.ShowBalloonTip(3000, title, message, icon);
                }
                catch { }
            }
        }

        public void ShowFirstTimeMinimizeTip()
        {
            if (!_shownMinimizeTip)
            {
                _shownMinimizeTip = true;
                ShowNotification(
                    "Gabriel Autoclick",
                    "O aplicativo continua funcionando em segundo plano. Clique no ícone para reabrir.",
                    ToolTipIcon.Info
                );
            }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }

                _contextMenu?.Dispose();
                _isDisposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
