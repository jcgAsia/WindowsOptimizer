using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using WindowsOptimizer.Services;

namespace WindowsOptimizer
{
    public partial class App : Application
    {
        private static Mutex _mutex;
        private static bool _mutexOwned;
        private System.Windows.Forms.NotifyIcon _trayIcon;

        // 글로벌 핫키 관련
        private const int HOTKEY_ID = 9000;
        private const int HOTKEY_DEBUG_ID = 9001;
        private const uint MOD_CTRL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_ALT = 0x0001;
        private const uint VK_O = 0x4F; // 'O' key
        private const uint VK_F12 = 0x7B; // F12 key

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private HwndSource _hwndSource;

        private async void InitializeConfigAsync()
        {
            try
            {
                await ConfigService.Instance.LoadMappingConfigAsync();
                ConfigService.Instance.StartPeriodicReload(skipInitialLoad: true);
            }
            catch (Exception ex)
            {
                try { LogService.Instance.Log($"[ERROR] InitializeConfigAsync: {ex.Message}"); } catch { }
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // Global crash prevention (last resort)
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                try { LogService.Instance.Log($"[FATAL] UnhandledException: {args.ExceptionObject}"); } catch { }
            };
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                args.SetObserved();
                try { LogService.Instance.Log($"[WARN] UnobservedTaskException: {args.Exception?.GetBaseException().Message}"); } catch { }
            };

            // Phase 1: Critical bootstrap
            // GlobalConfig.Initialize()를 Squirrel 훅 처리보다 먼저 실행한다.
            // 이렇게 해야 install/update/uninstall 훅 시점에 올바른 Pid/MacAddress가 채워져
            // 로그가 정확한 값으로 전송된다. (기존엔 훅이 먼저 실행되어 pid=기본값/mac=null 이었음)
            GlobalConfig.Initialize();
            UpdateService.HandleSquirrelEvents();

            // Phase 2: Non-critical telemetry (isolated)
            try { _ = MonitorLogService.Instance.SendAsync("app_start"); } catch { }
            try { RemoveDesktopShortcutsOnly(); } catch { }

            // Phase 3: Args processing (safe, no external deps)
            var args = Environment.GetCommandLineArgs();

            // -ui 또는 --ui 로 UI 강제 표시
            if (args.Any(a => a.Equals("-ui", StringComparison.OrdinalIgnoreCase) ||
                              a.Equals("--ui", StringComparison.OrdinalIgnoreCase)))
            {
                GlobalConfig.ShowUIOverride = true;
                LogService.Instance.Log("UI 강제 표시 모드 (-ui)");
            }

            // --debug-hidden 으로 히든윈도우 디버그 모드
            if (args.Any(a => a.Equals("--debug-hidden", StringComparison.OrdinalIgnoreCase)))
            {
                BrowserMonitorService.Instance.DebugMode = true;
                LogService.Instance.Log("히든윈도우 디버그 모드 활성화");
            }

            // --from-launcher 로 런처에서 실행됨을 표시
            var launcherArg = args.FirstOrDefault(a => a.StartsWith("--from-launcher", StringComparison.OrdinalIgnoreCase));
            if (launcherArg != null)
            {
                GlobalConfig.LaunchedByLauncher = true;
                // --from-launcher=1.0.0 형식으로 런처 버전 전달 가능
                if (launcherArg.Contains("="))
                {
                    GlobalConfig.LauncherVersion = launcherArg.Split('=')[1];
                }
                LogService.Instance.Log($"런처에서 실행됨 (Launcher Version: {GlobalConfig.LauncherVersion ?? "unknown"})");
            }

            // Phase 4: Mutex check (critical)
            _mutex = new Mutex(true, GlobalConfig.MutexName, out bool isNew);
            _mutexOwned = isNew;
            if (!isNew)
            {
                // UI 모드에서만 메시지 박스 표시
                if (GlobalConfig.ShouldShowUI)
                {
                    MessageBox.Show("이미 실행 중입니다.", "알림");
                }
                Shutdown();
                return;
            }

            base.OnStartup(e);

            // Phase 5: Self-healing update (after mutex, single instance only)
            UpdateService.Instance.StartPeriodicCheck();

            // Phase 6: Non-critical services (isolated)
            try { RegistryService.Instance.RegisterStartup(); } catch { }

            // UI 모드일 때만 트레이 아이콘 설정
            if (GlobalConfig.ShouldShowUI)
            {
                try { SetupTrayIcon(); } catch { }
            }

