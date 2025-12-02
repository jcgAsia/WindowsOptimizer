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
        public MainWindow()
        {
            InitializeComponent();
            LogService.Instance.LogAdded += OnLogAdded;

            if (DataContext is MainViewModel vm)
            {
                vm.ClearLogRequested += ClearLog;
                vm.ToggleMonitoringCommand.Execute(vm);
            }
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
            if (line.Contains("도메인 매칭") || line.Contains("트리거")) return Color.FromRgb(78, 201, 176);
            if (line.Contains("URL 변경")) return Color.FromRgb(206, 145, 120);
            if (line.Contains("백그라운드 탭")) return Color.FromRgb(86, 156, 214);
            if (line.Contains("오류") || line.Contains("실패")) return Color.FromRgb(244, 71, 71);
            if (line.Contains("시작") || line.Contains("중지")) return Color.FromRgb(220, 220, 170);
            return Color.FromRgb(170, 170, 170);
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        private void MinimizeToTray_Click(object sender, RoutedEventArgs e) => Hide();
    }
}
