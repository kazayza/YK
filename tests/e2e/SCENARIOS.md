# E2E scenario catalogue — YK Coatings ERP

Every **write-capable** screen, with its happy path and the assertions that matter.
Scenario ids are stable: they are the `id` written into `findings.jsonl`, so a
report can be traced back to this file.

**Status legend** — `DONE` exercised in the 2026-10-21 round 1 · `TODO` round 2.

---

## Test data markers

Everything the suite creates is prefixed `TEST-` so `database/cleanup-e2e-testdata.sql`
can remove it.

| Entity | Marker | Notes |
|---|---|---|
| Items | `TEST-RAW-A`, `TEST-RAW-B`, `TEST-PKG-A`, `TEST-FG-01` | item codes are **auto-generated** (`RM-…`, `FG-…`) — identify by **name** |
| Warehouses | `TEST-WH-RAW` (type 1), `TEST-WH-FG` (type 4) | select options show the **name**, not the code |
| Supplier | `TEST مورد` | |
| Customer | `TEST عميل` | |
| Employee | `TEST موظف 1` | linked to a `SystemUsers` row |
| Chart of accounts | `TEST-…` | e.g. `TEST-1100` — used by treasury/accounting scenarios |
| Cost centre | `TEST-…` | |
| Fiscal year | `2099` | for period close/open scenarios only |
| Payroll period | `2099-12` | contains the test employee only |

---

## 1 · الحسابات (Accounting) — 11 screens — **priority 1**

| id | Scenario | Assertions |
|---|---|---|
| `ACC-01` | chart of accounts: create a `TEST-` leaf account under a real parent | parent `CurrentBalance` aggregates; appears in `TrialBalance` |
| `ACC-02` | chart of accounts: create a `TEST-` account with a `TEST-` parent | tree renders; no cycle |
| `ACC-03` | **manual journal, unbalanced (debit ≠ credit)** | must be **rejected** — if accepted, critical defect |
| `ACC-04` | manual journal, balanced ⇒ post | `DebitTotal == CreditTotal`; leaf + every parent balance updated; `TrialBalance` balanced |
| `ACC-05` | manual journal in a **closed** period | must be rejected; capture the literal message |
| `ACC-06` | fiscal year: create ⇒ 12 periods auto-created | period rows exist and are open |
| `ACC-07` | **close a period (year 2099)** ⇒ try to post into it | posting must fail |
| `ACC-08` | **reopen the period** ⇒ post again | posting succeeds |
| `ACC-09` | **close the fiscal year** ⇒ post into it | blocked; then reopen |
| `ACC-10` | payment voucher: create ⇒ approve | `sp_PostPaymentVoucher` ran → journal exists; cash box balance changed; **`ChartOfAccounts.CurrentBalance` matches** (the `catch{}` at `TreasuryService.cs:956` must not swallow it) |
| `ACC-11` | receipt voucher: create ⇒ approve | same as `ACC-10` for `sp_PostReceiptVoucher` (`TreasuryService.cs:691`) |
| `ACC-12` | overhead expense: create ⇒ approve | period exists / FY open / period open / date inside period (`AccountingService.cs:1859-1888`) |
| `ACC-13` | expense without a period | must be rejected with a usable message |
| `ACC-14` | cheque: register ⇒ collect | bank + cash box balances move; journal exists |
| `ACC-15` | cheque: collect then **bounce** | the collection is reversed (balance returns) |
| `ACC-16` | cost centre: create ⇒ use in a journal | `JournalEntryDetails.CostCenterID` set |

## 2 · الخزينة (Treasury) — 4 screens — priority 2

