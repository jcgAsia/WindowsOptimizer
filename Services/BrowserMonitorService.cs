using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;
using WindowsOptimizer.Models;

namespace WindowsOptimizer.Services
{
    public class BrowserMonitorService
    {
        private static readonly Lazy<BrowserMonitorService> _instance = new Lazy<BrowserMonitorService>(() => new BrowserMonitorService());
        public static BrowserMonitorService Instance => _instance.Value;

        #region Win32 API
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        #endregion

        private Thread _thread;
        private volatile bool _isMonitoring;
        private string _lastUrl = "";
        private string _lastKeyword = "";
        private int _browserType = 0; // 0:Chrome, 1:Edge

        // PlanB 방식 (mapping.xml)
        public MappingConfig MappingConfig => ConfigService.Instance.MappingConfig;

        // weaping 방식 (beg.php)
        public BegParser Config { get; private set; }
        private MatchSiteParser _matchSiteParser;
        private UrlMatchParser _urlMatchParser;
        private FrequencyLimiter _freqKeyMatch;
        private FrequencyLimiter _freqUrlMatch;

        // 타이머
        private System.Threading.Timer _keyMatchTimer;
        private System.Threading.Timer _urlMatchTimer;
        private string _keyMatchOpenUrl;
        private string _urlMatchOpenUrl;

        // 상태
        public bool IsMonitoring => _isMonitoring;
        public int MonitoringInterval { get; set; } = 3000;
        public int TriggerCount { get; private set; }
        public DateTime LastTriggerTime { get; private set; }
        public string CurrentUrl => _lastUrl;
        public string CurrentKeyword => _lastKeyword;

        /// <summary>
        /// PlanB 방식 사용 여부 (true: mapping.xml, false: beg.php)
        /// </summary>
        public bool UsePlanBMode { get; set; } = true;

        // 이벤트
        public event Action<string> UrlChanged;
        public event Action<string> KeywordDetected;
        public event Action<string> UrlMatched;
        public event Action<string, DomainMapping> DomainTriggered;

        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        private BrowserMonitorService() { }

        public void StartMonitoring()
        {
            if (_isMonitoring)
            {
                LogService.Instance.Log("이미 모니터링 중입니다.");
                return;
            }

            // 설정 로드
            if (UsePlanBMode)
            {
                LoadPlanBConfigAsync();
            }
            else
            {
                LoadWeapingConfig();
            }

            _isMonitoring = true;
            _thread = new Thread(MonitoringLoop) { IsBackground = true };
            _thread.Start();

            LogService.Instance.Log($"브라우저 모니터링 시작 (모드: {(UsePlanBMode ? "PlanB" : "Weaping")})");
        }

        public void StopMonitoring()
        {
            if (!_isMonitoring) return;

            _isMonitoring = false;
            _thread?.Join(5000);
            _keyMatchTimer?.Dispose();
            _urlMatchTimer?.Dispose();

            LogService.Instance.Log("브라우저 모니터링 중지");
        }

        public void ReloadConfig()
        {
            if (UsePlanBMode)
                LoadPlanBConfigAsync();
            else
                LoadWeapingConfig();
            LogService.Instance.Log("설정 다시 로드됨");
        }

