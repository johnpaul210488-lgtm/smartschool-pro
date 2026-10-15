namespace SmartSchool.Core.Models;

/// <summary>School-wide notice shown in the parent app's Notifications tab.</summary>
public sealed class Announcement
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public string? CreatedByName { get; set; }

    /// <summary>Null = everyone; otherwise only parents of that class see it highlighted first.</summary>
    public int? ClassId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
