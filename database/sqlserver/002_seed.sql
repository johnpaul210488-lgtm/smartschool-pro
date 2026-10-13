/* =====================================================================================
   SmartSchool Pro - seed data (002)
   Mirrors the API's DbInitializer. Idempotent - safe to run more than once.

   Demo logins (CHANGE PASSWORDS on a real install):
     admin    / Admin@12345   (SuperAdmin)
     driver1  / Driver@123    (Driver - Bus #7)
     driver2  / Driver@123    (Driver - Bus #12)
     teacher1 / Teacher@123   (Teacher)   \
     parent1  / Parent@123    (Parent)     > demo extras, optional block at bottom
   ===================================================================================== */
USE SmartSchoolPro;
GO

-- ---------- users ----------
IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Username = 'admin')
    INSERT INTO dbo.Users (Username, PasswordHash, [Role], FullName, IsActive)
    VALUES ('admin',
            '100000.GhJNM1HvEOThSlS2WSpOqw==.HS4RfCyTStrJD96jORmZjZuaso/To+dLgR8XlAPFbfg=',
            'SuperAdmin', 'School Owner', 1);

IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Username = 'driver1')
    INSERT INTO dbo.Users (Username, PasswordHash, [Role], FullName, IsActive)
    VALUES ('driver1',
            '100000.Ak2sqbG8ZotRo0MOxkMm5Q==.LNY1kFD2oc6hE5oxfZX/oFrfpFMnwglcyhSdoPYZGV0=',
            'Driver', 'Muhammad Akram', 1);

IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Username = 'driver2')
    INSERT INTO dbo.Users (Username, PasswordHash, [Role], FullName, IsActive)
    VALUES ('driver2',
            '100000.KVTT5GF8IN1XZMKKTMHakw==.9bRFdRvYhB2d2L9ZMWM86nBg0Kff80jE8gGSawOVSy4=',
            'Driver', 'Shahid Mehmood', 1);
GO

-- ---------- 2 sample classes ----------
IF NOT EXISTS (SELECT 1 FROM dbo.Classes WHERE [Name] = '1' AND [Section] = 'A')
    INSERT INTO dbo.Classes ([Name], [Section]) VALUES ('1', 'A');
IF NOT EXISTS (SELECT 1 FROM dbo.Classes WHERE [Name] = '2' AND [Section] = 'A')
    INSERT INTO dbo.Classes ([Name], [Section]) VALUES ('2', 'A');
GO

-- ---------- 2 drivers, 2 buses, 5 students (only when tables are empty) ----------
IF NOT EXISTS (SELECT 1 FROM dbo.Drivers)
BEGIN
    DECLARE @du1 INT = (SELECT Id FROM dbo.Users WHERE Username = 'driver1');
    DECLARE @du2 INT = (SELECT Id FROM dbo.Users WHERE Username = 'driver2');

    INSERT INTO dbo.Drivers (FullName, Phone, LicenseNo, UserId) VALUES
        ('Muhammad Akram', '03007654321', 'KP-KHT-4521', @du1),
        ('Shahid Mehmood', '03015554321', 'KP-KHT-7822', @du2);

    DECLARE @dr1 INT = (SELECT Id FROM dbo.Drivers WHERE LicenseNo = 'KP-KHT-4521');
    DECLARE @dr2 INT = (SELECT Id FROM dbo.Drivers WHERE LicenseNo = 'KP-KHT-7822');

    INSERT INTO dbo.Buses (BusNumber, DriverId, [Route], Capacity, VehicleNo) VALUES
        ('7',  @dr1, 'Route 1 - Kohat City to Campus', 45, 'KPK-4521'),
        ('12', @dr2, 'Route 2 - Cantt / KDA to Campus', 40, 'KPK-7822');

    DECLARE @b7  INT = (SELECT Id FROM dbo.Buses WHERE BusNumber = '7');
    DECLARE @b12 INT = (SELECT Id FROM dbo.Buses WHERE BusNumber = '12');
    DECLARE @c1A INT = (SELECT Id FROM dbo.Classes WHERE [Name] = '1' AND [Section] = 'A');
    DECLARE @c2A INT = (SELECT Id FROM dbo.Classes WHERE [Name] = '2' AND [Section] = 'A');

    INSERT INTO dbo.Students
        (FullName, DateOfBirth, ClassId, RollNo, FatherName, FatherPhone, MotherPhone, [Address], BusId)
    VALUES
        ('Ahmed Raza',      '2018-03-12', @c1A, '12', 'Tariq Raza',      '03331234567', '03337654321', 'House 14, KDA Scheme, Kohat',  @b7),
        ('Fatima Noor',     '2018-07-04', @c1A, '15', 'Noor Muhammad',   '03451234567', NULL,          'Street 3, Cantt Area, Kohat',  @b7),
        ('Usman Ghani',     '2017-01-22', @c2A, '03', 'Abdul Ghani',     '03011234567', NULL,          'Mohallah Shaldara, Kohat',     @b7),
        ('Ayesha Siddiqui', '2017-09-30', @c2A, '08', 'Kamran Siddiqui', '03211234567', NULL,          'Pindi Road, Kohat',            @b12),
        ('Bilal Ahmed',     '2018-05-17', @c2A, '11', 'Ahmed Khan',      '03151234567', NULL,          'Togh Bala, Kohat',             @b12);

    PRINT 'Drivers, buses and students seeded.';
