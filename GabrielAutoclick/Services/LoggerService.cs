using System;
using System.IO;
using System.Text;
using System.Windows.Markup;
using GabrielAutoclick.Helpers;

namespace GabrielAutoclick.Services
{
    public static class LoggerService
    {
        private static readonly object _lock = new object();
        private static string? _logFilePath;
        private static bool _initialized = false;
        private static int _clickCountInBatch = 0;

        public static void Initialize()
        {
            if (_initialized) return;

            try
            {
                string logDir = Path.Combine(Constants.AppDataFolder, "logs");
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                _logFilePath = Path.Combine(logDir, "latest.log");

                // If existing log > 5MB, clean up
                if (File.Exists(_logFilePath))
                {
                    var fi = new FileInfo(_logFilePath);
                    if (fi.Length > 5 * 1024 * 1024)
                    {
                        File.Delete(_logFilePath);
                    }
                }

                _initialized = true;
                Log("========================================");
                Log($"Application session started - {Constants.AppName} v{Constants.AppVersion}");
                Log($"OS: {Environment.OSVersion}, .NET: {Environment.Version}, 64-bit: {Environment.Is64BitProcess}");
                Log("========================================");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Logger initialization error: {ex.Message}");
            }
        }

        public static void Log(string message)
        {
            try
            {
                string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
                System.Diagnostics.Debug.WriteLine(line);

                if (_logFilePath != null)
                {
                    lock (_lock)
                    {
                        File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
                    }
                }
            }
            catch { }
        }

        public static void LogClick(long totalClicks, int x, int y, uint result, int win32Error)
        {
            _clickCountInBatch++;
            if (_clickCountInBatch == 1 || _clickCountInBatch % 50 == 0 || result == 0)
            {
                if (result == 0)
                {
                    Log($"[SendInput FAIL] Click #{totalClicks} at ({x}, {y}) returned 0 (Win32 Error: {win32Error})");
                }
                else
                {
                    Log($"Click #{totalClicks} executed at ({x}, {y}) [SendInput OK]");
                }
            }
        }

        public static void LogException(string context, Exception ex)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[CRITICAL EXCEPTION] {context}");

            Exception? current = ex;
            int level = 0;
            while (current != null)
            {
                sb.AppendLine($"--- Exception Level {level} [{current.GetType().FullName}] ---");
                sb.AppendLine($"Message: {current.Message}");
                if (current is XamlParseException xamlEx)
                {
                    sb.AppendLine($"XamlParseException Details -> Line: {xamlEx.LineNumber}, Position: {xamlEx.LinePosition}, BaseUri: {xamlEx.BaseUri}, KeyContext: {xamlEx.KeyContext}");
                }
                sb.AppendLine($"StackTrace:\n{current.StackTrace}");
                sb.AppendLine($"Full ToString:\n{current}");
                current = current.InnerException;
                level++;
            }

            Log(sb.ToString());
        }
    }
}
