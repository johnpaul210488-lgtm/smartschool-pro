using SmartSchool.Core.Models;

namespace SmartSchool.Core.Interfaces;

/// <summary>One repository set + one SaveChanges per HTTP request (scoped).</summary>
public interface IUnitOfWork : IDisposable, IAsyncDisposable
{
    IRepository<User> Users { get; }
    IRepository<Student> Students { get; }
    IRepository<SchoolClass> Classes { get; }
    IRepository<Teacher> Teachers { get; }
    IRepository<Fee> Fees { get; }
    IRepository<Bus> Buses { get; }
    IRepository<Driver> Drivers { get; }
    IRepository<Trip> Trips { get; }
    IRepository<BusAttendance> BusAttendances { get; }
    IRepository<WhatsAppLog> WhatsAppLogs { get; }
    IRepository<WhatsAppConsent> WhatsAppConsents { get; }
    IRepository<ParentStudent> ParentStudents { get; }
    IRepository<DeviceToken> DeviceTokens { get; }
    IRepository<Announcement> Announcements { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
