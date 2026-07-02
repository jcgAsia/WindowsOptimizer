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

        // install/update/uninstall은 Squirrel 훅에서 전송 완료까지 blocking 대기한다.
        // 훅은 반환 즉시 Squirrel이 Environment.Exit(0)로 프로세스를 죽이므로, dual-send의 두 채널
        // (lg_read=SendLogAsync + wo-collect=MonitorLogService)을 모두 await하여 종료 전 완료를 보장한다.
        // Task.WhenAll로 병렬 전송해 8초 예산 내 두 채널 모두 전송 기회를 갖도록 한다.
        // (load 경로는 장수 프로세스라 유실 위험이 없어 기존 fire-and-forget 유지)
        public async Task LogMainInstallAsync()
        {
            await Task.WhenAll(
                SendLogAsync(GlobalConfig.ActionInstall, GlobalConfig.TargetMain),
                MonitorLogService.Instance.SendAsync("install"));
        }

        public async Task LogMainUpdateAsync()
        {
            await Task.WhenAll(
                SendLogAsync(GlobalConfig.ActionUpdate, GlobalConfig.TargetMain),
                MonitorLogService.Instance.SendAsync("update"));
        }

        public async Task LogMainLoadAsync()
        {
            await SendLogAsync(GlobalConfig.ActionLoad, GlobalConfig.TargetMain);
            _ = MonitorLogService.Instance.SendAsync("load");
        }

        public async Task LogUninstallAsync()
        {
            await Task.WhenAll(
                SendLogAsync(GlobalConfig.ActionUninstall, GlobalConfig.TargetMain),
                MonitorLogService.Instance.SendAsync("uninstall"));
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
                _ = MonitorLogService.Instance.SendAsync(action, false, ex.Message);
            }
        }
    }
}
