using MaterialDesignThemes.Wpf;

namespace SmartSchool.Admin.Services;

/// <summary>Flip the Material Design base theme (dark/light) at runtime.</summary>
public static class ThemeHelper
{
    public static void SetDark(bool dark)
    {
        var palette = new PaletteHelper();
        var theme = palette.GetTheme();
        theme.SetBaseTheme(dark ? BaseTheme.Dark : BaseTheme.Light);
        palette.SetTheme(theme);
    }
}
