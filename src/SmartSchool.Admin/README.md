# SmartSchool.Admin — Native Windows Desktop (WPF .NET 8)

Native WPF admin panel. **Koi browser / WebView / Electron nahi** — pure XAML controls + Material Design.

## Run karna (Windows PC par)

```powershell
# API pehle se chal rahi honi chahiye (http://localhost:5000)
cd src\SmartSchool.Admin
dotnet run
```

Login: `admin` / `Admin@12345` (ya jo bhi staff account banaya ho).

> Backend kisi AUR PC par ho (school server): `appsettings.json` mein `Api.BaseUrl` badal do,
> e.g. `"http://192.168.1.10:5000"` — koi code change nahi karna.

## Installable .exe banana (single file, .NET installed ki zaroorat nahi)

```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o publish
```

`publish\SmartSchool.Admin.exe` — ek hi file, USB se bhi chal jayegi (sirf `appsettings.json` saath rakhna).

## Kya andar hai

| Area | Files |
|---|---|
| Login | `Views/LoginWindow.xaml` — JWT login, encrypted session file (DPAPI), auto token refresh |
| Shell | `Views/MainWindow.xaml` — sidebar nav, theme toggle (🌙), live status line, toast panel |
| Dashboard | cards (students/fees/buses/pick-ins/WhatsApp) + **live attendance grid (SignalR)** |
| Students | list (search + class filter), add/edit form (**red-border validation**), profile (recent events + fees) |
| Fees | dashboard (dues card, filters, paging), collect payment, **printable receipt (FlowDocument → default printer)** |
| Buses | CRUD + driver assign |
| Live Tracking | **GMap.NET** (OpenStreetMap, offline cache) — SignalR `UpdateBusLocation` par bus pins move |
| Bus Attendance | today's log per bus — SignalR par rows top par insert |
| Drivers | CRUD + mobile-app login banana |
| Reports | monthly collection vs dues, today's pick/drop counts |

## Real-time wiring (SignalR `/hubs/notifications`)

`Services/SignalRClientService.cs` — JWT ke saath connect, **auto-reconnect**, phir
`WeakReferenceMessenger` se sab ViewModels ko typesafe messages:

| Server event | Client reaction |
|---|---|
| `SendPickInAlert` | toast (green) + sound + dashboard/log rows + student grid green chip |
| `SendDropOutAlert` | toast (orange) + sound + "Dropped" status |
| `UpdateBusLocation` | map pin move + live list speed/time |
| `SendTripUpdate` | trip toast + completed trip ka pin remove |

## Structure

```
Views/           XAML (LoginWindow, MainWindow, Dashboard, Students/, Fees/, Buses/, Drivers/, Reports/)
ViewModels/      MVVM (CommunityToolkit.Mvvm — [ObservableProperty]/[RelayCommand])
Services/        ApiClient (envelope-aware), SignalRClientService, ToastService, SecureTokenStore, ThemeHelper
Models/          API/SignalR DTO mirrors (System.Text.Json)
Helpers/         Converters, PasswordBoxHelper
appsettings.json Api.BaseUrl + HubPath
```
