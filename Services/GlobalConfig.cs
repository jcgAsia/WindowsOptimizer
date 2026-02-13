using Microsoft.Win32;
using System;
using System.IO;
using System.Net.NetworkInformation;
using System.Reflection;

namespace WindowsOptimizer.Services
{
    public static class GlobalConfig
    {
        public static string Pid { get; private set; } = "pb001"; // 기본값: 배포용
        public static string MacAddress { get; private set; }

        // 설치 모드: "Mockup" (UI 있음) / "Execute" (UI 없음, 기본값)
        public const string InstallModeMockup = "Mockup";
        public const string InstallModeExecute = "Execute";
        public static string InstallMode { get; private set; } = InstallModeExecute; // 기본값: UI 없음
        public static bool IsExecuteMode => InstallMode == InstallModeExecute;

        // 런타임 UI 표시 플래그 (-ui 인자 또는 핫키로 활성화)
        public static bool ShowUIOverride { get; set; } = false;
        public static bool ShouldShowUI => ShowUIOverride || !IsExecuteMode;

        public const string RegSubKey = @"SOFTWARE\WindowsOptimizer";
        public const string AppFolderName = "WindowsOptimizer";
        public const string MutexName = @"Global\WindowsOptimizerMutex";

        // GitHub Releases 기반 업데이트 URL
        public const string GitHubUpdateUrl = "https://jcgasia.github.io/WindowsOptimizer_Updater/";

        // Pid별 Mapping URL
        private const string MappingUrlBase = "https://raw.githubusercontent.com/jcgAsia/WindowsOptimizer_Updater/main/";
        public static string MappingUrl => Pid == "pb000"
            ? MappingUrlBase + "mapping_pb000.xml"
            : MappingUrlBase + "mapping.xml";

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
                using (var key = Registry.CurrentUser.CreateSubKey(RegSubKey))
                {
                    if (key != null)
                    {
                        var regPid = key.GetValue("pid")?.ToString();
                        var lastPidTxt = key.GetValue("last_pid_txt")?.ToString();
                        var autoUpdate = key.GetValue("auto_update");
                        var currentPidTxt = ReadPidFromFile();

                        if (autoUpdate != null)
                        {
                            // 자동 업데이트 직후: PID 유지, 마커 삭제, pid.txt 스냅샷 갱신
                            key.DeleteValue("auto_update", false);
                            if (!string.IsNullOrEmpty(currentPidTxt))
                                key.SetValue("last_pid_txt", currentPidTxt);
                            if (!string.IsNullOrEmpty(regPid))
                                Pid = regPid;
                        }
                        else if (!string.IsNullOrEmpty(currentPidTxt) && currentPidTxt != lastPidTxt)
                        {
                            // pid.txt가 변경됨 (재설치 또는 최초 설치) → PID 갱신
                            Pid = currentPidTxt;
                            key.SetValue("pid", currentPidTxt);
                            key.SetValue("last_pid_txt", currentPidTxt);
                        }
                        else if (!string.IsNullOrEmpty(regPid))
                        {
                            // 일반 시작: 레지스트리 PID 사용
                            Pid = regPid;
                        }
                        else if (!string.IsNullOrEmpty(currentPidTxt))
                        {
                            // 레지스트리에 PID 없음 (마이그레이션): pid.txt 사용
                            Pid = currentPidTxt;
                            key.SetValue("pid", currentPidTxt);
                            key.SetValue("last_pid_txt", currentPidTxt);
                        }
                        // else: 기본값 "pb001" 유지

                        // 설치 모드 로드
                        var regInstallMode = key.GetValue("InstallMode")?.ToString();
                        if (!string.IsNullOrEmpty(regInstallMode))
                            InstallMode = regInstallMode;
                    }
                }
            }
            catch { }

            MacAddress = GetMacAddress();
            LogService.Instance.Log($"초기화 완료 - PID:{Pid}, MAC:{MacAddress}, Mode:{InstallMode}");
        }

        /// <summary>
        /// exe와 같은 디렉토리의 pid.txt 파일에서 PID 읽기
        /// </summary>
        private static string ReadPidFromFile()
        {
            try
            {
                var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var pidFile = Path.Combine(exeDir, "pid.txt");
                if (File.Exists(pidFile))
                {
                    var pid = File.ReadAllText(pidFile).Trim();
                    if (!string.IsNullOrEmpty(pid))
                        return pid;
                }
            }
            catch { }
            return null;
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
