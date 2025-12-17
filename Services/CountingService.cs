using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace WindowsOptimizer.Services
{
    public class CountingService
    {
        private static readonly Lazy<CountingService> _instance = new Lazy<CountingService>(() => new CountingService());
        public static CountingService Instance => _instance.Value;

        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        private CountingService() { }

        /// <summary>
        /// 설치 시 카운팅 로그
        /// </summary>
        public async Task LogInstallAsync()
        {
            await SendCountingAsync("install");
        }

        /// <summary>
        /// 언인스톨 시 카운팅 로그
        /// </summary>
        public async Task LogUninstallAsync()
        {
            await SendCountingAsync("uninstall");
        }

        /// <summary>
        /// 로딩(실행) 시 카운팅 로그
        /// </summary>
        public async Task LogLoadingAsync()
        {
            await SendCountingAsync("loading");
        }

        private async Task SendCountingAsync(string eventType)
        {
            try
            {
                var url = $"{GlobalConfig.CountingBaseUrl}?event={eventType}&pid={GlobalConfig.Pid}&mac={Uri.EscapeDataString(GlobalConfig.MacAddress)}&v={UpdateService.Instance.CurrentVersion}";
                var response = await _http.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    LogService.Instance.Log($"[Counting] {eventType} 로그 전송 완료 (PID:{GlobalConfig.Pid})");
                }
                else
                {
                    LogService.Instance.Log($"[Counting] {eventType} 로그 전송 실패: HTTP {(int)response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[Counting] {eventType} 로그 전송 실패: {ex.Message}");
            }
        }
    }
}
