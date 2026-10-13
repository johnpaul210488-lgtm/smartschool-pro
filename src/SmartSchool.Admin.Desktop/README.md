# SmartSchool.Admin.Desktop (WPF — Phase 2)

**The admin panel is a real Windows desktop .exe — no browser, no Electron, no WebView.**

Planned stack and structure (created in Phase 2 of the build):

| Concern | Choice |
|---|---|
| Framework | WPF on .NET 8 (`net8.0-windows`, `UseWPF`) |
| UI toolkit | MahApps.Metro + Material Design themes |
| MVVM | CommunityToolkit.Mvvm (source-generated view-models) |
| HTTP | Refit (typed client against SmartSchool.API's envelope) |
| Real-time | `Microsoft.AspNetCore.SignalR.Client` with `WithAutomaticReconnect()` |
| Alerts | Windows toast + `SoundPlayer` on `SendPickInAlert` / `SendDropOutAlert` |
| Packaging | `PublishSingleFile` exe + MSIX / Inno Setup installer |

```
SmartSchool.Admin.Desktop/
├── App.xaml(.cs)                 # DI host, startup, global exception handling
├── Views/                        # LoginView, DashboardView, StudentsView, FeesView,
│                                 # BusesView, LiveTrackingView, WhatsAppLogsView, ReportsView
├── ViewModels/                   # one VM per view (CommunityToolkit.Mvvm)
├── Services/
│   ├── ApiClient/                # Refit interfaces mirroring the API controllers
│   ├── RealTimeClient.cs         # /hubs/notifications lifecycle + event dispatch
│   ├── AlertService.cs           # popup + sound for bus events
│   └── AuthSession.cs            # JWT storage (DPAPI-protected)
└── Resources/                    # sounds, icons, styles
```

**Contract cheat-sheet (already live in Phase 1):**
- API responses: `{ success, message, data, errors }` everywhere.
- Hub: `/hubs/notifications?access_token={jwt}` — staff auto-join `AdminGroup` on connect.
- Events: `SendPickInAlert`, `SendDropOutAlert` (popup+sound), `UpdateBusLocation` (map), `SendTripUpdate`.
- Hub methods for this app: `WatchBus(busId)` / `UnwatchBus(busId)`.

> Build note: WPF requires Windows (`net8.0-windows`) - build on the school office PC with
> `dotnet build` / Visual Studio 2022.
