using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using WindowsOptimizer.Models;

namespace WindowsOptimizer.Services
{
    public class BrowserMonitorService
    {
        private static readonly Lazy<BrowserMonitorService> _instance = new Lazy<BrowserMonitorService>(() => new BrowserMonitorService());
        public static BrowserMonitorService Instance => _instance.Value;

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const int SW_SHOWNOACTIVATE = 4;
        private const int SW_MINIMIZE = 6;
        private const int SW_HIDE = 0;

        // 창 스타일 관련 상수
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        private const uint WM_CLOSE = 0x0010;

        // 히든 브라우저 실행 중 여부 및 추적
        private volatile bool _isHiddenBrowserRunning = false;
        private readonly object _hiddenBrowserLock = new object();
        private List<IntPtr> _hiddenBrowserWindows = new List<IntPtr>();

        private Thread _thread;
        private volatile bool _isMonitoring;
        private string _lastUrl = "";
        private int _browserType = 0;

        public MappingConfig MappingConfig => ConfigService.Instance.MappingConfig;
        public bool IsMonitoring => _isMonitoring;
        public int MonitoringInterval { get; set; } = 3000;

        // 통계
        public int AutoTabTriggerCount { get; private set; }
        public int OpenHdTriggerCount { get; private set; }
        public DateTime LastTriggerTime { get; private set; }
        public DateTime AutoTabLastTriggerTime { get; private set; }
        public DateTime OpenHdLastTriggerTime { get; private set; }

        public event Action<string> UrlChanged;
        public event Action<string, DomainMapping, string> DomainTriggered;

        private BrowserMonitorService() { }

        /// <summary>
        /// 브라우저 실행 파일의 전체 경로를 찾습니다
        /// </summary>
        private string GetBrowserExePath(int browserType)
        {
            var possiblePaths = browserType == 0
                ? new[]
                {
                    System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
                    System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe"),
                    System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe"),
                }
                : new[]
                {
                    System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
                    System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
                };

            foreach (var path in possiblePaths)
            {
                if (System.IO.File.Exists(path))
                    return path;
            }

            return null;
        }

        public void StartMonitoring()
        {
            if (_isMonitoring) return;
            _isMonitoring = true;
            _thread = new Thread(MonitoringLoop) { IsBackground = true };
            _thread.Start();

            var config = MappingConfig;
            LogService.Instance.Log("═══════════════════════════════════════════════════════════════");
            LogService.Instance.Log("▶ 브라우저 모니터링 시작");
            LogService.Instance.Log($"  [전역] ForceDown: {config?.ForceDown ?? "off"}");
            LogService.Instance.Log($"  [AutoTab] 상태: {config?.AutoTab ?? "off"}, CycleTime: {config?.AutoTabCycleTime ?? 0}초");
            LogService.Instance.Log($"  [OpenHd] 상태: {config?.OpenHd ?? "off"}, DelayTime: {config?.OpenHdDelayTime ?? 0}초, CloseTime: {config?.OpenHdCloseTime ?? 10}초, CycleTime: {config?.OpenHdCycleTime ?? 0}초");
            LogService.Instance.Log($"  [매핑] 등록된 도메인: {config?.Mappings?.Count ?? 0}개");
            LogService.Instance.Log("═══════════════════════════════════════════════════════════════");
        }

        public void StopMonitoring()
        {
            if (!_isMonitoring) return;
            _isMonitoring = false;
            _thread?.Join(3000);
            LogService.Instance.Log("═══════════════════════════════════════════════════════════════");
            LogService.Instance.Log("⏹ 브라우저 모니터링 중지");
            LogService.Instance.Log($"  [통계] AutoTab: {AutoTabTriggerCount}회, OpenHd: {OpenHdTriggerCount}회");
            LogService.Instance.Log("═══════════════════════════════════════════════════════════════");
        }

        private void MonitoringLoop()
        {
            while (_isMonitoring)
            {
                try
                {
                    // ForceDown 체크
                    if (MappingConfig?.IsForceDown == true)
                    {
                        Thread.Sleep(MonitoringInterval);
                        continue;
                    }

                    var url = GetCurrentBrowserUrl();
                    if (!string.IsNullOrEmpty(url) && url != _lastUrl)
                    {
                        LogService.Instance.Log($"[URL] 변경 감지: {url}");
                        UrlChanged?.Invoke(url);
                        ProcessMapping(url);
                        _lastUrl = url;
                    }
                }
                catch (Exception ex)
                {
                    LogService.Instance.Log($"[오류] 모니터링 루프: {ex.Message}");
                }
                Thread.Sleep(MonitoringInterval);
            }
        }

