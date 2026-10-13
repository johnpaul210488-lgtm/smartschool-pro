namespace SmartSchool.Core.Models;

/// <summary>
/// Driver profile; the driver signs in with the linked User (Role = Driver).
/// One source of truth for assignment is Bus.DriverId (avoids circular FK).
/// </summary>
public sealed class Driver
{
    public int Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string LicenseNo { get; set; } = string.Empty;

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    public Bus? AssignedBus { get; set; }

    public ICollection<Trip> Trips { get; set; } = new List<Trip>();
}
