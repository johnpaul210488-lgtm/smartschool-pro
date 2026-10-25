using System.Windows;
using System.Windows.Controls;
using SmartSchool.Admin.ViewModels.Fees;

namespace SmartSchool.Admin.Views.Fees;

public partial class FeeChargeView : UserControl
{
    public FeeChargeView() => InitializeComponent();

    /// <summary>Fee type radio buttons (Monthly / Quarterly / Yearly / One-time) set the VM's cycle.</summary>
    private void Cycle_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is FeeChargeViewModel vm && sender is RadioButton { Tag: string cycle })
            vm.SelectedCycle = cycle;
    }
}
