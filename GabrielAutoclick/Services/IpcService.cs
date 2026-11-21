using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GabrielAutoclick.Services
{
    public static class IpcService
    {
        public const string PipeName = "GabrielAutoclick_IPC_Pipe_v1";

        /// <summary>
        /// Sends a command to an existing instance of Gabriel Autoclick via Named Pipe.
        /// Returns true if successfully delivered, false if no server is listening.
        /// </summary>
        public static bool SendSignalToExistingInstance(string message = "SHOW", int timeoutMs = 600)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(timeoutMs);

                using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
                writer.WriteLine(message);

                LoggerService.Log($"[IPC CLIENT] Sinal '{message}' enviado com sucesso.");
                return true;
            }
            catch (TimeoutException)
            {
                // No server listening within timeout
                return false;
            }
            catch (Exception ex)
            {
                LoggerService.Log($"[IPC CLIENT] Erro ao conectar ao pipe: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Background Named Pipe Server listening for commands from secondary instances.
        /// </summary>
        public class IpcServer : IDisposable
        {
            private CancellationTokenSource? _cts;
            private bool _isDisposed = false;

            public void StartServer(Action<string> onMessageReceived)
            {
                if (_cts != null) return;

                _cts = new CancellationTokenSource();
                Task.Run(() => ServerLoop(onMessageReceived, _cts.Token));
                LoggerService.Log("[IPC SERVER] Servidor IPC iniciado no canal: " + PipeName);
            }

            private async Task ServerLoop(Action<string> onMessageReceived, CancellationToken ct)
            {
                while (!ct.IsCancellationRequested)
                {
                    NamedPipeServerStream? server = null;
                    try
                    {
                        server = new NamedPipeServerStream(
                            PipeName,
                            PipeDirection.In,
                            1,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous
                        );

                        await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                        using var reader = new StreamReader(server, Encoding.UTF8);
                        string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);

                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            LoggerService.Log($"[IPC SERVER] Comando recebido: '{line}'");
                            onMessageReceived?.Invoke(line);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        LoggerService.Log($"[IPC SERVER] Exceção no loop do servidor: {ex.Message}");
                        await Task.Delay(200, ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        try
                        {
                            if (server != null)
                            {
                                if (server.IsConnected)
                                {
                                    server.Disconnect();
                                }
                                server.Dispose();
                            }
                        }
                        catch { }
                    }
                }
            }

            public void Dispose()
            {
                if (!_isDisposed)
                {
                    _cts?.Cancel();
                    _cts?.Dispose();
                    _cts = null;
                    _isDisposed = true;
                    LoggerService.Log("[IPC SERVER] Servidor IPC encerrado.");
                }
                GC.SuppressFinalize(this);
            }
        }
    }
}
