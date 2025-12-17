using System;
using System.IO;

namespace WindowsOptimizer.Services
{
    public class LogService
    {
        private static readonly Lazy<LogService> _instance = new Lazy<LogService>(() => new LogService());
        public static LogService Instance => _instance.Value;

        private readonly string _logDir;
        private readonly object _lock = new object();

        public event Action<string> LogAdded;
        public string LogDirectory => _logDir;

        private LogService()
        {
            _logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                   GlobalConfig.AppFolderName, "logs");
            try { Directory.CreateDirectory(_logDir); } catch { }
        }

        public void Log(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";

            lock (_lock)
            {
                try
                {
                    var path = Path.Combine(_logDir, $"app_{DateTime.Now:yyyyMMdd}.log");
                    File.AppendAllText(path, line + Environment.NewLine);
                }
                catch { }
            }

            Console.WriteLine(line);
            LogAdded?.Invoke(line);
        }
    }
}