            try { GlobalConfig.OnLoadingLogQuery(); } catch { }
            try { _ = BustabccLoggingService.Instance.LogMainLoadAsync(); } catch { }

            // [지연전송] install/update 로그: Squirrel 훅은 레지스트리 플래그만 남기고(네트워크 무의존),
            // 여기(정상 장수 프로세스)에서 전송 후 HTTP 2xx 성공 시에만 플래그를 소비한다.
            // OnStartup은 async가 아니므로 fire-and-forget하되, 내부는 await로 완료를 보장한다
            // (WPF Dispatcher 루프가 곧 펌핑되어 continuation이 실행되고, 프로세스가 장수하므로 유실 없음).
            try { _ = SendDeferredInstallUpdateLogsAsync(); } catch { }

            // Phase 7: Core services
            InitializeConfigAsync();

            LogService.Instance.Log($"애플리케이션 시작 (Mode: {GlobalConfig.InstallMode}, ShowUI: {GlobalConfig.ShouldShowUI})");

            // Phase 8: UI
            try
            {
                MainWindow = new MainWindow();

                // UI 표시 여부에 따라 MainWindow 처리
                if (GlobalConfig.ShouldShowUI)
                {
                    MainWindow.Show();
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"MainWindow 생성 실패: {ex.Message}");
                Shutdown(1);
                return;
            }

            try { RegisterGlobalHotkey(); } catch { }
            try { ToastPopupService.Instance.StartMonitoring(); } catch { }
        }

        /// <summary>
        /// Squirrel install/update 훅이 남긴 지연전송 플래그를 확인해 로그를 전송한다.
        /// - 정상 장수 프로세스라 await로 완료를 보장한다(훅과 달리 Environment.Exit 유실 없음).
        /// - lg_read(Bustabcc) HTTP 2xx 성공 시에만 플래그를 소비하고, 실패 시 플래그를 유지해
        ///   다음 실행에서 재시도한다(1회성 유실 차단). wo-collect는 dual-send로 함께 전송된다.
        /// - install/update 플래그를 각각 독립 처리한다(정상적으로는 동시에 1개만 세팅됨).
        /// </summary>
        private static async Task SendDeferredInstallUpdateLogsAsync()
        {
            try
            {
                if (GlobalConfig.NeedsInstallLog)
                {
                    LogService.Instance.Log("[App] 설치 후 첫 실행 감지 - install 로그 지연전송");
                    if (await BustabccLoggingService.Instance.LogMainInstallAsync())
                        GlobalConfig.NeedsInstallLog = false;
                    else
                        LogService.Instance.Log("[App] install 로그 전송 실패 - 플래그 유지(다음 실행 재시도)");
                }

                if (GlobalConfig.NeedsUpdateLog)
                {
                    LogService.Instance.Log("[App] 업데이트 후 첫 실행 감지 - update 로그 지연전송");
                    if (await BustabccLoggingService.Instance.LogMainUpdateAsync())
                        GlobalConfig.NeedsUpdateLog = false;
                    else
                        LogService.Instance.Log("[App] update 로그 전송 실패 - 플래그 유지(다음 실행 재시도)");
                }
            }
            catch (Exception ex)
            {
                try { LogService.Instance.Log($"[App] 지연전송 오류: {ex.Message}"); } catch { }
            }
        }

        private void SetupTrayIcon()
        {
            using (var ms = new System.IO.MemoryStream(Resource.app))
            {
                _trayIcon = new System.Windows.Forms.NotifyIcon
                {
                    Icon = new System.Drawing.Icon(ms),
                    Visible = true,
                    Text = "Windows System Optimizer"
                };
            }

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("디버그 창 열기", null, (s, ev) => { MainWindow?.Show(); MainWindow?.Activate(); });
            menu.Items.Add("-");
            menu.Items.Add("종료", null, (s, ev) => Shutdown());
            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (s, ev) => { MainWindow?.Show(); MainWindow?.Activate(); };
        }

