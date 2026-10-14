using System.Windows;
using SmartSchool.Admin.Services;
using SmartSchool.Admin.ViewModels;

namespace SmartSchool.Admin.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(AppSession session)
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;
    }

    protected override void OnClosed(EventArgs e)
    {
        _ = _vm.SignalR.DisposeAsync();
        base.OnClosed(e);
    }
}
