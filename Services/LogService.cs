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
        private string _lastLogDate;

        // 로그 파일 보관 일수
        private const int LOG_RETENTION_DAYS = 3;

        public event Action<string> LogAdded;
        public string LogDirectory => _logDir;

        private LogService()
        {
            _logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                   GlobalConfig.AppFolderName, "logs");
            try { Directory.CreateDirectory(_logDir); } catch { }

            _lastLogDate = DateTime.Now.ToString("yyyyMMdd");
            CleanupOldLogs();
        }

        public void Log(string message)
        {
            var now = DateTime.Now;
            var today = now.ToString("yyyyMMdd");
            var line = $"[{now:HH:mm:ss}] {message}";

            // 날짜가 바뀌면 오래된 로그 정리
            if (_lastLogDate != today)
            {
                _lastLogDate = today;
                CleanupOldLogs();
            }

            lock (_lock)
            {
                try
                {
                    var path = Path.Combine(_logDir, $"app_{today}.log");
                    File.AppendAllText(path, line + Environment.NewLine);
                }
                catch { }
            }

            Console.WriteLine(line);
            LogAdded?.Invoke(line);
        }

        /// <summary>
        /// 보관 기간(3일)보다 오래된 로그 파일을 삭제
        /// </summary>
        private void CleanupOldLogs()
        {
            try
            {
                var cutoffDate = DateTime.Today.AddDays(-LOG_RETENTION_DAYS);
                var logFiles = Directory.GetFiles(_logDir, "app_*.log");

                foreach (var file in logFiles)
                {
                    try
                    {
                        // 파일명에서 날짜 파싱: app_yyyyMMdd.log
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        var dateStr = fileName.Replace("app_", "");

                        if (DateTime.TryParseExact(dateStr, "yyyyMMdd", null,
                            System.Globalization.DateTimeStyles.None, out var fileDate))
                        {
                            if (fileDate < cutoffDate)
                            {
                                File.Delete(file);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
