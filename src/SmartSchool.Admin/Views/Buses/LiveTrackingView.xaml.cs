using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsPresentation;
using SmartSchool.Admin.ViewModels.Buses;

namespace SmartSchool.Admin.Views.Buses;

public partial class LiveTrackingView : UserControl
{
    private readonly Dictionary<int, GMapMarker> _markers = new();
    private LiveTrackingViewModel? _vm;

    public LiveTrackingView()
    {
        InitializeComponent();

        // Mobile-style street map (Google) — user neeche drop-down se Satellite/OSM bhi kar sakta hai.
        Map.MapProvider = GoogleMapProvider.Instance;
        GMaps.Instance.Mode = AccessMode.ServerAndCache;
        Map.Position = new PointLatLng(33.5869, 71.4414); // Kohat, KPK
        Map.Zoom = 13;

        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.LocationArrived -= OnLocation;
            _vm = DataContext as LiveTrackingViewModel;
            if (_vm is not null) _vm.LocationArrived += OnLocation;
        };
    }

    private void ProviderPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Map.MapProvider = ProviderPicker.SelectedIndex switch
        {
            1 => GoogleSatelliteMapProvider.Instance,
            2 => OpenStreetMapProvider.Instance,
            _ => GoogleMapProvider.Instance,
        };
    }

    private void OnLocation(Models.BusLocationUpdate u)
    {
        var newShape = BuildBusMarker(u.BusNumber, u.DriverName);
        if (!_markers.TryGetValue(u.BusId, out var marker))
        {
            marker = new GMapMarker(new PointLatLng(u.Latitude, u.Longitude))
            {
                Shape = newShape,
                Offset = new System.Windows.Point(-44, -22)
            };
            _markers[u.BusId] = marker;
            Map.Markers.Add(marker);
        }
        else
        {
            marker.Position = new PointLatLng(u.Latitude, u.Longitude);
            marker.Shape = newShape; // name/speed refresh ke liye hamesha fresh visual
        }
        Map.ZoomAndCenterMarkers(null);
        if (Map.Markers.Count == 1) Map.Position = new PointLatLng(u.Latitude, u.Longitude);
    }

    /// <summary>🚌 pill + "Bus #N" + driver-name chip — roadpe direct readable.</summary>
    private static UIElement BuildBusMarker(string busNumber, string driverName)
    {
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
                { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.55, Color = Colors.Black }
        };

        // Top: yellow school-bus pill
        var pill = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(255, 143, 0)),   // school-bus amber
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(7, 3, 7, 3),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var pillRow = new StackPanel { Orientation = Orientation.Horizontal };
        pillRow.Children.Add(new TextBlock
        {
            Text = "🚌",
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 3, 0)
        });
        pillRow.Children.Add(new TextBlock
        {
            Text = "#" + busNumber,
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        });
        pill.Child = pillRow;
        stack.Children.Add(pill);

        // Bottom: driver-name chip (drivers ka naam sab ko nazar aaye)
        var chip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(210, 33, 33, 33)),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 2, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        chip.Child = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(driverName) ? "—" : driverName,
            Foreground = Brushes.White,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold
        };
        stack.Children.Add(chip);
        return stack;
    }
}
