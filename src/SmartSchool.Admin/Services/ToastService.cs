using System.Collections.ObjectModel;
using System.Media;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SmartSchool.Admin.Services;

/// <summary>A single toast shown top-right of the main window.</summary>
public partial class ToastItem : ObservableObject
{
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private string _iconKind = "Information";
    [ObservableProperty] private string _accentBrush = "#4CAF50";
}

/// <summary>
/// In-app toast notifications (top-right corner). MainWindow binds its toast
/// panel to ToastService.Current.Items. Auto-dismisses after a few seconds.
/// </summary>
public sealed class ToastService
{
    public static ToastService Current { get; } = new();

    public ObservableCollection<ToastItem> Items { get; } = new();

    public void Show(string title, string message, string icon = "Information",
        string accent = "#4CAF50", bool playSound = true)
    {
        Application.Current?.Dispatcher.Invoke(async () =>
        {
            var toast = new ToastItem { Title = title, Message = message, IconKind = icon, AccentBrush = accent };
            Items.Insert(0, toast);
            while (Items.Count > 5) Items.RemoveAt(Items.Count - 1);

            if (playSound)
            {
                try { SystemSounds.Asterisk.Play(); } catch { /* audio device missing - fine */ }
            }

            await Task.Delay(TimeSpan.FromSeconds(7));
            if (Items.Contains(toast)) Items.Remove(toast);
        });
    }

    public void PickIn(string student, string bus, string time)
        => Show("🟢 PICK-IN", $"{student} — Bus #{bus}\n{time}", "BusSchool", "#4CAF50");

    public void DropOut(string student, string bus, string time)
        => Show("🟠 DROP-OUT", $"{student} — Bus #{bus} se utra\n{time}", "BusSide", "#FF9800");

    public void Error(string title, string message)
        => Show("❌ " + title, message, "AlertCircle", "#F44336", playSound: false);

    public void Info(string title, string message)
        => Show("ℹ️ " + title, message, "Information", "#2196F3", playSound: false);
}
