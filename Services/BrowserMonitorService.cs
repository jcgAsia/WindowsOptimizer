using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
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

            // WebView2로 URL 로드
            _ = WebView2Service.Instance.LoadUrlAsync(
                mapping.Target,
                config.OpenHdDelayTime,
                config.OpenHdCloseTime);
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
