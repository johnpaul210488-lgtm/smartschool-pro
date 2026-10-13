namespace SmartSchool.Core.Models;

public sealed class Student
{
    public int Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public DateOnly DateOfBirth { get; set; }

    public int ClassId { get; set; }
    public SchoolClass Class { get; set; } = null!;

    public string RollNo { get; set; } = string.Empty;

    /// <summary>Relative URL of the uploaded photo (upload module comes in a later phase).</summary>
    public string? PhotoUrl { get; set; }

    public string FatherName { get; set; } = string.Empty;

    /// <summary>Primary WhatsApp alert recipient.</summary>
    public string FatherPhone { get; set; } = string.Empty;

    public string? MotherPhone { get; set; }

    public string Address { get; set; } = string.Empty;

    /// <summary>Bus the student rides (null = not using school transport).</summary>
    public int? BusId { get; set; }
    public Bus? Bus { get; set; }

    /// <summary>Soft delete - fee/attendance history survives deactivation.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<Fee> Fees { get; set; } = new List<Fee>();
    public ICollection<BusAttendance> BusEvents { get; set; } = new List<BusAttendance>();
}
