using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace SmartSchool.Admin.Helpers;

/// <summary>
/// GridLayout.IsPersistent="True" — DataGrid ke columns ki width + order (drag se
/// re-arrange) %LocalAppData% mein save ho ke next time wapis aa jati hain.
/// Key = parent view ka type name + grid ka index us view mein.
/// </summary>
public static class GridLayoutHelper
{
    public static readonly DependencyProperty IsPersistentProperty =
        DependencyProperty.RegisterAttached("IsPersistent", typeof(bool), typeof(GridLayoutHelper),
            new PropertyMetadata(false, OnIsPersistentChanged));

    public static bool GetIsPersistent(DependencyObject d) => (bool)d.GetValue(IsPersistentProperty);
    public static void SetIsPersistent(DependencyObject d, bool value) => d.SetValue(IsPersistentProperty, value);

    private static string StorePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SmartSchool", "Admin", "grid-layouts.json");

    private sealed class ColState { public int Display { get; set; } public string Width { get; set; } = "1*"; }

    private static void OnIsPersistentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid g || e.NewValue is not true) return;
        g.Loaded += Grid_Loaded;
        g.Unloaded += Grid_Unloaded;
    }

    private static string KeyFor(DataGrid g)
    {
        var view = FindViewRoot(g);
        var idx = 0;
        foreach (var other in AllGrids(view))
        {
            if (ReferenceEquals(other, g)) break;
            idx++;
        }
        return (view?.GetType().Name ?? "Window") + "#" + idx;
    }

    private static DependencyObject? FindViewRoot(DataGrid g)
    {
        DependencyObject cur = g;
        var parent = System.Windows.Media.VisualTreeHelper.GetParent(cur);
        while (parent is not null and not Window)
        {
            cur = parent;
            if (parent is UserControl) { } // keep climbing to the topmost container
            parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
        }
        return cur;
    }

    private static IEnumerable<DataGrid> AllGrids(DependencyObject? root)
    {
        if (root is null) yield break;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is DataGrid dg) yield return dg;
            else
                foreach (var nested in AllGrids(child))
                    yield return nested;
        }
    }

    private static Dictionary<string, List<ColState>> LoadStore()
    {
        try
        {
            if (File.Exists(StorePath))
                return JsonSerializer.Deserialize<Dictionary<string, List<ColState>>>(File.ReadAllText(StorePath))
                       ?? new Dictionary<string, List<ColState>>();
        }
        catch { /* corrupt file → fresh */ }
        return new Dictionary<string, List<ColState>>();
    }

    private static void SaveStore(Dictionary<string, List<ColState>> store)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(store));
        }
        catch { /* disk issues — silently skip */ }
    }

    private static void Grid_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid g) return;
        try
        {
            var store = LoadStore();
            if (!store.TryGetValue(KeyFor(g), out var cols) || cols.Count != g.Columns.Count) return;
            foreach (var (col, st) in g.Columns.Zip(cols))
            {
                col.DisplayIndex = Math.Clamp(st.Display, 0, g.Columns.Count - 1);
                col.Width = (DataGridLength)new DataGridLengthConverter().ConvertFromString(st.Width)!;
            }
        }
        catch { /* bad saved state → default layout */ }
    }

    private static void Grid_Unloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var g = (DataGrid)sender;
            var store = LoadStore();
            store[KeyFor(g)] = g.Columns.Select(c => new ColState
            {
                Display = c.DisplayIndex,
                Width = c.Width.ToString()
            }).ToList();
            SaveStore(store);
        }
        catch { }
    }
}
