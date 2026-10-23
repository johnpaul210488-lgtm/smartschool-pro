using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace SmartSchool.Admin.Services;

/// <summary>
/// CHANGED: FIXED Waheed POS theme (green soft look) — picker band.
/// Sirf light/dark toggle bacha hai.
/// </summary>
public static class ThemeHelper
{
    private static readonly Color Primary = Color.FromRgb(0x2E, 0x7D, 0x32);   // CHANGED: Waheed green
    private static readonly Color Secondary = Color.FromRgb(0x66, 0xBB, 0x6A); // light green accent

    /// <summary>App start: maroon theme lagayein (light base).</summary>
    public static void ApplySaved() => SetDark(false);

    /// <summary>Dark toggle — cream base ya midnight-dark dono mein maroon accents.</summary>
    public static void SetDark(bool dark)
    {
        var app = System.Windows.Application.Current;
        if (app is null) return;

        var theme = new MaterialDesignThemes.Wpf.Theme();
        theme.SetPrimaryColor(Primary);
        theme.SetSecondaryColor(Secondary);
        if (dark) theme.SetDarkTheme(); else theme.SetLightTheme();

        var merged = app.Resources.MergedDictionaries;
        var old = merged.LastOrDefault(d => d.Contains("PrimaryHueMidBrush"));
        if (old is not null) merged.Remove(old);
        var dict = new System.Windows.ResourceDictionary();
        dict.SetTheme(theme);
        merged.Insert(0, dict);
    }
}
