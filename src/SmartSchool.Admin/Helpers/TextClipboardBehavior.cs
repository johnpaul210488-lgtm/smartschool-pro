using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

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
        textControl.ContextMenu = menu;
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
