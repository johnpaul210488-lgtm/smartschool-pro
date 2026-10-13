namespace SmartSchool.Core.Models;

public sealed class Bus
{
    public int Id { get; set; }

    /// <summary>Friendly number parents recognize, e.g. "7". Unique.</summary>
    public string BusNumber { get; set; } = string.Empty;

    /// <summary>Zero or one driver per bus.</summary>
    public int? DriverId { get; set; }
    public Driver? Driver { get; set; }

    /// <summary>Free-text route for now; a Routes/Stops module is planned.</summary>
    public string Route { get; set; } = string.Empty;

    public int Capacity { get; set; }

    /// <summary>Government registration / number plate.</summary>
    public string VehicleNo { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public ICollection<Student> Students { get; set; } = new List<Student>();
    public ICollection<Trip> Trips { get; set; } = new List<Trip>();
}
