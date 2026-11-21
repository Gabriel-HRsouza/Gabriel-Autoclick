using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using GabrielAutoclick.Helpers;
using GabrielAutoclick.Models;
using GabrielAutoclick.Services;
using MessageBox = System.Windows.MessageBox;

namespace GabrielAutoclick
{
    public partial class App : System.Windows.Application
    {
        private IpcService.IpcServer? _ipcServer;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. Initialize Logger
            LoggerService.Initialize();
            LoggerService.Log("[STARTUP] Processo iniciado");

            // 2. Setup Unhandled Exception Handlers
            SetupExceptionHandling();

            // 3. Check for Force Flag
            bool forceStart = false;
            foreach (var arg in e.Args)
            {
                if (arg.Equals("--force", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("--allow-multiple", StringComparison.OrdinalIgnoreCase))
                {
                    forceStart = true;
                    break;
                }
            }

            // 4. Single Instance IPC Check: Signal existing instance to restore window
            if (!forceStart)
            {
                LoggerService.Log("[STARTUP] Verificando existência de instância prévia via IPC...");
                if (IpcService.SendSignalToExistingInstance("SHOW"))
                {
                    LoggerService.Log("[STARTUP] Sinal 'SHOW' entregue com sucesso para a instância principal existente. Encerrando processo secundário.");
                    Shutdown();
                    return;
                }
                LoggerService.Log("[STARTUP] Nenhuma instância prévia detectada. Assumindo como Instância Principal.");
            }

            // 5. Ensure Application Does Not Automatically Exit When MainWindow is Hidden
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            try
            {
                // 6. Start IPC Server for receiving commands from future duplicate launches
                _ipcServer = new IpcService.IpcServer();
                _ipcServer.StartServer((cmd) =>
                {
                    string command = cmd.Trim().ToUpperInvariant();
                    LoggerService.Log($"[IPC] Comando recebido: '{command}'");
                    Dispatcher.Invoke(() =>
                    {
                        switch (command)
                        {
                            case "SHOW":
                                AppManager.Instance.ShowMainWindow();
                                break;
                            case "START_AUTOCLICK":
                            case "START":
                                AppManager.Instance.StartAutoClick();
                                break;
                            case "STOP_AUTOCLICK":
                            case "STOP":
                                AppManager.Instance.StopAutoClick(AutoClickStopReason.UserButton);
                                break;
                            case "TOGGLE":
                                AppManager.Instance.ToggleAutoClick(AutoClickStopReason.GlobalHotkey);
                                break;
                            case "PANIC":
                                AppManager.Instance.StopEverything();
                                break;
                            default:
                                AppManager.Instance.ShowMainWindow();
                                break;
                        }
                    });
                });

                // 7. Instantiate Main Window first
                LoggerService.Log("[STARTUP] Criando MainWindow");
                var mainWindow = new MainWindow();
                this.MainWindow = mainWindow;
                LoggerService.Log("[STARTUP] MainWindow criada");

                // 8. Initialize Central App Manager & Attach Window
                LoggerService.Log("[STARTUP] AppManager iniciando");
                var appManager = AppManager.Instance;
                appManager.MainWindowInstance = mainWindow;

                // 9. Display Main Window immediately on first launch
                if (appManager.SettingsService.CurrentSettings.StartMinimized)
                {
                    LoggerService.Log("[STARTUP] Configurado para iniciar minimizado na bandeja");
                }
                else
                {
                    LoggerService.Log("[STARTUP] Exibindo MainWindow na primeira execução");
                    appManager.ShowMainWindow();
                }

                // 10. Initialize System Tray Icon
                appManager.TrayIconService.Initialize();
                LoggerService.Log("[STARTUP] TrayIcon inicializado com sucesso");

                LoggerService.Log("[STARTUP] Aplicação pronta e operacional");
            }
            catch (Exception ex)
            {
                LoggerService.LogException("Fatal error during OnStartup", ex);
                MessageBox.Show(
                    $"Erro crítico ao iniciar o Gabriel Autoclick:\n{ex.Message}\n\nDetalhes salvos em: {Path.Combine(Constants.AppDataFolder, "logs", "latest.log")}",
                    "Gabriel Autoclick - Erro",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                Shutdown();
            }
        }

        private void SetupExceptionHandling()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    LoggerService.LogException("AppDomain.UnhandledException", ex);
                }
            };

            DispatcherUnhandledException += (s, args) =>
            {
                LoggerService.LogException("DispatcherUnhandledException", args.Exception);
                args.Handled = true;
            };

            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                LoggerService.LogException("TaskScheduler.UnobservedTaskException", args.Exception);
                args.SetObserved();
            };
        }

        protected override void OnExit(ExitEventArgs e)
        {
            LoggerService.Log("App.OnExit executing");
            try
            {
                _ipcServer?.Dispose();
            }
            catch { }

            base.OnExit(e);
        }
    }
}
