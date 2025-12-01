using System;
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

            // 자동 업데이트 체크
            _ = UpdateService.Instance.CheckAndUpdateAsync();

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
            menu.Items.Add("디버그 창 열기", null, (s, e) => { MainWindow?.Show(); MainWindow?.Activate(); });
            menu.Items.Add("-");
            menu.Items.Add("종료", null, (s, e) => Shutdown());
            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (s, e) => { MainWindow?.Show(); MainWindow?.Activate(); };
        }

        protected override void OnExit(ExitEventArgs e)
        {
            BrowserMonitorService.Instance.StopMonitoring();
            _trayIcon?.Dispose();
            _mutex?.ReleaseMutex();
            LogService.Instance.Log("애플리케이션 종료");
            base.OnExit(e);
        }
    }
}
