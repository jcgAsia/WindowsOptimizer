using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;

namespace WindowsOptimizer.Services
{
    public class MonitorLogService
    {
        private static readonly Lazy<MonitorLogService> _instance = new Lazy<MonitorLogService>(() => new MonitorLogService());
        public static MonitorLogService Instance => _instance.Value;

        private readonly HttpClient _http;
        private const string MonitorUrl = "https://wo-monitor.vercel.app/api/logs";
        private const string ApiKey = "wo-monitor-2026-jcgasia";

        private MonitorLogService()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            _http.DefaultRequestHeaders.Add("User-Agent", "WindowsOptimizer");
            _http.DefaultRequestHeaders.Add("x-api-key", ApiKey);
        }

        public string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

        /// <summary>
        /// 모니터링 서버에 로그 전송 (fire-and-forget)
        /// </summary>
        public async Task SendAsync(string action, bool success = true, string detail = null)
        {
            try
            {
                var json = $"{{\"pid\":\"{Escape(GlobalConfig.Pid)}\",\"action\":\"{Escape(action)}\",\"mac_address\":\"{Escape(GlobalConfig.MacAddress)}\",\"version\":\"{Escape(CurrentVersion)}\",\"success\":{(success ? "true" : "false")},\"detail\":\"{Escape(detail)}\"}}";
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                await _http.PostAsync(MonitorUrl, content);
            }
            catch { } // 모니터링 실패는 앱 동작에 영향 없음
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