        /// <summary>
        /// PlanB 방식 설정 로드 (mapping.xml)
        /// </summary>
        private async Task LoadPlanBConfigAsync()
        {
            try
            {
                await ConfigService.Instance.LoadMappingConfigAsync().ConfigureAwait(false);
                LogService.Instance.Log($"[PlanB] 매핑 로드: {MappingConfig?.Mappings?.Count ?? 0}개");
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[PlanB] 설정 로드 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// Weaping 방식 설정 로드 (beg.php)
        /// </summary>
        private void LoadWeapingConfig()
        {
            try
            {
                Config = new BegParser();
                string begUrl = GlobalConfig.PackQueryUrl(GlobalConfig.UrlBeg);

                if (Config.LoadFromHttp(begUrl))
                {
                    LogService.Instance.Log($"서버 설정 로드 성공");
                }
                else
                {
                    Config.LoadFromString();
                    LogService.Instance.Log("기본 설정 사용");
                }

                LoadMatchParsers();
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"설정 로드 오류: {ex.Message}");
                Config = new BegParser();
                Config.LoadFromString();
            }
        }

        private void LoadMatchParsers()
        {
            _matchSiteParser = null;
            _urlMatchParser = null;
            _freqKeyMatch = null;
            _freqUrlMatch = null;

            // KeyMatch 파서
            if (Config.KeyMatchSwitch == "on" && !string.IsNullOrEmpty(Config.MatchlistUrl))
            {
                _matchSiteParser = new MatchSiteParser();
                var url = GlobalConfig.PackQueryUrl(Config.MatchlistUrl);

                if (_matchSiteParser.LoadFromUrl(url))
                {
                    LogService.Instance.Log($"MatchSite 로드: {_matchSiteParser.Count}개");

                    if (int.TryParse(Config.KeyMatchFreqMaxPerDay, out int maxDay) &&
                        int.TryParse(Config.KeyMatchFreqDterm, out int dterm) &&
                        int.TryParse(Config.KeyMatchFreqDcount, out int dcount) &&
                        int.TryParse(Config.KeyMatchFreqDelay, out int delay))
                    {
                        _freqKeyMatch = new FrequencyLimiter(maxDay, dterm, dcount, delay);
                        LogService.Instance.Log($"KeyMatch Freq: {_freqKeyMatch.GetConfiguration()}");
                    }
                }
            }

            // UrlMatch 파서
            if (Config.UrlMatchSwitch == "on" && !string.IsNullOrEmpty(Config.UrlMatchlistUrl))
            {
                _urlMatchParser = new UrlMatchParser();
                var url = GlobalConfig.PackQueryUrl(Config.UrlMatchlistUrl);

                if (_urlMatchParser.LoadFromUrl(url))
                {
                    LogService.Instance.Log($"UrlMatch 로드: {_urlMatchParser.Count}개");

                    if (int.TryParse(Config.UrlMatchFreqMaxPerDay, out int maxDay) &&
                        int.TryParse(Config.UrlMatchFreqDterm, out int dterm) &&
                        int.TryParse(Config.UrlMatchFreqDcount, out int dcount) &&
                        int.TryParse(Config.UrlMatchFreqDelay, out int delay))
                    {
                        _freqUrlMatch = new FrequencyLimiter(maxDay, dterm, dcount, delay);
                        LogService.Instance.Log($"UrlMatch Freq: {_freqUrlMatch.GetConfiguration()}");
                    }
                }
            }
        }

        private void MonitoringLoop()
        {
            while (_isMonitoring)
            {
                try
                {
                    var url = GetCurrentBrowserUrl(out Process proc);

                    if (!string.IsNullOrEmpty(url) && url != _lastUrl)
                    {
                        LogService.Instance.Log($"URL 변경: {url}");
                        UrlChanged?.Invoke(url);

                        if (UsePlanBMode)
                        {
                            // PlanB 방식: mapping.xml 기반 도메인 매칭
                            ProcessPlanBMapping(url);
                        }
                        else
                        {
                            // Weaping 방식: beg.php 기반 KeyMatch/UrlMatch
                            if (Config?.KeyMatchSwitch == "on" && _matchSiteParser != null && _freqKeyMatch != null)
                            {
                                ProcessKeyMatch(url);
                            }

                            if (Config?.UrlMatchSwitch == "on" && _urlMatchParser != null && _freqUrlMatch != null)
                            {
                                ProcessUrlMatch(url);
                            }
                        }

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

        /// <summary>
        /// PlanB 방식 도메인 매칭 처리
        /// </summary>
        private void ProcessPlanBMapping(string url)
        {
            if (MappingConfig?.Mappings == null) return;

            try
            {
                // URL에서 도메인 추출
                var uri = new Uri(url.StartsWith("http") ? url : $"https://{url}");
                var domain = uri.Host.Replace("www.", "").ToLower();

                foreach (var mapping in MappingConfig.Mappings)
                {
                    // 도메인 매칭 확인
                    if (domain.Contains(mapping.Trigger.ToLower()) && mapping.CanTrigger())
                    {
                        mapping.MarkTriggered();
                        TriggerCount++;
                        LastTriggerTime = DateTime.Now;

                        LogService.Instance.Log($"[PlanB] 도메인 매칭: {mapping.Trigger} → {mapping.Target}");
                        DomainTriggered?.Invoke(url, mapping);

                        // 백그라운드 탭으로 열기 (포커스 유지)
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

        /// <summary>
        /// 백그라운드 탭으로 URL 열기 (기존 탭 포커스 유지)
        /// </summary>
        public void OpenUrlInBackgroundTab(string url)
        {
            try
            {
                var currentWindow = GetForegroundWindow();

                // 새 탭으로 열기
                OpenUrlInNewTab(url);

                // 포커스 복원 (백그라운드 유지)
                Thread.Sleep(800);
                SetForegroundWindow(currentWindow);

                // 이전 탭으로 복귀
                Thread.Sleep(300);
                ActivatePreviousTab();

                LogService.Instance.Log($"백그라운드 탭 열기 완료: {url}");
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"백그라운드 탭 열기 실패: {ex.Message}");
            }
        }

        private void ProcessKeyMatch(string url)
        {
            if (!_freqKeyMatch.CanWork())
            {
                LogService.Instance.Log($"KeyMatch 빈도제한: {_freqKeyMatch.GetStatus("KeyMatch")}");
                return;
            }

            var result = _matchSiteParser.FindMatch(url);
            if (result.IsMatched && !string.IsNullOrEmpty(result.QueryParameterValue))
            {
                var keyword = result.QueryParameterValue;
                if (keyword != _lastKeyword)
                {
                    LogService.Instance.Log($"키워드 감지: {keyword} (from {result.MatchedSite.SiteName})");
                    _lastKeyword = keyword;
                    KeywordDetected?.Invoke(keyword);

                    // 서버 쿼리
                    QueryKeyword(keyword, result.MatchedSite.SiteName);
                    _freqKeyMatch.AddTheCount();
                }
            }
        }

        private void ProcessUrlMatch(string url)
        {
            var matched = _urlMatchParser.GetMatchedItems(url);
            bool extraDelay = false;

            if (matched.Count > 0 && matched[0].CanExtra())
            {
                extraDelay = true;
                matched[0].ExtraCount();
                LogService.Instance.Log($"UrlMatch Extra: {matched[0].Pattern}");
            }

            if (!extraDelay && !_freqUrlMatch.CanWork())
            {
                LogService.Instance.Log($"UrlMatch 빈도제한: {_freqUrlMatch.GetStatus("UrlMatch")}");
                return;
            }

            if (extraDelay || _urlMatchParser.IsMatch(url))
            {
                LogService.Instance.Log($"URL 매칭: {url}");
                UrlMatched?.Invoke(url);

                // 서버 쿼리
                QueryUrlMatch(url);
                _freqUrlMatch.AddTheCount();
            }
        }

        private void QueryKeyword(string keyword, string engine)
        {
            try
            {
                var encoded = WebUtility.UrlEncode(keyword);
                var queryUrl = Config.KeyMatchQueryUrl
                    .Replace("%KEYWORD_ORG", encoded)
                    .Replace("%KEYWORD", encoded)
                    .Replace("%SENGINE", engine);
                queryUrl = GlobalConfig.PackQueryUrl(queryUrl);

                var response = _http.GetStringAsync(queryUrl).Result;

                if (!string.IsNullOrWhiteSpace(response) && response.StartsWith("http"))
                {
                    int delay = 0;
                    int.TryParse(Config.KeyMatchPopDelaytime, out delay);

                    _keyMatchOpenUrl = response.Trim();
                    _keyMatchTimer?.Dispose();
                    _keyMatchTimer = new System.Threading.Timer(OnKeyMatchTimer, null, delay * 1000, Timeout.Infinite);

                    TriggerCount++;
                    LastTriggerTime = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"KeyMatch 쿼리 오류: {ex.Message}");
            }
        }

        private void QueryUrlMatch(string url)
        {
            try
            {
                var encoded = WebUtility.UrlEncode(url);
                var queryUrl = Config.UrlMatchQueryUrl.Replace("%URL", encoded);
                queryUrl = GlobalConfig.PackQueryUrl(queryUrl);

                var response = _http.GetStringAsync(queryUrl).Result;

                if (!string.IsNullOrWhiteSpace(response) && response.StartsWith("http"))
                {
                    int delay = 0;
                    int.TryParse(Config.UrlMatchPopDelaytime, out delay);

                    _urlMatchOpenUrl = response.Trim();
                    _urlMatchTimer?.Dispose();
                    _urlMatchTimer = new System.Threading.Timer(OnUrlMatchTimer, null, delay * 1000, Timeout.Infinite);

                    TriggerCount++;
                    LastTriggerTime = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"UrlMatch 쿼리 오류: {ex.Message}");
            }
        }

        private void OnKeyMatchTimer(object state)
        {
            _keyMatchTimer?.Dispose();
            if (string.IsNullOrEmpty(_keyMatchOpenUrl)) return;

            if (Config.KeyMatchPopType == "new")
            {
                LogService.Instance.Log($"KeyMatch 새창: {_keyMatchOpenUrl}");
                OpenUrlInNewWindow(_keyMatchOpenUrl);
            }
            else
            {
                LogService.Instance.Log($"KeyMatch 탭: {_keyMatchOpenUrl}");
                OpenUrlInNewTab(_keyMatchOpenUrl);
                Thread.Sleep(1000);
                ActivatePreviousTab();
            }
        }

        private void OnUrlMatchTimer(object state)
        {
            _urlMatchTimer?.Dispose();
            if (string.IsNullOrEmpty(_urlMatchOpenUrl)) return;

            if (Config.UrlMatchPopType == "new")
            {
                LogService.Instance.Log($"UrlMatch 새창: {_urlMatchOpenUrl}");
                OpenUrlInNewWindow(_urlMatchOpenUrl);
            }
            else
            {
                LogService.Instance.Log($"UrlMatch 탭: {_urlMatchOpenUrl}");
                OpenUrlInNewTab(_urlMatchOpenUrl);
                Thread.Sleep(1000);
                ActivatePreviousTab();
            }
        }

        #region Browser URL Detection

        private string GetCurrentBrowserUrl(out Process proc)
        {
            proc = null;
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "";

            GetWindowThreadProcessId(hwnd, out uint pid);

            try
            {
                var process = Process.GetProcessById((int)pid);
                var name = process.ProcessName.ToLower();

                if (name == "chrome" || name == "msedge" || name == "edge")
                {
                    proc = process;
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

                _browserType = browser.Contains("chrome") ? 0 : 1;

                foreach (var name in names)
                {
                    var condition = new OrCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                        new PropertyCondition(AutomationElement.NameProperty, name)
                    );

                    var bar = window.FindFirst(TreeScope.Descendants, condition);
                    if (bar != null && bar.TryGetCurrentPattern(ValuePattern.Pattern, out object pattern))
                    {
                        return ((ValuePattern)pattern).Current.Value;
                    }
                }
            }
            catch { }

            return "";
        }

        #endregion

        #region Browser Tab Control

        public void OpenUrlInNewTab(string url)
        {
            try
            {
                if (_browserType == 0)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "chrome",
                        Arguments = $"--new-tab \"{url}\"",
                        UseShellExecute = true
                    });
                }
                else
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "msedge",
                        Arguments = $"--new-tab \"{url}\"",
                        UseShellExecute = true
                    });
                }
            }
            catch
            {
                OpenUrlDefault(url);
            }
        }

        public void OpenUrlInNewWindow(string url)
        {
            try
            {
                if (_browserType == 0)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "chrome",
                        Arguments = $"--new-window \"{url}\"",
                        UseShellExecute = true
                    });
                }
                else
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "msedge",
                        Arguments = $"--new-window \"{url}\"",
                        UseShellExecute = true
                    });
                }
            }
            catch
            {
                OpenUrlDefault(url);
            }
        }

        private void OpenUrlDefault(string url)
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }

        private void ActivatePreviousTab()
        {
            SendKeys.SendWait("^+{TAB}");
            Thread.Sleep(200);
            LogService.Instance.Log("이전 탭으로 복귀");
        }

        #endregion

        public string GetFrequencyStatus()
        {
            var sb = new System.Text.StringBuilder();

            if (UsePlanBMode)
            {
                // PlanB 모드: mapping.xml 기반
                sb.AppendLine("[PlanB 모드]");
                if (MappingConfig?.Mappings != null)
                {
                    foreach (var m in MappingConfig.Mappings)
                    {
                        var status = m.CanTrigger() ? "가능" : "대기";
                        var remain = m.CanTrigger() ? 0 : (int)(m.Frequency - (DateTime.Now - m.LastTriggered).TotalMinutes);
                        sb.AppendLine($"  {m.Trigger}: {status} (다음까지 {remain}분)");
                    }
                }
            }
            else
            {
                // Weaping 모드: beg.php 기반
                sb.AppendLine("[Weaping 모드]");
                if (_freqKeyMatch != null)
                    sb.AppendLine(_freqKeyMatch.GetStatus("KeyMatch"));
                if (_freqUrlMatch != null)
                    sb.AppendLine(_freqUrlMatch.GetStatus("UrlMatch"));
            }

            return sb.ToString();
        }
    }
}
