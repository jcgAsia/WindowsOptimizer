using Microsoft.Win32;
using System;
using System.Net.NetworkInformation;

namespace WindowsOptimizer.Services
{
    public static class GlobalConfig
    {
        public static string Pid { get; private set; } = "default";
        public static string MacAddress { get; private set; }

        public const string RegSubKey = @"SOFTWARE\WindowsOptimizer";
        public const string AppFolderName = "WindowsOptimizer";
        public const string MutexName = @"Global\WindowsOptimizerMutex";

        // GitHub Releases 기반 업데이트 URL (Settings -> Pages -> Branch -> main (root) -> save
        public const string GitHubUpdateUrl = "https://jcgasia.github.io/WindowsOptimizer_Updater/";
        public const string MappingUrl = "https://raw.githubusercontent.com/jcgAsia/WindowsOptimizer_Updater/main/mapping.xml";

        public static void Initialize()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegSubKey))
                    Pid = key?.GetValue("pid")?.ToString() ?? "default";
            }
            catch { }

            MacAddress = GetMacAddress();
            LogService.Instance.Log($"초기화 완료 - PID:{Pid}");
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
