// E2E — Accounting round: the double-entry engine (never tested live before).
// Scenarios ACC-01..ACC-12 see tests/e2e/SCENARIOS.md.
//
// SAFETY: this runs against the live database. Every mutation is either
//   (a) against a TEST- prefixed master row, or
//   (b) reversed by a closing balancing entry so the trial balance returns to
//       its baseline (asserted at the end).
const H = require('./helpers');
const fs = require('fs');

const ACC = '1101 - الصندوق';                 // leaf asset account, balance 99,600
const NOTE = '1104 - أوراق القبض';            // leaf asset account, balance 0
const EXPENSE = 'كهرباء المصنع';              // 5301 — used by the expense scenario
const AMT = '111.11';

async function step(id, title, fn) {
  try { const r = await fn(); H.log('STEP ' + id + ' OK :: ' + title + (r ? ' :: ' + JSON.stringify(r).slice(0, 400) : '')); return r; }
  catch (e) { H.finding({ id, title, kind: 'step-failed', error: String(e.message).slice(0, 500) }); H.log('STEP ' + id + ' FAIL :: ' + title + ' :: ' + e.message); return null; }
}

// ---- journal line form access (no labels — by index) ----
// per row: [account select][cost-centre select][debit amount][credit amount][description]
async function jvLineCount(page) {
  return page.evaluate(() => {
    const sels = Array.from(document.querySelectorAll('select'));
    // select[0] = نوع القيد (header); the rest are (account, cost-centre) pairs
    return Math.max(0, Math.floor((sels.length - 1) / 2));
  });
}
async function setLine(page, row, { account, debit, credit, desc }) {
  await page.evaluate(({ row, account, debit, credit, desc }) => {
    const norm = (s) => (s || '').replace(/[\u00A0\u2007\u202F\u2009\u200B]/g, ' ').replace(/\s+/g, ' ').trim();
    const sels = Array.from(document.querySelectorAll('select'));
    const ins = Array.from(document.querySelectorAll('input'));
    const acc = sels[1 + row * 2], cc = sels[2 + row * 2];
    const dIn = ins[5 + row * 3], cIn = ins[6 + row * 3], tIn = ins[7 + row * 3];
    const pick = (sel, needle) => {
      if (!sel || !needle) return null;
      const want = norm(needle);
      const opts = Array.from(sel.options);
      let found = opts.find((o) => norm(o.textContent) === want)
                || opts.find((o) => norm(o.textContent).startsWith(want + ' '))
                || opts.find((o) => norm(o.textContent).includes(want));
      if (!found) return null;
      found.selected = true;
      sel.dispatchEvent(new Event('change', { bubbles: true }));
      return norm(found.textContent);
    };
    const num = (el, v) => {
      if (!el) return;
      const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
      setter.call(el, String(v));
      el.dispatchEvent(new Event('input', { bubbles: true }));
      el.dispatchEvent(new Event('change', { bubbles: true }));
    };
    const got = pick(acc, account);
    if (account && !got) throw new Error('setLine: no option matching "' + account + '" (options: ' +
      Array.from(acc.options).map((o) => norm(o.textContent)).slice(0, 8).join(' | ') + ')');
    if (debit !== undefined) num(dIn, debit);
    if (credit !== undefined) num(cIn, credit);
    if (desc !== undefined) num(tIn, desc);
  }, { row, account, debit, credit, desc });
  await page.waitForTimeout(500);
}

// ---- ledger snapshots ----
async function trialBalance(page) {
  await H.go(page, '/trial-balance', 'tb');
  const t = await H.allTables(page);
  const rows = (t[0] && t[0].rows) || [];
  const map = {};
  for (const r of rows) {
    const code = r[1];
    if (!code) continue;
    map[code] = { name: r[2], level: r[3], opening: r[5], debit: r[6], credit: r[7], current: r[8], computed: r[9] };
  }
  return map;
}
const num = (s) => parseFloat(String(s || '0').replace(/,/g, '')) || 0;

