using System.Text.Json.Serialization;

namespace SmartSchool.Admin.Models;

// ============ Envelope (mirrors SmartSchool.Shared ApiResponse) ============
public sealed class ApiResponse<T>
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
    [JsonPropertyName("data")] public T? Data { get; set; }
    [JsonPropertyName("errors")] public List<string> Errors { get; set; } = new();
}

public sealed class PagedResult<T>
{
    [JsonPropertyName("items")] public List<T> Items { get; set; } = new();
    [JsonPropertyName("page")] public int Page { get; set; }
    [JsonPropertyName("pageSize")] public int PageSize { get; set; }
    [JsonPropertyName("totalCount")] public int TotalCount { get; set; }
    [JsonPropertyName("totalPages")] public int TotalPages { get; set; }
}

// ============ Auth ============
public sealed class LoginRequest
{
    [JsonPropertyName("username")] public string Username { get; set; } = string.Empty;
    [JsonPropertyName("password")] public string Password { get; set; } = string.Empty;
}

public sealed class UserInfoDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("username")] public string Username { get; set; } = string.Empty;
    [JsonPropertyName("fullName")] public string FullName { get; set; } = string.Empty;
    [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;
}

public sealed class LoginResponse
{
    [JsonPropertyName("token")] public string Token { get; set; } = string.Empty;
    [JsonPropertyName("expiresAtUtc")] public DateTime ExpiresAtUtc { get; set; }
    [JsonPropertyName("serverTimePakistan")] public string ServerTimePakistan { get; set; } = string.Empty;
    [JsonPropertyName("user")] public UserInfoDto? User { get; set; }
}

// ============ Dashboard ============
public sealed class DashboardSummary
{
    [JsonPropertyName("datePakistan")] public string DatePakistan { get; set; } = string.Empty;
    [JsonPropertyName("activeStudents")] public int ActiveStudents { get; set; }
    [JsonPropertyName("totalTeachers")] public int TotalTeachers { get; set; }
    [JsonPropertyName("totalBuses")] public int TotalBuses { get; set; }
    [JsonPropertyName("totalDrivers")] public int TotalDrivers { get; set; }
    [JsonPropertyName("activeTripsNow")] public int ActiveTripsNow { get; set; }
    [JsonPropertyName("pickInsToday")] public int PickInsToday { get; set; }
    [JsonPropertyName("dropOutsToday")] public int DropOutsToday { get; set; }
    [JsonPropertyName("pendingFeesCount")] public int PendingFeesCount { get; set; }
    [JsonPropertyName("pendingFeesAmount")] public decimal PendingFeesAmount { get; set; }
    [JsonPropertyName("collectedThisMonth")] public decimal CollectedThisMonth { get; set; }
    [JsonPropertyName("whatsAppSentToday")] public int WhatsAppSentToday { get; set; }
    [JsonPropertyName("whatsAppFailedTotal")] public int WhatsAppFailedTotal { get; set; }
}

// ============ Students ============
public class StudentListItem
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("fullName")] public string FullName { get; set; } = string.Empty;
    [JsonPropertyName("rollNo")] public string RollNo { get; set; } = string.Empty;
    [JsonPropertyName("className")] public string ClassName { get; set; } = string.Empty;
    [JsonPropertyName("classId")] public int ClassId { get; set; }
    [JsonPropertyName("busNumber")] public string? BusNumber { get; set; }
    [JsonPropertyName("fatherName")] public string FatherName { get; set; } = string.Empty;
    [JsonPropertyName("fatherPhone")] public string FatherPhone { get; set; } = string.Empty;
    [JsonPropertyName("photoUrl")] public string? PhotoUrl { get; set; }
    [JsonPropertyName("isActive")] public bool IsActive { get; set; }
    [JsonPropertyName("gender")] public string Gender { get; set; } = string.Empty;
    [JsonPropertyName("isNewStudent")] public bool IsNewStudent { get; set; }

    [JsonIgnore] public string GenderIcon => Gender == "Boy" ? "👦" : Gender == "Girl" ? "👧" : "·";

    // <-- CHANGED: photo ka poora URL (server par /uploads/* public serve hota hai; photo na ho to null)
    [JsonIgnore] public string? PhotoFullUrl => string.IsNullOrWhiteSpace(PhotoUrl)
        ? null
        : Services.ApiClient.Instance.BaseUrl.TrimEnd('/') + PhotoUrl;

    /// <summary>Live status driven by SignalR alerts (not from the API).</summary>
    [JsonIgnore] public string LiveStatus { get; set; } = string.Empty;
}

