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

        // Map init — kabhi bhi crash na kare; tiles ka probe Loaded ke baad ata hai.
        try
        {
            // Mobile-style street map (Google) — user neeche drop-down se Satellite/OSM bhi kar sakta hai.
            Map.MapProvider = GoogleMapProvider.Instance;
            GMaps.Instance.Mode = AccessMode.ServerAndCache;
            Map.Position = new PointLatLng(33.5869, 71.4414); // Kohat, KPK
            Map.Zoom = 13;
            Map.RetryLoadTile = 4;
        }
        catch (Exception ex)
        {
            ShowTileBanner("Map shuru nahi hua", ex.Message, "#FF6A00");
            _ = Services.BugReportService.SendAsync("Error", "LiveTracking map init fail", ex.Message, ex.ToString(), "LiveTrackingView");
        }

        Loaded += (_, _) => _ = ProbeTilesAsync();

        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.LocationArrived -= OnLocation;
            _vm = DataContext as LiveTrackingViewModel;
            if (_vm is not null) _vm.LocationArrived += OnLocation;
        };
    }

    /// <summary>Google/OSM tiles par 3s probe — block ho to khud OSM par shift karta hai, warna red warning + bug report.</summary>
    private async Task ProbeTilesAsync()
    {
        var googleOk = await TryFetchTileAsync("https://mt1.google.com/vt/lyrs=m&x=5722&y=4373&z=13");
        if (googleOk) return;

        var osmOk = await TryFetchTileAsync("https://a.tile.openstreetmap.org/13/5722/4373.png");
        if (osmOk)
        {
            ShowTileBanner("Google tiles block thin — OpenStreetMap mode on kar diya.",
                "Agar OSM bhi kharab ho jaye to drop-down se dobara choose karke dekhin. Ye masla khud report ho gaya.", "#FF6A00");
            Map.MapProvider = OpenStreetMapProvider.Instance;
            ProviderPicker.SelectedIndex = 2;
            _ = Services.BugReportService.SendAsync("Warning",
                "Live tracking: Google tiles unreachable (UAE network?)",
                "Google map tile probe 3 fail hui — auto-OSM fallback lagaya gaya.",
                null, "LiveTrackingView");
        }
        else
        {
            ShowTileBanner("Map tiles kisi bhi source se nahi aa rahi!",
                "Internet connection ya proxy/firewall tile servers block kar raha hai (mt.google.com, tile.openstreetmap.org). " +
                "Ye report hame bhej di gai hai — alag alag network se check karein.", "#D32F2F");
            _ = Services.BugReportService.SendAsync("Error",
                "Live tracking: ALL map tile servers unreachable",
                "Google bhi fail, OSM bhi fail — school network firewall/proxy block kar rahi hogi.",
                null, "LiveTrackingView");
        }
    }

    private static readonly System.Net.Http.HttpClient _tileHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private static async Task<bool> TryFetchTileAsync(string url)
    {
        try
        {
            using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("SmartSchoolPro-Admin/1.1 (+https://github.com/johnpaul210488-lgtm/smartschool-pro)");
            using var resp = await _tileHttp.SendAsync(req, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode) return false;
            var len = resp.Content.Headers.ContentLength ?? 0;
            var type = resp.Content.Headers.ContentType?.MediaType ?? "";
            return type.StartsWith("image") && len > 100;
        }
        catch { return false; }
    }

    private void ShowTileBanner(string title, string body, string colorHex)
    {
        TileBannerTitle.Text = title;
        TileBannerBody.Text = body;
        TileBanner.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
        TileBanner.Visibility = Visibility.Visible;
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
