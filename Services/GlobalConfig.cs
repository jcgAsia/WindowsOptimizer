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

        // Initialize()가 레지스트리에 pid를 기록하기 "직전"에 포착한, 기존 pid 존재 여부.
        // 초기화 순서상 Initialize()가 Squirrel 훅보다 먼저 실행되어 pid.txt를 레지스트리에 기록하므로,
        // OnAppInstall 훅에서 레지스트리 pid를 다시 읽으면 신규설치도 pid가 존재해 재설치로 오판된다.
        // → 훅은 이 플래그로 신규설치(false) vs 재설치(true)를 판별한다.
        public static bool HadExistingRegistryPid { get; private set; }

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

        // Bustabcc 서버 설정
        public const string BustabccLogUrl = "https://mitenews.com/PRG/lg_read.php";
        public const string ActionInstall = "install";
        public const string ActionUpdate = "update";
        public const string ActionLoad = "load";
        public const string ActionUninstall = "uninstall";
        public const int TargetMain = 1;

        // 토스트 팝업 설정
        public const string ToastDefaultUrl = "https://www.toastpop.net/CARD/card.php";
        public const int ToastPopupWidth = 300;
        public const int ToastPopupHeight = 250;

        // 런처 연동 설정
        public static bool LaunchedByLauncher { get; set; } = false;
        public static string LauncherVersion { get; set; }

        public static void Initialize()
        {
            try
            {
                // PID 결정: 자동 업데이트 마커 파일(exe 옆) 유무로 분기
                //   마커 있음 → 자동 업데이트 → 레지스트리 PID 유지
                //   마커 없음 → pid.txt를 신뢰 (신규 설치 또는 재설치)
                var currentPidTxt = ReadPidFromFile();
                var isAutoUpdate = CheckAndClearAutoUpdateMarker();

                using (var key = Registry.CurrentUser.CreateSubKey(RegSubKey))
                {
                    if (key != null)
                    {
                        var regPid = key.GetValue("pid")?.ToString();

                        // pid.txt 기록으로 값이 덮이기 전, 기존 pid 존재 여부를 포착(재설치 판별용)
                        HadExistingRegistryPid = !string.IsNullOrEmpty(regPid);

                        if (!string.IsNullOrEmpty(currentPidTxt))
                        {
                            // pid.txt 항상 최우선: 신규 설치, 재설치, 자동 업데이트 모두
                            Pid = currentPidTxt;
                            key.SetValue("pid", currentPidTxt);
                        }
                        else if (!string.IsNullOrEmpty(regPid))
                        {
                            // pid.txt 없음, 레지스트리에 PID 있음 → 기존 값 사용
                            Pid = regPid;
                        }
                        // else: 기본값 "pb001" 유지

                        // 설치 모드 로드
                        var regInstallMode = key.GetValue("InstallMode")?.ToString();
                        if (!string.IsNullOrEmpty(regInstallMode))
                            InstallMode = regInstallMode;

                        // 레거시 값 정리
                        key.DeleteValue("auto_update", false);
                        key.DeleteValue("last_pid_txt", false);
                    }
                }
            }
            catch { }

            MacAddress = GetMacAddress();
            LogService.Instance.Log($"초기화 완료 - PID:{Pid}, MAC:{MacAddress}, Mode:{InstallMode}");
        }

        /// <summary>
        /// 자동 업데이트 마커 파일 확인 후 삭제 (exe 옆 .auto_update 파일)
        /// Squirrel은 버전별 app-X.Y.Z 디렉토리를 사용하므로,
        /// 언인스톨/재설치 시 마커가 자동으로 사라짐
        /// </summary>
        private static bool CheckAndClearAutoUpdateMarker()
        {
            try
            {
                var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                // Squirrel 루트 (app-X.Y.Z의 부모 디렉토리)에서 마커 확인
                var squirrelRoot = Directory.GetParent(exeDir)?.FullName;
                if (squirrelRoot != null)
                {
                    var markerFile = Path.Combine(squirrelRoot, ".auto_update");
                    if (File.Exists(markerFile))
                    {
                        File.Delete(markerFile);
                        return true;
                    }
                }
            }
            catch { }
            return false;
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
                    var pid = File.ReadAllText(pidFile).Trim().TrimStart('\uFEFF');
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

        #region 지연전송 플래그 (install/update 로그 유실 방지)
        // 배경: Squirrel install/update 훅에서 로그를 blocking 전송하면, 대량 동시 자동업뎃 버스트 때
        //       서버 응답이 훅 대기(8초)를 넘겨 Task.Wait이 포기 → 훅 반환 → Environment.Exit(0)으로
        //       전송 소켓이 즉사 → 로그 유실(실측: update 3만건인데 서버 집계 0건)이 발생한다.
        // 대책: 훅에서는 네트워크에 의존하지 않고 레지스트리 플래그만 빠르게 기록하고,
        //       다음 정상 실행(App.OnStartup, 장수 프로세스)에서 await로 전송한 뒤
        //       HTTP 2xx 성공 시에만 플래그를 소비한다(실패 시 다음 실행에 재시도 → 1회성 유실 차단).
        // 저장 위치: HKCU\SOFTWARE\WindowsOptimizer (앱 기존 RegSubKey 하위). 런처 LauncherConfig 패턴과 동일.

        /// <summary>
        /// 신규 설치 후 첫 정상 실행에서 install 로그를 1회 전송해야 하는지 여부 (레지스트리 bool).
        /// OnAppInstall 훅(신규설치)이 true로 설정하고, 정상 실행이 2xx 전송 성공 시 false로 소비한다.
        /// </summary>
        public static bool NeedsInstallLog
        {
            get => GetRegBool("NeedsInstallLog");
            set => SetRegBool("NeedsInstallLog", value);
        }

        /// <summary>
        /// 업데이트(재설치/자동업데이트) 후 첫 정상 실행에서 update 로그를 1회 전송해야 하는지 여부 (레지스트리 bool).
        /// OnAppInstall 훅(재설치)/OnAppUpdate 훅이 true로 설정하고, 정상 실행이 2xx 전송 성공 시 false로 소비한다.
        /// </summary>
        public static bool NeedsUpdateLog
        {
            get => GetRegBool("NeedsUpdateLog");
            set => SetRegBool("NeedsUpdateLog", value);
        }

        private static bool GetRegBool(string name)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegSubKey))
                    return key?.GetValue(name)?.ToString() == "1";
            }
            catch { return false; }
        }

        private static void SetRegBool(string name, bool value)
        {
            // 쓰기 실패 시 1회 재시도. 플래그 소비(=false 기록) 실패가 유지되면 다음 부팅에서 재전송되어
            // 중복 집계로 이어지므로, 저확률이지만 최소한의 재시도로 방어한다.
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    using (var key = Registry.CurrentUser.CreateSubKey(RegSubKey))
                        key?.SetValue(name, value ? "1" : "0");
                    return;
                }
                catch (Exception ex)
                {
                    try { LogService.Instance.Log($"[GlobalConfig] {name} 저장 실패(시도 {attempt}/2): {ex.Message}"); } catch { }
                }
            }
        }
        #endregion
    }
}
