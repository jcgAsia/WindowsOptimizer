using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace WindowsOptimizer.Services
{
    public class ToastPopupService
    {
        private static readonly Lazy<ToastPopupService> _instance = new Lazy<ToastPopupService>(() => new ToastPopupService());
        public static ToastPopupService Instance => _instance.Value;

        private ManagementEventWatcher _processWatcher;
        private volatile bool _isMonitoring;
        private volatile bool _isPopupShowing;
        private int _todayShowCount;
        private DateTime _lastShowDate;
        private DateTime _lastShowTime;
        private DateTime _startTime;
        private readonly object _showLock = new object();

        // 브라우저 프로세스 이름 목록
        private readonly string[] _browserProcesses = { "chrome", "msedge", "edge" };

        // 초기화 대기 시간 (앱 시작 후 이 시간 동안은 팝업 표시 안함)
        private const int STARTUP_DELAY_SECONDS = 5;

        // 팝업 간 최소 간격 (분)
        private const int COOLDOWN_MINUTES = 5;

        public bool IsMonitoring => _isMonitoring;
        public int TodayShowCount => _todayShowCount;

        private ToastPopupService()
        {
            LoadShowCount();
        }

        /// <summary>
        /// 브라우저 실행 감지 시작
        /// </summary>
        public void StartMonitoring()
        {
            if (_isMonitoring) return;
            _isMonitoring = true;
            _startTime = DateTime.Now;

            try
            {
                // WMI를 통한 프로세스 시작 감지
                var query = new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace");
                _processWatcher = new ManagementEventWatcher(query);
                _processWatcher.EventArrived += OnProcessStarted;
                _processWatcher.Start();

                LogService.Instance.Log("[ToastPopup] 브라우저 실행 감지 시작");
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[ToastPopup] WMI 감지 시작 실패, 폴링 방식 사용: {ex.Message}");
                // WMI 실패 시 폴링 방식 fallback
                StartPollingMonitor();
            }

            // 초기화 대기 후 이미 실행 중인 브라우저 체크
            CheckBrowserAfterStartupDelay();
        }

        /// <summary>
        /// 초기화 대기 후 이미 실행 중인 브라우저가 있으면 팝업 표시
        /// </summary>
        private void CheckBrowserAfterStartupDelay()
        {
            Task.Run(async () =>
            {
                // 초기화 대기
                await Task.Delay(STARTUP_DELAY_SECONDS * 1000);

                if (!_isMonitoring) return;

                // 이미 실행 중인 브라우저 확인
                foreach (var browser in _browserProcesses)
                {
                    var processes = Process.GetProcessesByName(browser);
                    if (processes.Length > 0)
                    {
                        LogService.Instance.Log($"[ToastPopup] 초기화 완료, 실행 중인 브라우저 감지: {browser}");
                        TryShowPopup(browser);
                        break;
                    }
                }
            });
        }

        /// <summary>
        /// 폴링 방식 모니터링 (WMI 실패 시 fallback)
        /// </summary>
        private void StartPollingMonitor()
        {
            Task.Run(async () =>
            {
                bool[] browserRunning = new bool[_browserProcesses.Length];

                // 초기 상태 기록 (이미 실행 중인 브라우저)
                for (int i = 0; i < _browserProcesses.Length; i++)
                {
                    var processes = Process.GetProcessesByName(_browserProcesses[i]);
                    browserRunning[i] = processes.Length > 0;
                }

                while (_isMonitoring)
                {
                    try
                    {
                        for (int i = 0; i < _browserProcesses.Length; i++)
                        {
                            var processes = Process.GetProcessesByName(_browserProcesses[i]);
                            bool isRunning = processes.Length > 0;

                            // 실행되지 않았다가 실행된 경우
                            if (!browserRunning[i] && isRunning)
                            {
                                OnBrowserStarted(_browserProcesses[i]);
                            }
                            browserRunning[i] = isRunning;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Log($"[ToastPopup] 폴링 오류: {ex.Message}");
                    }

                    await Task.Delay(5000); // 5초마다 체크
                }
            });
        }

        /// <summary>
        /// 브라우저 실행 감지 중지
        /// </summary>
        public void StopMonitoring()
        {
            if (!_isMonitoring) return;
            _isMonitoring = false;

            try
            {
                _processWatcher?.Stop();
                _processWatcher?.Dispose();
                _processWatcher = null;
            }
            catch { }

            LogService.Instance.Log("[ToastPopup] 브라우저 실행 감지 중지");
        }

        private void OnProcessStarted(object sender, EventArrivedEventArgs e)
        {
            try
            {
                var processName = e.NewEvent.Properties["ProcessName"].Value?.ToString()?.ToLower();
                if (string.IsNullOrEmpty(processName)) return;

                // 브라우저 프로세스인지 확인
                foreach (var browser in _browserProcesses)
                {
                    if (processName.Contains(browser))
                    {
                        OnBrowserStarted(browser);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[ToastPopup] 프로세스 이벤트 처리 오류: {ex.Message}");
            }
        }

        private void OnBrowserStarted(string browserName)
        {
            // 앱 시작 후 초기화 대기 시간 체크
            var elapsedSinceStart = (DateTime.Now - _startTime).TotalSeconds;
            if (elapsedSinceStart < STARTUP_DELAY_SECONDS)
            {
                LogService.Instance.Log($"[ToastPopup] 초기화 대기 중 ({elapsedSinceStart:F0}/{STARTUP_DELAY_SECONDS}초), 스킵");
                return;
            }

            TryShowPopup(browserName);
        }

        /// <summary>
        /// 팝업 표시 시도 (조건 체크 후 표시)
        /// </summary>
        private void TryShowPopup(string browserName)
        {
            var config = ConfigService.Instance.MappingConfig;

            // 설정 확인
            if (config == null || !config.IsToastEnabled)
            {
                LogService.Instance.Log($"[ToastPopup] 비활성화 상태, 스킵");
                return;
            }

            // 이미 팝업이 표시 중인지 확인
            if (_isPopupShowing)
            {
                LogService.Instance.Log($"[ToastPopup] 이미 팝업 표시 중, 스킵");
                return;
            }

            // 쿨다운 체크 (마지막 팝업 표시 후 최소 간격)
            var elapsedSinceLastShow = (DateTime.Now - _lastShowTime).TotalMinutes;
            if (_lastShowTime != DateTime.MinValue && elapsedSinceLastShow < COOLDOWN_MINUTES)
            {
                LogService.Instance.Log($"[ToastPopup] 쿨다운 중 ({elapsedSinceLastShow:F1}/{COOLDOWN_MINUTES}분), 스킵");
                return;
            }

            // 날짜가 바뀌면 카운트 리셋
            if (_lastShowDate.Date != DateTime.Today)
            {
                _todayShowCount = 0;
                _lastShowDate = DateTime.Today;
                SaveShowCount();
            }

            // 최대 노출 횟수 확인
            if (_todayShowCount >= config.ToastMaxCount)
            {
                LogService.Instance.Log($"[ToastPopup] 최대 노출 횟수 도달 ({_todayShowCount}/{config.ToastMaxCount})");
                return;
            }

            lock (_showLock)
            {
                // 이중 체크
                if (_isPopupShowing) return;
                if (_todayShowCount >= config.ToastMaxCount) return;

                // 쿨다운 이중 체크
                var elapsed = (DateTime.Now - _lastShowTime).TotalMinutes;
                if (_lastShowTime != DateTime.MinValue && elapsed < COOLDOWN_MINUTES) return;

                _isPopupShowing = true;
                _todayShowCount++;
                _lastShowTime = DateTime.Now;
                SaveShowCount();
            }

            var toastUrl = string.IsNullOrEmpty(config.ToastUrl) ? GlobalConfig.ToastDefaultUrl : config.ToastUrl;

            LogService.Instance.Log($"[ToastPopup] 브라우저 감지 ({browserName}), 팝업 표시 ({_todayShowCount}/{config.ToastMaxCount})");

            // UI 스레드에서 팝업 표시
            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                ShowToastPopup(toastUrl);
            });
        }

        private void ShowToastPopup(string url)
        {
            try
            {
                var popup = new ToastPopupWindow(url);
                popup.Closed += (s, e) => _isPopupShowing = false;
                popup.Show();
            }
            catch (Exception ex)
            {
                _isPopupShowing = false;
                LogService.Instance.Log($"[ToastPopup] 팝업 표시 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// 노출 횟수 저장 (레지스트리)
        /// </summary>
        private void SaveShowCount()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(GlobalConfig.RegSubKey))
                {
                    key?.SetValue("ToastShowCount", _todayShowCount);
                    key?.SetValue("ToastShowDate", DateTime.Today.ToString("yyyy-MM-dd"));
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[ToastPopup] 카운트 저장 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 노출 횟수 로드 (레지스트리)
        /// </summary>
        private void LoadShowCount()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(GlobalConfig.RegSubKey))
                {
                    if (key == null) return;

                    var dateStr = key.GetValue("ToastShowDate")?.ToString();
                    if (DateTime.TryParse(dateStr, out var savedDate) && savedDate.Date == DateTime.Today)
                    {
                        _todayShowCount = Convert.ToInt32(key.GetValue("ToastShowCount") ?? 0);
                        _lastShowDate = savedDate;
                    }
                    else
                    {
                        _todayShowCount = 0;
                        _lastShowDate = DateTime.Today;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[ToastPopup] 카운트 로드 실패: {ex.Message}");
                _todayShowCount = 0;
                _lastShowDate = DateTime.Today;
            }
        }

        /// <summary>
        /// 수동으로 토스트 팝업 표시 (테스트용)
        /// </summary>
        public void ShowToastManually()
        {
            var config = ConfigService.Instance.MappingConfig;
            var toastUrl = config?.ToastUrl ?? GlobalConfig.ToastDefaultUrl;

            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                ShowToastPopup(toastUrl);
            });
        }

        /// <summary>
        /// 카운트 리셋 (테스트용)
        /// </summary>
        public void ResetCount()
        {
            lock (_showLock)
            {
                _todayShowCount = 0;
                _lastShowTime = DateTime.MinValue;
                SaveShowCount();
                LogService.Instance.Log("[ToastPopup] 카운트 리셋됨");
            }
        }
    }
}
