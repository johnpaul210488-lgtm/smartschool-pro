using System.Text.Json.Serialization;

namespace SmartSchool.Core.DTOs;

/// <summary>Single pickup/drop attendance event row shared between API and Admin clients.</summary>
public sealed class BusEventListItem
{
    public BusEventListItem(long id, int studentId, string studentName, string className,
        int busId, string busNumber, string driverName, string type, string timePakistan,
        double latitude, double longitude, string? location, bool whatsAppSent)
    {
        Id = id;
        StudentId = studentId;
        StudentName = studentName;
        ClassName = className;
        BusId = busId;
        BusNumber = busNumber;
        DriverName = driverName;
        Type = type;
        TimePakistan = timePakistan;
        Latitude = latitude;
        Longitude = longitude;
        Location = location;
        WhatsAppSent = whatsAppSent;
    }

    public BusEventListItem() { }

    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("studentId")] public int StudentId { get; set; }
    [JsonPropertyName("studentName")] public string StudentName { get; set; } = string.Empty;
    [JsonPropertyName("className")] public string ClassName { get; set; } = string.Empty;
    [JsonPropertyName("busId")] public int BusId { get; set; }
    [JsonPropertyName("busNumber")] public string BusNumber { get; set; } = string.Empty;
    [JsonPropertyName("driverName")] public string DriverName { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
    [JsonPropertyName("timePakistan")] public string TimePakistan { get; set; } = string.Empty;
    [JsonPropertyName("latitude")] public double Latitude { get; set; }
    [JsonPropertyName("longitude")] public double Longitude { get; set; }
    [JsonPropertyName("location")] public string? Location { get; set; }
    [JsonPropertyName("whatsAppSent")] public bool WhatsAppSent { get; set; }
}
