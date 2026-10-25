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
            "SmartSchool", "Admin", "grid-layouts-v2.json"); // v2: purani saved widths/order (jin se Paid column chhup jata tha) ek dafa reset

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

    // CHANGED: pehle WINDOW ke root tak chal-ta tha (sab pages ek hi key share karte = apas mein
    // state gir jati!) — ab pehla UserControl ancestor (yani asal page) he key ban-ta hai.
    private static DependencyObject FindViewRoot(DataGrid g)
    {
        var parent = System.Windows.Media.VisualTreeHelper.GetParent(g);
        while (parent is not null && parent is not UserControl)
            parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
        return parent ?? g;
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
            // CHANGED: purane galat-key records (sab pages ek key share karte the) ek dafa saaf
            if (store.Keys.Any(k => !k.Contains('#') || k.StartsWith("ContentPresenter")))
            {
                var bad = store.Keys.Where(k => k.StartsWith("ContentPresenter") || k.StartsWith("ContentControl")
                                             || k.StartsWith("Window") || !k.Contains('#')).ToList();
                foreach (var k in bad) store.Remove(k);
                if (bad.Count > 0) SaveStore(store);
            }
            if (!store.TryGetValue(KeyFor(g), out var cols) || cols.Count != g.Columns.Count) return;
            // CHANGED: pairing as-is; apply sirf DisplayIndex sorted order mein (transient collisions se bachke)
            var pairs = g.Columns.Zip(cols).ToList();
            foreach (var (col, st) in pairs)
                { var w = (DataGridLength)new DataGridLengthConverter().ConvertFromString(st.Width)!; if (!(w.IsAbsolute && w.Value < 60)) col.Width = w; }
            foreach (var pair in pairs.OrderBy(p => p.Second.Display))
                pair.First.DisplayIndex = Math.Clamp(pair.Second.Display, 0, g.Columns.Count - 1);
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
