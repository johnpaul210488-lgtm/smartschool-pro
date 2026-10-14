using System.Windows;
using System.Windows.Controls;

namespace SmartSchool.Admin.Helpers;

/// <summary>
/// PasswordBox doesn't expose Password for binding (by design). This attached
/// property bridges it: Bind the VM's string property here and it stays in sync.
/// </summary>
public static class PasswordBoxHelper
{
    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached("BoundPassword", typeof(string), typeof(PasswordBoxHelper),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnBoundPasswordChanged));

    public static string GetBoundPassword(DependencyObject d) => (string)d.GetValue(BoundPasswordProperty);
    public static void SetBoundPassword(DependencyObject d, string value) => d.SetValue(BoundPasswordProperty, value);

    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;
        box.PasswordChanged -= HandlePasswordChanged;
        var newVal = (string?)e.NewValue ?? string.Empty;
        if (box.Password != newVal) box.Password = newVal;
        box.PasswordChanged += HandlePasswordChanged;
    }

    private static void HandlePasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box) SetBoundPassword(box, box.Password);
    }
}
