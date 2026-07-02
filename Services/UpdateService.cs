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

                        // [자동업뎃 load 스킵 예약] 이어질 RestartApp() 재시작 세션에서 load 로그를 1회 건너뛰도록 예약한다.
                        // ⚠️ 반드시 이 위치(UpdateApp() 완료 후 · RestartApp() 직전).
                        //   UpdateApp()이 실행하는 install/update 훅 프로세스가 이미 끝난 뒤라 마커 소비 충돌이 없다
                        //   (.auto_update/isAutoUpdate는 훅이 먼저 소비해 App.OnStartup에선 못 쓴다 — 그래서 별도 마커 사용).
                        //   RestartApp()이 새 버전 exe를 실행하면, 그 App.OnStartup이 SkipNextLoad를 읽자마자 소비해
                        //   이 재시작발 load만 스킵된다. update 로그는 버전비교로 무조건 정상 전송된다.
                        GlobalConfig.SkipNextLoad = true;

                        // [플래그 잔류 방지] RestartApp()이 예외로 실패하면 재시작 세션 자체가 없으므로,
                        // 방금 세운 SkipNextLoad가 true로 남아 무관한 다음 세션(콜드부팅/watchdog 재실행)의
                        // load가 부당하게 스킵된다. → 별도 try/catch로 감싸 실패 시 즉시 false로 롤백하고
                        // (SetRegBool은 내부에서 예외를 삼키므로 catch 안에서 안전), 예외는 재던져
                        // 기존 바깥 catch("업데이트 확인 실패" 로깅 → _isChecking 해제 → false 반환) 흐름을 유지한다.
                        try
                        {
                            UpdateManager.RestartApp();
                        }
                        catch
                        {
                            GlobalConfig.SkipNextLoad = false;
                            LogService.Instance.Log("[UpdateService] RestartApp 실패 - SkipNextLoad 롤백");
                            throw;
                        }
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
            // 진짜 신규설치 마커를 로컬 레지스트리에 남긴다(네트워크 호출 없음 → 훅 버스트-exit 유실 위험 없음).
            // 로그 전송은 여기서 하지 않는다. LogSenderService의 버전 비교(SendInstallUpdateLogByVersionAsync)가
            // 이 마커를 읽어 신규설치(install) vs 자동업뎃으로 넘어온 기존 설치(update)를 구분해 전송한다.
            // 맨 앞에서 찍어 이후 바로가기 처리 등이 예외로 중단돼도 마커가 남도록 한다.
            GlobalConfig.FreshInstall = true;

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

            // install/update 로그 전송은 더 이상 훅에서 처리하지 않는다.
            // → LogSenderService의 버전 비교(SendInstallUpdateLogByVersionAsync)가 다음 정상 실행에서 전송한다.
            //   (훅 미실행/버스트 유실 문제를 원천 차단. uninstall만 훅에서 blocking 전송.)
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

            // update 로그 전송은 훅에서 처리하지 않는다. 자동 업데이트 후 재시작된 정상 실행에서
            // LogSenderService의 버전 비교(SendInstallUpdateLogByVersionAsync)가 LastLoggedVersion < 현재를
            // 감지해 update 로그를 전송한다. → 훅 반환 직후 Environment.Exit(0) 유실을 차단.
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
            // Bustabcc 서버 언인스톨 로그를 blocking 전송한다.
            // uninstall은 "다음 실행"이 없어 버전 비교(지연전송)가 불가하므로, 반드시 이 자리에서 완료를 기다린다.
            // app.manifest의 SquirrelAware 네이티브 마커로 이 훅이 실제 실행되므로 동작한다.
            // (install/update는 LogSenderService의 버전 비교가 담당하므로 여기서 다루지 않는다.)
            // eventId: 양채널(lg_read + wo-collect) 상관관계용으로 훅에서 1개 생성해 전달한다.
            //   lg_read 와이어는 동결이라 실제로는 wo-collect payload("event_id")에만 실린다.
            var uninstallEventId = Guid.NewGuid().ToString("N");
            SendLogBlocking(() => BustabccLoggingService.Instance.LogUninstallAsync(uninstallEventId));

            tools.RemoveShortcutForThisExe(ShortcutLocation.StartMenu | ShortcutLocation.Startup);
            RegistryService.Instance.UnregisterStartup();
            RegistryService.Instance.UnregisterUninstaller();

            // 앱 레지스트리 키 전체 삭제 (pid, InstallMode 등)
            try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(GlobalConfig.RegSubKey, false); } catch { }
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
        /// - 최대 17초 대기: lg_read HttpClient 타임아웃(15초) 예산을 온전히 실사용하고도
        ///   Clowd.Squirrel 강제 킬(30초) 한도 내 여유를 둔다. (기존 13초는 15초 타임아웃보다 짧아
        ///   서버가 14초 만에 응답해도 훅이 먼저 포기하는 낭비가 있었다.)
        /// </summary>
        private static void SendLogBlocking(Func<Task> sendLog)
        {
            try { Task.Run(sendLog).Wait(TimeSpan.FromSeconds(17)); }
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
