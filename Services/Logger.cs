using Microsoft.Extensions.Configuration;
using System;
using System.IO;

namespace AlarmaDisparadorCore.Services
{
    public static class Logger
    {
        private static readonly object _lock = new();
        private static readonly string _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.log");
        private const int MaxLines = 200;
        private static bool _consoleEnabled;

        public static void Configure(IConfiguration configuration)
        {
            if (configuration == null)
            {
                _consoleEnabled = false;
                return;
            }

            var section = configuration.GetSection("Logger");
            _consoleEnabled = section.GetValue("ConsoleEnabled", false);
        }

        public static void Log(string message)
        {
            try
            {
                lock (_lock)
                {
                    var line = $"{DateTime.Now:O} {message}";
                    File.AppendAllText(_logPath, line + Environment.NewLine);
                    Truncate();
                    if (_consoleEnabled)
                    {
                        Console.WriteLine(line);
                    }
                }
            }
            catch
            {
                // ignore logging failures
            }
        }

        public static void LogError(Exception ex, string context)
        {
            Log($"ERROR in {context}: {ex.Message} {ex.StackTrace}");
        }

        private static void Truncate()
        {
            try
            {
                var lines = File.ReadAllLines(_logPath);
                if (lines.Length > MaxLines)
                {
                    var lastLines = lines[^MaxLines..];
                    File.WriteAllLines(_logPath, lastLines);
                }
            }
            catch
            {
                // ignore truncation failures
            }
        }
    }
}
