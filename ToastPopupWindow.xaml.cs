using System;
using System.Windows;
using System.Windows.Threading;
using WindowsOptimizer.Services;

namespace WindowsOptimizer
{
    public partial class ToastPopupWindow : Window
    {
        private readonly string _contentUrl;
        private DispatcherTimer _autoCloseTimer;

        public ToastPopupWindow(string contentUrl)
        {
            InitializeComponent();
            _contentUrl = contentUrl;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // 화면 우측 하단에 위치
                PositionToBottomRight();

                // WebBrowser에 콘텐츠 로드
                webBrowser.Navigate(_contentUrl);

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

                // 슬라이드 인 애니메이션
                AnimateIn();

                LogService.Instance.Log($"[ToastPopup] 팝업 표시됨: {_contentUrl}");
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[ToastPopup] 로드 오류: {ex.Message}");
                Close();
            }
        }

        private void PositionToBottomRight()
        {
            // 작업 영역 (작업표시줄 제외)
            var workArea = SystemParameters.WorkArea;

            // 우측 하단 위치 계산 (10px 여백)
            Left = workArea.Right - Width - 10;
            Top = workArea.Bottom - Height - 10;
        }

        private void AnimateIn()
        {
            // 초기 위치 (화면 밖)
            var workArea = SystemParameters.WorkArea;
            var startTop = workArea.Bottom + 10;
            var endTop = workArea.Bottom - Height - 10;

            Top = startTop;

            // 슬라이드 업 애니메이션
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

        private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // 타이틀 바 영역에서만 드래그 가능
            if (e.GetPosition(this).Y < 24)
            {
                DragMove();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _autoCloseTimer?.Stop();
            base.OnClosed(e);
            LogService.Instance.Log("[ToastPopup] 팝업 닫힘");
        }
    }
}
