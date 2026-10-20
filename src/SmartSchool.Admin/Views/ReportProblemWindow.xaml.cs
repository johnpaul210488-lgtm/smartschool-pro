using System.Windows;
using SmartSchool.Admin.Services;

namespace SmartSchool.Admin.Views;

public partial class ReportProblemWindow : Window
{
    public ReportProblemWindow()
    {
        InitializeComponent();
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        var title = TitleBox.Text.Trim();
        var detail = DetailBox.Text.Trim();
        if (title.Length < 5)
        {
            MessageBox.Show("Title kam az kam 5 haroof ka likhein.", "Bug report",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SendBtn.IsEnabled = false;
        SendBtn.Content = "⏳ Bhej raha hun…";
        var result = await BugReportService.SendAsync(
            severity: "Info",
            title: title,
            message: detail.Length > 0 ? detail : title,
            screen: ScreenCheck.IsChecked == true ? BugReportService.CurrentScreen() : null,
            userNote: "manual report");
        SendBtn.IsEnabled = true;
        SendBtn.Content = "📤 Report bhejein";

        if (result.Ok)
        {
            var line = result.IssueNumber is int n
                ? $"✅ Report bhej di gai!\n\nGitHub par issue #{n} ban gai."
                : "✅ Report save ho gai!\n\nGitHub abhi off hai — server usay jab online ho khud GitHub bhej degi.";
            MessageBox.Show($"{line}\n\nBas developer ko keh dein: 'GitHub par nayi bug report aayi hai'.",
                "Report sent", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        else
        {
            MessageBox.Show($"Report nahi jai: {result.Error}\n\nServer chal raha hai? Dobara try karein.",
                "Report failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
