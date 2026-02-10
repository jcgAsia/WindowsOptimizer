using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using WindowsOptimizer.Models;
using WindowsOptimizer.Services;

namespace WindowsOptimizer.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        // 모니터링 상태
        [ObservableProperty] private string statusText = "대기 중";
        [ObservableProperty] private Brush statusColor = Brushes.Gray;
        [ObservableProperty] private string currentUrl = "-";
        [ObservableProperty] private string triggerInfo = "AutoTab:0 / OpenHd:0";
        [ObservableProperty] private string lastKeyword = "";
        [ObservableProperty] private string monitorButtonText = "▶ 모니터링 시작";
        [ObservableProperty] private string versionInfo = "";

        // 전역 제어
        [ObservableProperty] private string forceDownText = "OFF";
        [ObservableProperty] private Brush forceDownColor = Brushes.Gray;

        // AutoTab 상태
        [ObservableProperty] private string autoTabStatus = "OFF";
        [ObservableProperty] private Brush autoTabColor = Brushes.Gray;
        [ObservableProperty] private string autoTabCycleInfo = "CycleTime: -";
        [ObservableProperty] private string autoTabLastTime = "마지막 실행: -";
        [ObservableProperty] private string autoTabCount = "실행 횟수: 0회";

        // OpenHd 상태
        [ObservableProperty] private string openHdStatus = "OFF";
        [ObservableProperty] private Brush openHdColor = Brushes.Gray;
        [ObservableProperty] private string openHdDelayInfo = "DelayTime: -";
        [ObservableProperty] private string openHdCycleInfo = "CycleTime: -";
        [ObservableProperty] private string openHdCloseInfo = "CloseTime: -";
        [ObservableProperty] private string openHdLastTime = "마지막 실행: -";
        [ObservableProperty] private string openHdCount = "실행 횟수: 0회";

        // PID 정보
        [ObservableProperty] private string pidInfo = "PID: -";

        // Mapping 파일명
        public string MappingFileName => GlobalConfig.Pid == "pb000" ? "mapping_pb000.xml" : "mapping.xml";

        public ObservableCollection<MappingItemViewModel> MappingItems { get; } = new();
        public event Action ClearLogRequested;

        public MainViewModel()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionInfo = $"v{version} (PlanB v1.4)";
            PidInfo = $"PID: {GlobalConfig.Pid}";

            var svc = BrowserMonitorService.Instance;
            svc.UrlChanged += url => SafeInvoke(() => CurrentUrl = string.IsNullOrEmpty(url) ? "-" : url);
            svc.DomainTriggered += (url, m, type) => SafeInvoke(() =>
            {
                UpdateTriggerInfo();
                UpdateFunctionStatus();
                LastKeyword = $"[{type}] {m.Trigger}";
                UpdateMappingItems();
            });

            ConfigService.Instance.ConfigReloaded += () => SafeInvoke(() =>
            {
                UpdateStatus();
                UpdateConfigStatus();
                UpdateFunctionStatus();
                UpdateMappingItems();
            });
        }

        private void SafeInvoke(Action action) => Application.Current?.Dispatcher?.Invoke(action);

        private void UpdateStatus()
        {
            var svc = BrowserMonitorService.Instance;
            var config = svc.MappingConfig;

            if (config?.IsForceDown == true)
            {
                StatusText = "🚫 강제 중지됨 (ForceDown=ON)";
                StatusColor = Brushes.Red;
                MonitorButtonText = "⏹ 강제 중지 상태";
            }
            else if (svc.IsMonitoring)
            {
                StatusText = "✅ 모니터링 중";
                StatusColor = new SolidColorBrush(Color.FromRgb(78, 201, 176));
                MonitorButtonText = "⏹ 모니터링 중지";
            }
            else
            {
                StatusText = "⏸️ 중지됨";
                StatusColor = Brushes.Gray;
                MonitorButtonText = "▶ 모니터링 시작";
            }

            UpdateTriggerInfo();
        }

        private void UpdateConfigStatus()
        {
            var config = BrowserMonitorService.Instance.MappingConfig;
            if (config == null) return;

            // PID 정보 업데이트
            PidInfo = $"PID: {GlobalConfig.Pid}";

            // ForceDown 상태
            if (config.IsForceDown)
            {
                ForceDownText = "ON (전체 기능 중지)";
                ForceDownColor = new SolidColorBrush(Color.FromRgb(244, 71, 71));
            }
            else
            {
                ForceDownText = "OFF (정상 작동)";
                ForceDownColor = new SolidColorBrush(Color.FromRgb(78, 201, 176));
            }

            // AutoTab 상태
            if (config.IsAutoTabEnabled)
            {
                AutoTabStatus = "ON";
                AutoTabColor = new SolidColorBrush(Color.FromRgb(78, 201, 176));
            }
            else
            {
                AutoTabStatus = "OFF";
                AutoTabColor = Brushes.Gray;
            }
            AutoTabCycleInfo = config.AutoTabCycleTime > 0
                ? $"CycleTime: {config.AutoTabCycleTime}초 ({config.AutoTabCycleTime / 60}분)"
                : "CycleTime: 0 (횟수만 체크)";

            // OpenHd 상태
            if (config.IsOpenHdEnabled)
            {
                OpenHdStatus = "ON";
                OpenHdColor = new SolidColorBrush(Color.FromRgb(78, 201, 176));
            }
            else
            {
                OpenHdStatus = "OFF";
                OpenHdColor = Brushes.Gray;
            }
            OpenHdDelayInfo = config.OpenHdDelayTime > 0
                ? $"DelayTime: {config.OpenHdDelayTime}초 ({config.OpenHdDelayTime / 60}분)"
                : "DelayTime: 0 (즉시 열기)";
            OpenHdCycleInfo = config.OpenHdCycleTime > 0
                ? $"CycleTime: {config.OpenHdCycleTime}초 ({config.OpenHdCycleTime / 60}분)"
                : "CycleTime: 0 (횟수만 체크)";
            OpenHdCloseInfo = $"CloseTime: {config.OpenHdCloseTime}초 (창 유지)";
        }

        private void UpdateFunctionStatus()
        {
            var svc = BrowserMonitorService.Instance;

            // AutoTab 실행 정보
            AutoTabCount = $"실행 횟수: {svc.AutoTabTriggerCount}회";
            AutoTabLastTime = svc.AutoTabLastTriggerTime != DateTime.MinValue
                ? $"마지막: {svc.AutoTabLastTriggerTime:HH:mm:ss}"
                : "마지막: -";

            // OpenHd 실행 정보
            OpenHdCount = $"실행 횟수: {svc.OpenHdTriggerCount}회";
            OpenHdLastTime = svc.OpenHdLastTriggerTime != DateTime.MinValue
                ? $"마지막: {svc.OpenHdLastTriggerTime:HH:mm:ss}"
                : "마지막: -";
        }

        private void UpdateTriggerInfo()
        {
            var svc = BrowserMonitorService.Instance;
            TriggerInfo = $"AutoTab:{svc.AutoTabTriggerCount} / OpenHd:{svc.OpenHdTriggerCount}";
            if (svc.LastTriggerTime != DateTime.MinValue)
                TriggerInfo += $" (마지막: {svc.LastTriggerTime:HH:mm:ss})";
        }

        private void UpdateMappingItems()
        {
            MappingItems.Clear();
            var config = BrowserMonitorService.Instance.MappingConfig;
            if (config?.Mappings == null) return;

            foreach (var m in config.Mappings)
            {
                var lastTime = m.AutoTabLastTime > m.OpenHdLastTime ? m.AutoTabLastTime : m.OpenHdLastTime;
                MappingItems.Add(new MappingItemViewModel
                {
                    Trigger = m.Trigger,
                    Target = m.Target,
                    Frequency = m.Frequency,
                    AutoTabInfo = $"{m.AutoTabCount}/{m.Frequency}",
                    OpenHdInfo = $"{m.OpenHdCount}/{m.Frequency}",
                    LastTimeInfo = lastTime != DateTime.MinValue ? lastTime.ToString("HH:mm:ss") : "-"
                });
            }
        }

        [RelayCommand]
        private void ToggleMonitoring()
        {
            var svc = BrowserMonitorService.Instance;
            if (svc.IsMonitoring) svc.StopMonitoring();
            else svc.StartMonitoring();
            UpdateStatus();
            UpdateConfigStatus();
            UpdateFunctionStatus();
        }

        [RelayCommand]
        private void ReloadConfig()
        {
            _ = ConfigService.Instance.LoadMappingConfigAsync();
            UpdateStatus();
        }

        [RelayCommand]
        private void ClearLog() => ClearLogRequested?.Invoke();

        [RelayCommand]
        private void OpenLogFolder()
        {
            try { Process.Start("explorer.exe", LogService.Instance.LogDirectory); }
            catch { }
        }
    }

    public class MappingItemViewModel
    {
        public string Trigger { get; set; }
        public string Target { get; set; }
        public int Frequency { get; set; }
        public string AutoTabInfo { get; set; }
        public string OpenHdInfo { get; set; }
        public string LastTimeInfo { get; set; }
    }
}
