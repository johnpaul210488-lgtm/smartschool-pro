using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Messaging;
using SmartSchool.Admin.ViewModels.Buses;

namespace SmartSchool.Admin.Views.Buses;

public partial class BusAttendanceLogView : UserControl
{
    public BusAttendanceLogView()
    {
        InitializeComponent();
        WeakReferenceMessenger.Default.Register<PrintAttendanceMessage>(this, (_, _) => PrintLog());
    }

    private void PrintLog()
    {
        var printDialog = new PrintDialog();
        if (printDialog.ShowDialog() == true)
        {
            var grid = FindVisualChild<DataGrid>(this);
            if (grid is null) return;

            grid.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            grid.Arrange(new Rect(new Point(0, 0), grid.DesiredSize));

            printDialog.PrintVisual(grid, "Bus Attendance Log");
        }
    }

    private static T? FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            var child = VisualTreeHelper.GetChild(obj, i);
            if (child is T result) return result;
            var descendant = FindVisualChild<T>(child);
            if (descendant is not null) return descendant;
        }
        return null;
    }
}
