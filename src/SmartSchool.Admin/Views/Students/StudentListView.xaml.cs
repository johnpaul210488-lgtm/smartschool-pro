using System.Windows;
using System.Windows.Controls;

namespace SmartSchool.Admin.Views.Students;

public partial class StudentListView : UserControl
{
    public StudentListView()
    {
        InitializeComponent();
    }

    private void DismissImport_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.Students.StudentListViewModel vm)
        {
            vm.ImportStatus = string.Empty;
            vm.ImportDetails = string.Empty;
        }
    }
}
