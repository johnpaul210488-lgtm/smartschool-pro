# Fee plans, automated billing and partial payments

This document describes the finance behaviour added in **v1.1.52** and how to
upgrade an existing school database safely.

---

## 1. The model in one picture

```
FeeHead (catalogue: Tuition, Transport, Admission …)      -> reusable type, no amount
   └── ClassFeePlanItem (class template: amount + cycle + include flag)
          └── StudentFeePlanItem (optional per-student override / opt-in)
FeePlanAssignment  (class or student)  -> "is billing active?" + effective date + due day + calendar
Fee                -> ONE bill line (student + fee head + billing period), immutable when auto-generated
   └── FeePayment  -> ONE row per actual payment (partial payments supported)
```

* **Nothing is charged** until a class or student plan is **activated**.
* Editing a plan (or a student override) only affects **future** billing periods.
* `Fee.Month` is the period label (`2026-10`); `Fee.BillingPeriodKey` is the
  idempotency key (`Monthly:2026-10`, `Quarterly:2026-10`, `Annually:2026-04`, `ONETIME`).

## 2. Business rules enforced by the API

| Rule | Where |
|---|---|
| Backdated activation is rejected (`effectiveFrom` must be today or later) | `PUT /api/fee-plans/assignments/...` |
| Only the **current** period is billed at activation; older periods are never back-billed | `FeePlanBillingService` |
| A plan activated mid-period still bills that period, but the due date is clamped to the activation date (no instantly-overdue bills) | `FeePlanBillingService` |
| Duplicate bills are impossible (`UNIQUE (StudentId, FeeHeadId, BillingPeriodKey)` + worker re-checks) | `SchoolDbContext` / service |
| A payment can be **partial**: `0 < amount ≤ remaining balance` | `POST /api/fees/{id}/pay` |
| Every payment is an immutable `FeePayments` row; `AmountPaid`/`Status` are derived (`Pending → Partial → Paid`) | `FeesController.Pay` |
| Overpayment is rejected; paying a settled fee returns `409` | `FeesController.Pay` |
| Auto-generated bills and bills with payment history cannot be edited or deleted | `PUT/DELETE /api/fees/{id}` |
| Dues totals report the **remaining balance**, not the original invoice value | `GET /api/fees/dues` |
| Optional fee heads are only billed after a per-student opt-in | `FeePlanBillingService` |

Billing cycles: `Monthly`, `Quarterly` (the old `PerTerm` value is migrated to
`Quarterly`), `Annually` (from `SchoolYearStartMonth`), `OneTime`.

## 3. Endpoints added

| Endpoint | Roles | Purpose |
|---|---|---|
| `GET /api/fee-plans/heads` | Staff | Fee-head catalogue |
| `GET/PUT /api/fee-plans/classes/{classId}` | Staff | Class template (draft, no bills) |
| `GET/PUT /api/fee-plans/students/{studentId}` | Staff | Per-student overrides + opt-ins |
| `GET/PUT /api/fee-plans/assignments/class/{classId}` | Staff | Activation schedule for a class |
| `GET/PUT /api/fee-plans/assignments/student/{studentId}` | Staff | Activation schedule for one student |
| `POST /api/fee-plans/generate-now` | Staff | Run the idempotent billing sweep immediately |
| `GET /api/fees/{id}/payments` | Staff, owning parent | Payment ledger of one bill |
| `GET /api/fees/legacy-partials` | Staff | Old `Partial` rows that still need reconciliation |

A background worker (`FeePlanBillingWorker`) runs the same sweep **every 30 minutes**
and once at startup, so an activated plan bills even if nobody presses "Generate bills now".

## 4. Upgrading a school PC (existing database)

1. Install the new server build. On first start EF applies three migrations:
   `FeeAutomationAndPayments`, `StudentFeePlanEffectiveDates`, `ClassFeePlanEffectiveDates`.
2. Everything already collected is preserved:
   * old `Paid` invoices get `AmountPaid = Amount` **and** one `FeePayments` row
     (receipt number kept, method `Legacy`);
   * old `Partial` invoices keep `AmountPaid = 0` — the system never invents a paid amount.
3. Reconcile those legacy partial rows once:
   * open *Fees → Collect* and pay the verified remaining amount, **or**
   * run `database/sqlserver/099_legacy_partial_reconciliation.sql` (listing +
     an `UPDATE` template) after checking the paper receipt book.
4. Then configure fee plans: *Fee plans & billing* → class amounts → set the
   effective date / due day / calendars → **Save / activate** → **Generate bills now**.

The migration **does not block** on legacy partial rows; the API stays usable and
the rows are listed in `GET /api/fees/legacy-partials`.

## 5. Verification in CI

`.github/workflows/branch-verify.yml` runs on every non-`main` branch:

* solution build (API + WPF admin compile check) and `dotnet ef migrations script`;
* **API smoke test** on SQL Server 2022: activate a plan, generate bills, prove
  idempotency, record a partial payment, reject overpayment, settle the balance,
  check the ledger keeps both rows, confirm the bill disappears from dues;
* **upgrade path test**: builds the previous schema, inserts a legacy `Paid` and
  `Partial` fee, upgrades, and asserts the money is intact and no amount was invented;
* Flutter parent-app `analyze`;
* downloadable Windows admin `.exe` artifact.

Run the smoke test locally against a running API:

```bash
BASE_URL=http://localhost:5000 bash scripts/fees-api-smoke-test.sh
```
