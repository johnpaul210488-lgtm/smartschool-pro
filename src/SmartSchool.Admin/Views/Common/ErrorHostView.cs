using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartSchool.Admin.Views.Common;

/// <summary>
/// Kisi page ka load fail ho jaye to blank screen ki jagat yeh panel dikhta hai —
/// poora error text screenshot ke liye tayyar hota hai, aur report DB mein bhi jati hai.
/// </summary>
public class ErrorHostView : UserControl
{
    public ErrorHostView(string pageName, string message)
    {
        var border = new Border
        {
            Margin = new Thickness(24),
            Padding = new Thickness(22),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x1B, 0x1B)),
            VerticalAlignment = VerticalAlignment.Top
        };
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text = $"⚠ \"{pageName}\" page load nahi ho saka",
            FontSize = 18, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80))
        });
        sp.Children.Add(new TextBlock
        {
            Text = "Is par screenshot utha kar developer ko bhej dein:",
            FontSize = 12, Opacity = 0.8, Margin = new Thickness(0, 6, 0, 6),
            Foreground = Brushes.White
        });
        sp.Children.Add(new TextBox
        {
            Text = message, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true, MaxHeight = 300,
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x12, 0x12)),
            Foreground = Brushes.White, BorderThickness = new Thickness(0)
        });
        border.Child = sp;
        Content = border;
    }
}
