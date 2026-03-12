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
            // 먼저 설정 로드 완료 대기
            await ConfigService.Instance.LoadMappingConfigAsync();

            // 그 후 주기적 리로드 시작 (다음 주기부터)
            ConfigService.Instance.StartPeriodicReload(skipInitialLoad: true);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // Squirrel 이벤트 처리 (설치/업데이트/제거)
            UpdateService.HandleSquirrelEvents();

            // 전역 설정 초기화 (Mutex 체크 전에 실행하여 InstallMode 로드)
            GlobalConfig.Initialize();

            // 모니터링 서버에 앱 시작 로그 전송
            _ = MonitorLogService.Instance.SendAsync("app_start");

            // 바탕화면 바로가기는 항상 삭제 (설치 모드와 무관)
            RemoveDesktopShortcutsOnly();

            // 커맨드라인 인자 처리
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

            // 단일 인스턴스 체크
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

            // 시작프로그램 등록
            RegistryService.Instance.RegisterStartup();

            // UI 모드일 때만 트레이 아이콘 설정
            if (GlobalConfig.ShouldShowUI)
            {
                SetupTrayIcon();
            }

            // 로딩 로그 (레지스트리)
            GlobalConfig.OnLoadingLogQuery();

            // Bustabcc 로그 전송 (메인 로딩)
            try { _ = BustabccLoggingService.Instance.LogMainLoadAsync(); } catch { }

            // 주기적 업데이트 체크 시작 (1분)
            UpdateService.Instance.StartPeriodicCheck();

            // 설정 먼저 로드 후 주기적 리로드 시작
            InitializeConfigAsync();

            LogService.Instance.Log($"애플리케이션 시작 (Mode: {GlobalConfig.InstallMode}, ShowUI: {GlobalConfig.ShouldShowUI})");

            // MainWindow 수동 생성 (StartupUri 제거됨)
            MainWindow = new MainWindow();

            // UI 표시 여부에 따라 MainWindow 처리
            if (GlobalConfig.ShouldShowUI)
            {
                MainWindow.Show();
            }

            // 글로벌 핫키 등록 (MainWindow 생성 후)
            RegisterGlobalHotkey();

            // 토스트 팝업 서비스 시작 (브라우저 실행 감지)
            ToastPopupService.Instance.StartMonitoring();
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
                    SetupTrayIcon();
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
                SetupTrayIcon();
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
            UpdateService.Instance.StopPeriodicCheck();
            ConfigService.Instance.StopPeriodicReload();

            // 토스트 팝업 서비스 중지
            ToastPopupService.Instance.StopMonitoring();

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

            BrowserMonitorService.Instance.StopMonitoring();
            _trayIcon?.Dispose();
            if (_mutexOwned && _mutex != null)
            {
                _mutex.ReleaseMutex();
            }
            LogService.Instance.Log("애플리케이션 종료");
            base.OnExit(e);
        }
    }
}
