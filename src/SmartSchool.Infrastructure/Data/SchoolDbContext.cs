using Microsoft.EntityFrameworkCore;
using SmartSchool.Core.Models;

namespace SmartSchool.Infrastructure.Data;

public sealed class SchoolDbContext : DbContext
{
    public SchoolDbContext(DbContextOptions<SchoolDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<SchoolClass> Classes => Set<SchoolClass>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<Fee> Fees => Set<Fee>();
    public DbSet<Bus> Buses => Set<Bus>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<BusAttendance> BusAttendances => Set<BusAttendance>();
    public DbSet<WhatsAppLog> WhatsAppLogs => Set<WhatsAppLog>();
    public DbSet<ParentStudent> ParentStudents => Set<ParentStudent>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<Announcement> Announcements => Set<Announcement>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);

        mb.Entity<User>(e =>
        {
            e.ToTable("Users");
            e.Property(x => x.Username).HasMaxLength(50).IsRequired();
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.PasswordHash).HasMaxLength(200).IsRequired();
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(25);
            e.Property(x => x.PreferredLanguage).HasMaxLength(5).HasDefaultValue("en");
            e.Property(x => x.ReceiveWhatsApp).HasDefaultValue(true);
        });

        mb.Entity<Teacher>(e =>
        {
            e.ToTable("Teachers");
            e.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(20).IsRequired();
            e.Property(x => x.Subject).HasMaxLength(100);
            e.HasOne(x => x.User).WithOne(u => u.TeacherProfile)
             .HasForeignKey<Teacher>(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.UserId).IsUnique().HasFilter("[UserId] IS NOT NULL");
        });

        mb.Entity<SchoolClass>(e =>
        {
            e.ToTable("Classes");
            e.Property(x => x.Name).HasMaxLength(50).IsRequired();
            e.Property(x => x.Section).HasMaxLength(10).IsRequired();
            e.HasIndex(x => new { x.Name, x.Section }).IsUnique();
            e.HasOne(x => x.ClassTeacher).WithMany(t => t.Classes)
             .HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.SetNull);
            e.Ignore(x => x.DisplayName);
        });

        mb.Entity<Driver>(e =>
        {
            e.ToTable("Drivers");
            e.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(20).IsRequired();
            e.Property(x => x.LicenseNo).HasMaxLength(50).IsRequired();
            e.HasIndex(x => x.LicenseNo).IsUnique();
            e.HasOne(x => x.User).WithOne(u => u.DriverProfile)
             .HasForeignKey<Driver>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.UserId).IsUnique();
        });

        mb.Entity<Bus>(e =>
        {
            e.ToTable("Buses");
            e.Property(x => x.BusNumber).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.BusNumber).IsUnique();
            e.Property(x => x.Route).HasMaxLength(200).IsRequired();
            e.Property(x => x.VehicleNo).HasMaxLength(30).IsRequired();
            e.HasOne(x => x.Driver).WithOne(d => d.AssignedBus)
             .HasForeignKey<Bus>(x => x.DriverId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.DriverId).IsUnique().HasFilter("[DriverId] IS NOT NULL");
        });

        mb.Entity<Student>(e =>
        {
            e.ToTable("Students");
            e.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            e.Property(x => x.RollNo).HasMaxLength(20).IsRequired();
            e.Property(x => x.PhotoUrl).HasMaxLength(500);
            e.Property(x => x.FatherName).HasMaxLength(150).IsRequired();
            e.Property(x => x.FatherPhone).HasMaxLength(20).IsRequired();
            e.Property(x => x.MotherPhone).HasMaxLength(20);
            e.Property(x => x.Address).HasMaxLength(300).IsRequired();
            e.HasOne(x => x.Class).WithMany(c => c.Students)
             .HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Bus).WithMany(b => b.Students)
             .HasForeignKey(x => x.BusId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.ClassId);
            e.HasIndex(x => x.BusId);
            e.HasIndex(x => x.FatherPhone);
            e.HasIndex(x => x.FullName);
        });

        mb.Entity<Fee>(e =>
        {
            e.ToTable("Fees");
            e.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Month).HasMaxLength(7).IsRequired();
            e.Property(x => x.ReceiptNo).HasMaxLength(30);
            e.HasIndex(x => x.ReceiptNo).IsUnique().HasFilter("[ReceiptNo] IS NOT NULL");
            e.Property(x => x.Notes).HasMaxLength(300);
            e.HasOne(x => x.Student).WithMany(s => s.Fees)
             .HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.StudentId, x.Month });
            e.HasIndex(x => x.Status);
        });

        mb.Entity<Trip>(e =>
        {
            e.ToTable("Trips");
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasOne(x => x.Bus).WithMany(b => b.Trips)
             .HasForeignKey(x => x.BusId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Driver).WithMany(d => d.Trips)
             .HasForeignKey(x => x.DriverId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.BusId, x.Status });
            e.HasIndex(x => new { x.DriverId, x.Status });
        });

        mb.Entity<BusAttendance>(e =>
        {
            e.ToTable("BusAttendance");
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.LocationLabel).HasMaxLength(300);
            e.HasOne(x => x.Student).WithMany(s => s.BusEvents)
             .HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Bus).WithMany()
             .HasForeignKey(x => x.BusId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Driver).WithMany()
             .HasForeignKey(x => x.DriverId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Trip).WithMany(t => t.Events)
             .HasForeignKey(x => x.TripId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.StudentId, x.TimestampUtc });
            e.HasIndex(x => new { x.BusId, x.TimestampUtc });
            e.HasIndex(x => x.TripId);
        });

        mb.Entity<WhatsAppLog>(e =>
        {
            e.ToTable("WhatsAppLogs");
            e.HasIndex(x => x.CorrelationId).IsUnique();
            e.Property(x => x.ParentPhone).HasMaxLength(25).IsRequired();
            e.Property(x => x.Message).HasMaxLength(1600).IsRequired();
            e.Property(x => x.EventType).HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Error).HasMaxLength(1000);
            e.Property(x => x.ProviderMessageId).HasMaxLength(100);
            e.HasOne(x => x.Student).WithMany()
             .HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.StudentId);
        });

        mb.Entity<ParentStudent>(e =>
        {
            e.ToTable("ParentStudents");
            e.HasKey(x => new { x.ParentUserId, x.StudentId });
            e.HasOne(x => x.ParentUser).WithMany()
             .HasForeignKey(x => x.ParentUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Student).WithMany()
             .HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<DeviceToken>(e =>
        {
            e.ToTable("DeviceTokens");
            e.Property(x => x.Token).HasMaxLength(500).IsRequired();
            e.HasIndex(x => x.Token).IsUnique();
            e.Property(x => x.Platform).HasMaxLength(20).IsRequired();
            e.HasOne(x => x.User).WithMany()
             .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<Announcement>(e =>
        {
            e.ToTable("Announcements");
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Body).HasMaxLength(2000).IsRequired();
            e.Property(x => x.CreatedByName).HasMaxLength(150);
            e.HasIndex(x => x.CreatedAtUtc);
        });
    }
}
