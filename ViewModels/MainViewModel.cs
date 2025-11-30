using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowsOptimizer.Models;
using WindowsOptimizer.Services;

namespace WindowsOptimizer.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly StringBuilder _logBuilder = new StringBuilder();

        [ObservableProperty] private string statusText = "대기 중";
        [ObservableProperty] private Brush statusColor = Brushes.Gray;
        [ObservableProperty] private string currentUrl = "";
        [ObservableProperty] private string keyMatchInfo = "KeyMatch: -";
        [ObservableProperty] private string urlMatchInfo = "UrlMatch: -";
        [ObservableProperty] private string triggerInfo = "트리거: 0회";
        [ObservableProperty] private string lastKeyword = "";
        [ObservableProperty] private string frequencyStatus = "";
        [ObservableProperty] private string logText = "";
        [ObservableProperty] private string monitorButtonText = "모니터링 시작";
        [ObservableProperty] private string configKeyMatch = "";
        [ObservableProperty] private string configUrlMatch = "";
        [ObservableProperty] private string versionInfo = "";
        [ObservableProperty] private string modeInfo = "PlanB";
        [ObservableProperty] private bool usePlanBMode = true;

        public ObservableCollection<string> MatchSites { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> UrlPatterns { get; } = new ObservableCollection<string>();

        public MainViewModel()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionInfo = $"v{version}";

            // 이벤트 구독
            LogService.Instance.LogAdded += OnLogAdded;

            var svc = BrowserMonitorService.Instance;
            svc.UrlChanged += url => SafeInvoke(() => CurrentUrl = url);
            svc.KeywordDetected += kw => SafeInvoke(() => LastKeyword = $"키워드: {kw}");
            svc.UrlMatched += url => SafeInvoke(() => UpdateTriggerInfo());
            svc.DomainTriggered += (url, m) => SafeInvoke(() => 
            {
                UpdateTriggerInfo();
                LastKeyword = $"도메인: {m.Trigger}";
            });

            UpdateStatus();
        }

        private void SafeInvoke(Action action)
        {
            Application.Current?.Dispatcher?.Invoke(action);
        }

        private void OnLogAdded(string line)
        {
            SafeInvoke(() =>
            {
                _logBuilder.AppendLine(line);
                if (_logBuilder.Length > 100000)
                    _logBuilder.Remove(0, 20000);
                LogText = _logBuilder.ToString();
            });
        }

        private void UpdateStatus()
        {
            var svc = BrowserMonitorService.Instance;

            // 모드 동기화
            svc.UsePlanBMode = UsePlanBMode;
            ModeInfo = UsePlanBMode ? "PlanB (mapping.xml)" : "Weaping (beg.php)";

            if (svc.IsMonitoring)
            {
                StatusText = "● 모니터링 중";
                StatusColor = Brushes.Green;
                MonitorButtonText = "모니터링 중지";
            }
            else
            {
                StatusText = "○ 중지됨";
                StatusColor = Brushes.Gray;
                MonitorButtonText = "모니터링 시작";
            }

            UpdateTriggerInfo();
            UpdateConfigInfo();
            UpdateMappingLists();
            FrequencyStatus = svc.GetFrequencyStatus();
        }

        private void UpdateTriggerInfo()
        {
            var svc = BrowserMonitorService.Instance;
            TriggerInfo = $"트리거: {svc.TriggerCount}회";
            if (svc.LastTriggerTime != DateTime.MinValue)
                TriggerInfo += $" (마지막: {svc.LastTriggerTime:HH:mm:ss})";
        }

        private void UpdateConfigInfo()
        {
            var svc = BrowserMonitorService.Instance;

            if (UsePlanBMode)
            {
                // PlanB 모드
                var mapping = svc.MappingConfig;
                ConfigKeyMatch = $"[PlanB 모드]\n매핑 수: {mapping?.Mappings?.Count ?? 0}개";
                ConfigUrlMatch = $"서버: {ConfigService.Instance.ServerBaseUrl}";

                KeyMatchInfo = $"매핑: {mapping?.Mappings?.Count ?? 0}개";
                UrlMatchInfo = "PlanB 모드";
            }
            else
            {
                // Weaping 모드
                var config = svc.Config;
                if (config == null) return;

                ConfigKeyMatch = $"KeyMatch: {config.KeyMatchSwitch}\n" +
                                $"PopType: {config.KeyMatchPopType}\n" +
                                $"Freq: {config.KeyMatchFreqMaxPerDay}/{config.KeyMatchFreqDterm}s/{config.KeyMatchFreqDcount}";

                ConfigUrlMatch = $"UrlMatch: {config.UrlMatchSwitch}\n" +
                                $"PopType: {config.UrlMatchPopType}\n" +
                                $"Freq: {config.UrlMatchFreqMaxPerDay}/{config.UrlMatchFreqDterm}s/{config.UrlMatchFreqDcount}";

                KeyMatchInfo = $"KeyMatch: {(config.KeyMatchSwitch == "on" ? "ON" : "OFF")}";
                UrlMatchInfo = $"UrlMatch: {(config.UrlMatchSwitch == "on" ? "ON" : "OFF")}";
            }
        }

        private void UpdateMappingLists()
        {
            MatchSites.Clear();
            UrlPatterns.Clear();

            var svc = BrowserMonitorService.Instance;

            if (UsePlanBMode)
            {
                // PlanB 매핑 목록
                var mapping = svc.MappingConfig;
                if (mapping?.Mappings != null)
                {
                    foreach (var m in mapping.Mappings)
                    {
                        MatchSites.Add($"{m.Trigger} ({m.Frequency}분)");
                        UrlPatterns.Add(m.Target);
                    }
                }
            }
        }

        [RelayCommand]
        private void ToggleMonitoring()
        {
            var svc = BrowserMonitorService.Instance;
            svc.UsePlanBMode = UsePlanBMode;

            if (svc.IsMonitoring)
                svc.StopMonitoring();
            else
                svc.StartMonitoring();
            
            UpdateStatus();
        }

        [RelayCommand]
        private void ReloadConfig()
        {
            var svc = BrowserMonitorService.Instance;
            svc.UsePlanBMode = UsePlanBMode;
            svc.ReloadConfig();
            UpdateStatus();
        }

        [RelayCommand]
        private void ToggleMode()
        {
            UsePlanBMode = !UsePlanBMode;
            UpdateStatus();
        }

        [RelayCommand]
        private void ClearLog()
        {
            _logBuilder.Clear();
            LogText = "";
        }

        [RelayCommand]
        private void OpenLogFolder()
        {
            try
            {
                Process.Start("explorer.exe", LogService.Instance.LogDirectory);
            }
            catch { }
        }
    }
}
