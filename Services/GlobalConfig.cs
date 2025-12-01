using System;
using System.Net.Http;
using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace WindowsOptimizer.Services
{
    public enum LogType { Install, Delete, Loading, Update }

    public static class GlobalConfig
    {
        public static string Pid { get; private set; }
        public static string Cpid { get; private set; }
        public static string MacAddress { get; private set; }

        public static string UrlBeg { get; set; } = "http://api.weaping.co.kr/ssi/beg.php?pid=%CLIENTID&cid=%MACADDR";
        public static string UrlLog { get; set; } = "http://api.weaping.co.kr/ssi/log.php?type=%LOGTYPE&cpid=%COMPID&pid=%CLIENTID&cid=%MACADDR";

        public const string RegSubKey = @"SOFTWARE\WindowsOptimizer";
        public const string AppFolderName = "WindowsOptimizer";
        public const string MutexName = @"Global\WindowsOptimizerMutex";

        // SFTP 설정
        public static string SftpHost { get; set; } = "175.207.29.46";
        public static int SftpPort { get; set; } = 22212;
        public static string SftpUser { get; set; } = "rainmaker";
        public static string SftpPass { get; set; } = "dkfkclakfncl!@09";
        public static string SftpBasePath { get; set; } = "/home/rainmaker/planb";
        public static bool UseSftp { get; set; } = true;

        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        public static void Initialize()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegSubKey))
                {
                    if (key != null)
                    {
                        Pid = key.GetValue("pid")?.ToString() ?? "default";
                        Cpid = key.GetValue("cpid")?.ToString() ?? "default";
                    }
                    else
                    {
                        Pid = "default";
                        Cpid = "default";
                    }
                }
            }
            catch
            {
                Pid = "default";
                Cpid = "default";
            }

            MacAddress = GetMacAddress();
            LogService.Instance.Log($"초기화 완료 - PID:{Pid}, MAC:{MacAddress}");
        }

        public static string PackQueryUrl(string url)
        {
            return url.Replace("%CLIENTID", Pid).Replace("%MACADDR", MacAddress);
        }

        public static bool QueryLog(LogType logType)
        {
            string typeStr = logType switch
            {
                LogType.Install => "I",
                LogType.Delete => "D",
                LogType.Loading => "L",
                LogType.Update => "U",
                _ => "L"
            };

            try
            {
                var url = UrlLog.Replace("%LOGTYPE", typeStr).Replace("%COMPID", Cpid);
                url = PackQueryUrl(url);
                _ = _http.GetStringAsync(url).Result;
                return true;
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"로그 전송 실패: {ex.Message}");
                return false;
            }
        }

        public static void OnLoadingLogQuery()
        {
            string today = DateTime.Today.ToString("yyyy-MM-dd");

            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RegSubKey))
                {
                    if (key == null) return;

                    // 업데이트 체크
                    var updateDone = key.GetValue("update_done");
                    if (updateDone != null && Convert.ToInt32(updateDone) == 1)
                    {
                        key.SetValue("update_done", 0);
                        QueryLog(LogType.Update);
                    }

                    // 설치 체크
                    if (key.GetValue("install") != null)
                    {
                        key.DeleteValue("install");
                        QueryLog(LogType.Install);
                    }

                    // 로딩 로그 (하루 1회)
                    string lastRun = key.GetValue("loading", "")?.ToString();
                    if (lastRun != today)
                    {
                        key.SetValue("loading", today);
                        QueryLog(LogType.Loading);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"로딩 로그 오류: {ex.Message}");
            }
        }

        public static bool IsInternetConnected()
        {
            if (!NetworkInterface.GetIsNetworkAvailable()) return false;

            try
            {
                using (var ping = new Ping())
                {
                    var reply = ping.Send("8.8.8.8", 3000);
                    return reply.Status == IPStatus.Success;
                }
            }
            catch { return false; }
        }

        public static string GetMacAddress()
        {
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus == OperationalStatus.Up &&
                        (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                         nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) &&
                        nic.GetIPProperties().GatewayAddresses.Count > 0)
                    {
                        var bytes = nic.GetPhysicalAddress().GetAddressBytes();
                        return BitConverter.ToString(bytes).Replace("-", ":");
                    }
                }
            }
            catch { }
            return "00:00:00:00:00:00";
        }
    }
}