END
GO

-- ---------- demo extras (teacher + parent logins, current-month fee challans) ----------
IF NOT EXISTS (SELECT 1 FROM dbo.Teachers)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Username = 'teacher1')
        INSERT INTO dbo.Users (Username, PasswordHash, [Role], FullName, IsActive)
        VALUES ('teacher1',
                '100000.VK/NTwT9TfVJknydtvsWsQ==.bo8wv2IXRUufyXFqC0d+FCz5BrZDPf/8wEvuqbSrlkI=',
                'Teacher', 'Ayesha Khan', 1);

    IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Username = 'parent1')
        INSERT INTO dbo.Users (Username, PasswordHash, [Role], FullName, IsActive)
        VALUES ('parent1',
                '100000.KCynqkCmYs4Ba/3Gh5Ai6g==.hROTuIlcOHZqup/kZxJLZh6VDN3DL1rwVKXGmoDPgyQ=',
                'Parent', 'Tariq Raza', 1);

    DECLARE @tu INT = (SELECT Id FROM dbo.Users WHERE Username = 'teacher1');
    DECLARE @pu INT = (SELECT Id FROM dbo.Users WHERE Username = 'parent1');

    INSERT INTO dbo.Teachers (FullName, Phone, [Subject], UserId)
    VALUES ('Ayesha Khan', '03001234567', 'Mathematics', @tu);

    UPDATE dbo.Classes SET TeacherId = (SELECT Id FROM dbo.Teachers WHERE UserId = @tu)
    WHERE [Name] = '1' AND [Section] = 'A';

    DECLARE @firstStudent INT = (SELECT Id FROM dbo.Students WHERE FullName = 'Ahmed Raza');
    IF @firstStudent IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM dbo.ParentStudents WHERE ParentUserId = @pu AND StudentId = @firstStudent)
        INSERT INTO dbo.ParentStudents (ParentUserId, StudentId) VALUES (@pu, @firstStudent);

    -- current-month fee challans for every sample student
    DECLARE @month CHAR(7) = CONVERT(CHAR(7), SYSUTCDATETIME(), 126);
    DECLARE @due   DATE    = DATEADD(DAY, 5, CAST(SYSUTCDATETIME() AS DATE));

    INSERT INTO dbo.Fees (StudentId, Amount, [Month], DueDate, [Status])
    SELECT Id, 4500.00, @month, @due, 'Pending'
    FROM dbo.Students
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Fees f
                      WHERE f.StudentId = Students.Id AND f.[Month] = @month);

    PRINT 'Demo extras seeded.';
END
GO