public sealed class StudentRecentEvent
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
    [JsonPropertyName("timePakistan")] public string TimePakistan { get; set; } = string.Empty;
    [JsonPropertyName("busNumber")] public string BusNumber { get; set; } = string.Empty;
    [JsonPropertyName("location")] public string? Location { get; set; }
}

public sealed class StudentDetailResponse : StudentListItem
{
    [JsonPropertyName("dateOfBirth")] public DateOnly DateOfBirth { get; set; }
    [JsonPropertyName("busId")] public int? BusId { get; set; }
    [JsonPropertyName("motherPhone")] public string? MotherPhone { get; set; }
    [JsonPropertyName("address")] public string Address { get; set; } = string.Empty;
    [JsonPropertyName("totalOutstandingFees")] public decimal TotalOutstandingFees { get; set; }
    [JsonPropertyName("recentBusEvents")] public List<StudentRecentEvent> RecentBusEvents { get; set; } = new();
}

public sealed class StudentUpsertRequest
{
    [JsonPropertyName("fullName")] public string FullName { get; set; } = string.Empty;
    [JsonPropertyName("gender")] public string Gender { get; set; } = string.Empty;
    [JsonPropertyName("isNewStudent")] public bool IsNewStudent { get; set; }
    [JsonPropertyName("dateOfBirth")] public string DateOfBirth { get; set; } = string.Empty; // yyyy-MM-dd
    [JsonPropertyName("classId")] public int ClassId { get; set; }
    [JsonPropertyName("rollNo")] public string RollNo { get; set; } = string.Empty;
    [JsonPropertyName("photoUrl")] public string? PhotoUrl { get; set; }
    [JsonPropertyName("fatherName")] public string FatherName { get; set; } = string.Empty;
    [JsonPropertyName("fatherPhone")] public string FatherPhone { get; set; } = string.Empty;
    [JsonPropertyName("motherPhone")] public string? MotherPhone { get; set; }
    [JsonPropertyName("address")] public string Address { get; set; } = string.Empty;
    [JsonPropertyName("busId")] public int? BusId { get; set; }
    [JsonPropertyName("isActive")] public bool? IsActive { get; set; }
}

// ============ Classes ============
public sealed class ClassListItem
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("section")] public string Section { get; set; } = string.Empty;
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = string.Empty;
    [JsonPropertyName("teacherId")] public int? TeacherId { get; set; }
    [JsonPropertyName("classTeacherName")] public string? ClassTeacherName { get; set; }
    [JsonPropertyName("studentCount")] public int StudentCount { get; set; }
}

// ============ Buses ============
public sealed class BusListItem
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("busNumber")] public string BusNumber { get; set; } = string.Empty;
    [JsonPropertyName("route")] public string Route { get; set; } = string.Empty;
    [JsonPropertyName("vehicleNo")] public string VehicleNo { get; set; } = string.Empty;
    [JsonPropertyName("capacity")] public int Capacity { get; set; }
    [JsonPropertyName("driverId")] public int? DriverId { get; set; }
    [JsonPropertyName("driverName")] public string? DriverName { get; set; }
    [JsonPropertyName("isActive")] public bool IsActive { get; set; }
    [JsonPropertyName("studentCount")] public int StudentCount { get; set; }
}

public sealed class BusUpsertRequest
{
    [JsonPropertyName("busNumber")] public string BusNumber { get; set; } = string.Empty;
    [JsonPropertyName("route")] public string Route { get; set; } = string.Empty;
    [JsonPropertyName("vehicleNo")] public string VehicleNo { get; set; } = string.Empty;
    [JsonPropertyName("capacity")] public int Capacity { get; set; } = 40;
    [JsonPropertyName("isActive")] public bool IsActive { get; set; } = true;
}

// ============ Drivers ============
public sealed class DriverListItem
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("fullName")] public string FullName { get; set; } = string.Empty;
    [JsonPropertyName("phone")] public string Phone { get; set; } = string.Empty;
    [JsonPropertyName("licenseNo")] public string LicenseNo { get; set; } = string.Empty;
    [JsonPropertyName("userId")] public int UserId { get; set; }
    [JsonPropertyName("username")] public string Username { get; set; } = string.Empty;
    [JsonPropertyName("isActive")] public bool IsActive { get; set; }
    [JsonPropertyName("assignedBusId")] public int? AssignedBusId { get; set; }
    [JsonPropertyName("assignedBusNumber")] public string? AssignedBusNumber { get; set; }

    /// <summary>Dropdown mein naam ke saath bus number (agar kisi bus par assign ho).</summary>
    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(AssignedBusNumber)
        ? FullName
        : $"{FullName}  ·  Bus #{AssignedBusNumber}";
}

