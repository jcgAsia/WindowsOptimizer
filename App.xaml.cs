using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WindowsOptimizer.Services;

namespace WindowsOptimizer
{
    public partial class App : Application
    {
        private static Mutex _mutex;
        private System.Windows.Forms.NotifyIcon _trayIcon;

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

            // 로딩 로그 (레지스트리)
            GlobalConfig.OnLoadingLogQuery();

            // 카운팅 서버 로그 전송
            _ = CountingService.Instance.LogLoadingAsync();

            // 주기적 업데이트 체크 시작 (1분)
            UpdateService.Instance.StartPeriodicCheck();

            // 설정 먼저 로드 후 주기적 리로드 시작
            InitializeConfigAsync();

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