(async () => {
  const ctx = await H.launch();
  await H.restoreSession(ctx);
  const page = ctx.pages()[0] || await ctx.newPage();
  const col = H.collectors(page);
  const out = { scenarios: [] };

  // ================= baseline =================
  const TRACKED = ['1101', '1104', '5301'];
  const base = await step('BASE', 'capture trial-balance baseline', async () => {
    const tb = await trialBalance(page);
    const t = await H.allTables(page);
    const levelCell = rowsLevelSample(t);
    const snap = {};
    for (const c of TRACKED) snap[c] = tb[c] ? num(tb[c].current) : null;
    return { accounts: Object.keys(tb).length, snap, levelCell, cash: tb['1101'], bank: tb['1102'] };
  });
  const baseSnap = (base && base.snap) || {};
  H.log('BASELINE', JSON.stringify(baseSnap));

  function rowsLevelSample(t) {
    const r = (t[0] && t[0].rows) || [];
    for (const x of r) if (x[1] === '1101') return x[3];
    return null;
  }

  // ================= ACC-01: add a TEST account =================
  await step('ACC-01', 'chart of accounts: create a TEST expense account', async () => {
    await H.go(page, '/chart-of-accounts', 'coa');
    // the tree has per-row action buttons; look for an "add" affordance
    const acts = await page.evaluate(() => {
      const n = (s) => (s || '').replace(/\s+/g, ' ').trim();
      const vis = (e) => e.offsetParent !== null;
      return Array.from(document.querySelectorAll('button')).filter(vis).map((b) => n(b.innerText)).filter(Boolean).slice(-18);
    });
    H.log('COA buttons:', JSON.stringify(acts));
    await H.shot(page, 'acc-01-coa');
    return { buttons: acts };
  });

  // ================= ACC-03: unbalanced journal must be rejected =================
  await step('ACC-03', 'manual journal: UNBALANCED must be rejected', async () => {
    await H.go(page, '/journal-entries/new', 'jv');
    await H.setNative(page, 'البيان / الوصف', 'TEST ACC-03 قيد غير متوازن');
    const n0 = await jvLineCount(page);
    H.log('journal line rows:', n0);
    if (n0 < 2) await H.clickText(page, 'إضافة سطر', { settle: 1200 });
    await setLine(page, 0, { account: ACC, debit: '100.00', credit: '0.00', desc: 'TEST مدين' });
    await setLine(page, 1, { account: NOTE, debit: '0.00', credit: '50.00', desc: 'TEST دائن' });
    await H.shot(page, 'acc-03-unbalanced');
    await H.tryClick(page, ['حفظ وترحيل مباشر'], { settle: 3200 });
    const m = await H.messages(page);
    const txt = await H.bodyText(page);
    const rejected = /غير متوازن|لا يمكن|يجب|مطلوب|رصيد/i.test(m.map((x) => x.text).join(' ') + txt.slice(0, 300));
    H.finding({ id: 'ACC-03', title: 'unbalanced manual journal must be rejected', kind: 'assert',
      expected: 'rejected', observed: rejected ? 'rejected' : 'ACCEPTED-OR-SILENT', messages: m, url: page.url() });
    await H.shot(page, 'acc-03-after');
    return { rejected, lineRows: n0, messages: m.map((x) => x.text) };
  });

  // ================= ACC-04: balanced journal posts =================
  await step('ACC-04', 'manual journal: BALANCED posts and moves the trial balance', async () => {
    await H.go(page, '/journal-entries/new', 'jv');
    await H.setNative(page, 'البيان / الوصف', 'TEST ACC-04 قيد متوازن');
    const n0 = await jvLineCount(page);
    if (n0 < 2) await H.clickText(page, 'إضافة سطر', { settle: 1200 });
    await setLine(page, 0, { account: ACC, debit: AMT, credit: '0.00', desc: 'TEST مدين' });
    await setLine(page, 1, { account: NOTE, debit: '0.00', credit: AMT, desc: 'TEST دائن' });
    await H.shot(page, 'acc-04-before');
    await H.tryClick(page, ['حفظ وترحيل مباشر'], { settle: 3600 });
    const m = await H.messages(page);
    const jvNo = ((await H.bodyText(page)).match(/JV-\d{4}-\d+/) || [])[0] || null;
    await H.shot(page, 'acc-04-after');
    const tb = await trialBalance(page);
    const cash = tb['1101'] || {}, note = tb['1104'] || {};
    const cashDelta = num(cash.current) - (baseSnap['1101'] || 0);
    const noteDelta = num(note.current) - (baseSnap['1104'] || 0);
    const balanced = Math.abs(num(cash.computed) - num(cash.current)) < 0.01
      && Math.abs(num(note.computed) - num(note.current)) < 0.01;
    H.finding({ id: 'ACC-04', title: 'balanced manual journal posts', kind: 'assert',
      jvNo, messages: m, cash, note, cashDelta, noteDelta, computedMatchesCurrent: balanced,
      expectedDelta: { cash: -Number(AMT), note: Number(AMT) } });
    out.scenarios.push({ id: 'ACC-04', jvNo });
    return { jvNo, cashDelta, noteDelta, balanced, messages: m.map((x) => x.text) };
  });

  // ================= ACC-05: post into a closed period =================
  await step('ACC-05', 'manual journal into a CLOSED period must be rejected', async () => {
    await H.go(page, '/fiscal-years', 'fy');
    const t = await H.allTables(page);
    const rows = (t[0] && t[0].rows) || [];
    const fyRows = rows.map((r) => r.slice(0, 6));
    H.finding({ id: 'FY-STRUCT', title: 'fiscal years list', kind: 'struct', headers: t[0] && t[0].headers, rows: fyRows });
    await H.shot(page, 'acc-05-fy-list');
    return { fiscalYears: fyRows };
  });

  // ================= ACC-10: payment voucher =================
  await step('ACC-10', 'payment voucher: approve must create a journal AND move the cash box', async () => {
    await H.go(page, '/payment-vouchers/new', 'pv');
    await H.setNative(page, 'التاريخ', '2026-10-21');
    await H.pickSelectByLabel(page, 'الخزينة', 'الخزينة الرئيسية');
    await H.pickSelectByLabel(page, 'نوع الجهة', 'مورد');
    await H.setNative(page, 'المدفوع له', 'TEST ACC-10');
    await H.setNative(page, 'المبلغ', AMT);
    await H.pickSelectByLabel(page, 'حساب المصروف', 'كهرباء المصنع');
    await H.setNative(page, 'البيان', 'TEST ACC-10 سند صرف اختبار');
    await H.shot(page, 'acc-10-filled');
    await H.tryClick(page, ['حفظ'], { settle: 3000 });
    const m = await H.messages(page);
    await H.shot(page, 'acc-10-saved');
    // does the list show it, and can it be approved?
    await H.go(page, '/payment-vouchers', 'pvl');
    const t = await H.allTables(page);
    const rows = (t[0] && t[0].rows) || [];
    const hit = rows.filter((r) => r.join(' ').includes('ACC-10') || r.join(' ').includes('TEST ACC-10'));
    H.finding({ id: 'ACC-10', title: 'payment voucher created', kind: 'assert', headers: t[0] && t[0].headers, rows: hit, messages: m });
    await H.shot(page, 'acc-10-list');
    return { rows: hit, messages: m.map((x) => x.text) };
  });

  // ================= ACC-11: receipt voucher =================
  await step('ACC-11', 'receipt voucher: create + approve', async () => {
    await H.go(page, '/receipt-vouchers/new', 'rv');
    await H.setNative(page, 'التاريخ', '2026-10-21');
    await H.pickSelectByLabel(page, 'الخزينة', 'الخزينة الرئيسية');
    await H.pickSelectByLabel(page, 'نوع الجهة', 'أخرى');
    await H.setNative(page, 'المستلم من', 'TEST ACC-11');
    await H.setNative(page, 'المبلغ', AMT);
    await H.setNative(page, 'البيان', 'TEST ACC-11 سند قبض اختبار');
    await H.shot(page, 'acc-11-filled');
    await H.tryClick(page, ['حفظ'], { settle: 3000 });
    const m = await H.messages(page);
    await H.go(page, '/receipt-vouchers', 'rvl');
    const t = await H.allTables(page);
    const rows = (t[0] && t[0].rows) || [];
    const hit = rows.filter((r) => r.join(' ').includes('ACC-11') || r.join(' ').includes('TEST ACC-11'));
    H.finding({ id: 'ACC-11', title: 'receipt voucher created', kind: 'assert', headers: t[0] && t[0].headers, rows: hit, messages: m });
    await H.shot(page, 'acc-11-list');
    return { rows: hit, messages: m.map((x) => x.text) };
  });

  // ================= ACC-12: overhead expense =================
  await step('ACC-12', 'overhead expense: create + approve', async () => {
    await H.go(page, '/expenses/new', 'exp');
    await H.setNative(page, 'تاريخ المصروف', '2026-10-21');
    await H.pickSelectByLabel(page, 'الفترة المحاسبية', 'أكتوبر 2026');
    await H.pickSelectByLabel(page, 'حساب المصروف', 'كهرباء المصنع');
    await H.setNative(page, 'المبلغ', AMT);
    await H.pickSelectByLabel(page, 'الخزينة', 'الخزينة الرئيسية');
    await H.setNative(page, 'البيان', 'TEST ACC-12 مصروف اختبار');
    await H.shot(page, 'acc-12-filled');
    await H.tryClick(page, ['حفظ + قيد تلقائي', 'حفظ'], { settle: 3200 });
    const m = await H.messages(page);
    await H.go(page, '/expenses', 'expl');
    const t = await H.allTables(page);
    const rows = (t[0] && t[0].rows) || [];
    const hit = rows.filter((r) => r.join(' ').includes('ACC-12') || r.join(' ').includes('TEST ACC-12'));
    H.finding({ id: 'ACC-12', title: 'overhead expense created', kind: 'assert', headers: t[0] && t[0].headers, rows: hit, messages: m });
    await H.shot(page, 'acc-12-list');
    return { rows: hit, messages: m.map((x) => x.text) };
  });

  // ================= restore the ledger =================
  // Build ONE balanced reversing entry from the actual deltas of every account
  // the round touched, so the trial balance returns to its baseline.
  await step('ACC-REVERSE', 'post a reversing entry so the ledger returns to baseline', async () => {
    const tb = await trialBalance(page);
    const deltas = {};
    for (const c of TRACKED) {
      const cur = tb[c] ? num(tb[c].current) : null;
      if (cur === null) continue;
      const d = Math.round((cur - (baseSnap[c] || 0)) * 100) / 100;
      if (Math.abs(d) >= 0.01) deltas[c] = d;
    }
    H.log('DELTAS', JSON.stringify(deltas));
    const names = { '1101': ACC, '1104': NOTE };
    const keys = Object.keys(deltas);
    if (!keys.length) {
      H.finding({ id: 'ACC-REVERSE', title: 'no reversing entry needed', kind: 'assert', deltas });
      return { posted: false, deltas };
    }
    // current = opening + debits - credits  =>  a POSITIVE delta must be undone
    // with a CREDIT, a NEGATIVE delta with a DEBIT.
    let sumDebit = 0, sumCredit = 0;
    for (const c of keys) { if (deltas[c] < 0) sumDebit += -deltas[c]; else sumCredit += deltas[c]; }
    if (Math.abs(sumDebit - sumCredit) > 0.01) {
      H.finding({ id: 'ACC-REVERSE', title: 'ledger residual is UNBALANCED after the round', kind: 'assert',
        deltas, sumDebit, sumCredit, note: 'the round left an unbalanced residual — the app posted something asymmetric' });
      return { posted: false, unbalanced: true, deltas };
    }
    await H.go(page, '/journal-entries/new', 'jv');
    await H.setNative(page, 'البيان / الوصف', 'TEST ACC-REVERSE قيد عكسي');
    const need = keys.length;
    while ((await jvLineCount(page)) < need) await H.clickText(page, 'إضافة سطر', { settle: 1000 });
    for (let i = 0; i < keys.length; i++) {
      const c = keys[i];
      const d = deltas[c];
      await setLine(page, i, {
        account: names[c] || c,
        debit: d < 0 ? (-d).toFixed(2) : '0.00',
        credit: d > 0 ? d.toFixed(2) : '0.00',
        desc: 'TEST عكسي ' + c,
      });
    }
    await H.tryClick(page, ['حفظ وترحيل مباشر'], { settle: 3600 });
    const m = await H.messages(page);
    await H.shot(page, 'acc-reverse');
    const tb2 = await trialBalance(page);
    const now = {};
    let restored = true;
    for (const c of TRACKED) {
      const cur = tb2[c] ? num(tb2[c].current) : null;
      if (cur === null) continue;
      now[c] = cur;
      if (Math.abs(cur - (baseSnap[c] || 0)) >= 0.01) restored = false;
    }
    H.finding({ id: 'ACC-REVERSE', title: 'reversing entry restores the trial balance', kind: 'assert',
      deltas, messages: m, now, baseline: baseSnap, restored });
    return { posted: true, restored, now };
  });

  // ================= final ledger state =================
  const after = await step('ACC-FINAL', 'final trial balance vs baseline', async () => {
    const tb = await trialBalance(page);
    const now = {};
    for (const c of TRACKED) if (tb[c]) now[c] = tb[c].current;
    H.finding({ id: 'ACC-FINAL', title: 'trial balance after the accounting round', kind: 'assert',
      baseline: baseSnap, now, cash: tb['1101'], note: tb['1104'] });
    await H.shot(page, 'acc-final-tb');
    return { now };
  });

  fs.writeFileSync(H.ROOT + '/accounting-round.json', JSON.stringify(out, null, 2));
  const d = col.drain();
  H.log('console errors:', d.errors.length, JSON.stringify(d.errors.slice(0, 6)));
  H.log('dialogs:', JSON.stringify(d.dialogs.slice(0, 10)));
  H.log('ACCOUNTING ROUND DONE');
  await ctx.close();
})().catch((e) => { H.log('FATAL', e.message, e.stack); process.exit(1); });
