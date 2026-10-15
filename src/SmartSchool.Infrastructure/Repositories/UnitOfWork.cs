using SmartSchool.Core.Interfaces;
using SmartSchool.Core.Models;
using SmartSchool.Infrastructure.Data;

namespace SmartSchool.Infrastructure.Repositories;

/// <summary>Scoped per HTTP request; all repositories share the one DbContext.</summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly SchoolDbContext _context;

    public UnitOfWork(SchoolDbContext context)
    {
        _context = context;
        Users = new Repository<User>(context);
        Students = new Repository<Student>(context);
        Classes = new Repository<SchoolClass>(context);
        Teachers = new Repository<Teacher>(context);
        Fees = new Repository<Fee>(context);
        Buses = new Repository<Bus>(context);
        Drivers = new Repository<Driver>(context);
        Trips = new Repository<Trip>(context);
        BusAttendances = new Repository<BusAttendance>(context);
        WhatsAppLogs = new Repository<WhatsAppLog>(context);
        ParentStudents = new Repository<ParentStudent>(context);
        DeviceTokens = new Repository<DeviceToken>(context);
        Announcements = new Repository<Announcement>(context);
    }

    public IRepository<User> Users { get; }
    public IRepository<Student> Students { get; }
    public IRepository<SchoolClass> Classes { get; }
    public IRepository<Teacher> Teachers { get; }
    public IRepository<Fee> Fees { get; }
    public IRepository<Bus> Buses { get; }
    public IRepository<Driver> Drivers { get; }
    public IRepository<Trip> Trips { get; }
    public IRepository<BusAttendance> BusAttendances { get; }
    public IRepository<WhatsAppLog> WhatsAppLogs { get; }
    public IRepository<ParentStudent> ParentStudents { get; }
    public IRepository<DeviceToken> DeviceTokens { get; }
    public IRepository<Announcement> Announcements { get; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _context.SaveChangesAsync(ct);

    public void Dispose() => _context.Dispose();

    public ValueTask DisposeAsync() => _context.DisposeAsync();
}
