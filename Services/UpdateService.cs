using Squirrel;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsOptimizer.Services
{
    public class UpdateService
    {
        private static readonly Lazy<UpdateService> _instance = new Lazy<UpdateService>(() => new UpdateService());
        public static UpdateService Instance => _instance.Value;

        private Timer _timer;
        private bool _isChecking;

        public string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
        public int CheckIntervalMs { get; set; } = 60000; // 1분

        private UpdateService() { }

        /// <summary>
        /// 주기적 업데이트 체크 시작
        /// </summary>
        public void StartPeriodicCheck()
        {
            _timer = new Timer(async _ => await CheckAndUpdateAsync(), null, 0, CheckIntervalMs);
            LogService.Instance.Log($"[UpdateService] 주기적 업데이트 체크 시작 ({CheckIntervalMs / 1000}초)");
        }

        /// <summary>
        /// 주기적 체크 중지
        /// </summary>
        public void StopPeriodicCheck()
        {
            _timer?.Dispose();
            _timer = null;
        }

        /// <summary>
        /// Squirrel을 통한 GitHub Releases 자동 업데이트
        /// </summary>
        public async Task<bool> CheckAndUpdateAsync()
        {
            if (_isChecking) return false;
            _isChecking = true;

            try
            {
                // 주기적 체크는 조용히 진행 (새 버전 발견 시에만 로그)

                using (var mgr = new UpdateManager(GlobalConfig.GitHubUpdateUrl))
                {
                    var updateInfo = await mgr.CheckForUpdate();

                    if (updateInfo?.ReleasesToApply?.Count > 0)
                    {
                        var newVersion = updateInfo.FutureReleaseEntry?.Version?.ToString();
                        LogService.Instance.Log($"[UpdateService] 새 버전 발견: {newVersion}");

                        // 자동 업데이트 마커 저장 (OnAppUpdate에서 Setup.exe 재설치와 구분용)
                        SetAutoUpdateMarker();

                        await mgr.UpdateApp();
                        LogService.Instance.Log("[UpdateService] 업데이트 완료. 재시작 중...");

                        UpdateManager.RestartApp();
                        return true;
                    }
                    // 최신 버전일 때는 로그 생략 (1분마다 반복되므로)
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[UpdateService] 업데이트 확인 실패: {ex.Message}");
            }
            finally
            {
                _isChecking = false;
            }
            return false;
        }

        /// <summary>
        /// 앱 시작 시 Squirrel 이벤트 처리
        /// </summary>
        public static void HandleSquirrelEvents()
        {
            SquirrelAwareApp.HandleEvents(
                onInitialInstall: OnAppInstall,
                onAppUpdate: OnAppUpdate,
                onAppUninstall: OnAppUninstall
            );
        }

        private static void OnAppInstall(SemanticVersion version, IAppTools tools)
        {
            // 설치 인자에서 -mockup 또는 -ui 확인 (기본값: Execute 모드)
            var args = Environment.GetCommandLineArgs();
            bool isMockupMode = Array.Exists(args, a =>
                a.Equals("-mockup", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-ui", StringComparison.OrdinalIgnoreCase));

            // 설치 모드 저장 (기본값: Execute)
            GlobalConfig.SetInstallMode(isMockupMode
                ? GlobalConfig.InstallModeMockup
                : GlobalConfig.InstallModeExecute);

            // PID 처리는 GlobalConfig.Initialize()에서 pid.txt 기반으로 수행

            // 먼저 Squirrel이 자동 생성한 모든 바로가기 제거
            tools.RemoveShortcutForThisExe(ShortcutLocation.StartMenu | ShortcutLocation.Desktop | ShortcutLocation.Startup);

            // 수동으로 바탕화면 바로가기 파일 직접 삭제 (Squirrel 타이밍 이슈 대응)
            RemoveDesktopShortcuts();

            if (isMockupMode)
            {
                // Mockup 모드: 시작메뉴 + 시작프로그램 등록
                tools.CreateShortcutForThisExe(ShortcutLocation.StartMenu | ShortcutLocation.Startup);
            }
            else
            {
                // Execute 모드 (기본): 시작프로그램만 등록, 바로가기 없음
                tools.CreateShortcutForThisExe(ShortcutLocation.Startup);
            }

            // 프로그램 추가/삭제에 등록 (두 모드 공통)
            RegistryService.Instance.RegisterUninstaller();

            // 설치 카운팅 로그 전송
            try { _ = CountingService.Instance.LogInstallAsync(); } catch { }

            // Bustabcc 서버 설치 로그 전송
            try { _ = BustabccLoggingService.Instance.LogMainInstallAsync(); } catch { }
        }

        private static void OnAppUpdate(SemanticVersion version, IAppTools tools)
        {
            // PID 처리는 GlobalConfig.Initialize()에서 auto_update 마커 기반으로 수행

            // 저장된 설치 모드에 따라 바로가기 처리
            string installMode = GetSavedInstallMode();

            // 먼저 Squirrel이 자동 생성한 모든 바로가기 제거
            tools.RemoveShortcutForThisExe(ShortcutLocation.StartMenu | ShortcutLocation.Desktop | ShortcutLocation.Startup);

            // 수동으로 바탕화면 바로가기 파일 직접 삭제 (Squirrel 타이밍 이슈 대응)
            RemoveDesktopShortcuts();

            if (installMode == GlobalConfig.InstallModeExecute)
            {
                // Execute 모드: 시작프로그램만
                tools.CreateShortcutForThisExe(ShortcutLocation.Startup);
            }
            else
            {
                // Mockup 모드: 시작메뉴 + 시작프로그램
                tools.CreateShortcutForThisExe(ShortcutLocation.StartMenu | ShortcutLocation.Startup);
            }

            // Bustabcc 서버 업데이트 로그 전송
            try { _ = BustabccLoggingService.Instance.LogMainUpdateAsync(); } catch { }
        }

        /// <summary>
        /// 레지스트리에서 저장된 설치 모드 읽기
        /// </summary>
        private static string GetSavedInstallMode()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(GlobalConfig.RegSubKey))
                {
                    // 기본값: Execute 모드
                    return key?.GetValue("InstallMode")?.ToString() ?? GlobalConfig.InstallModeExecute;
                }
            }
            catch
            {
                return GlobalConfig.InstallModeExecute;
            }
        }

        private static void OnAppUninstall(SemanticVersion version, IAppTools tools)
        {
            // 언인스톨 카운팅 로그 전송
            try { _ = CountingService.Instance.LogUninstallAsync(); } catch { }

            // Bustabcc 서버 언인스톨 로그 전송
            try { _ = BustabccLoggingService.Instance.LogUninstallAsync(); } catch { }

            tools.RemoveShortcutForThisExe(ShortcutLocation.StartMenu | ShortcutLocation.Startup);
            RegistryService.Instance.UnregisterStartup();
            RegistryService.Instance.UnregisterUninstaller();

            // 앱 레지스트리 키 전체 삭제 (pid, InstallMode 등)
            try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(GlobalConfig.RegSubKey, false); } catch { }
        }

        private static void OnFirstRun()
        {
            LogService.Instance.Log("첫 실행 - 설치 완료");
        }

        /// <summary>
        /// 자동 업데이트 마커 저장 (Initialize에서 자동 업데이트와 Setup.exe 재설치 구분용)
        /// </summary>
        private static void SetAutoUpdateMarker()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(GlobalConfig.RegSubKey))
                {
                    key?.SetValue("auto_update", "1");
                }
            }
            catch { }
        }

        /// <summary>
        /// 바탕화면 바로가기 파일 수동 삭제 (Squirrel 타이밍 이슈 대응)
        /// </summary>
        private static void RemoveDesktopShortcuts()
        {
            try
            {
                string[] shortcutNames = {
                    "WindowsOptimizer.lnk",
                    "Windows System Optimizer.lnk"
                };

                // 사용자 바탕화면
                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                foreach (var name in shortcutNames)
                {
                    var path = Path.Combine(desktopPath, name);
                    if (File.Exists(path))
                        File.Delete(path);
                }

                // 공용 바탕화면
                var publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
                foreach (var name in shortcutNames)
                {
                    var path = Path.Combine(publicDesktop, name);
                    if (File.Exists(path))
                        File.Delete(path);
                }
            }
            catch { }
        }
    }
}
