using System.IO;
using System.Text.Json;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace SmartSchool.Admin.Services;

/// <summary>
/// App ki theme: 7 professional color palettes (user side panel se select karta hai)
/// + light/dark base — selection %LocalAppData% mein save rehti hai.
/// </summary>
public static class ThemeHelper
{
    public sealed record Palette(string Name, string Swatch, Color Primary, Color Secondary, bool Dark);

    /// <summary>7 professional variants — order yahi combo mein dikhega.</summary>
    public static readonly Palette[] Palettes =
    {
        new("Royal Indigo",   "🟦", Color.FromRgb(0x3F, 0x51, 0xB5), Color.FromRgb(0x00, 0xB8, 0xD4), false),
        new("Maroon Velvet",  "🟥", Color.FromRgb(0x7B, 0x1E, 0x3B), Color.FromRgb(0xC9, 0x9A, 0x66), false),
        new("Espresso Brown", "🟫", Color.FromRgb(0x4E, 0x34, 0x24), Color.FromRgb(0xD7, 0xA8, 0x6E), false),
        new("Royal Gold",     "🟨", Color.FromRgb(0x37, 0x47, 0x4F), Color.FromRgb(0xC9, 0xA2, 0x27), false),
        new("Nordic White",   "⬜", Color.FromRgb(0x45, 0x5A, 0x64), Color.FromRgb(0x8F, 0xB0, 0xC0), false),
        new("Midnight Onyx",  "⬛", Color.FromRgb(0x90, 0xA4, 0xAE), Color.FromRgb(0xFF, 0xB3, 0x00), true),
        new("Citrus Sunshine","🟡", Color.FromRgb(0xFF, 0x8F, 0x00), Color.FromRgb(0x2E, 0x5B, 0x4F), false),
    };

    private static string ThemePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SmartSchool", "Admin", "theme.json");

    private sealed class ThemeState { public int PaletteIndex { get; set; } }

    public static int CurrentIndex { get; private set; }

    /// <summary>App start: saved palette lagayein (na ho to Royal Indigo).</summary>
    public static void ApplySaved()
    {
        var idx = 0;
        try
        {
            if (File.Exists(ThemePath))
            {
                var s = JsonSerializer.Deserialize<ThemeState>(File.ReadAllText(ThemePath));
                if (s is not null) idx = s.PaletteIndex;
            }
        }
        catch { }
        SetPalette(Math.Clamp(idx, 0, Palettes.Length - 1));
    }

    public static void SetPalette(int index)
    {
        var p = Palettes[Math.Clamp(index, 0, Palettes.Length - 1)];
        CurrentIndex = Palettes.ToList().IndexOf(p);
        Apply(p);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ThemePath)!);
            File.WriteAllText(ThemePath, JsonSerializer.Serialize(new ThemeState { PaletteIndex = CurrentIndex }));
        }
        catch { }
    }

    public static void SetDark(bool dark) => Apply(Palettes[CurrentIndex] with { Dark = dark }); // back-compat (🌙 toggle ab palette combo se aaraha hai)

    private static void Apply(Palette p)
    {
        var palette = new PaletteHelper();
        var theme = palette.GetTheme();
        theme.SetBaseTheme(p.Dark ? BaseTheme.Dark : BaseTheme.Light);
        theme.SetPrimaryColor(p.Primary);
        theme.SetSecondaryColor(p.Secondary);
        palette.SetTheme(theme);
    }
}
