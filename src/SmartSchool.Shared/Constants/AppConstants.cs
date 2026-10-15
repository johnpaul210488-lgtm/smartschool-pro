namespace SmartSchool.Shared.Constants;

/// <summary>Role strings for [Authorize(Roles = ...)] - const so attributes can use them.</summary>
public static class AppRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string Teacher = "Teacher";
    public const string Driver = "Driver";
    public const string Parent = "Parent";

    /// <summary>Office staff that can mutate records.</summary>
    public const string Staff = SuperAdmin + "," + Admin;

    /// <summary>Staff + teachers (read-heavy screens, dashboard).</summary>
    public const string StaffAndTeachers = SuperAdmin + "," + Admin + "," + Teacher;

    /// <summary>Who is allowed to post bus events.</summary>
    public const string EventPosters = Driver + "," + Admin + "," + SuperAdmin;
}

/// <summary>SignalR group names (spec: AdminGroup, ParentGroup_{studentId}).</summary>
public static class HubGroups
{
    public const string AdminGroup = "AdminGroup";
    public static string ParentGroup(int studentId) => $"ParentGroup_{studentId}";
    public static string BusGroup(int busId) => $"BusGroup_{busId}";
}

/// <summary>SignalR client-side method names the server pushes.</summary>
public static class HubEvents
{
    public const string SendPickInAlert = "SendPickInAlert";
    public const string SendDropOutAlert = "SendDropOutAlert";
    public const string UpdateBusLocation = "UpdateBusLocation";
    public const string SendTripUpdate = "SendTripUpdate";
    public const string SendAdminAlert = "SendAdminAlert";
}
