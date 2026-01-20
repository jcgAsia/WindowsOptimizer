using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using WindowsOptimizer.Services;
using WindowsOptimizer.ViewModels;

namespace WindowsOptimizer
{
    public partial class MainWindow : Window
    {
        private bool _monitoringStarted = false;

        public MainWindow()
        {
            InitializeComponent();
            LogService.Instance.LogAdded += OnLogAdded;

            if (DataContext is MainViewModel vm)
            {
                vm.ClearLogRequested += ClearLog;

                // 설정이 이미 로드되었으면 바로 시작, 아니면 이벤트 대기
                if (ConfigService.Instance.MappingConfig != null)
                {
                    vm.ToggleMonitoringCommand.Execute(vm);
                    _monitoringStarted = true;
                }
                else
                {
                    ConfigService.Instance.ConfigReloaded += OnConfigFirstLoaded;
                }
            }
        }

        private void OnConfigFirstLoaded()
        {
            if (_monitoringStarted) return;
            _monitoringStarted = true;

            ConfigService.Instance.ConfigReloaded -= OnConfigFirstLoaded;

            Dispatcher.Invoke(() =>
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.ToggleMonitoringCommand.Execute(vm);
                }
            });
        }

        private void ClearLog() => LogRichTextBox.Document.Blocks.Clear();

        private void OnLogAdded(string line)
        {
            Dispatcher.Invoke(() =>
            {
                var paragraph = LogRichTextBox.Document.Blocks.FirstBlock as Paragraph;
                if (paragraph == null)
                {
                    paragraph = new Paragraph();
                    LogRichTextBox.Document.Blocks.Add(paragraph);
                }

                var color = GetLogColor(line);
                paragraph.Inlines.Add(new Run(line + Environment.NewLine) { Foreground = new SolidColorBrush(color) });
                LogRichTextBox.ScrollToEnd();

                if (paragraph.Inlines.Count > 500)
                {
                    for (int i = 0; i < 50; i++)
                        paragraph.Inlines.Remove(paragraph.Inlines.FirstInline);
                }
            });
        }

        private Color GetLogColor(string line)
        {
            if (line.Contains("도메인 매칭") || line.Contains("트리거") || line.Contains("매칭")) return Color.FromRgb(78, 201, 176);
            if (line.Contains("URL 변경") || line.Contains("URL]")) return Color.FromRgb(206, 145, 120);
            if (line.Contains("백그라운드 탭") || line.Contains("AutoTab")) return Color.FromRgb(86, 156, 214);
            if (line.Contains("OpenHd") || line.Contains("히든")) return Color.FromRgb(78, 201, 176);
            if (line.Contains("DelayTime") || line.Contains("대기")) return Color.FromRgb(220, 220, 170);
            if (line.Contains("오류") || line.Contains("실패")) return Color.FromRgb(244, 71, 71);
            if (line.Contains("시작") || line.Contains("중지") || line.Contains("완료")) return Color.FromRgb(220, 220, 170);
            if (line.Contains("Counting") || line.Contains("카운팅")) return Color.FromRgb(181, 206, 168);
            return Color.FromRgb(170, 170, 170);
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        private void MinimizeToTray_Click(object sender, RoutedEventArgs e) => Hide();

        private void ShowToastTest_Click(object sender, RoutedEventArgs e)
        {
            ToastPopupService.Instance.ShowToastManually();
        }

        private void ResetToastCount_Click(object sender, RoutedEventArgs e)
        {
            ToastPopupService.Instance.ResetCount();
            MessageBox.Show("토스트 카운트가 리셋되었습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"링크를 열 수 없습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
