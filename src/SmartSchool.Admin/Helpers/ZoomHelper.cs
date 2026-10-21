using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SmartSchool.Admin.Helpers;

/// <summary>
/// Poori app ka zoom (choti laptop screens): Ctrl +/- ya window ke 🔍 buttons se.
/// Zoom %LocalAppData%\SmartSchool\Admin\ui-settings.json mein save rehta hai.
/// Range: 70% – 160%, step 10%.
/// </summary>
public static class ZoomHelper
{
    private static readonly Dictionary<FrameworkElement, ScaleTransform> _trans = new();

    private static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SmartSchool", "Admin", "ui-settings.json");

    private sealed class UiSettings { public double Zoom { get; set; } = 1.0; }

    public static double CurrentZoom { get; private set; } = LoadZoom();

    public static event Action<double>? ZoomChanged;

    private static double LoadZoom()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var s = JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(SettingsPath));
                if (s is not null) return Math.Clamp(s.Zoom, 0.7, 1.6);
            }
        }
        catch { }
        return 1.0;
    }

    private static void SaveZoom()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new UiSettings { Zoom = CurrentZoom }));
        }
        catch { }
    }

    /// <summary>Apply zoom to a window: wrap ka root element transform + Ctrl hotkeys.</summary>
    public static void Apply(Window window, FrameworkElement root)
    {
        var st = new ScaleTransform(CurrentZoom, CurrentZoom);
        root.LayoutTransform = st;
        _trans[root] = st;

        window.PreviewKeyDown -= OnKeyDown;
        window.PreviewKeyDown += OnKeyDown;
        window.Closed += (_, _) => SaveZoom();
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        switch (e.Key)
        {
            case Key.OemPlus:
            case Key.Add:
                ZoomIn();
                e.Handled = true;
                break;
            case Key.OemMinus:
            case Key.Subtract:
                ZoomOut();
                e.Handled = true;
                break;
            case Key.D0:
            case Key.NumPad0:
                ResetZoom();
                e.Handled = true;
                break;
        }
    }

    public static void ZoomIn() => Set(CurrentZoom + 0.1);
    public static void ZoomOut() => Set(CurrentZoom - 0.1);
    public static void ResetZoom() => Set(1.0);

    private static void Set(double zoom)
    {
        zoom = Math.Clamp(zoom, 0.7, 1.6);
        if (Math.Abs(zoom - CurrentZoom) < 0.001) return;
        CurrentZoom = zoom;
        foreach (var t in _trans.Values)
        {
            t.ScaleX = zoom;
            t.ScaleY = zoom;
        }
        SaveZoom();
        ZoomChanged?.Invoke(zoom);
    }
}
