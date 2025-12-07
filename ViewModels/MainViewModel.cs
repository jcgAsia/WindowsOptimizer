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
        [ObservableProperty] private string statusText = "대기 중";
        [ObservableProperty] private Brush statusColor = Brushes.Gray;
        [ObservableProperty] private string currentUrl = "-";
        [ObservableProperty] private string triggerInfo = "AutoTab:0 / OpenHd:0";
        [ObservableProperty] private string lastKeyword = "";
        [ObservableProperty] private string monitorButtonText = "▶ 모니터링 시작";
        [ObservableProperty] private string versionInfo = "";
        [ObservableProperty] private string modeInfo = "PlanB v1.3";
        [ObservableProperty] private string configStatus = "";

        public ObservableCollection<MappingItemViewModel> MappingItems { get; } = new();
        public event Action ClearLogRequested;

        public MainViewModel()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionInfo = $"v{version}";

            var svc = BrowserMonitorService.Instance;
            svc.UrlChanged += url => SafeInvoke(() => CurrentUrl = string.IsNullOrEmpty(url) ? "-" : url);
            svc.DomainTriggered += (url, m, type) => SafeInvoke(() =>
            {
                UpdateTriggerInfo();
                LastKeyword = $"[{type}] {m.Trigger}";
                UpdateMappingItems();
            });

            ConfigService.Instance.ConfigReloaded += () => SafeInvoke(() =>
            {
                UpdateStatus();
                UpdateConfigStatus();
                LogService.Instance.Log("[MainViewModel] 설정 리로드됨");
            });
        }

        private void SafeInvoke(Action action) => Application.Current?.Dispatcher?.Invoke(action);

        private void UpdateStatus()
        {
            var svc = BrowserMonitorService.Instance;
            var config = svc.MappingConfig;

            if (config?.IsForceDown == true)
            {
                StatusText = "강제 중지됨 (ForceDown)";
                StatusColor = Brushes.Red;
            }
            else if (svc.IsMonitoring)
            {
                StatusText = "모니터링 중";
                StatusColor = new SolidColorBrush(Color.FromRgb(78, 201, 176));
                MonitorButtonText = "⏹ 모니터링 중지";
            }
            else
            {
                StatusText = "중지됨";
                StatusColor = Brushes.Gray;
                MonitorButtonText = "▶ 모니터링 시작";
            }

            UpdateTriggerInfo();
            UpdateMappingItems();
        }

        private void UpdateConfigStatus()
        {
            var config = BrowserMonitorService.Instance.MappingConfig;
            if (config == null) return;

            var autoTab = config.IsAutoTabEnabled ? $"ON({config.AutoTabCycleTime}s)" : "OFF";
            var openHd = config.IsOpenHdEnabled ? $"ON({config.OpenHdCloseTime}s/{config.OpenHdCycleTime}s)" : "OFF";
            ConfigStatus = $"AutoTab:{autoTab} | OpenHd:{openHd}";
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
                MappingItems.Add(new MappingItemViewModel
                {
                    Trigger = m.Trigger,
                    Target = m.Target,
                    Frequency = m.Frequency,
                    StatusText = $"AT:{m.AutoTabCount} HD:{m.OpenHdCount}"
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
        public string StatusText { get; set; }
    }
}