using SmartSchool.Shared.Enums;

namespace SmartSchool.Core.DTOs;

/// <summary>
/// Body for POST /api/bus-attendance/pick-in and /drop-out. Posted by the driver
/// app (and replayed by its offline outbox). TimestampUtc should be when the
/// event HAPPENED (device clock); server time is used when omitted.
/// </summary>
public sealed class BusEventRequest
{
    public int StudentId { get; set; }

    /// <summary>Optional for drivers (resolved from student); handy for admin testing.</summary>
    public int? BusId { get; set; }
    public int? DriverId { get; set; }
    public int? TripId { get; set; }

    public DateTime? TimestampUtc { get; set; }

    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public sealed record BusEventResponse(
    long EventId,
    string Type,
    string TimePakistan,
    string Location,
    bool AdminNotified,
    int WhatsAppRecipientsQueued);

public sealed record BusEventListItem(
    long Id,
    int StudentId,
    string StudentName,
    string ClassName,
    int BusId,
    string BusNumber,
    string DriverName,
    string Type,
    string TimePakistan,
    double Latitude,
    double Longitude,
    string? Location,
    bool WhatsAppSent);

public sealed class TripStartRequest
{
    public int BusId { get; set; }
    public TripType Type { get; set; } = TripType.ToSchool;
}

public sealed record TripResponse(
    int Id,
    int BusId,
    string BusNumber,
    string Route,
    int DriverId,
    string DriverName,
    string Type,
    string Status,
    string StartedAtPakistan,
    string? EndedAtPakistan,
    int EventCount);