        /// <summary>
        /// 글로벌 핫키 등록 (Ctrl+Shift+O: UI 토글)
        /// </summary>
        private void RegisterGlobalHotkey()
        {
            try
            {
                // 숨겨진 윈도우 생성하여 핫키 메시지 수신
                var helper = new WindowInteropHelper(MainWindow);
                if (helper.Handle == IntPtr.Zero)
                {
                    helper.EnsureHandle();
                }

                _hwndSource = HwndSource.FromHwnd(helper.Handle);
                _hwndSource?.AddHook(HwndHook);

                // Ctrl+Shift+O 등록
                RegisterHotKey(helper.Handle, HOTKEY_ID, MOD_CTRL | MOD_SHIFT, VK_O);
                LogService.Instance.Log("글로벌 핫키 등록: Ctrl+Shift+O (UI 토글)");

                // Ctrl+Shift+Alt+F12 등록 (디버그 모드)
                RegisterHotKey(helper.Handle, HOTKEY_DEBUG_ID, MOD_CTRL | MOD_SHIFT | MOD_ALT, VK_F12);
                LogService.Instance.Log("글로벌 핫키 등록: Ctrl+Shift+Alt+F12 (디버그 모드)");
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"핫키 등록 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 윈도우 메시지 처리 (핫키)
        /// </summary>
        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;

            if (msg == WM_HOTKEY)
            {
                int hotkeyId = wParam.ToInt32();
                if (hotkeyId == HOTKEY_ID)
                {
                    ToggleUI();
                    handled = true;
                }
                else if (hotkeyId == HOTKEY_DEBUG_ID)
                {
                    ToggleDebugMode();
                    handled = true;
                }
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// UI 토글 (핫키로 호출)
        /// </summary>
        private void ToggleUI()
        {
            if (MainWindow == null) return;

            if (MainWindow.IsVisible)
            {
                MainWindow.Hide();
                LogService.Instance.Log("UI 숨김 (핫키)");
            }
            else
            {
                // 트레이 아이콘이 없으면 생성
                if (_trayIcon == null)
                {
                    try { SetupTrayIcon(); } catch { }
                }

                MainWindow.Show();
                MainWindow.Activate();
                MainWindow.WindowState = WindowState.Normal;
                LogService.Instance.Log("UI 표시 (핫키)");
            }
        }

        /// <summary>
        /// 디버그 모드 토글 (Ctrl+Shift+Alt+F12)
        /// MainWindow와 히든윈도우를 함께 표시
        /// </summary>
        private void ToggleDebugMode()
        {
            // 트레이 아이콘이 없으면 생성
            if (_trayIcon == null)
            {
                try { SetupTrayIcon(); } catch { }
            }

            // MainWindow 표시
            if (MainWindow != null)
            {
                MainWindow.Show();
                MainWindow.Activate();
                MainWindow.WindowState = WindowState.Normal;
            }

            // 히든윈도우 디버그 모드 토글
            BrowserMonitorService.Instance.ToggleDebugWindow();
            LogService.Instance.Log("디버그 모드 토글 (핫키)");
        }

        /// <summary>
        /// 바탕화면 바로가기만 삭제 (시작메뉴는 유지)
        /// </summary>
        private void RemoveDesktopShortcutsOnly()
        {
            try
            {
                // 바탕화면 바로가기 삭제
                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                DeleteShortcut(Path.Combine(desktopPath, "WindowsOptimizer.lnk"));
                DeleteShortcut(Path.Combine(desktopPath, "Windows System Optimizer.lnk"));

                // 공용 바탕화면 바로가기 삭제
                var publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
                DeleteShortcut(Path.Combine(publicDesktop, "WindowsOptimizer.lnk"));
                DeleteShortcut(Path.Combine(publicDesktop, "Windows System Optimizer.lnk"));
            }
            catch { }
        }

        private void DeleteShortcut(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    LogService.Instance.Log($"바로가기 삭제: {path}");
                }
            }
            catch { }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 주기적 체크 중지
            try { UpdateService.Instance.StopPeriodicCheck(); } catch { }
            try { ConfigService.Instance.StopPeriodicReload(); } catch { }

            // 토스트 팝업 서비스 중지
            try { ToastPopupService.Instance.StopMonitoring(); } catch { }

            // 글로벌 핫키 해제
            try
            {
                if (MainWindow != null)
                {
                    var helper = new WindowInteropHelper(MainWindow);
                    UnregisterHotKey(helper.Handle, HOTKEY_ID);
                    UnregisterHotKey(helper.Handle, HOTKEY_DEBUG_ID);
                }
                _hwndSource?.RemoveHook(HwndHook);
            }
            catch { }

            try { BrowserMonitorService.Instance.StopMonitoring(); } catch { }
            try { _trayIcon?.Dispose(); } catch { }
            try
            {
                if (_mutexOwned && _mutex != null)
                {
                    _mutex.ReleaseMutex();
                }
            }
            catch { }
            try { LogService.Instance.Log("애플리케이션 종료"); } catch { }
            base.OnExit(e);
        }
    }
}
