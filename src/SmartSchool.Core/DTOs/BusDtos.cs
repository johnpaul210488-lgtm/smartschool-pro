namespace SmartSchool.Core.DTOs;

public sealed record BusListItem(
    int Id,
    string BusNumber,
    string Route,
    string VehicleNo,
    int Capacity,
    int? DriverId,
    string? DriverName,
    bool IsActive,
    int StudentCount);

public sealed class BusUpsertRequest
{
    public string BusNumber { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string VehicleNo { get; set; } = string.Empty;
    public int Capacity { get; set; } = 40;
    public bool IsActive { get; set; } = true;
}

public sealed class AssignDriverRequest
{
    /// <summary>Null unassigns the current driver.</summary>
    public int? DriverId { get; set; }
}

/// <summary>One row of the driver's pickup list (driver mobile app).</summary>
public sealed record BusManifestItem(
    int StudentId,
    string FullName,
    string ClassName,
    string RollNo,
    string? PhotoUrl,
    string FatherName,
    string FatherPhone,
    string? MotherPhone,
    string Address,
    // Today's event type if already boarded/dropped, else null.
    string? TodayStatus);
