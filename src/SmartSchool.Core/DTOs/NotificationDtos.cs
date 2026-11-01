namespace SmartSchool.Core.DTOs;

/// <summary>SignalR payload pushed on SendPickInAlert / SendDropOutAlert.</summary>
public sealed record BusEventNotification
{
    public required long EventId { get; init; }
    public required int StudentId { get; init; }
    public required string StudentName { get; init; }
    public required string ClassName { get; init; }
    public required int BusId { get; init; }
    public required string BusNumber { get; init; }
    public required string DriverName { get; init; }
    /// <summary>"PickIn" | "DropOut"</summary>
    public required string EventType { get; init; }
    /// <summary>Pakistan time, e.g. "13 Oct 2026 07:42 AM".</summary>
    public required string TimePakistan { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public required string LocationLabel { get; init; }
}

/// <summary>SignalR payload pushed on SendTripUpdate.</summary>
public sealed record TripNotification
{
    public required int TripId { get; init; }
    public required int BusId { get; init; }
    public required string BusNumber { get; init; }
    public required string DriverName { get; init; }
    /// <summary>"ToSchool" | "ToHome"</summary>
    public required string Type { get; init; }
    /// <summary>"Active" | "Completed" | "Cancelled"</summary>
    public required string Status { get; init; }
    public required string TimePakistan { get; init; }
}

/// <summary>SignalR payload pushed on UpdateBusLocation (live map).</summary>
public sealed record BusLocationUpdate
{
    public required int BusId { get; init; }
    public required string BusNumber { get; init; }
    public required string DriverName { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public double? SpeedKmh { get; init; }
    public required string TimePakistan { get; init; }
}

/// <summary>SignalR payload pushed on SendAdminAlert (e.g. WhatsApp permanent failure).</summary>
public sealed record AdminAlertNotification
{
    public required string Title { get; init; }
    public required string Message { get; init; }
    public required string TimePakistan { get; init; }
}

/// <summary>SignalR + FCM payload pushed when a child's fee is created / overdue.</summary>
public sealed record FeeDueNotification
{
    public required int StudentId { get; init; }
    public required string StudentName { get; init; }
    public required string ClassName { get; init; }
    public required string Month { get; init; }
    public required decimal Amount { get; init; }
    public required decimal Balance { get; init; }
    public required string DueDate { get; init; } // "dd MMM yyyy" in Pakistan time
    public required string TimePakistan { get; init; }
}