public sealed class DriverCreateRequest
{
    [JsonPropertyName("fullName")] public string FullName { get; set; } = string.Empty;
    [JsonPropertyName("phone")] public string Phone { get; set; } = string.Empty;
    [JsonPropertyName("licenseNo")] public string LicenseNo { get; set; } = string.Empty;
    [JsonPropertyName("username")] public string Username { get; set; } = string.Empty;
    [JsonPropertyName("password")] public string Password { get; set; } = string.Empty;
}

public sealed class DriverUpdateRequest
{
    [JsonPropertyName("fullName")] public string FullName { get; set; } = string.Empty;
    [JsonPropertyName("phone")] public string Phone { get; set; } = string.Empty;
    [JsonPropertyName("licenseNo")] public string LicenseNo { get; set; } = string.Empty;
    [JsonPropertyName("isActive")] public bool IsActive { get; set; } = true;
}

// ============ Fees ============
public sealed class FeeListItem
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("studentId")] public int StudentId { get; set; }
    [JsonPropertyName("studentName")] public string StudentName { get; set; } = string.Empty;
    [JsonPropertyName("className")] public string ClassName { get; set; } = string.Empty;
    [JsonPropertyName("amount")] public decimal Amount { get; set; }
    [JsonPropertyName("month")] public string Month { get; set; } = string.Empty;
    [JsonPropertyName("dueDate")] public DateOnly DueDate { get; set; }
    [JsonPropertyName("paidDate")] public DateOnly? PaidDate { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("receiptNo")] public string? ReceiptNo { get; set; }
    [JsonPropertyName("notes")] public string? Notes { get; set; }
    [JsonPropertyName("amountPaid")] public decimal AmountPaid { get; set; }
    [JsonPropertyName("balance")] public decimal Balance { get; set; }
    [JsonPropertyName("feeHeadName")] public string? FeeHeadName { get; set; }
    [JsonPropertyName("isAutoGenerated")] public bool IsAutoGenerated { get; set; }
    [JsonPropertyName("lastPaymentAmount")] public decimal LastPaymentAmount { get; set; }
    public bool HasBalance => Balance > 0m;
}

public sealed class FeePaymentItem
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("feeId")] public int FeeId { get; set; }
    [JsonPropertyName("amount")] public decimal Amount { get; set; }
    [JsonPropertyName("paidDate")] public DateOnly PaidDate { get; set; }
    [JsonPropertyName("receiptNo")] public string ReceiptNo { get; set; } = string.Empty;
    [JsonPropertyName("method")] public string? Method { get; set; }
    [JsonPropertyName("notes")] public string? Notes { get; set; }
}

public sealed class FeeCreateRequest
{
    [JsonPropertyName("studentId")] public int StudentId { get; set; }
    [JsonPropertyName("amount")] public decimal Amount { get; set; }
    [JsonPropertyName("month")] public string Month { get; set; } = string.Empty; // yyyy-MM
    [JsonPropertyName("dueDate")] public string DueDate { get; set; } = string.Empty; // yyyy-MM-dd
    [JsonPropertyName("notes")] public string? Notes { get; set; }
}

public sealed class DuesSummaryResponse
{
    [JsonPropertyName("totalUnpaidCount")] public int TotalUnpaidCount { get; set; }
    [JsonPropertyName("totalUnpaidAmount")] public decimal TotalUnpaidAmount { get; set; }
    [JsonPropertyName("items")] public List<FeeListItem> Items { get; set; } = new();
}