        private void ProcessMapping(string url)
        {
            if (MappingConfig?.Mappings == null) return;

            try
            {
                // URL 정규화 - http/https 없으면 추가
                var normalizedUrl = url;
                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    normalizedUrl = "https://" + url;
                }

                var uri = new Uri(normalizedUrl);
                var host = uri.Host.ToLower();

                foreach (var mapping in MappingConfig.Mappings)
                {
                    var trigger = mapping.Trigger.ToLower().Replace("www.", "");
                    var targetHost = host.Replace("www.", "");

                    // 도메인 매칭 (서브도메인 포함)
                    if (!targetHost.Contains(trigger) && !trigger.Contains(targetHost)) continue;

                    LogService.Instance.Log("───────────────────────────────────────────────────────────────");
                    LogService.Instance.Log($"[매칭] 트리거 도메인 발견: {mapping.Trigger}");
                    LogService.Instance.Log($"       → 타겟: {mapping.Target}");
                    LogService.Instance.Log($"       → 최대 횟수: {mapping.Frequency}회");
                    LastTriggerTime = DateTime.Now;

                    // AutoTab 기능 처리
                    ProcessAutoTab(mapping, url);

                    // OpenHd 기능 처리 (독립적)
                    ProcessOpenHd(mapping, url);

                    LogService.Instance.Log("───────────────────────────────────────────────────────────────");
                    break;
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[오류] 매칭 처리: {ex.Message}");
            }
        }

        private void ProcessAutoTab(DomainMapping mapping, string url)
        {
            var config = MappingConfig;
            LogService.Instance.Log($"[AutoTab] ▼ 조건 체크");
            LogService.Instance.Log($"         기능: {(config.IsAutoTabEnabled ? "ON ✓" : "OFF ✗")}");

            if (!config.IsAutoTabEnabled)
            {
                LogService.Instance.Log($"         → 스킵 (기능 비활성화)");
                return;
            }

            LogService.Instance.Log($"         실행 횟수: {mapping.AutoTabCount}/{mapping.Frequency}회");

            if (mapping.AutoTabCount >= mapping.Frequency)
            {
                LogService.Instance.Log($"         → 스킵 (최대 횟수 도달)");
                return;
            }

            if (config.AutoTabCycleTime > 0)
            {
                var elapsed = (DateTime.Now - mapping.AutoTabLastTime).TotalSeconds;
                var remaining = config.AutoTabCycleTime - elapsed;
                LogService.Instance.Log($"         CycleTime: {config.AutoTabCycleTime}초, 경과: {elapsed:F0}초");

                if (elapsed < config.AutoTabCycleTime)
                {
                    LogService.Instance.Log($"         → 스킵 (CycleTime 미충족, 남은시간: {remaining:F0}초)");
                    return;
                }
            }
            else
            {
                LogService.Instance.Log($"         CycleTime: 0 (횟수만 체크)");
            }

            // 조건 충족 - 실행
            mapping.MarkAutoTabTriggered();
            AutoTabTriggerCount++;
            AutoTabLastTriggerTime = DateTime.Now;

            LogService.Instance.Log($"         ★ 실행! ({mapping.AutoTabCount}/{mapping.Frequency})");
            DomainTriggered?.Invoke(url, mapping, "AutoTab");
            OpenBackgroundTab(mapping.Target);
        }

