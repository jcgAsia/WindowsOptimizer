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

            // Console 색상 출력
            PrintColoredLog(line, message);
            LogAdded?.Invoke(line);
        }

        private void PrintColoredLog(string line, string message)
        {
            var prevColor = Console.ForegroundColor;
            
            if (message.Contains("[PlanB] 도메인 매칭") || message.Contains("트리거"))
                Console.ForegroundColor = ConsoleColor.Green;
            else if (message.Contains("URL 변경"))
                Console.ForegroundColor = ConsoleColor.DarkYellow;
            else if (message.Contains("백그라운드 탭") || message.Contains("열기 완료"))
                Console.ForegroundColor = ConsoleColor.Cyan;
            else if (message.Contains("오류") || message.Contains("실패"))
                Console.ForegroundColor = ConsoleColor.Red;
            else if (message.Contains("시작") || message.Contains("중지"))
                Console.ForegroundColor = ConsoleColor.Yellow;
            else if (message.Contains("매핑 로드") || message.Contains("설정"))
                Console.ForegroundColor = ConsoleColor.DarkGreen;
            else
                Console.ForegroundColor = ConsoleColor.Gray;

            Console.WriteLine(line);
            Console.ForegroundColor = prevColor;
        }
    }
}
