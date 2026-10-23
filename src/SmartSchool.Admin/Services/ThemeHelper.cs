using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace SmartSchool.Admin.Services;

/// <summary>
/// CHANGED: multi-palette theme-picker project band (user ne pancancel kar diya).
/// Ab ek hi FIXED theme: MAROON & CREAM (Themes/MyTheme.xaml + yahan base colors).
/// Sirf light/dark toggle bacha hai (Midnight-on-woff mode ke liye).
/// </summary>
public static class ThemeHelper
{
    private static readonly Color Primary = Color.FromRgb(0x7B, 0x1F, 0x33);   // Mar700
    private static readonly Color Secondary = Color.FromRgb(0xB0, 0x54, 0x6A); // Mar500

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