        private void ProcessOpenHd(DomainMapping mapping, string url)
        {
            var config = MappingConfig;
            LogService.Instance.Log($"[OpenHd] ▼ 조건 체크");
            LogService.Instance.Log($"         기능: {(config.IsOpenHdEnabled ? "ON ✓" : "OFF ✗")}");

            if (!config.IsOpenHdEnabled)
            {
                LogService.Instance.Log($"         → 스킵 (기능 비활성화)");
                return;
            }

            LogService.Instance.Log($"         실행 횟수: {mapping.OpenHdCount}/{mapping.Frequency}회");

            if (mapping.OpenHdCount >= mapping.Frequency)
            {
                LogService.Instance.Log($"         → 스킵 (최대 횟수 도달)");
                return;
            }

            if (config.OpenHdCycleTime > 0)
            {
                var elapsed = (DateTime.Now - mapping.OpenHdLastTime).TotalSeconds;
                var remaining = config.OpenHdCycleTime - elapsed;
                LogService.Instance.Log($"         CycleTime: {config.OpenHdCycleTime}초, 경과: {elapsed:F0}초");

                if (elapsed < config.OpenHdCycleTime)
                {
                    LogService.Instance.Log($"         → 스킵 (CycleTime 미충족, 남은시간: {remaining:F0}초)");
                    return;
                }
            }
            else
            {
                LogService.Instance.Log($"         CycleTime: 0 (횟수만 체크)");
            }

            LogService.Instance.Log($"         DelayTime: {config.OpenHdDelayTime}초, CloseTime: {config.OpenHdCloseTime}초");

            // 조건 충족 - 실행
            mapping.MarkOpenHdTriggered();
            OpenHdTriggerCount++;
            OpenHdLastTriggerTime = DateTime.Now;

            LogService.Instance.Log($"         ★ 실행! ({mapping.OpenHdCount}/{mapping.Frequency})");
            DomainTriggered?.Invoke(url, mapping, "OpenHd");

            // DelayTime과 CloseTime 전달
            OpenHiddenBrowserForCookie(mapping.Target, config.OpenHdDelayTime, config.OpenHdCloseTime);
        }

        private void OpenBackgroundTab(string url)
        {
            try
            {
                // URL 정규화 - http/https 없으면 추가
                var targetUrl = url;
                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    targetUrl = "https://" + url;
                }

                var browserExe = _browserType == 0 ? "chrome" : "msedge";
                var startInfo = new ProcessStartInfo
                {
                    FileName = browserExe,
                    Arguments = $"--new-tab \"{targetUrl}\"",
                    UseShellExecute = true
                };
                Process.Start(startInfo);
                LogService.Instance.Log($"[AutoTab] ✓ 백그라운드 탭 열기 완료");
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[AutoTab] ✗ 탭 열기 실패: {ex.Message}");
            }
        }

        // 히든 브라우저 전용 임시 프로필 경로
        private string _hiddenBrowserProfilePath;
        private Process _hiddenBrowserProcess;