| id | Scenario | Assertions |
|---|---|---|
| `TRE-01` | cash box: create with a `TEST-` GL account | `AccountID` linked; appears as a posting target |
| `TRE-02` | **cash box with NO `AccountID`** ⇒ transfer between two boxes | ⚠️ `ApproveTransferAsync:1101` skips the journal while still moving the balances — **money moved with no accounting entry** |
| `TRE-03` | bank account: create ⇒ issue a cheque from it | balances + journal consistent |
| `TRE-04` | cash transfer: create ⇒ approve | journal created **by hand** (`TreasuryService.cs:1109-1115`, no SP); debit == credit; lands in the **right fiscal year** (the period lookup is `TOP 1 … ORDER BY FiscalYearID DESC`) |
| `TRE-05` | cash transfer with no open period for today | must be rejected (`TreasuryService.cs:1083-1093`) |
| `TRE-06` | currency/exchange-rate guard | an invoice in a non-default currency must be rejected (`SalesInvoiceService.cs:899-900`) |

## 3 · المبيعات (Sales) — 10 screens — priority 3

| id | Scenario | Assertions |
|---|---|---|
| `SAL-01` | quotation: create ⇒ approve | price from the price list propagates |
| `SAL-02` | sales order: create ⇒ approve | approval is status-only (`:514`) — no stock reservation, no customer balance |
| `SAL-03` | sales invoice from an order ⇒ approve | **FIFO deduction on the sale** (not proven in round 1) + `Customers.CurrentBalance` + inventory balance |
| `SAL-04` | sales invoice whose line does not match the order | must be rejected (`:906-909`) |
| `SAL-05` | sales invoice in a non-default currency | must be rejected (`:899-900`) |
| `SAL-06` | sales return: create ⇒ approve | restocks at the **FIFO layer** (`:549`) + customer balance decremented (`:555-560`) + journal (`:563`) |
| `SAL-07` | duplicate sales return | must be blocked (`:531-542`) |
| `SAL-08` | receipt voucher from a customer | cash received + customer balance |
| `SAL-09` | sales invoice with `PaidAmount > 0` | must be rejected (`:901-902` — payment posting path not available) |
| `SAL-10` | selling an item whose FIFO layers do not reconcile | must fail closed with the literal message |

## 4 · المشتريات (Purchasing) — 6 screens — priority 4

| id | Scenario | Assertions |
|---|---|---|
| `PUR-01` | purchase request: create ⇒ approve with per-line quantities | `ApprovedQty` written; shows up in the PO picker |
| `PUR-02` | **PR ⇒ PO ⇒ GRN** (the chained hand-off never exercised) | `GetApprovedPRsForPOAsync` works end to end |
| `PUR-03` | **purchase invoice: approve** | 🔴 **a journal MUST exist** — `PurchaseInvoiceService.cs:569-581` swallows the failure and commits anyway. Search `/journal-entries` for `SourceDocType='PI'` |
| `PUR-04` | purchase invoice priced differently from the PO | which price enters FIFO? (round 1 showed: the **PO** price) |
| `PUR-05` | purchase return: create ⇒ approve | `sp_UpdateInventoryAfterReturn` + supplier balance + journal (`:348-354` swallow) |
| `PUR-06` | purchase invoice with no linked GRN | must be rejected |

## 5 · المخازن (Inventory) — 2 screens — priority 5

| id | Scenario | Assertions |
|---|---|---|
| `INV-01` | **stock transfer: approve & execute** | 🔴 G2 — does `InventoryBatchBalance` change? does `AverageCost`? both balances correct; the perpetual invariant holds on **both** warehouses |
| `INV-02` | transfer of more than available | literal message; **is the transfer left stuck in status 2?** (G6) |
| `INV-03` | transfer of reserved stock | `sp_ExecuteStockTransfer` compares `CurrentQty`, not `AvailableQty` |
| `INV-04` | **opening balance** | `SetOpeningBalanceAsync` — does it create a batch row? (if not, the SKU becomes unsellable) |
| `INV-05` | stock count on a batch-tracked item (re-test of G1) | batch sum == balance after the count |

## 6 · الموارد البشرية (HR) — 19 screens — priority 6

