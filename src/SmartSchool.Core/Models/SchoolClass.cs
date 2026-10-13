namespace SmartSchool.Core.Models;

/// <summary>
/// One row per class-section: Name = "5", Section = "A" => "Class 5-A".
/// Section lives here - a student links to exactly one class-section via ClassId.
/// </summary>
public sealed class SchoolClass
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Section { get; set; } = string.Empty;

    /// <summary>Class teacher (homeroom).</summary>
    public int? TeacherId { get; set; }
    public Teacher? ClassTeacher { get; set; }

    public ICollection<Student> Students { get; set; } = new List<Student>();

    public string DisplayName => $"{Name}-{Section}";
}
