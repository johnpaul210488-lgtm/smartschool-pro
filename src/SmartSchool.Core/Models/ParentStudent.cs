namespace SmartSchool.Core.Models;

/// <summary>Links a Parent login to one or more children.</summary>
public sealed class ParentStudent
{
    public int ParentUserId { get; set; }
    public User ParentUser { get; set; } = null!;

    public int StudentId { get; set; }
    public Student Student { get; set; } = null!;
}
