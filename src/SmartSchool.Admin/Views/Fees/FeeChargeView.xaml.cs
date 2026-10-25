using System.Windows.Controls;
using System.Windows.Input;

namespace SmartSchool.Admin.Views.Fees;

public partial class FeeChargeView : UserControl
{
    public FeeChargeView() => InitializeComponent();

    /// <summary>Clicking empty space in the statement gives the page keyboard focus for arrow / Page keys.</summary>
    private void StatementScroll_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ScrollViewer sv) sv.Focus();
    }

    /// <summary>Arrow, Page Up/Down, Home and End scroll the statement. Text boxes and lists keep their own keys.</summary>
    private void StatementScroll_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase or ComboBox or DatePicker
            or System.Windows.Controls.Primitives.Selector) return;

        switch (e.Key)
        {
            case Key.Down: sv.ScrollToVerticalOffset(sv.VerticalOffset + 40); break;
            case Key.Up: sv.ScrollToVerticalOffset(sv.VerticalOffset - 40); break;
            case Key.PageDown: sv.PageDown(); break;
            case Key.PageUp: sv.PageUp(); break;
            case Key.Home: sv.ScrollToTop(); break;
            case Key.End: sv.ScrollToEnd(); break;
            default: return;
        }
        e.Handled = true;
    }
}
