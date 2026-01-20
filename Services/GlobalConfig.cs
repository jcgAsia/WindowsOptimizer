using Microsoft.Win32;
using System;
using System.Net.NetworkInformation;

namespace WindowsOptimizer.Services
{
    public static class GlobalConfig
    {
#if DEBUG
        public static string Pid { get; private set; } = "pb000";
#else
        public static string Pid { get; private set; } = "pb001";
#endif
        public static string MacAddress { get; private set; }

        // 설치 모드: "Mockup" (UI 있음) / "Execute" (UI 없음, 기본값)
        public const string InstallModeMockup = "Mockup";
        public const string InstallModeExecute = "Execute";
        public static string InstallMode { get; private set; } = InstallModeExecute; // 기본값: UI 없음
        public static bool IsExecuteMode => InstallMode == InstallModeExecute;

        // 런타임 UI 표시 플래그 (-ui 인자 또는 핫키로 활성화)
        public static bool ShowUIOverride { get; set; } = false;
#if DEBUG
        public static bool ShouldShowUI => true;
#else
        public static bool ShouldShowUI => ShowUIOverride || !IsExecuteMode;
#endif

        public const string RegSubKey = @"SOFTWARE\WindowsOptimizer";
        public const string AppFolderName = "WindowsOptimizer";
        public const string MutexName = @"Global\WindowsOptimizerMutex";

        // GitHub Releases 기반 업데이트 URL
        public const string GitHubUpdateUrl = "https://jcgasia.github.io/WindowsOptimizer_Updater/";
        public const string MappingUrl = "https://raw.githubusercontent.com/jcgAsia/WindowsOptimizer_Updater/main/mapping.xml";

        // 카운팅 서버 URL (실제 서버 주소로 변경 필요)
        public const string CountingBaseUrl = "https://your-counting-server.com/api/count";

        // Bustabcc 서버 설정
        public const string BustabccLogUrl = "https://bustabcc.net/PRG/lg_read.php";
        public const string ActionInstall = "install";
        public const string ActionUpdate = "update";
        public const string ActionLoad = "load";
        public const string ActionUninstall = "uninstall";
        public const int TargetUpdater = 0;
        public const int TargetMain = 1;

        // 토스트 팝업 설정
        public const string ToastDefaultUrl = "https://www.bustabcc.net/CARD/card.php";
        public const int ToastPopupWidth = 300;
        public const int ToastPopupHeight = 250;

        // 런처 연동 설정
        public static bool LaunchedByLauncher { get; set; } = false;
        public static string LauncherVersion { get; set; }

        public static void Initialize()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegSubKey))
                {
                    var regPid = key?.GetValue("pid")?.ToString();
                    if (!string.IsNullOrEmpty(regPid))
                        Pid = regPid;

                    // 설치 모드 로드
                    var regInstallMode = key?.GetValue("InstallMode")?.ToString();
                    if (!string.IsNullOrEmpty(regInstallMode))
                        InstallMode = regInstallMode;
                }
            }
            catch { }

            MacAddress = GetMacAddress();
            LogService.Instance.Log($"초기화 완료 - PID:{Pid}, MAC:{MacAddress}, Mode:{InstallMode}");
        }

        /// <summary>
        /// 설치 모드 설정 (설치 시 호출)
        /// </summary>
        public static void SetInstallMode(string mode)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RegSubKey))
                {
                    key?.SetValue("InstallMode", mode);
                    InstallMode = mode;
                }
                LogService.Instance.Log($"설치 모드 설정: {mode}");
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"설치 모드 설정 실패: {ex.Message}");
            }
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
