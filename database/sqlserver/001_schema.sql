/* =====================================================================================
   SmartSchool Pro - SQL Server schema (001)
   Mirrors the EF Core model 1:1. Two ways to create the database - pick ONE:

     A) Let EF manage it (recommended):
          cd src/SmartSchool.Api
          dotnet ef database update          (applies migrations)

     B) Run this script + 002_seed.sql manually in SSMS / sqlcmd / Azure Data Studio.

   Enums are stored as readable strings with CHECK constraints.
   All date-times are UTC in the DB; the API converts to Pakistan time (UTC+5) on the wire.
   WARNING: re-running DROPs every table and wipes all data.
   ===================================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF DB_ID(N'SmartSchoolPro') IS NULL
    CREATE DATABASE SmartSchoolPro;
GO
USE SmartSchoolPro;
GO

-- ---------- drop children first (safe re-run) ----------
IF OBJECT_ID('dbo.DeviceTokens',  'U') IS NOT NULL DROP TABLE dbo.DeviceTokens;
IF OBJECT_ID('dbo.ParentStudents','U') IS NOT NULL DROP TABLE dbo.ParentStudents;
IF OBJECT_ID('dbo.WhatsAppLogs',  'U') IS NOT NULL DROP TABLE dbo.WhatsAppLogs;
IF OBJECT_ID('dbo.BusAttendance', 'U') IS NOT NULL DROP TABLE dbo.BusAttendance;
IF OBJECT_ID('dbo.Trips',         'U') IS NOT NULL DROP TABLE dbo.Trips;
IF OBJECT_ID('dbo.Fees',          'U') IS NOT NULL DROP TABLE dbo.Fees;
IF OBJECT_ID('dbo.Students',      'U') IS NOT NULL DROP TABLE dbo.Students;
IF OBJECT_ID('dbo.Buses',         'U') IS NOT NULL DROP TABLE dbo.Buses;
IF OBJECT_ID('dbo.Classes',       'U') IS NOT NULL DROP TABLE dbo.Classes;
IF OBJECT_ID('dbo.Drivers',       'U') IS NOT NULL DROP TABLE dbo.Drivers;
IF OBJECT_ID('dbo.Teachers',      'U') IS NOT NULL DROP TABLE dbo.Teachers;
IF OBJECT_ID('dbo.Users',         'U') IS NOT NULL DROP TABLE dbo.Users;
GO

-- ---------- security ----------
CREATE TABLE dbo.Users
(
    Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
    Username     NVARCHAR(50)      NOT NULL,
    PasswordHash NVARCHAR(200)     NOT NULL,              -- PBKDF2: {iterations}.{salt}.{hash}
    [Role]       NVARCHAR(20)      NOT NULL,
    FullName     NVARCHAR(150)     NOT NULL,
    IsActive     BIT               NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT (1),
    CreatedAtUtc DATETIME2         NOT NULL CONSTRAINT DF_Users_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT UQ_Users_Username UNIQUE (Username),
    CONSTRAINT CK_Users_Role CHECK ([Role] IN ('SuperAdmin','Admin','Teacher','Driver','Parent'))
);

CREATE TABLE dbo.Teachers
(
    Id       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Teachers PRIMARY KEY,
    FullName NVARCHAR(150)     NOT NULL,
    Phone    NVARCHAR(20)      NOT NULL,
    [Subject] NVARCHAR(100)    NULL,
    UserId   INT               NULL,
    IsActive BIT               NOT NULL CONSTRAINT DF_Teachers_IsActive DEFAULT (1),
    CONSTRAINT FK_Teachers_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE SET NULL
);
CREATE UNIQUE INDEX UX_Teachers_UserId ON dbo.Teachers (UserId) WHERE UserId IS NOT NULL;

CREATE TABLE dbo.Classes
(
    Id        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Classes PRIMARY KEY,
    [Name]    NVARCHAR(50)      NOT NULL,            -- e.g. '5'
    [Section] NVARCHAR(10)      NOT NULL,            -- e.g. 'A'  => "Class 5-A"
    TeacherId INT               NULL,
    CONSTRAINT UQ_Classes_Name_Section UNIQUE ([Name], [Section]),
    CONSTRAINT FK_Classes_Teachers FOREIGN KEY (TeacherId) REFERENCES dbo.Teachers (Id) ON DELETE SET NULL
);

CREATE TABLE dbo.Drivers
(
    Id        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Drivers PRIMARY KEY,
    FullName  NVARCHAR(150)     NOT NULL,
    Phone     NVARCHAR(20)      NOT NULL,
    LicenseNo NVARCHAR(50)      NOT NULL,
    UserId    INT               NOT NULL,            -- driver always has a login (driver app)
    IsActive  BIT               NOT NULL CONSTRAINT DF_Drivers_IsActive DEFAULT (1),
    CONSTRAINT UQ_Drivers_LicenseNo UNIQUE (LicenseNo),
    CONSTRAINT FK_Drivers_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX UX_Drivers_UserId ON dbo.Drivers (UserId);

-- ---------- transport ----------
CREATE TABLE dbo.Buses
(
    Id        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Buses PRIMARY KEY,
    BusNumber NVARCHAR(20)      NOT NULL,            -- the number parents know, e.g. '7'
    DriverId  INT               NULL,
    [Route]   NVARCHAR(200)     NOT NULL,
    Capacity  INT               NOT NULL,
    VehicleNo NVARCHAR(30)      NOT NULL,            -- number plate
    IsActive  BIT               NOT NULL CONSTRAINT DF_Buses_IsActive DEFAULT (1),
    CONSTRAINT UQ_Buses_BusNumber UNIQUE (BusNumber),
    CONSTRAINT FK_Buses_Drivers FOREIGN KEY (DriverId) REFERENCES dbo.Drivers (Id) ON DELETE SET NULL
);
-- one driver drives at most one bus at a time
CREATE UNIQUE INDEX UX_Buses_DriverId ON dbo.Buses (DriverId) WHERE DriverId IS NOT NULL;

-- ---------- academics ----------
CREATE TABLE dbo.Students
(
    Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Students PRIMARY KEY,
    FullName     NVARCHAR(150)     NOT NULL,
    DateOfBirth  DATE              NOT NULL,
    ClassId      INT               NOT NULL,
    RollNo       NVARCHAR(20)      NOT NULL,
    PhotoUrl     NVARCHAR(500)     NULL,
    FatherName   NVARCHAR(150)     NOT NULL,
    FatherPhone  NVARCHAR(20)      NOT NULL,         -- WhatsApp alert target
    MotherPhone  NVARCHAR(20)      NULL,
    [Address]    NVARCHAR(300)     NOT NULL,
    BusId        INT               NULL,
    IsActive     BIT               NOT NULL CONSTRAINT DF_Students_IsActive DEFAULT (1),
    CreatedAtUtc DATETIME2         NOT NULL CONSTRAINT DF_Students_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_Students_Classes FOREIGN KEY (ClassId) REFERENCES dbo.Classes (Id),          -- NO ACTION (Restricted)
    CONSTRAINT FK_Students_Buses   FOREIGN KEY (BusId)   REFERENCES dbo.Buses (Id)   ON DELETE SET NULL
);
CREATE INDEX IX_Students_ClassId     ON dbo.Students (ClassId);
CREATE INDEX IX_Students_BusId       ON dbo.Students (BusId);
CREATE INDEX IX_Students_FatherPhone ON dbo.Students (FatherPhone);
CREATE INDEX IX_Students_FullName    ON dbo.Students (FullName);

CREATE TABLE dbo.Fees
(
    Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Fees PRIMARY KEY,
    StudentId    INT               NOT NULL,
    Amount       DECIMAL(18,2)     NOT NULL,
    [Month]      NVARCHAR(7)       NOT NULL,         -- 'yyyy-MM'
    DueDate      DATE              NOT NULL,
    PaidDate     DATE              NULL,
    [Status]     NVARCHAR(20)      NOT NULL CONSTRAINT DF_Fees_Status DEFAULT ('Pending'),
    ReceiptNo    NVARCHAR(30)      NULL,             -- INV-{yyyyMM}-{id:D6}
    Notes        NVARCHAR(300)     NULL,
    CreatedAtUtc DATETIME2         NOT NULL CONSTRAINT DF_Fees_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_Fees_Students FOREIGN KEY (StudentId) REFERENCES dbo.Students (Id) ON DELETE CASCADE,
    CONSTRAINT CK_Fees_Status CHECK ([Status] IN ('Pending','Paid','Overdue','Partial')),
    CONSTRAINT CK_Fees_Month  CHECK ([Month] LIKE '[0-9][0-9][0-9][0-9]-[0-1][0-9]')
);
CREATE UNIQUE INDEX UX_Fees_ReceiptNo ON dbo.Fees (ReceiptNo) WHERE ReceiptNo IS NOT NULL;
CREATE INDEX IX_Fees_StudentId_Month  ON dbo.Fees (StudentId, [Month]);
CREATE INDEX IX_Fees_Status           ON dbo.Fees ([Status]);

-- ---------- trips & real-time bus events ----------
CREATE TABLE dbo.Trips
(
    Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Trips PRIMARY KEY,
    BusId        INT               NOT NULL,
    DriverId     INT               NOT NULL,
    [Type]       NVARCHAR(20)      NOT NULL,         -- ToSchool | ToHome
    [Status]     NVARCHAR(20)      NOT NULL CONSTRAINT DF_Trips_Status DEFAULT ('Active'),
    StartedAtUtc DATETIME2         NOT NULL,
    EndedAtUtc   DATETIME2         NULL,
    CONSTRAINT FK_Trips_Buses   FOREIGN KEY (BusId)    REFERENCES dbo.Buses (Id),    -- NO ACTION
    CONSTRAINT FK_Trips_Drivers FOREIGN KEY (DriverId) REFERENCES dbo.Drivers (Id),  -- NO ACTION
    CONSTRAINT CK_Trips_Type   CHECK ([Type]   IN ('ToSchool','ToHome')),
    CONSTRAINT CK_Trips_Status CHECK ([Status] IN ('Active','Completed','Cancelled'))
);
CREATE INDEX IX_Trips_BusId_Status    ON dbo.Trips (BusId, [Status]);
CREATE INDEX IX_Trips_DriverId_Status ON dbo.Trips (DriverId, [Status]);

CREATE TABLE dbo.BusAttendance
(
    Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BusAttendance PRIMARY KEY,
    StudentId     INT              NOT NULL,
    BusId         INT              NOT NULL,
    DriverId      INT              NOT NULL,
    TripId        INT              NULL,
    [Type]        NVARCHAR(10)     NOT NULL,         -- PickIn | DropOut
    TimestampUtc  DATETIME2        NOT NULL,
    Latitude      FLOAT            NOT NULL,
    Longitude     FLOAT            NOT NULL,
    LocationLabel NVARCHAR(300)    NULL,
    WhatsAppSent  BIT              NOT NULL CONSTRAINT DF_BusAttendance_WaSent DEFAULT (0),
    CONSTRAINT FK_BusAttendance_Students FOREIGN KEY (StudentId) REFERENCES dbo.Students (Id),  -- NO ACTION
    CONSTRAINT FK_BusAttendance_Buses    FOREIGN KEY (BusId)     REFERENCES dbo.Buses (Id),     -- NO ACTION
    CONSTRAINT FK_BusAttendance_Drivers  FOREIGN KEY (DriverId)  REFERENCES dbo.Drivers (Id),   -- NO ACTION
    CONSTRAINT FK_BusAttendance_Trips    FOREIGN KEY (TripId)    REFERENCES dbo.Trips (Id) ON DELETE SET NULL,
    CONSTRAINT CK_BusAttendance_Type CHECK ([Type] IN ('PickIn','DropOut'))
);
CREATE INDEX IX_BusAttendance_StudentId_Timestamp ON dbo.BusAttendance (StudentId, TimestampUtc);
CREATE INDEX IX_BusAttendance_BusId_Timestamp     ON dbo.BusAttendance (BusId, TimestampUtc);
CREATE INDEX IX_BusAttendance_TripId              ON dbo.BusAttendance (TripId);

-- ---------- WhatsApp outbox ----------
CREATE TABLE dbo.WhatsAppLogs
(
    Id                BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WhatsAppLogs PRIMARY KEY,
    CorrelationId     UNIQUEIDENTIFIER NOT NULL,
    StudentId         INT              NULL,         -- survives student deletion
    EventType         NVARCHAR(20)     NULL,
    ParentPhone       NVARCHAR(25)     NOT NULL,
    [Message]         NVARCHAR(1600)   NOT NULL,
    [Status]          NVARCHAR(20)     NOT NULL CONSTRAINT DF_WaLogs_Status DEFAULT ('Queued'),
    RetryCount        INT              NOT NULL CONSTRAINT DF_WaLogs_Retry DEFAULT (0),
    FailureCode       NVARCHAR(40)     NULL,
    Error             NVARCHAR(1000)   NULL,
    ProviderMessageId NVARCHAR(100)    NULL,         -- provider receipt / DRYRUN-*
    CreatedAtUtc      DATETIME2        NOT NULL CONSTRAINT DF_WaLogs_Created DEFAULT (SYSUTCDATETIME()),
    LastAttemptAtUtc       DATETIME2        NULL,
    RetryWindowStartedAtUtc DATETIME2       NULL,
    NextAttemptAtUtc       DATETIME2        NULL,
    SentAtUtc              DATETIME2        NULL,
    CONSTRAINT UQ_WaLogs_CorrelationId UNIQUE (CorrelationId),
    CONSTRAINT FK_WaLogs_Students FOREIGN KEY (StudentId) REFERENCES dbo.Students (Id) ON DELETE SET NULL,
    CONSTRAINT CK_WaLogs_Status CHECK ([Status] IN ('Queued','Sent','Failed'))
);
CREATE INDEX IX_WaLogs_Status_NextAttempt ON dbo.WhatsAppLogs ([Status], NextAttemptAtUtc);
CREATE INDEX IX_WaLogs_StudentId          ON dbo.WhatsAppLogs (StudentId);

-- ---------- parent app links & push tokens ----------
CREATE TABLE dbo.ParentStudents
(
    ParentUserId INT NOT NULL,
    StudentId    INT NOT NULL,
    CONSTRAINT PK_ParentStudents PRIMARY KEY (ParentUserId, StudentId),
    CONSTRAINT FK_ParentStudents_Users    FOREIGN KEY (ParentUserId) REFERENCES dbo.Users (Id)    ON DELETE CASCADE,
    CONSTRAINT FK_ParentStudents_Students FOREIGN KEY (StudentId)    REFERENCES dbo.Students (Id) ON DELETE CASCADE
);

CREATE TABLE dbo.DeviceTokens
(
    Id           BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DeviceTokens PRIMARY KEY,
    UserId       INT               NOT NULL,
    Token        NVARCHAR(500)     NOT NULL,         -- FCM registration token
    Platform     NVARCHAR(20)      NOT NULL,         -- android | ios
    UpdatedAtUtc DATETIME2         NOT NULL CONSTRAINT DF_DeviceTokens_Updated DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT UQ_DeviceTokens_Token UNIQUE (Token),
    CONSTRAINT FK_DeviceTokens_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE
);
GO

PRINT 'SmartSchoolPro schema created. Run 002_seed.sql next.';
GO
