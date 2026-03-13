using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using WindowsOptimizer.Services;

namespace WindowsOptimizer
{
    public partial class ToastPopupWindow : Window
    {
        private readonly string _contentUrl;
        private DispatcherTimer _autoCloseTimer;
        private bool _isNavigationCompleted;
        private bool _isInitialNavigation = true;

        public ToastPopupWindow(string contentUrl)
        {
            InitializeComponent();
            _contentUrl = contentUrl;

            // 초기에 화면 밖에 숨김 (로드 완료 전까지)
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 10;
            Top = workArea.Bottom + 100;
            Opacity = 0;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // WebView2 초기화
                await webView.EnsureCoreWebView2Async(null);

                // WebView2 이벤트 등록 - 새 창 열기 차단
                webView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;

                // 네비게이션 시작 이벤트 - 다른 URL로 이동 차단
                webView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;

                // 네비게이션 완료 이벤트 등록
                webView.NavigationCompleted += WebView_NavigationCompleted;

                // 콘텐츠 로드 시작
                webView.Source = new Uri(_contentUrl);

                LogService.Instance.Log($"[ToastPopup] WebView2 초기화 완료, URL 로드 시작: {_contentUrl}");
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[ToastPopup] 로드 오류: {ex.Message}");
                Close();
            }
        }

        private void CoreWebView2_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            // 새 창 열기 요청을 차단하고 기본 브라우저로 열기
            e.Handled = true;
            LogService.Instance.Log($"[ToastPopup] NewWindowRequested 차단, URL: {e.Uri}");
            OpenUrlInDefaultBrowser(e.Uri);
        }

        private void CoreWebView2_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            // 초기 로드는 허용
            if (_isInitialNavigation)
            {
                _isInitialNavigation = false;
                LogService.Instance.Log($"[ToastPopup] 초기 네비게이션: {e.Uri}");
                return;
            }

            // 이후 다른 URL로 이동하려는 경우 차단하고 기본 브라우저로 열기
            e.Cancel = true;
            LogService.Instance.Log($"[ToastPopup] NavigationStarting 차단, URL: {e.Uri}");
            OpenUrlInDefaultBrowser(e.Uri);
        }

        private void WebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            try
            {
                if (_isNavigationCompleted) return;
                _isNavigationCompleted = true;

                if (!e.IsSuccess)
                {
                    LogService.Instance.Log($"[ToastPopup] 페이지 로드 실패: {e.WebErrorStatus}");
                    Close();
                    return;
                }

                // 페이지 로드 완료 후 팝업 표시
                Opacity = 1;
                AnimateIn();

                // 30초 후 자동 닫기
                _autoCloseTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(30)
                };
                _autoCloseTimer.Tick += (s, args) =>
                {
                    _autoCloseTimer.Stop();
                    CloseWithAnimation();
                };
                _autoCloseTimer.Start();

                LogService.Instance.Log($"[ToastPopup] 팝업 표시됨: {_contentUrl}");
            }
            catch { }
        }

        private void AnimateIn()
        {
            var workArea = SystemParameters.WorkArea;
            var startTop = workArea.Bottom + 10;
            var endTop = workArea.Bottom - Height - 10;

            Top = startTop;

            var animation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = startTop,
                To = endTop,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                }
            };

            BeginAnimation(TopProperty, animation);
        }

        private void CloseWithAnimation()
        {
            try
            {
                var workArea = SystemParameters.WorkArea;
                var endTop = workArea.Bottom + 10;

                var animation = new System.Windows.Media.Animation.DoubleAnimation
                {
                    To = endTop,
                    Duration = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                    {
                        EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn
                    }
                };

                animation.Completed += (s, e) => Close();
                BeginAnimation(TopProperty, animation);
            }
            catch
            {
                Close();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _autoCloseTimer?.Stop();
            CloseWithAnimation();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 타이틀 바 영역에서 드래그
            if (e.GetPosition(this).Y < 30)
            {
                try { DragMove(); } catch { }
            }
        }

        private void ContentArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            LogService.Instance.Log($"[ToastPopup] ContentArea 클릭됨");
            // 콘텐츠 영역 클릭 시 기본 브라우저로 URL 열기
            OpenUrlInDefaultBrowser(_contentUrl);
        }

        private void OpenUrlInDefaultBrowser(string url)
        {
            try
            {
                _autoCloseTimer?.Stop();

                LogService.Instance.Log($"[ToastPopup] 기본 브라우저로 열기 시도: {url}");

                // 명시적으로 explorer.exe를 통해 URL 열기 (확실한 기본 브라우저 실행)
                var psi = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{url}\"",
                    UseShellExecute = false
                };

                var process = Process.Start(psi);
                LogService.Instance.Log($"[ToastPopup] explorer.exe로 URL 열기 완료, ProcessId: {process?.Id}");

                CloseWithAnimation();
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[ToastPopup] 브라우저 열기 실패: {ex.Message}");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _autoCloseTimer?.Stop();

            if (webView?.CoreWebView2 != null)
            {
                webView.CoreWebView2.NewWindowRequested -= CoreWebView2_NewWindowRequested;
                webView.CoreWebView2.NavigationStarting -= CoreWebView2_NavigationStarting;
            }

            if (webView != null)
            {
                webView.NavigationCompleted -= WebView_NavigationCompleted;
                webView.Dispose();
            }

            base.OnClosed(e);
            LogService.Instance.Log("[ToastPopup] 팝업 닫힘");
        }
    }
}
