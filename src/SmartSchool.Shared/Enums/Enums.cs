namespace SmartSchool.Shared.Enums;

/// <summary>Application roles. Stored in the DB and JWT as strings.</summary>
public enum UserRole
{
    SuperAdmin = 0,
    Admin = 1,
    Teacher = 2,
    Driver = 3,
    Parent = 4
}

public enum FeeStatus
{
    Pending = 0,
    Paid = 1,
    Overdue = 2,
    Partial = 3
}

public enum BusAttendanceType
{
    PickIn = 1,
    DropOut = 2
}

public enum WhatsAppStatus
{
    Queued = 0,
    Sent = 1,
    Failed = 2,
    // Historical Queued rows belong to the retired provider and are never swept.
    PendingQr = 3,
    Sending = 4,
    Submitted = 5,
    Unknown = 6,
    Held = 7,
    Expired = 8
}

public enum TripType
{
    /// <summary>Morning: picking students from home to school.</summary>
    ToSchool = 1,
    /// <summary>Afternoon: dropping students from school to home.</summary>
    ToHome = 2
}

public enum TripStatus
{
    Active = 1,
    Completed = 2,
    Cancelled = 3
}
