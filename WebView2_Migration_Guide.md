# WebView2 Migration Guide for WindowsOptimizer

## 개요
OpenHd(히든 브라우저) 기능을 Chrome 프로세스 실행 방식에서 WebView2 컨트롤 방식으로 변경

## 현재 문제점
1. 작업표시줄 마우스오버 시 히든창 노출
2. 히든 브라우저 다수 열림
3. 브라우저 깜박임
4. 별도 프로필로 인한 쿠키 분리 → 실적 미연결

## 변경 목표
- 작업표시줄 완전 미노출
- 깜박임 없음
- 안정적인 쿠키 저장
- 단일 WebView2 인스턴스 관리

---

## 1. NuGet 패키지 추가

### 파일: `WindowsOptimizer.csproj`

```xml
<ItemGroup>
  <PackageReference Include="Clowd.Squirrel" Version="2.11.1" />
  <PackageReference Include="CommunityToolkit.Mvvm" Version="8.2.2" />
  <!-- 추가 -->
  <PackageReference Include="Microsoft.Web.WebView2" Version="1.0.2903.40" />
</ItemGroup>
```

---

## 2. 새 서비스 생성

### 파일: `Services/WebView2Service.cs` (신규)

```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace WindowsOptimizer.Services
{
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

        private string _userDataFolder;

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
                // 숨겨진 윈도우 생성 (화면 밖)
                _hiddenWindow = new Window
                {
                    Width = 1,
                    Height = 1,
                    Left = -32000,
                    Top = -32000,
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Visibility = Visibility.Hidden
                };

                _webView = new WebView2
                {
                    Width = 800,
                    Height = 600
                };

                _hiddenWindow.Content = _webView;
                _hiddenWindow.Show();
                _hiddenWindow.Hide();

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
```

---

## 3. BrowserMonitorService 수정

### 파일: `Services/BrowserMonitorService.cs`

#### 3.1 삭제할 필드 및 메서드

```csharp
// 삭제 대상 필드 (라인 ~45-55)
private volatile bool _isHiddenBrowserRunning = false;
private readonly object _hiddenBrowserLock = new object();
private List<IntPtr> _hiddenBrowserWindows = new List<IntPtr>();
private string _hiddenBrowserProfilePath;
private Process _hiddenBrowserProcess;

// 삭제 대상 메서드
- GetHiddenBrowserProfilePath()
- OpenHiddenBrowserForCookie()
- CloseHiddenBrowserProcess()
- HideNewWindowQuickly()
- HideWindowFromTaskbar()
- MoveNewWindowOffScreen()
- GetBrowserWindows()
- CloseAllHiddenBrowserWindows()
- CloseHiddenWindow()
- CloseWindowByTitle()
```

#### 3.2 수정할 메서드

**ProcessOpenHd 메서드 수정 (라인 ~230)**

기존:
```csharp
OpenHiddenBrowserForCookie(mapping.Target, config.OpenHdDelayTime, config.OpenHdCloseTime);
```

변경:
```csharp
// WebView2로 URL 로드
_ = WebView2Service.Instance.LoadUrlAsync(
    mapping.Target, 
    config.OpenHdDelayTime, 
    config.OpenHdCloseTime);
```

#### 3.3 삭제할 DllImport (더 이상 필요 없음)

```csharp
// 아래 DllImport들 중 히든 브라우저 전용 항목 삭제 가능
// (AutoTab에서 사용하지 않는다면)
[DllImport("user32.dll")] private static extern bool SetWindowPos(...)
[DllImport("user32.dll")] private static extern bool ShowWindow(...)
[DllImport("user32.dll")] private static extern int GetWindowLong(...)
[DllImport("user32.dll")] private static extern int SetWindowLong(...)

// 관련 상수들도 삭제
private const int GWL_EXSTYLE = -20;
private const int WS_EX_TOOLWINDOW = 0x00000080;
private const int WS_EX_APPWINDOW = 0x00040000;
private const int SW_SHOWNOACTIVATE = 4;
private const int SW_MINIMIZE = 6;
private const int SW_HIDE = 0;
```

---

## 4. App.xaml.cs 수정

### 파일: `App.xaml.cs`

#### 4.1 OnStartup에 WebView2 초기화 추가

```csharp
// InitializeConfigAsync 메서드 수정 또는 별도 추가
private async void InitializeConfigAsync()
{
    // 기존 코드
    await ConfigService.Instance.LoadMappingConfigAsync();
    ConfigService.Instance.StartPeriodicReload(skipInitialLoad: true);
    
    // WebView2 초기화 추가
    await WebView2Service.Instance.InitializeAsync();
}
```

#### 4.2 OnExit에 정리 코드 추가

```csharp
protected override void OnExit(ExitEventArgs e)
{
    // 기존 코드...
    UpdateService.Instance.StopPeriodicCheck();
    ConfigService.Instance.StopPeriodicReload();
    
    // WebView2 정리 추가
    WebView2Service.Instance.Dispose();
    
    // 기존 코드 계속...
    BrowserMonitorService.Instance.StopMonitoring();
    // ...
}
```

---

## 5. 변경 파일 요약

| 파일 | 작업 |
|------|------|
| `WindowsOptimizer.csproj` | WebView2 NuGet 패키지 추가 |
| `Services/WebView2Service.cs` | **신규 생성** |
| `Services/BrowserMonitorService.cs` | 히든 브라우저 코드 제거, WebView2 호출로 대체 |
| `App.xaml.cs` | WebView2 초기화/정리 추가 |

---

## 6. 테스트 체크리스트

- [ ] 앱 시작 시 WebView2 정상 초기화
- [ ] OpenHd 트리거 시 URL 로드 확인
- [ ] 작업표시줄에 창 미노출 확인
- [ ] DelayTime/CloseTime 정상 작동
- [ ] 쿠키 저장 확인 (WebView2Data 폴더)
- [ ] 앱 종료 시 리소스 정리
- [ ] AutoTab 기능 영향 없음

---

## 7. 주의사항

1. **WebView2 런타임 필요**: 사용자 PC에 WebView2 런타임 설치 필요 (Windows 10 2004+ 기본 포함)
2. **쿠키 분리**: WebView2는 자체 쿠키 저장소 사용 (Chrome과 별도)
3. **첫 실행 지연**: WebView2 초기화에 1-2초 소요 가능

---

## 8. 빌드 후 배포

```powershell
# 버전 업데이트 후 빌드
.\build-release.ps1 -Version "1.2.0"
```
