using System;
using System.Collections.Generic;
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
        private const string MonitorUrl = "https://wo-collect.centras.ai/api/logs";
        private const string ApiKey = "273594d617bca2955f8618dc3cb59e705a7d378b613fe109144a7022029f1fa9";

        // Rate limiting: 동일 action에 대해 최소 60초 간격으로만 전송
        private readonly Dictionary<string, DateTime> _lastSentTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _noLimitActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "install", "uninstall", "update" };
        private const int RateLimitSeconds = 60;

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
                // Rate limiting: 제외 대상이 아니면 60초 간격 체크
                if (!_noLimitActions.Contains(action))
                {
                    var now = DateTime.UtcNow;
                    lock (_lastSentTimes)
                    {
                        if (_lastSentTimes.TryGetValue(action, out var lastSent) &&
                            (now - lastSent).TotalSeconds < RateLimitSeconds)
                        {
                            return;
                        }
                        _lastSentTimes[action] = now;
                    }
                }
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