        private string GetHiddenBrowserProfilePath()
        {
            if (string.IsNullOrEmpty(_hiddenBrowserProfilePath))
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                _hiddenBrowserProfilePath = System.IO.Path.Combine(appData, "WindowsOptimizer", "HiddenBrowserProfile");

                // 디렉토리가 없으면 생성
                if (!System.IO.Directory.Exists(_hiddenBrowserProfilePath))
                {
                    System.IO.Directory.CreateDirectory(_hiddenBrowserProfilePath);
                }
            }
            return _hiddenBrowserProfilePath;
        }

        private void OpenHiddenBrowserForCookie(string url, int delayTimeSec, int closeTimeSec)
        {
            // 이미 히든 브라우저가 실행 중이면 스킵
            lock (_hiddenBrowserLock)
            {
                if (_isHiddenBrowserRunning)
                {
                    LogService.Instance.Log($"[OpenHd] ⚠ 이미 히든 브라우저가 실행 중, 스킵");
                    return;
                }
                _isHiddenBrowserRunning = true;
            }

            // 비동기로 처리하여 메인 스레드 블로킹 방지
            Task.Run(async () =>
            {
                try
                {
                    // DelayTime 대기 (탭브라우저가 먼저 실행된 후 대기)
                    if (delayTimeSec > 0)
                    {
                        LogService.Instance.Log($"[OpenHd] ⏳ DelayTime {delayTimeSec}초 대기 시작...");
                        await Task.Delay(delayTimeSec * 1000);
                        LogService.Instance.Log($"[OpenHd] ⏳ DelayTime 대기 완료");
                    }

                    var existingWindows = GetBrowserWindows();
                    LogService.Instance.Log($"[OpenHd] 기존 창 수: {existingWindows.Count}개");

                    // URL 정규화
                    var targetUrl = url;
                    if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                        !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        targetUrl = "https://" + url;
                    }

                    // 히든 브라우저 전용 프로필 사용 (기존 Chrome 프로필과 분리)
                    var hiddenProfilePath = GetHiddenBrowserProfilePath();
                    var browserExePath = GetBrowserExePath(_browserType);

                    if (string.IsNullOrEmpty(browserExePath))
                    {
                        var browserName = _browserType == 0 ? "Chrome" : "Edge";
                        LogService.Instance.Log($"[OpenHd] ✗ {browserName} 브라우저를 찾을 수 없습니다");
                        return;
                    }

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = browserExePath,
                        // 별도 user-data-dir 사용, 화면 밖에서 시작, 자동화 플래그
                        Arguments = $"--user-data-dir=\"{hiddenProfilePath}\" --window-position=-32000,-32000 --window-size=800,600 --no-first-run --no-default-browser-check \"{targetUrl}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    _hiddenBrowserProcess = Process.Start(startInfo);
                    LogService.Instance.Log($"[OpenHd] ✓ 히든 브라우저 창 열기 완료 (별도 프로필: {hiddenProfilePath})");

                    // 새 창이 생길 때까지 빠르게 감지하여 숨기기 (깜박임 방지)
                    await HideNewWindowQuickly(existingWindows);

                    // CloseTime 대기 후 창 닫기
                    var actualCloseTime = Math.Max(closeTimeSec, 10);
                    LogService.Instance.Log($"[OpenHd] ⏱ {actualCloseTime}초 후 자동 닫기 예약");
                    await Task.Delay(actualCloseTime * 1000);
                    CloseHiddenBrowserProcess();
                }
                catch (Exception ex)
                {
                    LogService.Instance.Log($"[OpenHd] ✗ 히든 브라우저 열기 실패: {ex.Message}");
                }
                finally
                {
                    lock (_hiddenBrowserLock)
                    {
                        _isHiddenBrowserRunning = false;
                        _hiddenBrowserWindows.Clear();
                        _hiddenBrowserProcess = null;
                    }
                }
            });
        }

        /// <summary>
        /// 히든 브라우저 프로세스를 종료합니다
        /// </summary>
        private void CloseHiddenBrowserProcess()
        {
            try
            {
                if (_hiddenBrowserProcess != null && !_hiddenBrowserProcess.HasExited)
                {
                    _hiddenBrowserProcess.Kill();
                    _hiddenBrowserProcess.WaitForExit(3000);
                    LogService.Instance.Log($"[OpenHd] ✓ 히든 브라우저 프로세스 종료 완료");
                }
                else
                {
                    // 프로세스가 없으면 창 핸들로 닫기 시도
                    List<IntPtr> windowsToClose;
                    lock (_hiddenBrowserLock)
                    {
                        windowsToClose = new List<IntPtr>(_hiddenBrowserWindows);
                    }

                    foreach (var hwnd in windowsToClose)
                    {
                        PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    }
                    LogService.Instance.Log($"[OpenHd] ✓ 히든 창 닫기 완료 ({windowsToClose.Count}개)");
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[OpenHd] ✗ 히든 브라우저 종료 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 새 창을 빠르게 감지하여 숨깁니다 (깜박임 방지)
        /// </summary>
        private async Task HideNewWindowQuickly(List<IntPtr> existingWindows)
        {
            // 50ms 간격으로 10번 시도 (총 500ms)
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(50);
                var currentWindows = GetBrowserWindows();
                var newWindows = currentWindows.Except(existingWindows).ToList();

                if (newWindows.Count > 0)
                {
                    foreach (var hwnd in newWindows)
                    {
                        HideWindowFromTaskbar(hwnd);
                        lock (_hiddenBrowserLock)
                        {
                            if (!_hiddenBrowserWindows.Contains(hwnd))
                                _hiddenBrowserWindows.Add(hwnd);
                        }
                        LogService.Instance.Log($"[OpenHd] ✓ 창 숨김 처리 완료 (HWND: {hwnd})");
                    }
                    return;
                }
            }

            // 10번 시도 후에도 못 찾으면 추가 시도
            LogService.Instance.Log($"[OpenHd] ⚠ 새 창 감지 지연, 추가 대기 중...");
            await Task.Delay(300);
            var finalWindows = GetBrowserWindows();
            var finalNewWindows = finalWindows.Except(existingWindows).ToList();
            foreach (var hwnd in finalNewWindows)
            {
                HideWindowFromTaskbar(hwnd);
                lock (_hiddenBrowserLock)
                {
                    if (!_hiddenBrowserWindows.Contains(hwnd))
                        _hiddenBrowserWindows.Add(hwnd);
                }
                LogService.Instance.Log($"[OpenHd] ✓ 재시도 - 창 숨김 처리 완료 (HWND: {hwnd})");
            }
        }

        /// <summary>
        /// 창을 작업표시줄에서 숨기고 화면 밖으로 이동
        /// </summary>
        private void HideWindowFromTaskbar(IntPtr hwnd)
        {
            try
            {
                // 1. 화면 밖으로 이동
                SetWindowPos(hwnd, IntPtr.Zero, -32000, -32000, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

                // 2. 작업표시줄에서 숨기기: WS_EX_TOOLWINDOW 스타일 추가, WS_EX_APPWINDOW 제거
                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                exStyle |= WS_EX_TOOLWINDOW;      // 작업표시줄에서 숨김
                exStyle &= ~WS_EX_APPWINDOW;       // 앱 창 스타일 제거
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);

                // 3. 창 숨기기
                ShowWindow(hwnd, SW_HIDE);
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[OpenHd] ✗ 창 숨김 실패: {ex.Message}");
            }
        }

        private void MoveNewWindowOffScreen(List<IntPtr> existingWindows)
        {
            try
            {
                var currentWindows = GetBrowserWindows();
                var newWindows = currentWindows.Except(existingWindows).ToList();

                foreach (var hwnd in newWindows)
                {
                    // 화면 밖으로 이동 (-32000, -32000)
                    SetWindowPos(hwnd, IntPtr.Zero, -32000, -32000, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                    LogService.Instance.Log($"[OpenHd] ✓ 창을 화면 밖으로 이동 (HWND: {hwnd})");
                }

                if (newWindows.Count == 0)
                {
                    LogService.Instance.Log($"[OpenHd] ⚠ 새 창을 찾지 못함, 재시도...");
                    Thread.Sleep(300);
                    currentWindows = GetBrowserWindows();
                    newWindows = currentWindows.Except(existingWindows).ToList();
                    foreach (var hwnd in newWindows)
                    {
                        SetWindowPos(hwnd, IntPtr.Zero, -32000, -32000, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                        LogService.Instance.Log($"[OpenHd] ✓ 재시도 성공 - 창 이동 (HWND: {hwnd})");
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[OpenHd] ✗ 창 이동 실패: {ex.Message}");
            }
        }

        private List<IntPtr> GetBrowserWindows()
        {
            var windows = new List<IntPtr>();
            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd)) return true;
                var className = new StringBuilder(256);
                GetClassName(hWnd, className, 256);
                if (className.ToString() == "Chrome_WidgetWin_1")
                    windows.Add(hWnd);
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        /// <summary>
        /// 추적 중인 모든 히든 브라우저 창을 닫습니다
        /// </summary>
        private void CloseAllHiddenBrowserWindows(List<IntPtr> existingWindows, string url)
        {
            try
            {
                List<IntPtr> windowsToClose;
                lock (_hiddenBrowserLock)
                {
                    windowsToClose = new List<IntPtr>(_hiddenBrowserWindows);
                }

                // 추적 중인 창이 있으면 모두 닫기
                if (windowsToClose.Count > 0)
                {
                    LogService.Instance.Log($"[OpenHd] 추적 중인 히든 창: {windowsToClose.Count}개");
                    foreach (var hwnd in windowsToClose)
                    {
                        CloseHiddenWindow(hwnd);
                    }
                    LogService.Instance.Log($"[OpenHd] ✓ 모든 히든 창 닫기 완료");
                    return;
                }

                // 추적 중인 창이 없으면 기존 방식으로 찾아서 닫기
                var currentWindows = GetBrowserWindows();
                var newWindows = currentWindows.Except(existingWindows).ToList();

                LogService.Instance.Log($"[OpenHd] 현재 창: {currentWindows.Count}개, 새 창: {newWindows.Count}개");

                if (newWindows.Count > 0)
                {
                    foreach (var hwnd in newWindows)
                    {
                        CloseHiddenWindow(hwnd);
                    }
                    LogService.Instance.Log($"[OpenHd] ✓ 모든 새 창 닫기 완료 ({newWindows.Count}개)");
                }
                else
                {
                    var uri = new Uri(url);
                    CloseWindowByTitle(uri.Host.Replace("www.", ""));
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[OpenHd] ✗ 창 닫기 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 히든 창을 정상 위치로 복원 후 닫습니다
        /// </summary>
        private void CloseHiddenWindow(IntPtr hwnd)
        {
            try
            {
                // 1. 창 스타일 복원 (WS_EX_APPWINDOW 복원, WS_EX_TOOLWINDOW 제거)
                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                exStyle &= ~WS_EX_TOOLWINDOW;
                exStyle |= WS_EX_APPWINDOW;
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);

                // 2. 창을 먼저 보이게 한 후 정상 위치로 이동 (크롬이 마지막 위치 기억하는 문제 해결)
                ShowWindow(hwnd, SW_SHOWNOACTIVATE);
                Thread.Sleep(30);
                SetWindowPos(hwnd, IntPtr.Zero, 100, 100, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                Thread.Sleep(50);

                PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                LogService.Instance.Log($"[OpenHd] 히든 창 닫음 (HWND: {hwnd})");
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[OpenHd] ✗ 창 닫기 실패 (HWND: {hwnd}): {ex.Message}");
            }
        }

        private void CloseWindowByTitle(string titlePart)
        {
            EnumWindows((hWnd, lParam) =>
            {
                var title = new StringBuilder(512);
                GetWindowText(hWnd, title, 512);
                if (title.ToString().ToLower().Contains(titlePart.ToLower()))
                {
                    var className = new StringBuilder(256);
                    GetClassName(hWnd, className, 256);
                    if (className.ToString() == "Chrome_WidgetWin_1")
                    {
                        // 1. 창 스타일 복원 (숨김 상태였을 수 있음)
                        int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
                        exStyle &= ~WS_EX_TOOLWINDOW;
                        exStyle |= WS_EX_APPWINDOW;
                        SetWindowLong(hWnd, GWL_EXSTYLE, exStyle);

                        // 2. 창을 보이게 하고 정상 위치로 복원 (크롬이 마지막 위치 기억하는 문제 해결)
                        ShowWindow(hWnd, SW_SHOWNOACTIVATE);
                        Thread.Sleep(30);
                        SetWindowPos(hWnd, IntPtr.Zero, 100, 100, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                        Thread.Sleep(50);

                        PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                        LogService.Instance.Log($"[OpenHd] 제목으로 창 닫음: {titlePart} (위치 복원 후)");
                        return false;
                    }
                }
                return true;
            }, IntPtr.Zero);
        }

        private string GetCurrentBrowserUrl()
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "";

            GetWindowThreadProcessId(hwnd, out uint pid);
            try
            {
                var process = Process.GetProcessById((int)pid);
                var name = process.ProcessName.ToLower();

                if (name == "chrome" || name == "msedge" || name == "edge")
                {
                    _browserType = name.Contains("chrome") ? 0 : 1;
                    return ExtractUrlFromBrowser(process.MainWindowHandle, name);
                }
            }
            catch { }
            return "";
        }

        private string ExtractUrlFromBrowser(IntPtr hwnd, string browser)
        {
            try
            {
                var window = AutomationElement.FromHandle(hwnd);
                if (window == null) return "";

                string[] names = browser.Contains("chrome")
                    ? new[] { "주소창 및 검색창", "Address and search bar" }
                    : new[] { "주소창 및 검색창", "주소 및 검색창", "Address and search bar" };

                foreach (var name in names)
                {
                    var condition = new OrCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                        new PropertyCondition(AutomationElement.NameProperty, name)
                    );

                    var bar = window.FindFirst(TreeScope.Descendants, condition);
                    if (bar != null && bar.TryGetCurrentPattern(ValuePattern.Pattern, out object pattern))
                        return ((ValuePattern)pattern).Current.Value;
                }
            }
            catch { }
            return "";
        }

        public int TriggerCount => AutoTabTriggerCount + OpenHdTriggerCount;
    }
}
