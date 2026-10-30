using System.Windows;
using System.Windows.Controls;
using SmartSchool.Admin.ViewModels.Fees;

namespace SmartSchool.Admin.Views.Fees;

public partial class FeePlanView : UserControl
{
    public FeePlanView() => InitializeComponent();

    /// <summary>Period tabs (Monthly / Quarterly / Yearly / One-time) filter the fee grid.</summary>
    private void Period_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is FeePlanViewModel vm && sender is RadioButton { Tag: string period })
            vm.SelectedCycle = period;
    }
}
