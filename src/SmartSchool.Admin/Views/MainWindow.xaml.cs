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
        // CHANGED: poori window ka zoom — Ctrl +/- ya neeche 🔍 buttons; %LocalAppData% mein save
        Helpers.ZoomHelper.Apply(this, MainRoot);
        ZoomLabel.Text = $"{Helpers.ZoomHelper.CurrentZoom * 100:0}%";
        Helpers.ZoomHelper.ZoomChanged += z => ZoomLabel.Text = $"{z * 100:0}%";
        // CHANGED (added): guide Hissa C — Ctrl + mouse wheel se bhi zoom
        PreviewMouseWheel += MainWindow_CtrlWheelZoom;
        _vm = new MainViewModel();
        DataContext = _vm;
    }

    private void MainWindow_CtrlWheelZoom(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.Control) return;
        e.Handled = true;
        if (e.Delta > 0) Helpers.ZoomHelper.ZoomIn(); else Helpers.ZoomHelper.ZoomOut();
    }

    private void ZoomIn_Click(object sender, System.Windows.RoutedEventArgs e) => Helpers.ZoomHelper.ZoomIn();
    private void ZoomOut_Click(object sender, System.Windows.RoutedEventArgs e) => Helpers.ZoomHelper.ZoomOut();

    private void ReportProblem_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var w = new ReportProblemWindow { Owner = this };
        w.ShowDialog();
    }

    protected override void OnClosed(EventArgs e)
    {
        _ = _vm.SignalR.DisposeAsync();
        base.OnClosed(e);
    }
}
