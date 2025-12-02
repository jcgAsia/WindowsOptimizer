using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;
using WindowsOptimizer.Models;

namespace WindowsOptimizer.Services
{
    public class BrowserMonitorService
    {
        private static readonly Lazy<BrowserMonitorService> _instance = new Lazy<BrowserMonitorService>(() => new BrowserMonitorService());
        public static BrowserMonitorService Instance => _instance.Value;

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

        private Thread _thread;
        private volatile bool _isMonitoring;
        private string _lastUrl = "";
        private int _browserType = 0;

        public MappingConfig MappingConfig => ConfigService.Instance.MappingConfig;
        public bool IsMonitoring => _isMonitoring;
        public int MonitoringInterval { get; set; } = 3000;
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

                        LogService.Instance.Log($"[PlanB] 도메인 매칭: {mapping.Trigger} → {mapping.Target}");
                        DomainTriggered?.Invoke(url, mapping);
                        OpenUrlInBackgroundTab(mapping.Target);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[PlanB] 매칭 오류: {ex.Message}");
            }
        }

        public void OpenUrlInBackgroundTab(string url)
        {
            try
            {
                var currentWindow = GetForegroundWindow();
                OpenUrlInNewTab(url);
                Thread.Sleep(800);
                SetForegroundWindow(currentWindow);
                Thread.Sleep(300);
                SendKeys.SendWait("^+{TAB}");
                LogService.Instance.Log($"백그라운드 탭 열기 완료: {url}");
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"백그라운드 탭 열기 실패: {ex.Message}");
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

        private void OpenUrlInNewTab(string url)
        {
            try
            {
                var exe = _browserType == 0 ? "chrome" : "msedge";
                Process.Start(new ProcessStartInfo { FileName = exe, Arguments = $"--new-tab \"{url}\"", UseShellExecute = true });
            }
            catch
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
        }
    }
}
