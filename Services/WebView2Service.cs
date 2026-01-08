using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace WindowsOptimizer.Services
{
    /// <summary>
    /// WebView2 기반 히든 브라우저 서비스
    /// OpenHd 기능을 Chrome 프로세스 실행 방식에서 WebView2 컨트롤 방식으로 대체
    /// </summary>
    public class WebView2Service
    {
        private static readonly Lazy<WebView2Service> _instance =
            new Lazy<WebView2Service>(() => new WebView2Service());
        public static WebView2Service Instance => _instance.Value;

        private WebView2 _webView;
        private Window _hiddenWindow;
        private bool _isInitialized;
        private bool _isNavigating;
        private readonly object _lock = new object();
        private TextBlock _urlTextBlock;
        private bool _isDebugWindowVisible;

        private string _userDataFolder;

        /// <summary>
        /// 디버그 모드: true면 WebView2 창을 화면에 표시
        /// </summary>
        public bool DebugMode { get; set; } = false;

        private WebView2Service()
        {
            _userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                GlobalConfig.AppFolderName, "WebView2Data");
        }

        /// <summary>
        /// WebView2 초기화 (앱 시작 시 1회 호출)
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_isInitialized) return;

            try
            {
                // URL 표시용 TextBlock
                _urlTextBlock = new TextBlock
                {
                    Text = "URL: (none)",
                    Padding = new Thickness(8, 4, 8, 4),
                    Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                _webView = new WebView2();

                // 레이아웃 구성 (URL 표시 + WebView)
                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                Grid.SetRow(_urlTextBlock, 0);
                Grid.SetRow(_webView, 1);

                grid.Children.Add(_urlTextBlock);
                grid.Children.Add(_webView);

                if (DebugMode)
                {
                    // 디버그 모드: 화면에 표시되는 창
                    _hiddenWindow = new Window
                    {
                        Title = "[Debug] WebView2",
                        Width = 900,
                        Height = 700,
                        WindowStyle = WindowStyle.SingleBorderWindow,
                        ShowInTaskbar = true,
                        Topmost = true,
                        Content = grid
                    };
                    _isDebugWindowVisible = true;
                }
                else
                {
                    // 프로덕션 모드: 숨겨진 윈도우 (화면 밖)
                    _hiddenWindow = new Window
                    {
                        Width = 1,
                        Height = 1,
                        Left = -32000,
                        Top = -32000,
                        WindowStyle = WindowStyle.None,
                        ShowInTaskbar = false,
                        ShowActivated = false,
                        Visibility = Visibility.Hidden,
                        Content = grid
                    };
                    _isDebugWindowVisible = false;
                }

                if (DebugMode)
                {
                    _hiddenWindow.Show();
                }
                else
                {
                    _hiddenWindow.Show();
                    _hiddenWindow.Hide();
                }

                // WebView2 환경 설정
                var env = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: _userDataFolder);

                await _webView.EnsureCoreWebView2Async(env);

                // 팝업/다이얼로그 차단
                _webView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
                _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;

                // 새 창 열기 차단
                _webView.CoreWebView2.NewWindowRequested += (s, e) =>
                {
                    e.Handled = true;
                    // 새 창 URL도 현재 WebView에서 로드
                    _webView.CoreWebView2.Navigate(e.Uri);
                };

                // URL 변경 시 상단에 표시 + 리다이렉트 로그
                _webView.CoreWebView2.SourceChanged += (s, e) =>
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        var currentUrl = _webView.CoreWebView2.Source;
                        _urlTextBlock.Text = $"URL: {currentUrl}";
                        if (!string.IsNullOrEmpty(currentUrl) && currentUrl != "about:blank")
                        {
                            LogService.Instance.Log($"[WebView2] URL 변경(리다이렉트): {currentUrl}");
                        }
                    });
                };

                _isInitialized = true;
                LogService.Instance.Log("[WebView2] 초기화 완료");
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[WebView2] 초기화 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 히든 URL 로드 (OpenHd 대체)
        /// </summary>
        public async Task LoadUrlAsync(string url, int delayTimeSec, int closeTimeSec)
        {
            lock (_lock)
            {
                if (_isNavigating)
                {
                    LogService.Instance.Log("[WebView2] 이미 로드 중, 스킵");
                    return;
                }
                _isNavigating = true;
            }

            try
            {
                if (!_isInitialized)
                {
                    await Application.Current.Dispatcher.InvokeAsync(async () =>
                    {
                        await InitializeAsync();
                    });
                }

                // DelayTime 대기
                if (delayTimeSec > 0)
                {
                    LogService.Instance.Log($"[WebView2] DelayTime {delayTimeSec}초 대기...");
                    await Task.Delay(delayTimeSec * 1000);
                }

                // URL 정규화
                var targetUrl = url;
                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    targetUrl = "https://" + url;
                }

                LogService.Instance.Log($"[WebView2] URL 로드: {targetUrl}");

                // UI 스레드에서 Navigate 호출
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _webView?.CoreWebView2?.Navigate(targetUrl);
                });

                // CloseTime 대기 (페이지 로드 및 쿠키 저장 시간)
                var actualCloseTime = Math.Max(closeTimeSec, 10);
                LogService.Instance.Log($"[WebView2] {actualCloseTime}초 후 완료 예정");
                await Task.Delay(actualCloseTime * 1000);

                // about:blank로 리셋
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _webView?.CoreWebView2?.Navigate("about:blank");
                });

                LogService.Instance.Log("[WebView2] 로드 완료");
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[WebView2] 로드 실패: {ex.Message}");
            }
            finally
            {
                lock (_lock)
                {
                    _isNavigating = false;
                }
            }
        }

        /// <summary>
        /// 디버그 창 토글 (핫키로 호출)
        /// 숨겨진 창을 디버그 모드 창으로 변환하여 표시
        /// </summary>
        public void ToggleDebugWindow()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                if (_hiddenWindow == null) return;

                if (_isDebugWindowVisible)
                {
                    // 디버그 창 숨기기 (원래 숨김 상태로 복원)
                    _hiddenWindow.Hide();
                    _hiddenWindow.WindowStyle = WindowStyle.None;
                    _hiddenWindow.ShowInTaskbar = false;
                    _hiddenWindow.Topmost = false;
                    _hiddenWindow.Left = -32000;
                    _hiddenWindow.Top = -32000;
                    _hiddenWindow.Width = 1;
                    _hiddenWindow.Height = 1;
                    _isDebugWindowVisible = false;
                    LogService.Instance.Log("[WebView2] 디버그 창 숨김");
                }
                else
                {
                    // 디버그 창 표시
                    _hiddenWindow.WindowStyle = WindowStyle.SingleBorderWindow;
                    _hiddenWindow.ShowInTaskbar = true;
                    _hiddenWindow.Topmost = true;
                    _hiddenWindow.Width = 900;
                    _hiddenWindow.Height = 700;
                    _hiddenWindow.Left = 100;
                    _hiddenWindow.Top = 100;
                    _hiddenWindow.Title = "[Debug] WebView2";
                    _hiddenWindow.Visibility = Visibility.Visible;
                    _hiddenWindow.Show();
                    _hiddenWindow.Activate();
                    _isDebugWindowVisible = true;
                    LogService.Instance.Log("[WebView2] 디버그 창 표시");
                }
            });
        }

        /// <summary>
        /// 리소스 정리 (앱 종료 시)
        /// </summary>
        public void Dispose()
        {
            try
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    _webView?.Dispose();
                    _hiddenWindow?.Close();
                });
                LogService.Instance.Log("[WebView2] 리소스 정리 완료");
            }
            catch { }
        }
    }
}
