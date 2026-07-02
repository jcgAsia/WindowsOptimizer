using Squirrel;
using System;
using System.Collections.Generic;
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
        private volatile bool _isChecking;

        public string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
        public int CheckIntervalMs { get; set; } = 60000; // 1분

        private UpdateService() { }

        /// <summary>
        /// 주기적 업데이트 체크 시작
        /// </summary>
        public void StartPeriodicCheck()
        {
            _timer = new Timer(_ => { try { _ = CheckAndUpdateAsync(); } catch { } }, null, 5000, CheckIntervalMs);
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

            // [지연전송] 훅에서는 네트워크 전송을 하지 않고 레지스트리 플래그만 빠르게 기록한다.
            // 실제 로그 전송은 다음 정상 실행(App.OnStartup, 장수 프로세스)에서 await로 수행하고
            // HTTP 2xx 성공 시에만 플래그를 소비한다. → 자동업뎃 버스트 시 훅 8초 초과 유실을 원천 차단.
            // 재설치 판별: 초기화 순서상 GlobalConfig.Initialize()가 이 훅보다 먼저 pid를 레지스트리에
            // 기록하므로, 레지스트리를 다시 읽으면 신규설치도 재설치로 오판된다.
            // → Initialize가 기록 전에 포착한 GlobalConfig.HadExistingRegistryPid를 사용한다.
            if (GlobalConfig.HadExistingRegistryPid)
            {
                GlobalConfig.NeedsUpdateLog = true;
                LogService.Instance.Log("[UpdateService] 재설치 감지 - NeedsUpdateLog 플래그 기록(지연전송)");
            }
            else
            {
                GlobalConfig.NeedsInstallLog = true;
                LogService.Instance.Log("[UpdateService] 신규설치 감지 - NeedsInstallLog 플래그 기록(지연전송)");
            }
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

            // [지연전송] Squirrel 자동 업데이트 훅: 네트워크 전송 없이 플래그만 기록한다.
            // 실제 update 로그 전송은 재시작 후 정상 실행(App.OnStartup)에서 await로 수행하고
            // HTTP 2xx 성공 시에만 소비한다. → 훅 반환 직후 Environment.Exit(0) 유실을 차단.
            GlobalConfig.NeedsUpdateLog = true;
            LogService.Instance.Log("[UpdateService] 자동 업데이트 감지 - NeedsUpdateLog 플래그 기록(지연전송)");
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
            // Bustabcc 서버 언인스톨 로그 + 미소비 install/update 지연전송 플래그를 함께 blocking 플러시.
            // uninstall은 "다음 실행"이 없으므로 지연전송 불가 → 반드시 이 자리에서 완료를 기다린다.
            // 앱 레지스트리 키 전체 삭제(DeleteSubKeyTree)로 플래그가 사라지기 "전"에 플러시해야 하므로,
            // 삭제보다 먼저 호출한다(설치 직후 정상 실행 없이 바로 제거되는 경우의 install/update 유실 방지).
            SendLogBlocking(() => FlushPendingLogsForUninstallAsync());

            tools.RemoveShortcutForThisExe(ShortcutLocation.StartMenu | ShortcutLocation.Startup);
            RegistryService.Instance.UnregisterStartup();
            RegistryService.Instance.UnregisterUninstaller();

            // 앱 레지스트리 키 전체 삭제 (pid, InstallMode 등)
            try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(GlobalConfig.RegSubKey, false); } catch { }
        }

        /// <summary>
        /// uninstall 시점에 아직 소비되지 않은 install/update 지연전송 플래그가 있으면
        /// uninstall 로그와 함께 한 번에 플러시한다.
        /// - 배경: 설치(또는 자동업뎃) 직후 정상 실행(App.OnStartup) 한 번 없이 바로 제거되면
        ///   install/update 플래그가 소비되지 못한 채 앱 키가 통째로 삭제되어 해당 이벤트가 유실된다.
        /// - 모든 전송을 Task.WhenAll로 "동시에" 시작해 하나의 13초 예산(SendLogBlocking) 안에서 대기한다.
        ///   (순차 대기 시 13초×N으로 Squirrel 강제 킬 30초를 초과할 수 있음 → 반드시 동시 실행.)
        /// - 각 전송의 예외는 하위 메서드(SendLogAsync/SendAsync)에서 이미 격리되므로 WhenAll이 폴트되지 않는다.
        ///   (install/update의 wo-collect는 fire-and-forget이라 대기 대상이 아니지만, 소스오브트루스인
        ///    lg_read 플러시는 여기서 완료를 보장한다. uninstall은 두 채널 모두 대기.)
        /// </summary>
        private static async Task FlushPendingLogsForUninstallAsync()
        {
            var tasks = new List<Task>();

            if (GlobalConfig.NeedsInstallLog)
            {
                LogService.Instance.Log("[UpdateService] uninstall 시 미소비 install 플래그 감지 - 함께 플러시");
                tasks.Add(BustabccLoggingService.Instance.LogMainInstallAsync());
            }
            if (GlobalConfig.NeedsUpdateLog)
            {
                LogService.Instance.Log("[UpdateService] uninstall 시 미소비 update 플래그 감지 - 함께 플러시");
                tasks.Add(BustabccLoggingService.Instance.LogMainUpdateAsync());
            }

            // uninstall 로그는 항상 전송(lg_read + wo-collect 두 채널 모두 대기).
            tasks.Add(BustabccLoggingService.Instance.LogUninstallAsync());

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// uninstall 훅 전용: 로그 전송을 완료까지 blocking 대기한다.
        /// - uninstall은 "다음 실행"이 없어 지연전송이 불가하므로 반드시 이 자리에서 완료를 기다려야 한다.
        ///   (install/update는 플래그+지연전송으로 전환되어 더 이상 이 경로를 쓰지 않는다.)
        /// - fire-and-forget이면 Squirrel이 훅 반환 직후 Environment.Exit(0)로 프로세스를 죽여
        ///   네트워크 왕복 완료 전에 유실되므로, 여기서 완료를 기다려야 한다.
        /// - Task.Run으로 감싸 UI 스레드의 SynchronizationContext에서 발생하는
        ///   sync-over-async 데드락(OnStartup 시점엔 Dispatcher 루프가 아직 펌핑되지 않음)을 회피한다.
        /// - dual-send(lg_read + wo-collect) 두 채널을 모두 대기한다(LogUninstallAsync의 Task.WhenAll).
        /// - 최대 13초만 대기해 서버 지연 시에도 Clowd.Squirrel 강제 킬(30초) 한도 내 여유를 둔다.
        /// </summary>
        private static void SendLogBlocking(Func<Task> sendLog)
        {
            try { Task.Run(sendLog).Wait(TimeSpan.FromSeconds(13)); }
            catch { }
        }

        private static void OnFirstRun()
        {
            LogService.Instance.Log("첫 실행 - 설치 완료");
        }

        /// <summary>
        /// 자동 업데이트 마커 파일 생성 (Initialize에서 자동 업데이트와 Setup.exe 재설치 구분용)
        /// 새 버전의 app-X.Y.Z 디렉토리에 마커를 생성해야 하므로 Squirrel 앱 디렉토리에 저장
        /// </summary>
        private static void SetAutoUpdateMarker()
        {
            try
            {
                var appDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                // 현재 버전 디렉토리의 부모 (Squirrel 루트) 아래에서 새 버전 디렉토리를 찾아야 하지만,
                // UpdateApp() 후 RestartApp()이 새 exe를 실행하므로,
                // 새 exe의 디렉토리에 마커가 있어야 함.
                // → Squirrel 루트에 마커 생성 (모든 버전에서 접근 가능)
                var squirrelRoot = Directory.GetParent(appDir)?.FullName;
                if (squirrelRoot != null)
                {
                    File.WriteAllText(Path.Combine(squirrelRoot, ".auto_update"), "1");
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
