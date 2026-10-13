using SmartSchool.Shared.Enums;

namespace SmartSchool.Core.Models;

/// <summary>
/// One Pick-In / Drop-Out moment. The client-supplied timestamp wins over
/// server time so offline driver-app syncs keep the true boarding moment.
/// </summary>
public sealed class BusAttendance
{
    public long Id { get; set; }

    public int StudentId { get; set; }
    public Student Student { get; set; } = null!;

    public int BusId { get; set; }
    public Bus Bus { get; set; } = null!;

    public int DriverId { get; set; }
    public Driver Driver { get; set; } = null!;

    public int? TripId { get; set; }
    public Trip? Trip { get; set; }

    public BusAttendanceType Type { get; set; }

    /// <summary>UTC in the database; always presented as Pakistan time on the wire.</summary>
    public DateTime TimestampUtc { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    /// <summary>Reverse-geocoded address or "GPS 33.58690, 71.44140" fallback.</summary>
    public string? LocationLabel { get; set; }

    public bool WhatsAppSent { get; set; }
}