// CHANGED: configurable draft fee plan; enum values are strings for the API's enum converter.
public sealed class ClassFeePlanItemDto
{
    [JsonPropertyName("feeHeadId")] public int FeeHeadId { get; set; }
    [JsonPropertyName("code")] public string Code { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("category")] public string Category { get; set; } = string.Empty;
    [JsonPropertyName("defaultCycle")] public string DefaultCycle { get; set; } = "Monthly";
    [JsonPropertyName("billingCycle")] public string BillingCycle { get; set; } = "Monthly";
    [JsonPropertyName("amount")] public decimal Amount { get; set; }
    [JsonPropertyName("isIncluded")] public bool IsIncluded { get; set; }
    [JsonPropertyName("isOptionalForStudent")] public bool IsOptionalForStudent { get; set; }
    [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
}

public sealed class SaveClassFeePlanRequest
{
    [JsonPropertyName("items")] public List<SaveClassFeePlanItemRequest> Items { get; set; } = new();
}

public sealed class SaveClassFeePlanItemRequest
{
    [JsonPropertyName("feeHeadId")] public int FeeHeadId { get; set; }
    [JsonPropertyName("billingCycle")] public string BillingCycle { get; set; } = "Monthly";
    [JsonPropertyName("amount")] public decimal Amount { get; set; }
    [JsonPropertyName("isIncluded")] public bool IsIncluded { get; set; }
    [JsonPropertyName("isOptionalForStudent")] public bool IsOptionalForStudent { get; set; }
}

// CHANGED: class/student assignment schedule + student opt-in/override contracts.
public sealed class FeePlanAssignmentDto
{
    [JsonPropertyName("isActive")] public bool IsActive { get; set; }
    [JsonPropertyName("effectiveFrom")] public DateOnly EffectiveFrom { get; set; }
    [JsonPropertyName("dueDay")] public int DueDay { get; set; }
    [JsonPropertyName("quarterStartMonth")] public int QuarterStartMonth { get; set; }
    [JsonPropertyName("schoolYearStartMonth")] public int SchoolYearStartMonth { get; set; }
}
public sealed class SaveFeePlanAssignmentRequest
{
    [JsonPropertyName("isActive")] public bool IsActive { get; set; }
    [JsonPropertyName("effectiveFrom")] public DateOnly EffectiveFrom { get; set; }
    [JsonPropertyName("dueDay")] public int DueDay { get; set; }
    [JsonPropertyName("quarterStartMonth")] public int QuarterStartMonth { get; set; }
    [JsonPropertyName("schoolYearStartMonth")] public int SchoolYearStartMonth { get; set; }
}
public sealed class StudentFeePlanItemDto
{
    [JsonPropertyName("feeHeadId")] public int FeeHeadId { get; set; }
    [JsonPropertyName("code")] public string Code { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("category")] public string Category { get; set; } = string.Empty;
    [JsonPropertyName("isOptionalForStudent")] public bool IsOptionalForStudent { get; set; }
    [JsonPropertyName("isOverridden")] public bool IsOverridden { get; set; }
    [JsonPropertyName("isOptedIn")] public bool IsOptedIn { get; set; }
    [JsonPropertyName("isIncluded")] public bool IsIncluded { get; set; }
    [JsonPropertyName("amount")] public decimal Amount { get; set; }
    [JsonPropertyName("billingCycle")] public string BillingCycle { get; set; } = "Monthly";
}
public sealed class SaveStudentFeePlanRequest
{
    [JsonPropertyName("items")] public List<SaveStudentFeePlanItemRequest> Items { get; set; } = new();
}
public sealed class SaveStudentFeePlanItemRequest
{
    [JsonPropertyName("feeHeadId")] public int FeeHeadId { get; set; }
    [JsonPropertyName("isOverridden")] public bool IsOverridden { get; set; }
    [JsonPropertyName("isOptedIn")] public bool IsOptedIn { get; set; }
    [JsonPropertyName("isIncluded")] public bool IsIncluded { get; set; }
    [JsonPropertyName("amount")] public decimal Amount { get; set; }
    [JsonPropertyName("billingCycle")] public string BillingCycle { get; set; } = "Monthly";
}

// ============ Bus attendance / tracking ============
public sealed class BusEventListItem
{
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

// ============ SignalR payloads ============
public sealed class BusEventNotification
{
    [JsonPropertyName("eventId")] public long EventId { get; set; }
    [JsonPropertyName("studentId")] public int StudentId { get; set; }
    [JsonPropertyName("studentName")] public string StudentName { get; set; } = string.Empty;
    [JsonPropertyName("className")] public string ClassName { get; set; } = string.Empty;
    [JsonPropertyName("busId")] public int BusId { get; set; }
    [JsonPropertyName("busNumber")] public string BusNumber { get; set; } = string.Empty;
    [JsonPropertyName("driverName")] public string DriverName { get; set; } = string.Empty;
    [JsonPropertyName("eventType")] public string EventType { get; set; } = string.Empty; // PickIn | DropOut
    [JsonPropertyName("timePakistan")] public string TimePakistan { get; set; } = string.Empty;
    [JsonPropertyName("latitude")] public double Latitude { get; set; }
    [JsonPropertyName("longitude")] public double Longitude { get; set; }
    [JsonPropertyName("locationLabel")] public string LocationLabel { get; set; } = string.Empty;
}

public sealed class BusLocationUpdate
{
    [JsonPropertyName("busId")] public int BusId { get; set; }
    [JsonPropertyName("busNumber")] public string BusNumber { get; set; } = string.Empty;
    [JsonPropertyName("driverName")] public string DriverName { get; set; } = string.Empty;
    [JsonPropertyName("latitude")] public double Latitude { get; set; }
    [JsonPropertyName("longitude")] public double Longitude { get; set; }
    [JsonPropertyName("speedKmh")] public double? SpeedKmh { get; set; }
    [JsonPropertyName("timePakistan")] public string TimePakistan { get; set; } = string.Empty;
}

public sealed class TripNotification
{
    [JsonPropertyName("tripId")] public int TripId { get; set; }
    [JsonPropertyName("busId")] public int BusId { get; set; }
    [JsonPropertyName("busNumber")] public string BusNumber { get; set; } = string.Empty;
    [JsonPropertyName("driverName")] public string DriverName { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("timePakistan")] public string TimePakistan { get; set; } = string.Empty;
}

/// <summary>Server-side red flag pushed on SendAdminAlert (e.g. WhatsApp permanently failed after 3 retries).</summary>
public sealed class AdminAlertNotification
{
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
    [JsonPropertyName("timePakistan")] public string TimePakistan { get; set; } = string.Empty;
}

// ============ Bug / error reports ============
public sealed class ErrorReportCreatedResponse
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("issueNumber")] public int? IssueNumber { get; set; }
    [JsonPropertyName("occurrences")] public int Occurrences { get; set; }
}

