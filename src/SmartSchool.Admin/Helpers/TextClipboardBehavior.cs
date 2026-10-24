using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace SmartSchool.Admin.Helpers;

/// <summary>Provides a familiar right-click clipboard menu for all editable/read-only text fields.</summary>
public static class TextClipboardBehavior
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        // CHANGED: every TextBox/RichTextBox gets standard mouse-accessible clipboard commands.
        EventManager.RegisterClassHandler(
            typeof(TextBoxBase), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnTextControlLoaded), true);
        // CHANGED: clear stale text selection as soon as another control takes keyboard focus.
        EventManager.RegisterClassHandler(
            typeof(TextBoxBase), Keyboard.LostKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(OnTextControlLostKeyboardFocus), true);
    }

    private static void OnTextControlLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBoxBase textControl || textControl.ContextMenu is not null) return;

        var menu = new ContextMenu();
        AddCommand(menu, "Cut", ApplicationCommands.Cut, textControl);
        AddCommand(menu, "Copy", ApplicationCommands.Copy, textControl);
        AddCommand(menu, "Paste", ApplicationCommands.Paste, textControl);
        menu.Items.Add(new Separator());
        AddCommand(menu, "Select all", ApplicationCommands.SelectAll, textControl);
        menu.Closed += (_, _) => ClearSelection(textControl);
        textControl.ContextMenu = menu;
    }

    private static void OnTextControlLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBoxBase control) return;
        // Allow right-click Copy/Cut to run first; clear when focus has really left the field.
        control.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (control.ContextMenu?.IsOpen != true) ClearSelection(control);
        }));
    }

    private static void ClearSelection(TextBoxBase control)
    {
        if (control is TextBox textBox && textBox.SelectionLength > 0)
            textBox.Select(0, 0);
        else if (control is RichTextBox richTextBox && !string.IsNullOrEmpty(richTextBox.Selection.Text))
            richTextBox.CaretPosition = richTextBox.Selection.End;
    }

    private static void AddCommand(ContextMenu menu, string label, RoutedUICommand command, IInputElement target)
    {
        menu.Items.Add(new MenuItem
        {
            Header = label,
            Command = command,
            CommandTarget = target
        });
    }
}
