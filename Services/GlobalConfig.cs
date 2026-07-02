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

        #region 버전 비교 기반 install/update 로그 (훅 무의존, 유실/중복 방지)
        // 배경: Squirrel install/update 훅은 (a) SDK-style에서 SquirrelAware 미인식으로 원천 미실행이거나,
        //       (b) 대량 동시 자동업뎃 버스트 때 훅 대기 초과 후 Environment.Exit(0)로 전송이 유실됐다.
        //       → 훅에 의존하지 않고, 정상 장수 프로세스(App.OnStartup)에서 "마지막으로 로그를 보낸 버전"과
        //         현재 어셈블리 버전을 비교해 install/update를 판별한다.
        // 규칙: 기록 없음 → install, 기록 < 현재 → update, 같음/다운그레이드 → 무전송.
        //       lg_read HTTP 2xx 성공 시에만 LastLoggedVersion을 현재로 갱신한다(실패 시 유지 → 다음 부팅 재시도).
        // 저장 위치: HKCU\SOFTWARE\WindowsOptimizer (앱 기존 RegSubKey 하위).

        /// <summary>
        /// 마지막으로 install/update 로그를 성공적으로 전송한 어셈블리 버전 문자열(예: "2.7.10.0").
        /// 미설정(null/빈문자열)이면 아직 한 번도 로그를 보내지 않은 상태(첫 실행)로 간주한다.
        /// </summary>
        public static string LastLoggedVersion
        {
            get => GetRegString("LastLoggedVersion");
            set => SetRegString("LastLoggedVersion", value);
        }

        /// <summary>
        /// 진짜 신규설치 마커. OnAppInstall 훅이 실행되면 true로 남는다(로컬 레지스트리 쓰기만, 네트워크 무관).
        /// - app.manifest의 SquirrelAware 마커 덕에 신규설치 시 OnAppInstall이 실제 실행되므로 여기서 마커가 찍힌다.
        /// - 반면 "첫 매니페스트 빌드로 자동업뎃되는 기존 설치"는 OnAppUpdate(no-op)만 타고 OnAppInstall이 안 불려 마커가 없다.
        /// → SendInstallUpdateLogByVersionAsync가 LastLoggedVersion이 없을 때 이 마커로
        ///   신규설치(마커 true → install) vs 자동업뎃 넘어온 기존 설치(마커 없음 → update)를 정확히 구분한다.
        ///   lg_read 2xx 성공 시 false로 소비해 재전송을 막는다.
        /// 저장 형식: "1"=true, 그 외/미설정=false.
        /// </summary>
        public static bool FreshInstall
        {
            get => GetRegBool("FreshInstall");
            set => SetRegBool("FreshInstall", value);
        }

        /// <summary>
        /// 자동업뎃(UpdateApp→RestartApp)으로 재시작되는 "바로 다음 실행"에서 load 로그를 1회만 건너뛰기 위한 마커.
        /// - UpdateService가 RestartApp() 직전에 true로 세팅하고, App.OnStartup이 Mutex(단일 인스턴스) 획득 직후
        ///   읽자마자 false로 소비한다(로컬 레지스트리 읽기/쓰기만 → 네트워크 무관, 훅 버스트-exit 유실 위험 없음).
        ///   Mutex 이후 소비여야 뮤텍스를 잃고 Shutdown될 중복 프로세스가 플래그를 선소비하는 레이스가 없다.
        /// - load만 스킵한다. update 로그(SendInstallUpdateLogByVersionAsync)는 이 마커와 무관하게 무조건 전송된다.
        /// - 콜드부팅/런처발(watchdog) 재실행은 이 마커가 세팅되지 않으므로 load가 정상 전송된다.
        /// - 재시작 직후 즉사해도 이미 false로 소비돼 있어, 다음 콜드부팅 load가 잘못 스킵되지 않는다(안전 방향).
        /// ⚠️ 기존 .auto_update 마커/isAutoUpdate 재사용 금지: 그건 Squirrel 훅 프로세스가 먼저 소비해
        ///    App.OnStartup 시점엔 이미 사라져 있어 load 스킵 판별에 쓸 수 없다.
        /// 저장 위치: HKCU\SOFTWARE\WindowsOptimizer. 저장 형식: "1"=true, 그 외/미설정=false.
        /// </summary>
        public static bool SkipNextLoad
        {
            get => GetRegBool("SkipNextLoad");
            set => SetRegBool("SkipNextLoad", value);
        }

        private static string GetRegString(string name)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegSubKey))
                    return key?.GetValue(name)?.ToString();
            }
            catch { return null; }
        }

        private static void SetRegString(string name, string value)
        {
            // 쓰기 실패 시 1회 재시도. 버전 갱신 실패가 유지되면 다음 부팅에서 update가 재전송되어
            // 중복 집계로 이어지므로, 저확률이지만 최소한의 재시도로 방어한다.
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    using (var key = Registry.CurrentUser.CreateSubKey(RegSubKey))
                        key?.SetValue(name, value ?? string.Empty);
                    return;
                }
                catch (Exception ex)
                {
                    try { LogService.Instance.Log($"[GlobalConfig] {name} 저장 실패(시도 {attempt}/2): {ex.Message}"); } catch { }
                }
            }
        }

        // "1"만 true로 본다. 미설정(null)/그 외 값은 false → 마커 없음이 곧 "신규설치 아님".
        private static bool GetRegBool(string name) => GetRegString(name) == "1";

        // SetRegString의 쓰기 재시도 로직을 그대로 재사용한다(마커 갱신 실패 시 오분류 방어).
        private static void SetRegBool(string name, bool value) => SetRegString(name, value ? "1" : "0");
        #endregion
    }
}