// ============ Fee screen: simple charge + full student history ============
public sealed class FeeChargeResultDto
{
    [JsonPropertyName("studentCount")] public int StudentCount { get; set; }
    [JsonPropertyName("created")] public int Created { get; set; }
    [JsonPropertyName("skipped")] public int Skipped { get; set; }
}

public sealed class FeeHistoryRowDto
{
    [JsonPropertyName("feeId")] public int FeeId { get; set; }
    [JsonPropertyName("month")] public string Month { get; set; } = string.Empty;
    [JsonPropertyName("feeHead")] public string FeeHead { get; set; } = string.Empty;
    [JsonPropertyName("cycle")] public string Cycle { get; set; } = string.Empty;
    [JsonPropertyName("dueDate")] public DateOnly DueDate { get; set; }
    [JsonPropertyName("amount")] public decimal Amount { get; set; }
    [JsonPropertyName("amountPaid")] public decimal AmountPaid { get; set; }
    [JsonPropertyName("balance")] public decimal Balance { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("receiptNo")] public string? ReceiptNo { get; set; }
    [JsonPropertyName("isAutoGenerated")] public bool IsAutoGenerated { get; set; }

    /// <summary>Bill plan se khud bani (auto) hai ya hath se.</summary>
    [JsonIgnore] public string TypeText => IsAutoGenerated ? "Plan se" : "Hath se";

    /// <summary>Bill par koi payment nahi, paid nahi, aur auto nahi: tabhi edit/delete ho sakta hai.</summary>
    [JsonIgnore] public bool CanChange => !IsAutoGenerated && AmountPaid <= 0m && !string.Equals(Status, "Paid", StringComparison.OrdinalIgnoreCase);

    /// <summary>Upar bill select karne par ek seedha message: kya ho sakta hai aur kyun.</summary>
    [JsonIgnore]
    public string WhyNotChange => IsAutoGenerated
        ? "Yeh bill plan se bani hai, is liye yahan edit/delete nahi hoti. Plan badal kar aage ki bills rokein."
        : AmountPaid > 0m || string.Equals(Status, "Paid", StringComparison.OrdinalIgnoreCase)
            ? "Is bill par payment ho chuki hai, is liye edit/delete nahi hoti. Pehle us payment ko reverse karein."
            : "Is bill ki amount, due date badal sakte hain ya bill delete kar sakte hain.";
}

