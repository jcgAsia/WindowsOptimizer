using System.Threading;
using System.Windows;
using WindowsOptimizer.Services;

namespace WindowsOptimizer
{
    public partial class App : Application
    {
        private static Mutex _mutex;
        private System.Windows.Forms.NotifyIcon _trayIcon;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Squirrel 이벤트 처리 (설치/업데이트/제거)
            UpdateService.HandleSquirrelEvents();

            // 단일 인스턴스 체크
            _mutex = new Mutex(true, GlobalConfig.MutexName, out bool isNew);
            if (!isNew)
            {
                MessageBox.Show("이미 실행 중입니다.", "알림");
                Shutdown();
                return;
            }

            base.OnStartup(e);

            // 전역 설정 초기화
            GlobalConfig.Initialize();

            // 시작프로그램 등록
            RegistryService.Instance.RegisterStartup();

            // 트레이 아이콘 설정
            SetupTrayIcon();

            // 로딩 로그
            GlobalConfig.OnLoadingLogQuery();

            // 주기적 업데이트 체크 시작 (1분)
            UpdateService.Instance.StartPeriodicCheck();

            // 주기적 설정 리로드 시작 (1분)
            ConfigService.Instance.StartPeriodicReload();

            LogService.Instance.Log("애플리케이션 시작");

#if !DEBUG
            MainWindow?.Hide();
#endif
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

        protected override void OnExit(ExitEventArgs e)
        {
            // 주기적 체크 중지
            UpdateService.Instance.StopPeriodicCheck();
            ConfigService.Instance.StopPeriodicReload();

            BrowserMonitorService.Instance.StopMonitoring();
            _trayIcon?.Dispose();
            _mutex?.ReleaseMutex();
            LogService.Instance.Log("애플리케이션 종료");
            base.OnExit(e);
        }
    }
}
