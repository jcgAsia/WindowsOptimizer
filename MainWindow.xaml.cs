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

            // 로그 지우기 이벤트 연결
            if (DataContext is MainViewModel vm)
            {
                vm.ClearLogRequested += ClearLog;
            }
        }

        private void ClearLog()
        {
            LogRichTextBox.Document.Blocks.Clear();
        }

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
                var run = new Run(line + Environment.NewLine) { Foreground = new SolidColorBrush(color) };
                paragraph.Inlines.Add(run);
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
            if (line.Contains("[PlanB] 도메인 매칭") || line.Contains("트리거"))
                return Color.FromRgb(78, 201, 176);
            if (line.Contains("URL 변경"))
                return Color.FromRgb(206, 145, 120);
            if (line.Contains("백그라운드 탭") || line.Contains("열기 완료"))
                return Color.FromRgb(86, 156, 214);
            if (line.Contains("오류") || line.Contains("실패"))
                return Color.FromRgb(244, 71, 71);
            if (line.Contains("시작") || line.Contains("중지"))
                return Color.FromRgb(220, 220, 170);
            if (line.Contains("매핑 로드") || line.Contains("설정"))
                return Color.FromRgb(181, 206, 168);
            return Color.FromRgb(170, 170, 170);
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        private void MinimizeToTray_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }
    }
}