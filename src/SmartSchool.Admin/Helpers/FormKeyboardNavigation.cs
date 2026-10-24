using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SmartSchool.Admin.Helpers;

/// <summary>Form-only keyboard flow: Enter advances, Tab/Shift+Tab stay native, arrows navigate at text boundaries.</summary>
public static class FormKeyboardNavigation
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(FormKeyboardNavigation),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        if (e.NewValue is true) element.PreviewKeyDown += OnPreviewKeyDown;
        else element.PreviewKeyDown -= OnPreviewKeyDown;
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || sender is not UIElement scope || Keyboard.Modifiers != ModifierKeys.None) return;
        var focused = Keyboard.FocusedElement as UIElement;
        if (focused is null || !IsWithinScope(focused, scope)) return;

        var textBox = focused as TextBoxBase;
        if (textBox is null && focused is not ComboBox && focused is not PasswordBox) return;

        if (e.Key == Key.Enter)
        {
            if (textBox is RichTextBox || textBox is TextBox { AcceptsReturn: true } ||
                focused is ComboBox { IsDropDownOpen: true }) return;
            Move(focused, FocusNavigationDirection.Next, e);
            return;
        }

        if (textBox is TextBox tb)
        {
            // Keep normal caret movement; only move focus when the caret has reached the relevant edge.
            if (tb.SelectionLength != 0) return;
            if (e.Key == Key.Left && tb.CaretIndex == 0)
                Move(focused, FocusNavigationDirection.Left, e);
            else if (e.Key == Key.Right && tb.CaretIndex >= tb.Text.Length)
                Move(focused, FocusNavigationDirection.Right, e);
            else if (e.Key == Key.Up && !tb.AcceptsReturn)
                Move(focused, FocusNavigationDirection.Up, e);
            else if (e.Key == Key.Down && !tb.AcceptsReturn)
                Move(focused, FocusNavigationDirection.Down, e);
        }
        else if (textBox is not null && e.Key is Key.Up or Key.Down)
        {
            Move(focused, e.Key == Key.Up ? FocusNavigationDirection.Up : FocusNavigationDirection.Down, e);
        }
    }

    private static void Move(UIElement focused, FocusNavigationDirection direction, KeyEventArgs e)
    {
        if (focused.MoveFocus(new TraversalRequest(direction))) e.Handled = true;
    }

    private static bool IsWithinScope(DependencyObject element, DependencyObject scope)
    {
        DependencyObject? current = element;
        while (current is not null)
        {
            if (ReferenceEquals(current, scope)) return true;
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return false;
    }
}
