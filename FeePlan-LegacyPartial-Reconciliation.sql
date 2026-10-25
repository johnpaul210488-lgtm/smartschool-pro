-- Read-only pre-migration inventory of legacy fees marked Partial.
-- The previous fee model did not persist an amount-paid value for these rows.
-- Do not infer or update amounts from this report; reconcile against receipts/records.

SELECT COUNT_BIG(*) AS LegacyPartialFeeCount
FROM dbo.Fees
WHERE [Status] = N'Partial';

SELECT
    f.Id AS FeeId,
    s.Id AS StudentId,
    s.FullName AS StudentName,
    s.RollNo,
    CONCAT(c.Name, N'-', c.Section) AS ClassName,
    f.[Month],
    f.Amount AS InvoiceAmount,
    f.DueDate,
    f.PaidDate,
    f.ReceiptNo,
    f.Notes,
    f.CreatedAtUtc
FROM dbo.Fees AS f
INNER JOIN dbo.Students AS s ON s.Id = f.StudentId
INNER JOIN dbo.Classes AS c ON c.Id = s.ClassId
WHERE f.[Status] = N'Partial'
ORDER BY c.Name, c.Section, s.FullName, f.[Month], f.Id;
