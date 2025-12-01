using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
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
        [ObservableProperty] private string statusText = "대기 중";
        [ObservableProperty] private Brush statusColor = Brushes.Gray;
        [ObservableProperty] private string currentUrl = "-";
        [ObservableProperty] private string triggerInfo = "0회";
        [ObservableProperty] private string lastKeyword = "";
        [ObservableProperty] private string monitorButtonText = "▶ 모니터링 시작";
        [ObservableProperty] private string versionInfo = "";
        [ObservableProperty] private string modeInfo = "PlanB";

        public ObservableCollection<MappingItemViewModel> MappingItems { get; } = new();

        // 로그 지우기 이벤트
        public event Action ClearLogRequested;

        public MainViewModel()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionInfo = $"v{version}";

            var svc = BrowserMonitorService.Instance;
            svc.UrlChanged += url => SafeInvoke(() => CurrentUrl = string.IsNullOrEmpty(url) ? "-" : url);
            svc.DomainTriggered += (url, m) => SafeInvoke(() =>
            {
                UpdateTriggerInfo();
                LastKeyword = $"도메인: {m.Trigger}";
                UpdateMappingItems();
            });
            svc.ConfigLoaded += () => SafeInvoke(() => UpdateMappingItems());

            UpdateStatus();
        }

        private void SafeInvoke(Action action) => Application.Current?.Dispatcher?.Invoke(action);

        private void UpdateStatus()
        {
            var svc = BrowserMonitorService.Instance;

            if (svc.IsMonitoring)
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

        private void UpdateTriggerInfo()
        {
            var svc = BrowserMonitorService.Instance;
            TriggerInfo = $"{svc.TriggerCount}회";
            if (svc.LastTriggerTime != DateTime.MinValue)
                TriggerInfo += $" (마지막: {svc.LastTriggerTime:HH:mm:ss})";
        }

        private void UpdateMappingItems()
        {
            MappingItems.Clear();
            var mapping = BrowserMonitorService.Instance.MappingConfig;
            if (mapping?.Mappings == null) return;

            foreach (var m in mapping.Mappings)
            {
                MappingItems.Add(new MappingItemViewModel
                {
                    Trigger = m.Trigger,
                    Target = m.Target,
                    Frequency = m.Frequency,
                    StatusText = m.CanTrigger() ? "대기" : GetRemainTime(m)
                });
            }
        }

        private string GetRemainTime(DomainMapping m)
        {
            var remain = m.Frequency - (DateTime.Now - m.LastTriggered).TotalMinutes;
            return remain > 0 ? $"{remain:F0}분 후" : "대기";
        }

        [RelayCommand]
        private void ToggleMonitoring()
        {
            var svc = BrowserMonitorService.Instance;
            if (svc.IsMonitoring)
                svc.StopMonitoring();
            else
                svc.StartMonitoring();

            UpdateStatus();
        }

        [RelayCommand]
        private void ReloadConfig()
        {
            BrowserMonitorService.Instance.ReloadConfig();
            UpdateStatus();
        }

        [RelayCommand]
        private void ClearLog()
        {
            ClearLogRequested?.Invoke();
        }

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