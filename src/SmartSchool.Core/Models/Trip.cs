using SmartSchool.Shared.Enums;

namespace SmartSchool.Core.Models;

/// <summary>
/// One driving session: driver starts a trip on a bus; Pick-In/Drop-Out
/// events hang off it.
/// </summary>
public sealed class Trip
{
    public int Id { get; set; }

    public int BusId { get; set; }
    public Bus Bus { get; set; } = null!;

    public int DriverId { get; set; }
    public Driver Driver { get; set; } = null!;

    public TripType Type { get; set; }

    public TripStatus Status { get; set; } = TripStatus.Active;

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? EndedAtUtc { get; set; }

    public ICollection<BusAttendance> Events { get; set; } = new List<BusAttendance>();
}
