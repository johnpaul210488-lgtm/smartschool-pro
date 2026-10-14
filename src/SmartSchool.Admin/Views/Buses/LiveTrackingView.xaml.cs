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

        Map.MapProvider = OpenStreetMapProvider.Instance;
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

    private void OnLocation(Models.BusLocationUpdate u)
    {
        if (!_markers.TryGetValue(u.BusId, out var marker))
        {
            marker = new GMapMarker(new PointLatLng(u.Latitude, u.Longitude))
            {
                Shape = BuildBusMarker(u.BusNumber),
                Offset = new System.Windows.Point(-17, -17)
            };
            _markers[u.BusId] = marker;
            Map.Markers.Add(marker);
        }
        else
        {
            marker.Position = new PointLatLng(u.Latitude, u.Longitude);
        }
        Map.ZoomAndCenterMarkers(null);
        if (Map.Markers.Count == 1) Map.Position = new PointLatLng(u.Latitude, u.Longitude);
    }

    private static UIElement BuildBusMarker(string busNumber)
    {
        var grid = new Grid { Width = 40, Height = 40 };
        var circle = new Ellipse
        {
            Width = 34,
            Height = 34,
            Fill = new SolidColorBrush(Color.FromRgb(255, 145, 0)),
            Stroke = Brushes.White,
            StrokeThickness = 2,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.5 }
        };
        var text = new TextBlock
        {
            Text = "#" + busNumber,
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(circle);
        grid.Children.Add(text);
        return grid;
    }
}
