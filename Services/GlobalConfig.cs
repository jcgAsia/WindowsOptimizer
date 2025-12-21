using Microsoft.Win32;
using System;
using System.Net.NetworkInformation;

namespace WindowsOptimizer.Services
{
    public static class GlobalConfig
    {
        // 파트너 아이디 (빌드 타입에 따라 설정)
        // pb000: Mockup용, pb001: 실제 배포용
#if DEBUG
        public static string Pid { get; private set; } = "pb000";  // Mockup
#else
        public static string Pid { get; private set; } = "pb001";  // Live
#endif
        public static string MacAddress { get; private set; }

        public const string RegSubKey = @"SOFTWARE\WindowsOptimizer";
        public const string AppFolderName = "WindowsOptimizer";
        public const string MutexName = @"Global\WindowsOptimizerMutex";

        // GitHub Releases 기반 업데이트 URL
        public const string GitHubUpdateUrl = "https://jcgasia.github.io/WindowsOptimizer_Updater/";
        public const string MappingUrl = "https://raw.githubusercontent.com/jcgAsia/WindowsOptimizer_Updater/main/mapping.xml";

        // 카운팅 서버 URL (실제 서버 주소로 변경 필요)
        public const string CountingBaseUrl = "https://your-counting-server.com/api/count";

        #region Bustabcc 서버 설정
        // 메인 도메인
        public const string BustabccDomain = "bustabcc.net";

        // 로그 전송 URL (bid 파라미터는 암호화된 쿼리스트링)
        public const string BustabccLogUrl = "https://bustabcc.net/PRG/lg_read.php";

        // XML 업데이트 URL
        public const string BustabccUpdateUrl = "https://bustabcc.net/SWC/ups_read.php";

        // 로그 액션 타입
        public const string ActionInstall = "install";
        public const string ActionUpdate = "update";
        public const string ActionLoad = "load";
        public const string ActionUninstall = "uninstall";

        // 타겟 타입 (0=업데이터, 1=메인)
        public const int TargetUpdater = 0;
        public const int TargetMain = 1;
        #endregion

        public static void Initialize()
        {
            try
            {
                // 레지스트리에 PID가 설정되어 있으면 사용, 없으면 빌드 타입 기본값 유지
                using (var key = Registry.CurrentUser.OpenSubKey(RegSubKey))
                {
                    var regPid = key?.GetValue("pid")?.ToString();
                    if (!string.IsNullOrEmpty(regPid))
                    {
                        Pid = regPid;
                    }
                    // 레지스트리에 없으면 빌드 타입 기본값(pb000/pb001) 유지
                }
            }
            catch { }

            MacAddress = GetMacAddress();
            LogService.Instance.Log($"초기화 완료 - PID:{Pid}, MAC:{MacAddress}");
        }

        /// <summary>
        /// PID 설정 (Mockup/Live 구분용)
        /// </summary>
        public static void SetPid(string pid)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RegSubKey))
                {
                    key?.SetValue("pid", pid);
                    Pid = pid;
                }
                LogService.Instance.Log($"PID 설정 완료: {pid}");
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"PID 설정 실패: {ex.Message}");
            }
        }

        public static void OnLoadingLogQuery()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RegSubKey))
                {
                    if (key == null) return;
                    string today = DateTime.Today.ToString("yyyy-MM-dd");
                    if (key.GetValue("loading", "")?.ToString() != today)
                        key.SetValue("loading", today);
                }
            }
            catch { }
        }

        public static string GetMacAddress()
        {
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus == OperationalStatus.Up &&
                        nic.GetIPProperties().GatewayAddresses.Count > 0)
                        return BitConverter.ToString(nic.GetPhysicalAddress().GetAddressBytes()).Replace("-", ":");
                }
            }
            catch { }
            return "00:00:00:00:00:00";
        }
    }
}