public sealed class FeeHistoryPaymentDto
{
    [JsonPropertyName("paymentId")] public int PaymentId { get; set; }
    [JsonPropertyName("feeId")] public int FeeId { get; set; }
    [JsonPropertyName("feeHead")] public string FeeHead { get; set; } = string.Empty;
    [JsonPropertyName("month")] public string Month { get; set; } = string.Empty;
    [JsonPropertyName("paidAtPkt")] public DateTime PaidAtPkt { get; set; }
    [JsonPropertyName("amount")] public decimal Amount { get; set; }
    [JsonPropertyName("receiptNo")] public string ReceiptNo { get; set; } = string.Empty;
    [JsonPropertyName("method")] public string? Method { get; set; }
    [JsonPropertyName("notes")] public string? Notes { get; set; }
}

public sealed class StudentFeeHistoryDto
{
    [JsonPropertyName("studentId")] public int StudentId { get; set; }
    [JsonPropertyName("studentName")] public string StudentName { get; set; } = string.Empty;
    [JsonPropertyName("className")] public string ClassName { get; set; } = string.Empty;
    [JsonPropertyName("rollNo")] public string RollNo { get; set; } = string.Empty;
    [JsonPropertyName("fatherName")] public string FatherName { get; set; } = string.Empty;
    [JsonPropertyName("fatherPhone")] public string FatherPhone { get; set; } = string.Empty;
    [JsonPropertyName("totalAmount")] public decimal TotalAmount { get; set; }
    [JsonPropertyName("totalPaid")] public decimal TotalPaid { get; set; }
    [JsonPropertyName("totalBalance")] public decimal TotalBalance { get; set; }
    [JsonPropertyName("fees")] public List<FeeHistoryRowDto> Fees { get; set; } = new();
    [JsonPropertyName("payments")] public List<FeeHistoryPaymentDto> Payments { get; set; } = new();
}

public sealed class FeeCollectResultDto
{
    [JsonPropertyName("collected")] public decimal Collected { get; set; }
    [JsonPropertyName("remainingBalance")] public decimal RemainingBalance { get; set; }
    [JsonPropertyName("billsTouched")] public int BillsTouched { get; set; }
}

/// <summary>Fee dashboard row: one student with all fee heads summed.</summary>
public sealed class StudentFeeSummaryDto
{
    [JsonPropertyName("studentId")] public int StudentId { get; set; }
    [JsonPropertyName("studentName")] public string StudentName { get; set; } = string.Empty;
    [JsonPropertyName("rollNo")] public string RollNo { get; set; } = string.Empty;
    [JsonPropertyName("className")] public string ClassName { get; set; } = string.Empty;
    [JsonPropertyName("fatherName")] public string FatherName { get; set; } = string.Empty;
    [JsonPropertyName("fatherPhone")] public string FatherPhone { get; set; } = string.Empty;
    [JsonPropertyName("totalAmount")] public decimal TotalAmount { get; set; }
    [JsonPropertyName("totalPaid")] public decimal TotalPaid { get; set; }
    [JsonPropertyName("totalBalance")] public decimal TotalBalance { get; set; }
    [JsonPropertyName("hasOverdue")] public bool HasOverdue { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;

    /// <summary>Paid / Due / Partial / Unpaid / No fee. Uses the server value, or works it out here if the server is older.</summary>
    [JsonIgnore]
    public string StatusText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Status)) return Status;
            if (TotalAmount <= 0m) return "No fee";
            if (TotalBalance <= 0m) return "Paid";
            if (HasOverdue) return "Due";
            return TotalPaid > 0m ? "Partial" : "Unpaid";
        }
    }
}

/// <summary>Server build version (from api/system/version).</summary>
public sealed class ServerVersionDto
{
    [JsonPropertyName("version")] public string Version { get; set; } = string.Empty;
}

/// <summary>A bill removed from the statement that can be restored.</summary>
public sealed class DeletedBillDto
{
    [JsonPropertyName("feeId")] public int FeeId { get; set; }
    [JsonPropertyName("feeHead")] public string FeeHead { get; set; } = string.Empty;
    [JsonPropertyName("month")] public string Month { get; set; } = string.Empty;
    [JsonPropertyName("dueDate")] public DateOnly DueDate { get; set; }
    [JsonPropertyName("amount")] public decimal Amount { get; set; }
    [JsonPropertyName("deletedAtPkt")] public DateTime DeletedAtPkt { get; set; }
}

public sealed class FeeHeadItemDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("code")] public string Code { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("category")] public string Category { get; set; } = string.Empty;
}
