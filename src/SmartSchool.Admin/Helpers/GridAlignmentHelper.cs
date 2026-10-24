using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SmartSchool.Admin.Helpers;

/// <summary>Centers bound DataGrid text vertically and horizontally across all app tables.</summary>
public static class GridAlignmentHelper
{
    public static readonly DependencyProperty CenterRowTextProperty = DependencyProperty.RegisterAttached(
        "CenterRowText", typeof(bool), typeof(GridAlignmentHelper),
        new PropertyMetadata(false, OnCenterRowTextChanged));

    private static readonly Style CenteredTextStyle = CreateTextStyle();
    private static readonly Style CenteredCheckBoxStyle = CreateCheckBoxStyle();

    public static bool GetCenterRowText(DependencyObject d) => (bool)d.GetValue(CenterRowTextProperty);
    public static void SetCenterRowText(DependencyObject d, bool value) => d.SetValue(CenterRowTextProperty, value);

    private static void OnCenterRowTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid || e.NewValue is not true) return;
        grid.Loaded += (_, _) => Apply(grid);
        grid.AutoGeneratingColumn += (_, args) => ApplyColumn(args.Column);
        if (grid.IsLoaded) Apply(grid);
    }

    private static void Apply(DataGrid grid)
    {
        // CHANGED: also center TextBlocks inside hand-authored DataGridTemplateColumn cells.
        grid.Resources[typeof(TextBlock)] = CenteredTextStyle;
        // CHANGED: give each grid its own mouse-accessible Copy/Select All menu.
        if (grid.ContextMenu is null)
        {
            var menu = new ContextMenu();
            menu.Items.Add(new MenuItem { Header = "Copy", Command = ApplicationCommands.Copy, CommandTarget = grid });
            menu.Items.Add(new MenuItem { Header = "Select all", Command = ApplicationCommands.SelectAll, CommandTarget = grid });
            grid.ContextMenu = menu;
        }
        foreach (var column in grid.Columns) ApplyColumn(column);
    }

    private static void ApplyColumn(DataGridColumn column)
    {
        if (column is DataGridTextColumn textColumn && textColumn.ElementStyle is null)
            textColumn.ElementStyle = CenteredTextStyle;
        else if (column is DataGridCheckBoxColumn checkBoxColumn && checkBoxColumn.ElementStyle is null)
            checkBoxColumn.ElementStyle = CenteredCheckBoxStyle;
    }

    private static Style CreateTextStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
        style.Seal();
        return style;
    }

    private static Style CreateCheckBoxStyle()
    {
        var style = new Style(typeof(CheckBox));
        style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Seal();
        return style;
    }
}
