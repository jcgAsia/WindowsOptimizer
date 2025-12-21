using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace WindowsOptimizer.Services
{
    /// <summary>
    /// bustabcc.net 서버로 로그를 전송하는 서비스
    /// URL 형식: https://bustabcc.net/PRG/lg_read.php?bid=%BID
    /// %BID에는 XOR256 암호화된 쿼리스트링이 들어감
    /// </summary>
    public class BustabccLoggingService
    {
        private static readonly Lazy<BustabccLoggingService> _instance =
            new Lazy<BustabccLoggingService>(() => new BustabccLoggingService());
        public static BustabccLoggingService Instance => _instance.Value;

        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        // 로그 전송 결과 이벤트
        public event Action<bool, string, string> LogSent; // success, action, message

        // 마지막 전송 정보
        public DateTime LastSentTime { get; private set; } = DateTime.MinValue;
        public bool LastSentSuccess { get; private set; }
        public string LastSentAction { get; private set; } = "";

        private BustabccLoggingService() { }

        #region 업데이터 로그 (target=0)

        /// <summary>
        /// 업데이터 설치 로그
        /// client=%CLIENTID&action=install&target=0&macadd=%MAC
        /// </summary>
        public async Task LogUpdaterInstallAsync()
        {
            await SendLogAsync(GlobalConfig.ActionInstall, GlobalConfig.TargetUpdater);
        }

        /// <summary>
        /// 업데이터 업데이트 로그
        /// client=%CLIENTID&action=update&target=0&macadd=%MAC
        /// </summary>
        public async Task LogUpdaterUpdateAsync()
        {
            await SendLogAsync(GlobalConfig.ActionUpdate, GlobalConfig.TargetUpdater);
        }

        /// <summary>
        /// 업데이터 로딩 로그
        /// client=%CLIENTID&action=load&target=0&macadd=%MAC
        /// </summary>
        public async Task LogUpdaterLoadAsync()
        {
            await SendLogAsync(GlobalConfig.ActionLoad, GlobalConfig.TargetUpdater);
        }

        #endregion

        #region 메인 로그 (target=1)

        /// <summary>
        /// 메인 설치 로그
        /// client=%CLIENTID&action=install&target=1&macadd=%MAC
        /// </summary>
        public async Task LogMainInstallAsync()
        {
            await SendLogAsync(GlobalConfig.ActionInstall, GlobalConfig.TargetMain);
        }

        /// <summary>
        /// 메인 업데이트 로그
        /// client=%CLIENTID&action=update&target=1&macadd=%MAC
        /// </summary>
        public async Task LogMainUpdateAsync()
        {
            await SendLogAsync(GlobalConfig.ActionUpdate, GlobalConfig.TargetMain);
        }

        /// <summary>
        /// 메인 로딩 로그
        /// client=%CLIENTID&action=load&target=1&macadd=%MAC
        /// </summary>
        public async Task LogMainLoadAsync()
        {
            await SendLogAsync(GlobalConfig.ActionLoad, GlobalConfig.TargetMain);
        }

        #endregion

        #region 언인스톨 로그

        /// <summary>
        /// 삭제 로그
        /// client=%CLIENTID&action=uninstall&target=0&macadd=%MAC
        /// </summary>
        public async Task LogUninstallAsync()
        {
            await SendLogAsync(GlobalConfig.ActionUninstall, GlobalConfig.TargetUpdater);
        }

        #endregion

        /// <summary>
        /// 로그 전송 - 쿼리스트링을 XOR256 암호화하여 bid 파라미터로 전송
        /// </summary>
        /// <param name="action">액션 타입 (install, update, load, uninstall)</param>
        /// <param name="target">타겟 타입 (0=업데이터, 1=메인)</param>
        private async Task SendLogAsync(string action, int target)
        {
            try
            {
                // 쿼리스트링 생성
                // client=%CLIENTID&action=xxx&target=xxx&macadd=%MAC
                var queryString = $"client={GlobalConfig.Pid}" +
                                  $"&action={action}" +
                                  $"&target={target}" +
                                  $"&macadd={GlobalConfig.MacAddress}";

                // XOR256 암호화 (HEX 문자열 반환)
                var encryptedBid = Xor256CryptoService.Instance.Encrypt(queryString);

                // URL 생성
                var url = $"{GlobalConfig.BustabccLogUrl}?bid={encryptedBid}";

                LogService.Instance.Log($"[Bustabcc] 로그 전송 시도: action={action}, target={target}");
                LogService.Instance.Log($"[Bustabcc] URL: {url}");

                var response = await _http.GetAsync(url);

                LastSentTime = DateTime.Now;
                LastSentAction = action;

                if (response.IsSuccessStatusCode)
                {
                    LastSentSuccess = true;
                    var msg = $"전송 성공: {action} (target={target})";
                    LogService.Instance.Log($"[Bustabcc] {msg}");
                    LogSent?.Invoke(true, action, msg);
                }
                else
                {
                    LastSentSuccess = false;
                    var msg = $"전송 실패: HTTP {(int)response.StatusCode}";
                    LogService.Instance.Log($"[Bustabcc] {msg}");
                    LogSent?.Invoke(false, action, msg);
                }
            }
            catch (Exception ex)
            {
                LastSentTime = DateTime.Now;
                LastSentAction = action;
                LastSentSuccess = false;
                var msg = $"전송 오류: {ex.Message}";
                LogService.Instance.Log($"[Bustabcc] {msg}");
                LogSent?.Invoke(false, action, msg);
            }
        }

        /// <summary>
        /// XOR256 복호화 (디버깅용)
        /// </summary>
        public string DecryptBid(string encryptedHex)
        {
            return Xor256CryptoService.Instance.Decrypt(encryptedHex);
        }
    }
}
