using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace SmartSchool.Admin.Helpers;

/// <summary>Copies selected DataGrid cell text (including template cells) as a header + TSV data range.</summary>
public static class GridClipboardBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(GridClipboardBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid) return;
        if (e.NewValue is true)
        {
            grid.PreviewKeyDown += OnPreviewKeyDown;
            if (grid.ContextMenu is null)
            {
                var menu = new ContextMenu();
                var copy = new MenuItem { Header = "Copy" };
                copy.Click += (_, _) => CopySelection(grid);
                menu.Items.Add(copy);
                menu.Items.Add(new MenuItem
                {
                    Header = "Select all",
                    Command = ApplicationCommands.SelectAll,
                    CommandTarget = grid
                });
                grid.ContextMenu = menu;
            }
        }
        else
        {
            grid.PreviewKeyDown -= OnPreviewKeyDown;
        }
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not DataGrid grid || e.Key != Key.C ||
            (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;

        // Let an active editor copy its selected characters, not the whole grid cell.
        if (Keyboard.FocusedElement is TextBox editor && editor.SelectionLength > 0) return;
        if (Keyboard.FocusedElement is RichTextBox richEditor && !string.IsNullOrEmpty(richEditor.Selection.Text)) return;
        _ = CopySelection(grid);
        e.Handled = true;
    }

    private static bool CopySelection(DataGrid grid)
    {
        var cells = new Dictionary<(int Row, int Column), DataGridCellInfo>();
        foreach (var cell in grid.SelectedCells)
        {
            if (!cell.IsValid) continue;
            var row = grid.Items.IndexOf(cell.Item);
            if (row >= 0) cells[(row, cell.Column.DisplayIndex)] = cell;
        }

        var wholeRows = false;
        if (cells.Count == 0 && grid.SelectedItems.Count > 0)
        {
            wholeRows = true;
            foreach (var item in grid.SelectedItems)
            {
                var row = grid.Items.IndexOf(item);
                if (row < 0) continue;
                foreach (var column in grid.Columns)
                    cells[(row, column.DisplayIndex)] = new DataGridCellInfo(item, column);
            }
        }
        if (cells.Count == 0 && grid.CurrentCell.IsValid)
        {
            var row = grid.Items.IndexOf(grid.CurrentCell.Item);
            if (row >= 0) cells[(row, grid.CurrentCell.Column.DisplayIndex)] = grid.CurrentCell;
        }
        if (cells.Count == 0) return false;

        var rowIndexes = cells.Keys.Select(x => x.Row).Distinct().OrderBy(x => x).ToArray();
        var displayIndexes = cells.Keys.Select(x => x.Column).Distinct().OrderBy(x => x).ToArray();
        var columnsByDisplayIndex = grid.Columns.ToDictionary(c => c.DisplayIndex);
        var output = new StringBuilder();
        output.AppendLine(string.Join('\t', displayIndexes.Select(i => CleanCellText(GetHeaderText(columnsByDisplayIndex[i].Header)))));

        foreach (var rowIndex in rowIndexes)
        {
            var values = new List<string>(displayIndexes.Length);
            foreach (var displayIndex in displayIndexes)
            {
                if (cells.TryGetValue((rowIndex, displayIndex), out var cell))
                {
                    values.Add(CleanCellText(GetCellText(grid, cell, rowIndex)));
                }
                else if (wholeRows)
                {
                    var column = columnsByDisplayIndex[displayIndex];
                    values.Add(CleanCellText(GetCellText(grid, new DataGridCellInfo(grid.Items[rowIndex], column), rowIndex)));
                }
                else values.Add(string.Empty);
            }
            output.AppendLine(string.Join('\t', values));
        }

        try
        {
            Clipboard.SetText(output.ToString().TrimEnd('\r', '\n'), TextDataFormat.UnicodeText);
            return true;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return false; // another process briefly owns the clipboard; do not crash the app
        }
    }

    private static string GetCellText(DataGrid grid, DataGridCellInfo cellInfo, int rowIndex)
    {
        if (cellInfo.Column is DataGridBoundColumn bound && bound.ClipboardContentBinding is Binding binding)
        {
            try
            {
                var probe = new TextBlock { DataContext = cellInfo.Item };
                var copy = new Binding
                {
                    Path = binding.Path,
                    Mode = BindingMode.OneWay,
                    Converter = binding.Converter,
                    ConverterParameter = binding.ConverterParameter,
                    ConverterCulture = binding.ConverterCulture,
                    StringFormat = binding.StringFormat,
                    TargetNullValue = binding.TargetNullValue,
                    FallbackValue = binding.FallbackValue
                };
                BindingOperations.SetBinding(probe, TextBlock.TextProperty, copy);
                probe.GetBindingExpression(TextBlock.TextProperty)?.UpdateTarget();
                return probe.Text ?? string.Empty;
            }
            catch
            {
                // A display template fallback below is safer than blocking copy for one unusual binding.
            }
        }

        var row = grid.ItemContainerGenerator.ContainerFromIndex(rowIndex) as DataGridRow;
        var presenter = row is null ? null : FindDescendant<DataGridCellsPresenter>(row);
        var visualCell = presenter?.ItemContainerGenerator.ContainerFromIndex(cellInfo.Column.DisplayIndex) as DataGridCell;
        if (visualCell is null) return string.Empty;

        var text = FindDescendants<TextBlock>(visualCell)
            .Where(x => x.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(x.Text))
            .Select(x => x.Text.Trim());
        return string.Join(" ", text);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            var nested = FindDescendant<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in FindDescendants<T>(child)) yield return nested;
        }
    }

    private static string GetHeaderText(object? header) => header switch
    {
        TextBlock textBlock => textBlock.Text,
        ContentControl contentControl => contentControl.Content?.ToString() ?? string.Empty,
        _ => header?.ToString() ?? string.Empty
    };

    private static string CleanCellText(string value)
        => value.Replace('\t', ' ').Replace("\r", " ").Replace("\n", " ").Trim();
}
