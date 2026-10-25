namespace SmartSchool.Core.Models;

/// <summary>Activation and billing calendar for exactly one class or one student.</summary>
public sealed class FeePlanAssignment
{
    public int Id { get; set; }
    public int? ClassId { get; set; }
    public SchoolClass? Class { get; set; }
    public int? StudentId { get; set; }
    public Student? Student { get; set; }
    public bool IsActive { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public int DueDay { get; set; } = 10;
    public int QuarterStartMonth { get; set; } = 1;
    public int SchoolYearStartMonth { get; set; } = 4;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
