IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] int NOT NULL IDENTITY,
        [Username] nvarchar(50) NOT NULL,
        [PasswordHash] nvarchar(200) NOT NULL,
        [Role] nvarchar(20) NOT NULL,
        [FullName] nvarchar(150) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE TABLE [DeviceTokens] (
        [Id] bigint NOT NULL IDENTITY,
        [UserId] int NOT NULL,
        [Token] nvarchar(500) NOT NULL,
        [Platform] nvarchar(20) NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_DeviceTokens] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DeviceTokens_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE TABLE [Drivers] (
        [Id] int NOT NULL IDENTITY,
        [FullName] nvarchar(150) NOT NULL,
        [Phone] nvarchar(20) NOT NULL,
        [LicenseNo] nvarchar(50) NOT NULL,
        [UserId] int NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Drivers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Drivers_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE TABLE [Teachers] (
        [Id] int NOT NULL IDENTITY,
        [FullName] nvarchar(150) NOT NULL,
        [Phone] nvarchar(20) NOT NULL,
        [Subject] nvarchar(100) NULL,
        [UserId] int NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Teachers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Teachers_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE SET NULL
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE TABLE [Buses] (
        [Id] int NOT NULL IDENTITY,
        [BusNumber] nvarchar(20) NOT NULL,
        [DriverId] int NULL,
        [Route] nvarchar(200) NOT NULL,
        [Capacity] int NOT NULL,
        [VehicleNo] nvarchar(30) NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Buses] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Buses_Drivers_DriverId] FOREIGN KEY ([DriverId]) REFERENCES [Drivers] ([Id]) ON DELETE SET NULL
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE TABLE [Classes] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(50) NOT NULL,
        [Section] nvarchar(10) NOT NULL,
        [TeacherId] int NULL,
        CONSTRAINT [PK_Classes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Classes_Teachers_TeacherId] FOREIGN KEY ([TeacherId]) REFERENCES [Teachers] ([Id]) ON DELETE SET NULL
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE TABLE [Trips] (
        [Id] int NOT NULL IDENTITY,
        [BusId] int NOT NULL,
        [DriverId] int NOT NULL,
        [Type] nvarchar(20) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [StartedAtUtc] datetime2 NOT NULL,
        [EndedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_Trips] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Trips_Buses_BusId] FOREIGN KEY ([BusId]) REFERENCES [Buses] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Trips_Drivers_DriverId] FOREIGN KEY ([DriverId]) REFERENCES [Drivers] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE TABLE [Students] (
        [Id] int NOT NULL IDENTITY,
        [FullName] nvarchar(150) NOT NULL,
        [DateOfBirth] date NOT NULL,
        [ClassId] int NOT NULL,
        [RollNo] nvarchar(20) NOT NULL,
        [PhotoUrl] nvarchar(500) NULL,
        [FatherName] nvarchar(150) NOT NULL,
        [FatherPhone] nvarchar(20) NOT NULL,
        [MotherPhone] nvarchar(20) NULL,
        [Address] nvarchar(300) NOT NULL,
        [BusId] int NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Students] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Students_Buses_BusId] FOREIGN KEY ([BusId]) REFERENCES [Buses] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_Students_Classes_ClassId] FOREIGN KEY ([ClassId]) REFERENCES [Classes] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE TABLE [BusAttendance] (
        [Id] bigint NOT NULL IDENTITY,
        [StudentId] int NOT NULL,
        [BusId] int NOT NULL,
        [DriverId] int NOT NULL,
        [TripId] int NULL,
        [Type] nvarchar(10) NOT NULL,
        [TimestampUtc] datetime2 NOT NULL,
        [Latitude] float NOT NULL,
        [Longitude] float NOT NULL,
        [LocationLabel] nvarchar(300) NULL,
        [WhatsAppSent] bit NOT NULL,
        CONSTRAINT [PK_BusAttendance] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BusAttendance_Buses_BusId] FOREIGN KEY ([BusId]) REFERENCES [Buses] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BusAttendance_Drivers_DriverId] FOREIGN KEY ([DriverId]) REFERENCES [Drivers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BusAttendance_Students_StudentId] FOREIGN KEY ([StudentId]) REFERENCES [Students] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BusAttendance_Trips_TripId] FOREIGN KEY ([TripId]) REFERENCES [Trips] ([Id]) ON DELETE SET NULL
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE TABLE [Fees] (
        [Id] int NOT NULL IDENTITY,
        [StudentId] int NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Month] nvarchar(7) NOT NULL,
        [DueDate] date NOT NULL,
        [PaidDate] date NULL,
        [Status] nvarchar(20) NOT NULL,
        [ReceiptNo] nvarchar(30) NULL,
        [Notes] nvarchar(300) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Fees] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Fees_Students_StudentId] FOREIGN KEY ([StudentId]) REFERENCES [Students] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE TABLE [ParentStudents] (
        [ParentUserId] int NOT NULL,
        [StudentId] int NOT NULL,
        CONSTRAINT [PK_ParentStudents] PRIMARY KEY ([ParentUserId], [StudentId]),
        CONSTRAINT [FK_ParentStudents_Students_StudentId] FOREIGN KEY ([StudentId]) REFERENCES [Students] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ParentStudents_Users_ParentUserId] FOREIGN KEY ([ParentUserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE TABLE [WhatsAppLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [CorrelationId] uniqueidentifier NOT NULL,
        [StudentId] int NULL,
        [ParentPhone] nvarchar(25) NOT NULL,
        [Message] nvarchar(1600) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [RetryCount] int NOT NULL,
        [Error] nvarchar(1000) NULL,
        [ProviderMessageId] nvarchar(100) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [LastAttemptAtUtc] datetime2 NULL,
        [SentAtUtc] datetime2 NULL,
        CONSTRAINT [PK_WhatsAppLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WhatsAppLogs_Students_StudentId] FOREIGN KEY ([StudentId]) REFERENCES [Students] ([Id]) ON DELETE SET NULL
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_BusAttendance_BusId_TimestampUtc] ON [BusAttendance] ([BusId], [TimestampUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_BusAttendance_DriverId] ON [BusAttendance] ([DriverId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_BusAttendance_StudentId_TimestampUtc] ON [BusAttendance] ([StudentId], [TimestampUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_BusAttendance_TripId] ON [BusAttendance] ([TripId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Buses_BusNumber] ON [Buses] ([BusNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Buses_DriverId] ON [Buses] ([DriverId]) WHERE [DriverId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Classes_Name_Section] ON [Classes] ([Name], [Section]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Classes_TeacherId] ON [Classes] ([TeacherId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DeviceTokens_Token] ON [DeviceTokens] ([Token]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DeviceTokens_UserId] ON [DeviceTokens] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Drivers_LicenseNo] ON [Drivers] ([LicenseNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Drivers_UserId] ON [Drivers] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Fees_ReceiptNo] ON [Fees] ([ReceiptNo]) WHERE [ReceiptNo] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Fees_Status] ON [Fees] ([Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Fees_StudentId_Month] ON [Fees] ([StudentId], [Month]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ParentStudents_StudentId] ON [ParentStudents] ([StudentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Students_BusId] ON [Students] ([BusId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Students_ClassId] ON [Students] ([ClassId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Students_FatherPhone] ON [Students] ([FatherPhone]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Students_FullName] ON [Students] ([FullName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Teachers_UserId] ON [Teachers] ([UserId]) WHERE [UserId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Trips_BusId_Status] ON [Trips] ([BusId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Trips_DriverId_Status] ON [Trips] ([DriverId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_Username] ON [Users] ([Username]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_WhatsAppLogs_CorrelationId] ON [WhatsAppLogs] ([CorrelationId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WhatsAppLogs_Status] ON [WhatsAppLogs] ([Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WhatsAppLogs_StudentId] ON [WhatsAppLogs] ([StudentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175012_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261013175012_InitialCreate', N'8.0.10');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015165514_AddWhatsAppLogEventType'
)
BEGIN
    ALTER TABLE [WhatsAppLogs] ADD [EventType] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015165514_AddWhatsAppLogEventType'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261015165514_AddWhatsAppLogEventType', N'8.0.10');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015172940_ParentAppPhase5'
)
BEGIN
    ALTER TABLE [Users] ADD [Phone] nvarchar(25) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015172940_ParentAppPhase5'
)
BEGIN
    ALTER TABLE [Users] ADD [PreferredLanguage] nvarchar(5) NOT NULL DEFAULT N'en';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015172940_ParentAppPhase5'
)
BEGIN
    ALTER TABLE [Users] ADD [ReceiveWhatsApp] bit NOT NULL DEFAULT CAST(1 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015172940_ParentAppPhase5'
)
BEGIN
    CREATE TABLE [Announcements] (
        [Id] int NOT NULL IDENTITY,
        [Title] nvarchar(200) NOT NULL,
        [Body] nvarchar(2000) NOT NULL,
        [CreatedByName] nvarchar(150) NULL,
        [ClassId] int NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Announcements] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015172940_ParentAppPhase5'
)
BEGIN
    CREATE INDEX [IX_Announcements_CreatedAtUtc] ON [Announcements] ([CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015172940_ParentAppPhase5'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261015172940_ParentAppPhase5', N'8.0.10');
END;
GO

COMMIT;
GO

