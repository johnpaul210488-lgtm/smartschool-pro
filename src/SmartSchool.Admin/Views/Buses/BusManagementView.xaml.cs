using System.Windows.Controls;
using SmartSchool.Admin.ViewModels.Buses;

namespace SmartSchool.Admin.Views.Buses;

public partial class BusManagementView : UserControl
{
    public BusManagementView()
    {
        InitializeComponent();
    }

    private void DriverCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is BusManagementViewModel vm && sender is ComboBox cb && cb.SelectedItem != null)
        {
            vm.AssignDriverCommand.Execute(cb.SelectedItem);
            cb.SelectedIndex = -1;
        }
    }
}
