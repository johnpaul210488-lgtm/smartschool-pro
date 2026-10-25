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

-- ---------------------------------------------------------------------------
-- After verifying each legacy 'Partial' row against the paper receipt book,
-- record the verified amount (keep Status = N'Partial' until it is fully paid).
-- Replace the values below with your own reconciled data, then run per row.
-- The API sets AmountPaid automatically for every new payment, so this script
-- only exists for rows created by the pre-automation fee model.
-- ---------------------------------------------------------------------------
-- UPDATE dbo.Fees
-- SET AmountPaid = 5000.00                       -- verified amount received
-- WHERE Id = @FeeId AND [Status] = N'Partial' AND AmountPaid = 0;
--
-- Full remaining-balance collection for a reconciled row (use the admin app
-- "Collect fee" screen instead of SQL whenever possible).

-- Rows still needing reconciliation (these are also listed by
-- GET /api/fees/legacy-partials inside the admin app):
SELECT COUNT_BIG(*) AS PendingReconciliationCount
FROM dbo.Fees
WHERE [Status] = N'Partial' AND AmountPaid = 0;
