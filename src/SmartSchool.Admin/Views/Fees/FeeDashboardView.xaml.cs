using System.Windows.Controls;
using System.Windows.Input;
using SmartSchool.Admin.Models;
using SmartSchool.Admin.ViewModels.Fees;

namespace SmartSchool.Admin.Views.Fees;

public partial class FeeDashboardView : UserControl
{
    public FeeDashboardView()
    {
        InitializeComponent();
    }

    /// <summary>Double-click a student row → collect page.</summary>
    private void StudentGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is FeeDashboardViewModel vm && sender is DataGrid { SelectedItem: StudentFeeSummaryDto s })
            vm.CollectCommand.Execute(s);
    }
}
