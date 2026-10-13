namespace SmartSchool.Core.Models;

public sealed class Teacher
{
    public int Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string? Subject { get; set; }

    /// <summary>Optional linked login account (Role = Teacher).</summary>
    public int? UserId { get; set; }
    public User? User { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<SchoolClass> Classes { get; set; } = new List<SchoolClass>();
}
