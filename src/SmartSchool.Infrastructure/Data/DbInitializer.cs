using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartSchool.Core.Models;
using SmartSchool.Shared.Enums;
using SmartSchool.Shared.Helpers;

namespace SmartSchool.Infrastructure.Data;

/// <summary>
/// Runs at startup: applies EF migrations (falls back to EnsureCreated before the
/// first migration exists), then seeds per spec:
///   1 admin user, 2 sample classes, 5 students, 2 buses, 2 drivers (with logins).
/// With "SeedDemoData": true, a teacher + parent login and current-month fee
/// challans are added too, so every role can be tested immediately.
/// All seeding is idempotent.
/// </summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration config, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();

        var hasMigrations = db.Database.GetMigrations().Any();
        if (hasMigrations)
        {
            logger.LogInformation("Applying EF Core migrations...");
            await db.Database.MigrateAsync();
        }
        else
        {
            logger.LogWarning("No EF migrations found - using EnsureCreated(). " +
                "For production use: dotnet ef database update (or the SQL scripts in database/sqlserver).");
            await db.Database.EnsureCreatedAsync();
        }

        // ---- 1 admin user ----
        if (!await db.Users.AnyAsync())
        {
            logger.LogInformation("Seeding users (admin / Admin@12345)...");
            db.Users.Add(new User
            {
                Username = "admin",
                FullName = "School Owner",
                Role = UserRole.SuperAdmin,
                PasswordHash = PasswordHasher.Hash("Admin@12345"),
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // ---- 2 sample classes ----
        if (!await db.Classes.AnyAsync())
        {
            logger.LogInformation("Seeding classes 1-A and 2-A...");
            db.Classes.AddRange(
                new SchoolClass { Name = "1", Section = "A" },
                new SchoolClass { Name = "2", Section = "A" });
            await db.SaveChangesAsync();
        }

        // ---- 2 drivers with logins ----
        if (!await db.Drivers.AnyAsync())
        {
            logger.LogInformation("Seeding 2 drivers (driver1 / driver2, password Driver@123)...");
            var driverUser1 = new User
            {
                Username = "driver1",
                FullName = "Muhammad Akram",
                Role = UserRole.Driver,
                PasswordHash = PasswordHasher.Hash("Driver@123"),
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            var driverUser2 = new User
            {
                Username = "driver2",
                FullName = "Shahid Mehmood",
                Role = UserRole.Driver,
                PasswordHash = PasswordHasher.Hash("Driver@123"),
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Users.AddRange(driverUser1, driverUser2);
            await db.SaveChangesAsync();

            db.Drivers.AddRange(
                new Driver { FullName = "Muhammad Akram", Phone = "03007654321", LicenseNo = "KP-KHT-4521", UserId = driverUser1.Id },
                new Driver { FullName = "Shahid Mehmood", Phone = "03015554321", LicenseNo = "KP-KHT-7822", UserId = driverUser2.Id });
            await db.SaveChangesAsync();
        }

        // ---- 2 buses (one per driver) ----
        if (!await db.Buses.AnyAsync())
        {
            logger.LogInformation("Seeding 2 buses (#7 and #12)...");
            var driver1 = await db.Drivers.FirstAsync(d => d.LicenseNo == "KP-KHT-4521");
            var driver2 = await db.Drivers.FirstAsync(d => d.LicenseNo == "KP-KHT-7822");
            db.Buses.AddRange(
                new Bus { BusNumber = "7", Route = "Route 1 - Kohat City to Campus", VehicleNo = "KPK-4521", Capacity = 45, DriverId = driver1.Id },
                new Bus { BusNumber = "12", Route = "Route 2 - Cantt / KDA to Campus", VehicleNo = "KPK-7822", Capacity = 40, DriverId = driver2.Id });
            await db.SaveChangesAsync();
        }

        // ---- 5 sample students ----
        if (!await db.Students.AnyAsync())
        {
            logger.LogInformation("Seeding 5 sample students...");
            var class1A = await db.Classes.FirstAsync(c => c.Name == "1" && c.Section == "A");
            var class2A = await db.Classes.FirstAsync(c => c.Name == "2" && c.Section == "A");
            var bus7 = await db.Buses.FirstAsync(b => b.BusNumber == "7");
            var bus12 = await db.Buses.FirstAsync(b => b.BusNumber == "12");

            db.Students.AddRange(
                new Student { FullName = "Ahmed Raza", DateOfBirth = new DateOnly(2018, 3, 12), ClassId = class1A.Id, RollNo = "12", FatherName = "Tariq Raza", FatherPhone = "03331234567", MotherPhone = "03337654321", Address = "House 14, KDA Scheme, Kohat", BusId = bus7.Id },
                new Student { FullName = "Fatima Noor", DateOfBirth = new DateOnly(2018, 7, 4), ClassId = class1A.Id, RollNo = "15", FatherName = "Noor Muhammad", FatherPhone = "03451234567", Address = "Street 3, Cantt Area, Kohat", BusId = bus7.Id },
                new Student { FullName = "Usman Ghani", DateOfBirth = new DateOnly(2017, 1, 22), ClassId = class2A.Id, RollNo = "03", FatherName = "Abdul Ghani", FatherPhone = "03011234567", Address = "Mohallah Shaldara, Kohat", BusId = bus7.Id },
                new Student { FullName = "Ayesha Siddiqui", DateOfBirth = new DateOnly(2017, 9, 30), ClassId = class2A.Id, RollNo = "08", FatherName = "Kamran Siddiqui", FatherPhone = "03211234567", Address = "Pindi Road, Kohat", BusId = bus12.Id },
                new Student { FullName = "Bilal Ahmed", DateOfBirth = new DateOnly(2018, 5, 17), ClassId = class2A.Id, RollNo = "11", FatherName = "Ahmed Khan", FatherPhone = "03151234567", Address = "Togh Bala, Kohat", BusId = bus12.Id });
            await db.SaveChangesAsync();
        }

        // ---- optional demo extras (teacher + parent logins, fee challans) ----
        if (!config.GetValue<bool>("SeedDemoData") || await db.Teachers.AnyAsync())
            return;

        logger.LogInformation("Seeding demo extras (teacher1 / parent1)...");
        var teacherUser = new User
        {
            Username = "teacher1",
            FullName = "Ayesha Khan",
            Role = UserRole.Teacher,
            PasswordHash = PasswordHasher.Hash("Teacher@123"),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var parentUser = new User
        {
            Username = "parent1",
            FullName = "Tariq Raza",
            Role = UserRole.Parent,
            PasswordHash = PasswordHasher.Hash("Parent@123"),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Users.AddRange(teacherUser, parentUser);
        await db.SaveChangesAsync();

        var teacher = new Teacher
        {
            FullName = "Ayesha Khan",
            Phone = "03001234567",
            Subject = "Mathematics",
            UserId = teacherUser.Id
        };
        db.Teachers.Add(teacher);
        await db.SaveChangesAsync();

        var classOneA = await db.Classes.FirstAsync(c => c.Name == "1" && c.Section == "A");
        classOneA.TeacherId = teacher.Id;

        var firstStudent = await db.Students.FirstAsync(s => s.FullName == "Ahmed Raza");
        db.ParentStudents.Add(new ParentStudent { ParentUserId = parentUser.Id, StudentId = firstStudent.Id });

        var month = PkTime.TodayPk.ToString("yyyy-MM");
        var due = PkTime.TodayPk.AddDays(5);
        var allStudents = await db.Students.Select(s => s.Id).ToListAsync();
        foreach (var studentId in allStudents)
        {
            db.Fees.Add(new Fee
            {
                StudentId = studentId,
                Amount = 4500m,
                Month = month,
                DueDate = due,
                Status = FeeStatus.Pending
            });
        }
        await db.SaveChangesAsync();

        logger.LogInformation("Demo data seeded. Logins -> teacher1/Teacher@123, driver1|driver2/Driver@123, parent1/Parent@123");
    }
}
