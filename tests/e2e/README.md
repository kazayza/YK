# E2E harness — YK Coatings ERP

Browser-driven end-to-end tests against a **running instance** of the ERP.
Driven by Playwright directly (Node), not by the MCP server, so the suite can run
headless in CI and leave machine-readable findings behind.

```
tests/e2e/
  helpers.js       shared driver (navigation, form filling, assertions, findings)
  SCENARIOS.md     the catalogue: every write-capable screen and its happy path
  run-<module>.js  one runner per module group
  report.js        aggregates findings.jsonl -> Markdown report
```

---

## Running

```powershell
# 1. the app must be up
dotnet run --project "D:\YK Coatings\YkWeb\YKCoatings.csproj"
#    -> http://localhost:5021

# 2. sign in once (headed) so a session can be captured
node tests/e2e/login.js
#    -> types nothing; waits for you to type the credentials in the Chrome window
#    -> writes the session to <E2E_OUT>/session.json

# 3. run a module group
node tests/e2e/run-accounting.js

# 4. aggregate
node tests/e2e/report.js
```

### Environment variables

| Variable | Default | Meaning |
|---|---|---|
| `E2E_BASE` | `http://localhost:5021` | base URL of the app under test |
| `E2E_OUT` | `%LOCALAPPDATA%\Temp\kilo\e2e` | where screenshots / findings / browser profile go (kept **out** of the repo) |
| `PLAYWRIGHT_PATH` | the copy bundled with `@playwright/mcp` | path to the `playwright` package |

### Requirements
* Node 18+ and the `playwright` package (bundled with `@playwright/mcp`, installed with
  `npm i -g @playwright/mcp`).
* Google Chrome installed (the harness uses `channel: 'chrome'` — no browser download).
* The app running and the database reachable.

---

## Output

| File | Content |
|---|---|
| `<E2E_OUT>/findings.jsonl` | one JSON object per finding — the machine-readable record |
| `<E2E_OUT>/shots/*.png` | screenshots |
| `<E2E_OUT>/sweep.json` | result of the read-only screen sweep |
| `<E2E_OUT>/session.json` | the captured Blazor session (encrypted `sessionStorage` + cookies) |

`findings.jsonl` is the source of truth. Each entry carries an `id` matching the
scenario id in `SCENARIOS.md`, so a report can be cross-referenced against the plan.

---

## ⚠️ Rules that MUST be followed

These are not style preferences — each one was learned by hitting the failure.

1. **Wait for rendered text after every navigation.**
   `App.razor` uses `InteractiveServerRenderMode(prerender: false)`, so the server
   ships an empty shell and the content arrives over SignalR. `body.childElementCount`
   is **not** a readiness signal (the `<script>` tags count). `helpers.settle()`
   waits for visible text — use it.

2. **Wait for saves to finish.** Every write takes 5–8 s (Azure SQL + SignalR).
   `helpers.clickBtn`/`clickText` call `waitIdle()` which polls until no
   "جارٍ الحفظ…" button is visible. Read tables only after that.

3. **`@bind` does not see `page.fill()`.** Blazor needs the native setter:
   ```js
   const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
   setter.call(el, String(value));
   el.dispatchEvent(new Event('input',  { bubbles: true }));
   el.dispatchEvent(new Event('change', { bubbles: true }));
   ```
   `helpers.setNative()` does this. Without it the field *looks* filled but the
   model never changes.

4. **Some `<label>` elements wrap their control.**
   `<label class="sequence-field"><span>…</span><input></label>` — the control is
   a descendant of the label, not of its parent. `helpers.tagField()` starts the
   walk at the label itself.

5. **Two buttons can share the same text** (e.g. "إضافة تسلسل" in the toolbar *and*
   in the modal). Scope the click to the modal.

6. **Match items by CODE, not name.** `SearchableSelect` shows both, but codes are
   unique; names are not (the test data has deliberate duplicates). Use
   `helpers.pickSS(page, 'الصنف', 'TEST خام أ', 'RM-2026-000002')`.

7. **Never close a real fiscal period.** Use a test fiscal year (2099) — closing a
   period blocks every posting that resolves to it and there is no rollback UI.

8. **Never "pay" a real payroll run.** `PayPayrollService` marks loans settled and
   is irreversible. Use a test employee in a test period.

9. **Treasury/accounting scenarios must target `TEST-` accounts.**
   `sp_PostPaymentVoucher` and `sp_PostReceiptVoucher` write
   `ChartOfAccounts.CurrentBalance` directly — a scenario against a real account
   changes real balances.

10. **`browser_run_code_unsafe` is RCE** — never used here.

---

## Assertions

Always verify the **data**, not just the screen:

```
InventoryBalance.CurrentQty  == SUM(InventoryTransactions.Quantity)
                             == SUM(InventoryBatchBalance.CurrentQty)   -- if batch rows exist
JournalEntry: DebitTotal     == CreditTotal
TrialBalance                 balanced
Customers.CurrentBalance     == Σ invoices − Σ returns
CashBoxes.CurrentBalance     == Σ its movements
```

A screen can look perfectly right while the ledger is wrong — that is exactly how
the batch/stock-count desync was found.
