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

        // install/update는 지연전송 경로에서 호출된다(LogSenderService 백그라운드 루프 — 정상 장수 프로세스).
        //  - 반환값 = lg_read(Bustabcc) HTTP 2xx 여부. 호출부(sender)가 이 값이 true일 때만 플래그를 소비한다.
        //    (실패 시 플래그 유지 → 세션 내 백오프 재시도 + 다음 실행 재시도. 런처 Program.cs와 동일 원칙:
        //     소비는 lg_read 결과로만 판단.)
        //  - wo-collect(MonitorLogService)는 lg_read가 2xx로 성공한 "그 부팅"에서만 뒤이어 1회 발사한다(fire-and-forget).
        //    (과거엔 무조건 먼저 발사했으나, lg_read 실패로 플래그가 유지되어 재시도할 때마다 wo-collect가 또
        //     발사되어 wo-monitor 대시보드에 중복 집계됐다. install/update는 rate-limit 예외라 억제도 안 됨.
        //     → lg_read 성공=플래그 소비=이벤트당 1회이므로, 그 시점에만 발사하면 재시도 부팅엔 미발사 → 중복 제거.)
        //  - lg_read 실패 시엔 wo-collect를 아예 발사하지 않으므로, 실패 리포트도 보내지 않는다(reportWoCollectError:false)
        //    → wo-collect의 성공/실패 이벤트 경합을 방지.
        public async Task<bool> LogMainInstallAsync()
        {
            bool lgOk = await SendLogAsync(GlobalConfig.ActionInstall, GlobalConfig.TargetMain, reportWoCollectError: false);
            if (lgOk) { _ = MonitorLogService.Instance.SendAsync("install"); }
            return lgOk;
        }

        public async Task<bool> LogMainUpdateAsync()
        {
            bool lgOk = await SendLogAsync(GlobalConfig.ActionUpdate, GlobalConfig.TargetMain, reportWoCollectError: false);
            if (lgOk) { _ = MonitorLogService.Instance.SendAsync("update"); }
            return lgOk;
        }

        // load는 outbox 경로에서 호출된다(LogSenderService가 유일한 호출자).
        //  - lg_read만 전송하고 2xx 여부(bool)를 반환한다. sender가 true일 때만 outbox 엔트리를 소비하고
        //    wo-collect를 같은 eventId로 뒤이어 1회 발사한다(install/update와 동일한 "성공 시에만" 원칙).
        //    (과거엔 여기서 반환값을 버리고 wo-collect를 무조건 발사했으나, outbox 재시도 구조에서는
        //     재시도마다 wo-collect가 중복 발사되므로 발사 책임을 sender로 옮겼다.)
        //  - 실패 리포트도 보내지 않는다(reportWoCollectError:false) — 백오프 재시도마다 실패 이벤트가
        //    쌓이는 것을 방지(load의 wo-collect는 lg_read 성공 시 1회가 전부).
        public async Task<bool> LogMainLoadAsync()
        {
            return await SendLogAsync(GlobalConfig.ActionLoad, GlobalConfig.TargetMain, reportWoCollectError: false);
        }

        // uninstall은 "다음 실행"이 없어 지연전송 불가 → Squirrel 훅에서 blocking(17초)으로 완료를 기다린다.
        // dual-send 두 채널(lg_read + wo-collect)을 Task.WhenAll로 모두 대기해 종료 전 완료 기회를 확보한다.
        // eventId: 훅(OnAppUninstall)이 1개 생성해 양채널에 동일하게 부여하는 상관관계 ID.
        //   단, lg_read 와이어는 동결이라 실제로는 wo-collect payload("event_id")에만 실린다.
        public async Task LogUninstallAsync(string eventId = null)
        {
            await Task.WhenAll(
                SendLogAsync(GlobalConfig.ActionUninstall, GlobalConfig.TargetMain),
                MonitorLogService.Instance.SendAsync("uninstall", eventId: eventId));
        }

        // 반환: lg_read(Bustabcc) HTTP 2xx면 true, 비2xx/예외면 false.
        // reportWoCollectError: 예외 시 wo-collect에 실패 리포트를 보낼지 여부(기본 true=uninstall 기존 동작 유지).
        //   재시도 경로(install/update/load)는 wo-collect를 성공 시에만 별도 발사하므로 false로 호출해
        //   재시도마다 실패 이벤트가 중복 적재되는 것을 막는다.
        // 와이어 포맷(queryString 조립/순서, XOR256, URL)은 단 한 글자도 변경하지 않는다.
        private async Task<bool> SendLogAsync(string action, int target, bool reportWoCollectError = true)
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
                    return true;
                }
                else
                {
                    LogService.Instance.Log($"[Bustabcc] {action} 전송 실패: HTTP {(int)response.StatusCode}");
                    if (!string.IsNullOrEmpty(responseContent.Replace("\n", string.Empty)))
                        LogService.Instance.Log($"[Bustabcc] 응답: {responseContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[Bustabcc] {action} 전송 오류: {ex.Message}");
                if (ex.InnerException != null)
                    LogService.Instance.Log($"[Bustabcc] 내부 오류: {ex.InnerException.Message}");
                if (reportWoCollectError)
                    _ = MonitorLogService.Instance.SendAsync(action, false, ex.Message);
                return false;
            }
        }
    }
}