| id | Scenario | Assertions |
|---|---|---|
| `HR-01` | employee: create ⇒ link to a user | `SystemUsers.EmployeeID` set — every HR approval path needs it |
| `HR-02` | department + job title | `/jobtitles` and `/holidays` were **never even rendered** in round 1 |
| `HR-03` | attendance: manual entry | row written; feeds leave/absence |
| `HR-04` | attendance: **fingerprint import** | `ImportFromFingerprintAsync` bulk insert; per-row provenance |
| `HR-05` | leave type + leave balance | `/leavetypes` was never rendered |
| `HR-06` | leave: request ⇒ manager approval ⇒ HR approval | balance decremented **once**, not twice |
| `HR-07` | leave: reject | balance untouched |
| `HR-08` | loan: create ⇒ approve | **instalment schedule** (`:194-215`) count + amounts — payroll deducts from it later |
| `HR-09` | penalty/reward: approve | feeds the payroll run |
| `HR-10` | **payroll: create ⇒ calculate ⇒ approve ⇒ pay** (year 2099, test employee only) | net pay, loan instalment deduction, payroll journal, loan status |
| `HR-11` | payroll: cancel | reversals |
| `HR-12` | payroll: approve before calculate | must be blocked (`:369-370`) |

## 7 · الأمان (Security) — 4 screens — priority 7

| id | Scenario | Assertions |
|---|---|---|
| `SEC-01` | role with narrow permissions ⇒ new user ⇒ open every write screen | any screen that opens despite missing permission is a **defect** |
| `SEC-02` | disable the admin role | 🔴 does it lock **everyone** out? is there a backdoor? |
| `SEC-03` | edit your own role/permissions | privilege escalation must be impossible |
| `SEC-04` | `/counter` and `/dev-session-test` without permission | `Counter.razor` has **no permission check at all** — publicly reachable today |
| `SEC-05` | audit log: is the write recorded **in the same transaction**? | the audit write is outside the caller's transaction (`AuditService.cs:27`) |

## 8 · البيانات الأساسية (Basic data) — 11 screens — priority 8

| id | Scenario | Assertions |
|---|---|---|
| `BAS-01` | category / unit / currency / payment term / price list / customer: CRUD | |
| `BAS-02` | currency: set the default | a wrong `IsDefault` makes **every sales invoice fail to approve** |
| `BAS-03` | unit conversion factor | quantities in that unit are scaled everywhere |
| `BAS-04` | **Excel item import** | template `templates/Items_Import_Template.xlsx`; bulk commit (`:274`); generated codes do not collide |
| `BAS-05` | system settings | any switch that changes invoice behaviour |

## 9 · اختبارات عرضية (cross-cutting)

| id | Scenario | Assertions |
|---|---|---|
| `XCT-01` | permission matrix: one role per module + an empty role + a disabled role | every screen blocked accordingly |
| `XCT-02` | negative cases per module (insufficient stock, wrong date, duplicate, zero amount) | literal error messages captured |
| `XCT-03` | two concurrent sessions on the same record | last write wins? any corruption? |
| `XCT-04` | print / export on every main list | does it actually produce a file? |
| `XCT-05` | **full read-only sweep** of every route after each fix | regression guard |

---

## Round 1 results (2026-10-21) — for reference

| id | Result |
|---|---|
| `FIFO-01` | ✅ FIFO proven — the issue line split 50 @ 10.00 + 20 @ 14.00 = 780.00 |
| `FIFO-02` | ✅ perpetual invariant held on the raw material |
| `SEQ-01` | 🔴 the `BOM` sequence was missing ⇒ no BOM could be created + the unhandled exception killed the circuit (**fixed during the run**) |
| `CNT-01` | 🔴 the stock count does not update `InventoryBatchBalance` ⇒ permanent desync |
| `CNT-02` | 🔴 the count adjustment is booked at cost `0.00` (source: `Items.AverageCost`, never set by batch posting) |
| `PRV-01` | 🟠 the issue preview shows the average cost, the posting applies real FIFO (12% difference) |
| `UI-01` | 🟡 the item code is auto-generated; the typed code is discarded |
| `UI-02` | 🟡 the items list search does not match codes despite the placeholder promising it |
| `UI-03` | 🟡 the "stock status" column in the items list is simulated |
| `UI-04` | 🟡 the production order does not display its required materials |
| `SEQ-02` | 🟡 the `SQ` sequence prefix was `QT-` ⇒ `QT--2026-000003`; and `ITM` was missing entirely |
