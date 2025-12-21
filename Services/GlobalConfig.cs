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

        public static void Initialize()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegSubKey))
                {
                    var regPid = key?.GetValue("pid")?.ToString();
                    if (!string.IsNullOrEmpty(regPid))
                        Pid = regPid;
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
