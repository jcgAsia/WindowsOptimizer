using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace WindowsOptimizer.Services
{
    public class BustabccLoggingService
    {
        private static readonly Lazy<BustabccLoggingService> _instance =
            new Lazy<BustabccLoggingService>(() => new BustabccLoggingService());
        public static BustabccLoggingService Instance => _instance.Value;

        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        private BustabccLoggingService() { }

        public async Task LogUpdaterInstallAsync()
        {
            await SendLogAsync(GlobalConfig.ActionInstall, GlobalConfig.TargetUpdater);
        }

        public async Task LogUpdaterUpdateAsync()
        {
            await SendLogAsync(GlobalConfig.ActionUpdate, GlobalConfig.TargetUpdater);
        }

        public async Task LogUpdaterLoadAsync()
        {
            await SendLogAsync(GlobalConfig.ActionLoad, GlobalConfig.TargetUpdater);
        }

        public async Task LogMainInstallAsync()
        {
            await SendLogAsync(GlobalConfig.ActionInstall, GlobalConfig.TargetMain);
        }

        public async Task LogMainUpdateAsync()
        {
            await SendLogAsync(GlobalConfig.ActionUpdate, GlobalConfig.TargetMain);
        }

        public async Task LogMainLoadAsync()
        {
            await SendLogAsync(GlobalConfig.ActionLoad, GlobalConfig.TargetMain);
        }

        public async Task LogUninstallAsync()
        {
            await SendLogAsync(GlobalConfig.ActionUninstall, GlobalConfig.TargetMain);
        }

        private async Task SendLogAsync(string action, int target)
        {
            try
            {
                var queryString = $"client={GlobalConfig.Pid}" +
                                  $"&action={action}" +
                                  $"&target={target}" +
                                  $"&macadd={GlobalConfig.MacAddress}";

                LogService.Instance.Log($"[Bustabcc] 전송 시작 - {action} (target={target})");
                LogService.Instance.Log($"[Bustabcc] Query: {queryString}");

                var encryptedBid = Xor256CryptoService.Instance.Encrypt(queryString);
                var url = $"{GlobalConfig.BustabccLogUrl}?bid={encryptedBid}";

                var response = await _http.GetAsync(url);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    LogService.Instance.Log($"[Bustabcc] {action} 전송 성공 (HTTP {(int)response.StatusCode})");
                    if (!string.IsNullOrEmpty(responseContent.Replace("\n", string.Empty)))
                        LogService.Instance.Log($"[Bustabcc] 응답: {responseContent}");
                }
                else
                {
                    LogService.Instance.Log($"[Bustabcc] {action} 전송 실패: HTTP {(int)response.StatusCode}");
                    if (!string.IsNullOrEmpty(responseContent.Replace("\n", string.Empty)))
                        LogService.Instance.Log($"[Bustabcc] 응답: {responseContent}");
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[Bustabcc] {action} 전송 오류: {ex.Message}");
                if (ex.InnerException != null)
                    LogService.Instance.Log($"[Bustabcc] 내부 오류: {ex.InnerException.Message}");
            }
        }
    }
}
