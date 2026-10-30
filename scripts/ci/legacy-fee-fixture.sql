-- Legacy fixture used by CI to prove the fee-automation migration upgrades a database
-- that already contains finance data (one Paid fee, one Partial fee from the old model).
-- Run AFTER `dotnet ef database update 20261024165714_FeePlanFoundation`
-- and BEFORE `dotnet ef database update`.

IF NOT EXISTS (SELECT 1 FROM dbo.Classes WHERE [Name] = N'7' AND [Section] = N'A')
    INSERT INTO dbo.Classes ([Name], [Section], TeacherId) VALUES (N'7', N'A', NULL);

DECLARE @ClassId INT = (SELECT Id FROM dbo.Classes WHERE [Name] = N'7' AND [Section] = N'A');

IF NOT EXISTS (SELECT 1 FROM dbo.Students WHERE RollNo = N'LEG-001')
    INSERT INTO dbo.Students
        (FullName, Gender, IsNewStudent, DateOfBirth, ClassId, RollNo, PhotoUrl,
         FatherName, FatherPhone, MotherPhone, Address, BusId, IsActive, CreatedAtUtc)
    VALUES
        (N'Legacy Test Student', N'Boy', 0, '2015-04-01', @ClassId, N'LEG-001', NULL,
         N'Legacy Father', N'03001234567', NULL, N'Kohat', NULL, 1, SYSUTCDATETIME());

DECLARE @StudentId INT = (SELECT Id FROM dbo.Students WHERE RollNo = N'LEG-001');

IF NOT EXISTS (SELECT 1 FROM dbo.Fees WHERE StudentId = @StudentId)
    INSERT INTO dbo.Fees (StudentId, Amount, [Month], DueDate, PaidDate, [Status], ReceiptNo, Notes, CreatedAtUtc)
    VALUES
        (@StudentId, 5000.00, N'2026-09', '2026-09-10', '2026-09-08', N'Paid', N'INV-202609-000001', N'legacy paid fee', SYSUTCDATETIME()),
        (@StudentId, 5000.00, N'2026-10', '2026-10-10', NULL, N'Partial', NULL, N'legacy partial fee - amount verified manually', SYSUTCDATETIME());

-- Prove the provider upgrade never releases historical or ambiguous alerts.
IF NOT EXISTS (SELECT 1 FROM dbo.WhatsAppLogs WHERE [Message] = N'LEGACY-QUEUED-SENTINEL')
    INSERT INTO dbo.WhatsAppLogs
        (CorrelationId, StudentId, EventType, ParentPhone, [Message], [Status], RetryCount,
         Error, ProviderMessageId, CreatedAtUtc, LastAttemptAtUtc, SentAtUtc)
    VALUES
        (NEWID(), @StudentId, N'PickIn', N'+923001234567', N'LEGACY-QUEUED-SENTINEL', N'Queued', 0,
         NULL, NULL, DATEADD(HOUR, -1, SYSUTCDATETIME()), NULL, NULL),
        (NEWID(), @StudentId, N'PickIn', N'+923001234567', N'LEGACY-HANDOFF-SENTINEL', N'Sending', 0,
         NULL, NULL, DATEADD(HOUR, -1, SYSUTCDATETIME()), DATEADD(HOUR, -1, SYSUTCDATETIME()), NULL);

SELECT @StudentId AS LegacyStudentId;
