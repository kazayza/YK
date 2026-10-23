// E2E harness helpers for YK Coatings ERP (Blazor Server)
// Direct Playwright driver (MCP tools are not loaded in this session).
// Output (screenshots / findings / browser profile) lives OUTSIDE the repo by
// default; override with the E2E_OUT environment variable.
const fs = require('fs');
const path = require('path');
const PW_PATH = process.env.PLAYWRIGHT_PATH
  || 'C:/Users/Admin/AppData/Roaming/npm/node_modules/@playwright/mcp/node_modules/playwright';
const pw = require(PW_PATH);

const BASE = process.env.E2E_BASE || 'http://localhost:5021';
const ROOT = process.env.E2E_OUT || 'C:/Users/Admin/AppData/Local/Temp/kilo/e2e';
const SHOTS = ROOT + '/shots';
const LOGS = ROOT + '/logs';
const PROFILE = ROOT + '/profile';
const SESSION = ROOT + '/session.json';
const FINDINGS = ROOT + '/findings.jsonl';
const TABS = ROOT + '/tabs.json';

for (const d of [SHOTS, LOGS, PROFILE]) if (!fs.existsSync(d)) fs.mkdirSync(d, { recursive: true });

const ts = () => new Date().toISOString().slice(11, 19);
function log(...a) {
  const s = a.map((x) => (typeof x === 'string' ? x : JSON.stringify(x))).join(' ');
  console.log(`[${ts()}] ${s}`);
}
function finding(o) {
  o.ts = new Date().toISOString();
  fs.appendFileSync(FINDINGS, JSON.stringify(o) + '\n');
  log('FINDING ' + JSON.stringify(o).slice(0, 400));
}
function readFindings() {
  if (!fs.existsSync(FINDINGS)) return [];
  return fs.readFileSync(FINDINGS, 'utf8').split('\n').filter(Boolean).map((l) => JSON.parse(l));
}

// ---------- browser ----------
async function launch() {
  const ctx = await pw.chromium.launchPersistentContext(PROFILE, {
    headless: false,
    channel: 'chrome',
    viewport: { width: 1440, height: 900 },
    args: ['--start-maximized'],
  });
  ctx.setDefaultTimeout(20000);
  ctx.setDefaultNavigationTimeout(60000);
  return ctx;
}

async function restoreSession(ctx) {
  if (!fs.existsSync(SESSION)) return false;
  try {
    const s = JSON.parse(fs.readFileSync(SESSION, 'utf8'));
    if (s.cookies && s.cookies.length) await ctx.addCookies(s.cookies);
    if (s.ss) {
      await ctx.addInitScript((ss) => {
        try { for (const [k, v] of Object.entries(ss)) sessionStorage.setItem(k, v); } catch (e) {}
      }, s.ss);
    }
    log('session restored:', Object.keys(s.ss || {}).join(','));
    return true;
  } catch (e) { log('session restore failed:', e.message); return false; }
}

async function saveSession(ctx, page) {
  const ss = await page.evaluate(() => {
    const o = {};
    try { for (let i = 0; i < sessionStorage.length; i++) { const k = sessionStorage.key(i); o[k] = sessionStorage.getItem(k); } } catch (e) {}
    return o;
  });
  const cookies = await ctx.cookies();
  fs.writeFileSync(SESSION, JSON.stringify({ ss, cookies }, null, 2));
  log('session saved, sessionStorage keys:', Object.keys(ss).join(','));
}

// ---------- diagnostics ----------
function collectors(page) {
  const errors = [];
  const failed = [];
  const dialogs = [];
  page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text().slice(0, 500)); });
  page.on('pageerror', (e) => errors.push('pageerror: ' + e.message.slice(0, 500)));
  page.on('response', (r) => { if (r.status() >= 400) failed.push(r.status() + ' ' + r.url().slice(0, 160)); });
  page.on('dialog', async (d) => { dialogs.push(d.type() + ': ' + d.message().slice(0, 200)); await d.accept(); });
  return {
    errors, failed, dialogs,
    drain() { const e = errors.splice(0), f = failed.splice(0), d = dialogs.splice(0); return { errors: e, failed: f, dialogs: d }; },
  };
}

// ---------- page primitives ----------
// STRICT readiness check.
// The Blazor layout always renders the sidebar + header, so "the page has text"
// is NOT a readiness signal — a page whose component threw renders the nav only.
// Read the CONTENT AREA (.app-body-inner) and require real content in it.
async function settle(page) {
  try {
    return await page.evaluate(async () => {
      const deadline = Date.now() + 25000;
      const visible = (el) => !!el && el.offsetParent !== null && getComputedStyle(el).display !== 'none';
      const rendered = () => {
        const c = document.querySelector('.app-body-inner');
        if (!c) return false;
        const txt = (c.innerText || '').replace(/\s+/g, ' ').trim();
        if (txt.length < 25) return false;
        if (/جاري التحميل|جارٍ التحميل|جار التحميل|Loading\.\.\./.test(txt)) return false;
        const busy = c.querySelector('.loader-luxury, .skeleton, [class*="loader"], [class*="spinner"]');
        if (visible(busy)) return false;
        if (visible(document.querySelector('#blazor-error-ui'))) return false;
        return true;
      };
      while (Date.now() < deadline) {
        if (rendered()) return 'ready';
        await new Promise((r) => setTimeout(r, 250));
      }
      return 'timeout';
    });
  } catch (e) { return 'error:' + e.message; }
}

async function go(page, p, label) {
  const url = p.startsWith('http') ? p : BASE + p;
  let nav = 'ok';
  try { await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 60000 }); }
  catch (e) { nav = 'navfail:' + e.message.slice(0, 120); }
  const s = await settle(page);
  log('NAV', label || p, '->', nav, '| settle:', s, '|', page.url().replace(BASE, ''));
  return { nav, settle: s, url: page.url() };
}

async function bodyText(page) {
  return page.evaluate(() => (document.body ? document.body.innerText : ''));
}

async function shot(page, name) {
  const f = path.join(SHOTS, name + '.png');
  try { await page.screenshot({ path: f, fullPage: true }); } catch (e) { log('shot fail', name, e.message); }
  return f;
}

// ---------- form helpers ----------
async function tagField(page, labelText, kind) {
  return page.evaluate(({ lt, k }) => {
    const norm = (s) => (s || '')
      .replace(/[\u{1F300}-\u{1FAFF}\u{2190}-\u{21FF}\u{2600}-\u{27BF}\u{FE0F}]/gu, '')
      .replace(/\u00A0/g, ' ').replace(/\s+/g, ' ').trim();
    const lab = Array.from(document.querySelectorAll('label, .field-label, .f-label-luxury, .fl-label, .flabel, .lbl-luxury, .form-label'))
      .find((l) => { const t = norm(l.textContent); return t === lt || t.startsWith(lt + ' '); });
    if (!lab) return { ok: false, why: 'label "' + lt + '" not found' };
    // NB: some labels WRAP their control (<label><span>..</span><input></label>),
    // so the walk must start at the label itself, not its parent.
    let node = lab;
    let best = null;
    for (let i = 0; i < 5 && node; i++) {
      const ctrls = Array.from(node.querySelectorAll(k)).filter((c) => !c.disabled);
      if (ctrls.length === 1) { best = ctrls[0]; break; }
      if (ctrls.length > 1 && !best) best = ctrls[0];
      node = node.parentElement;
    }
    if (!best) return { ok: false, why: 'no ' + k + ' under label "' + lt + '"' };
    // exclusive tagging: only one control carries the marker at a time
    for (const el of document.querySelectorAll('[data-e2e="f"]')) el.removeAttribute('data-e2e');
    best.setAttribute('data-e2e', 'f');
    return { ok: true, tag: best.tagName, name: best.getAttribute('name') || best.id || '' };
  }, { lt: labelText, k: kind });
}

async function fillByLabel(page, labelText, value) {
  const r = await tagField(page, labelText, 'input');
  if (!r.ok) throw new Error('fillByLabel: ' + r.why);
  const el = page.locator('input[data-e2e="f"], textarea[data-e2e="f"]').first();
  await el.fill(String(value));
  await el.dispatchEvent('change').catch(() => {});
  return r;
}

async function selectByLabel(page, labelText, optionText, opts = {}) {
  const deadline = Date.now() + (opts.timeout || 12000);
  let last = null;
  while (Date.now() < deadline) {
    const r = await tagField(page, labelText, 'select');
    if (!r.ok) throw new Error('selectByLabel: ' + r.why);
    const sel = page.locator('select[data-e2e="f"]').first();
    const opts2 = (await sel.locator('option').allTextContents()).map((o) => o.trim());
    let target = opts2.find((o) => o === optionText) || opts2.find((o) => o.startsWith(optionText));
    if (!target) target = opts2.find((o) => o.includes(optionText));
    if (target) { await sel.selectOption({ label: target }); return target; }
    last = opts2;
    await page.waitForTimeout(500);
  }
  throw new Error(`selectByLabel: option "${optionText}" not found in "${labelText}". available: ${(last || []).join(' | ')}`);
}

async function tagButton(page, text) {
  return page.evaluate((t) => {
    const norm = (s) => (s || '')
      .replace(/[\u{1F300}-\u{1FAFF}\u{2190}-\u{21FF}\u{2600}-\u{27BF}\u{FE0F}]/gu, '')
      .replace(/\u00A0/g, ' ').replace(/\s+/g, ' ').trim();
    const target = norm(t);
    const btns = Array.from(document.querySelectorAll('button')).filter((b) => b.offsetParent !== null);
    let hit = btns.find((b) => norm(b.innerText) === target);
    if (!hit) hit = btns.find((b) => norm(b.innerText).includes(target));
    if (!hit) return { ok: false, available: btns.map((b) => norm(b.innerText)).filter(Boolean) };
    for (const el of document.querySelectorAll('[data-e2e="b"]')) el.removeAttribute('data-e2e');
    hit.setAttribute('data-e2e', 'b');
    return { ok: true, text: norm(hit.innerText) };
  }, text);
}

async function clickBtn(page, text, opts = {}) {
  const deadline = Date.now() + (opts.timeout || 25000);
  let lastAvail = null;
  while (Date.now() < deadline) {
    const r = await page.evaluate((t) => {
      const norm = (s) => (s || '').replace(/[\u{1F300}-\u{1FAFF}\u{2190}-\u{21FF}\u{2600}-\u{27BF}\u{FE0F}]/gu, '').replace(/\s+/g, ' ').trim();
      const target = norm(t);
      const btns = Array.from(document.querySelectorAll('button')).filter((b) => b.offsetParent !== null);
      const enabled = btns.filter((b) => !b.disabled);
      let hit = enabled.find((b) => norm(b.innerText) === target);
      if (!hit) hit = enabled.find((b) => norm(b.innerText).includes(target));
      if (hit) {
        for (const el of document.querySelectorAll('[data-e2e="b"]')) el.removeAttribute('data-e2e');
        hit.setAttribute('data-e2e', 'b');
        return { ok: true, text: norm(hit.innerText) };
      }
      return { ok: false, available: btns.map((b) => norm(b.innerText)).filter(Boolean) };
    }, text);
    if (r.ok) {
      const el = page.locator('[data-e2e="b"]').first();
      try {
        await el.scrollIntoViewIfNeeded().catch(() => {});
        await el.click({ timeout: 5000 });
        await page.waitForTimeout(opts.settle === false ? 250 : (opts.settle || 900));
        if (opts.idle !== false) await waitIdle(page, opts.idleTimeout || 40000);
        return r;
      } catch (e) { /* element re-rendered — resolve again */ }
    }
    lastAvail = r.available;
    await page.waitForTimeout(600);
  }
  throw new Error('clickBtn "' + text + '" not clickable. available: ' + (lastAvail || []).join(' | '));
}

// Click the first of several button labels that is present AND enabled.
async function tryClick(page, texts, opts = {}) {
  const errors = [];
  for (const t of texts) {
    try { const r = await clickBtn(page, t, opts); return { clicked: t, r }; }
    catch (e) { errors.push(t + ': ' + String(e.message).slice(0, 120)); }
  }
  throw new Error('tryClick failed for [' + texts.join(' | ') + '] -> ' + errors.join(' ; '));
}

async function hasButton(page, text) {
  const r = await tagButton(page, text);
  return r.ok;
}

// Wait until no "saving…" button / spinner is visible. Azure SQL round-trips take 5-8s.
async function waitIdle(page, timeout = 40000) {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    let busy = false;
    try {
      busy = await page.evaluate(() => {
        const vis = (el) => el.offsetParent !== null && getComputedStyle(el).display !== 'none';
        const norm = (s) => (s || '').replace(/\s+/g, ' ').trim();
        const saving = Array.from(document.querySelectorAll('button')).filter(vis)
          .some((b) => /جاري|جارٍ/.test(norm(b.innerText)));
        const spinners = Array.from(document.querySelectorAll('.btn-spinner, .btn-spinner-sm, .spinner, .loader-luxury'))
          .filter(vis).length > 0;
        return saving || spinners;
      });
    } catch (e) { return false; }
    if (!busy) { await page.waitForTimeout(400); return true; }
    await page.waitForTimeout(500);
  }
  H.log('waitIdle TIMED OUT');
  return false;
}

// Click any element (not just <button>) whose visible text matches — source cards, tabs, links.
async function clickText(page, text, opts = {}) {
  const deadline = Date.now() + (opts.timeout || 20000);
  let r = null;
  for (;;) {
    r = await page.evaluate((t) => {
      const norm = (s) => (s || '').replace(/[\u{1F300}-\u{1FAFF}\u{2190}-\u{21FF}\u{2600}-\u{27BF}\u{FE0F}]/gu, '').replace(/\s+/g, ' ').trim();
      const target = norm(t);
      const sel = ['.source-card-luxury', '.tab-btn', '.tab-item', '.tab-header', 'button', 'a', '.nav-link-luxury', '.clinic-tab', '[role="tab"]'];
      const cands = [];
      for (const s of sel) for (const el of document.querySelectorAll(s)) if (el.offsetParent !== null) cands.push(el);
      let hit = cands.find((e) => norm(e.innerText) === target);
      if (!hit) hit = cands.find((e) => norm(e.innerText).includes(target));
      if (!hit) return { ok: false, available: cands.map((e) => norm(e.innerText)).filter(Boolean).slice(0, 40) };
      for (const el of document.querySelectorAll('[data-e2e="t"]')) el.removeAttribute('data-e2e');
      hit.setAttribute('data-e2e', 't');
      return { ok: true, text: norm(hit.innerText), cls: hit.className };
    }, text);
    if (r.ok) break;
    if (Date.now() > deadline) throw new Error('clickText "' + text + '" not found. available: ' + (r.available || []).join(' | '));
    await page.waitForTimeout(600);
  }
  const el = page.locator('[data-e2e="t"]').first();
  await el.scrollIntoViewIfNeeded().catch(() => {});
  await el.click({ timeout: opts.timeout || 15000 });
  await page.waitForTimeout(opts.settle === false ? 250 : (opts.settle || 900));
  if (opts.idle !== false) await waitIdle(page, opts.idleTimeout || 40000);
  return r;
}

// Pick a value from a SearchableSelect that belongs to the given label.
async function pickSS(page, labelText, query, matchText) {
  const found = await page.evaluate((lt) => {
    const norm = (s) => (s || '').replace(/\s+/g, ' ').trim();
    const labels = Array.from(document.querySelectorAll('label, .field-label, .f-label-luxury, .fl-label, .flabel'));
    const lab = labels.find((l) => norm(l.textContent) === lt);
    if (!lab) return false;
    let node = lab.parentElement;
    for (let i = 0; i < 6 && node; i++) {
      const w = node.querySelector('.ss-wrap');
      if (w) {
        for (const el of document.querySelectorAll('[data-e2e="ss"]')) el.removeAttribute('data-e2e');
        w.setAttribute('data-e2e', 'ss');
        return true;
      }
      node = node.parentElement;
    }
    return false;
  }, labelText);
  if (!found) throw new Error('pickSS: no SearchableSelect for label "' + labelText + '"');
  const wrap = page.locator('[data-e2e="ss"]').first();
  const input = wrap.locator('.ss-input');
  await input.click({ timeout: 10000 });
  await input.fill(String(query));
  try {
    await page.waitForFunction(
      (sel) => document.querySelectorAll(sel + ' .ss-option').length > 0,
      '[data-e2e="ss"]', { timeout: 15000 });
  } catch (e) {
    const avail = await wrap.locator('.ss-option').allTextContents().catch(() => []);
    const empty = await wrap.locator('.ss-empty').allTextContents().catch(() => []);
    const val = await input.inputValue().catch(() => '');
    throw new Error(`pickSS("${labelText}"): no options for query "${val}". visible=${JSON.stringify(avail).slice(0, 400)} empty=${JSON.stringify(empty)}`);
  }
  const opt = wrap.locator('.ss-option').filter({ hasText: matchText }).first();
  const n = await opt.count();
  if (!n) {
    const avail = await wrap.locator('.ss-option').allTextContents();
    throw new Error(`pickSS("${labelText}"): no option matching "${matchText}". available: ${avail.join(' | ').slice(0, 600)}`);
  }
  await opt.waitFor({ state: 'visible', timeout: 15000 });
  await opt.click();
  await page.waitForTimeout(700);
  const chosen = await wrap.locator('.ss-selected-text').first().textContent().catch(() => null);
  return { chosen: (chosen || '').trim() };
}

// Read the value of a control by its label.
async function readByLabel(page, labelText) {
  return page.evaluate((lt) => {
    const norm = (s) => (s || '').replace(/\s+/g, ' ').trim();
    const lab = Array.from(document.querySelectorAll('label, .field-label, .f-label-luxury, .fl-label, .flabel, .lbl-luxury'))
      .find((l) => norm(l.textContent) === lt);
    if (!lab) return null;
    let node = lab;
    for (let i = 0; i < 5 && node; i++) {
      const c = node.querySelector('input, select, textarea');
      if (c) return c.value;
      node = node.parentElement;
    }
    return null;
  }, labelText);
}

// Search a list page's text filter and report whether a code appears.
async function searchList(page, selector, needle) {
  const inp = page.locator(selector).first();
  if (!(await inp.count())) return { found: false, reason: 'filter input not found' };
  await inp.fill(needle);
  await inp.dispatchEvent('change').catch(() => {});
  await page.waitForTimeout(1500);
  const txt = await page.evaluate(() => document.body.innerText);
  return { found: txt.includes(needle), url: page.url() };
}

// Shared label matching rule used by setNative / pickSelectByLabel:
// exact match OR "label + ' '..." prefix. Forms in this app render labels like
// "التاريخ * (PaymentDate)" and "البيان / الوصف *" — exact match alone is not enough.

// Set a value using the native setter so Blazor's @bind actually sees it.
// page.fill() does NOT trigger Blazor's @bind on Server-side components.
async function setNative(page, label, v) {
  const r = await page.evaluate(({ l, val, m }) => {
    const norm = (s) => (s || '').replace(/\s+/g, ' ').trim();
    const clean = (s) => norm(s).replace(/\s*\*\s*$/, '').trim();
    const hit = (el, L) => { const t = clean(el.textContent); return t === L || t.startsWith(L + ' '); };
    for (const x of document.querySelectorAll('label, .form-lbl, .field-label, .f-label-luxury, .flabel, .sequence-field > span')) {
      if (!hit(x, l)) continue;
      let node = x.tagName === 'SPAN' ? x.parentElement : x;
      let inp = null;
      for (let i = 0; i < 4 && node; i++) {
        inp = node.querySelector('input, textarea');
        if (inp) break;
        node = node.parentElement;
      }
      if (inp) {
        if (inp.disabled) return { ok: false, why: 'disabled' };
        const proto = inp.tagName === 'TEXTAREA' ? window.HTMLTextAreaElement.prototype : window.HTMLInputElement.prototype;
        const setter = Object.getOwnPropertyDescriptor(proto, 'value').set;
        setter.call(inp, String(val));
        inp.dispatchEvent(new Event('input', { bubbles: true }));
        inp.dispatchEvent(new Event('change', { bubbles: true }));
        return { ok: true, val: inp.value };
      }
    }
    return { ok: false, why: 'not found' };
  }, { l: label, val: v });
  if (!r.ok) throw new Error('setNative "' + label + '": ' + r.why);
  await page.waitForTimeout(500);
  return r;
}

// Pick a <select> option by the label above it.
// Option labels are rendered with non-breaking spaces in several forms, so the
// comparison is normalised (all whitespace-like chars -> ' ').
// Matching order: exact -> "needle + ' '" prefix -> contains.
async function pickSelectByLabel(page, label, needle) {
  const r = await page.evaluate(({ l, nd }) => {
    const norm = (s) => (s || '').replace(/[\u00A0\u2007\u202F\u2009\u200B]/g, ' ').replace(/\s+/g, ' ').trim();
    const clean = (s) => norm(s).replace(/\s*\*\s*$/, '').trim();
    const hit = (el, L) => { const t = clean(el.textContent); return t === L || t.startsWith(L + ' '); };
    for (const x of document.querySelectorAll('label, .form-lbl, .field-label, .f-label-luxury, .flabel, .sequence-field > span')) {
      if (!hit(x, l)) continue;
      let node = x.tagName === 'SPAN' ? x.parentElement : x;
      for (let i = 0; i < 5 && node; i++) {
        const s = node.querySelector('select');
        if (s) {
          const want = norm(nd);
          const opts = Array.from(s.options).map((o) => ({ o, t: norm(o.textContent) }));
          let found = opts.find((x2) => x2.t === want)
                   || opts.find((x2) => x2.t.startsWith(want + ' '))
                   || opts.find((x2) => x2.t.includes(want))
                   || opts.find((x2) => x2.t.startsWith(want));
          if (found) { found.o.selected = true; s.dispatchEvent(new Event('change', { bubbles: true })); return found.t; }
          return 'NO_MATCH:' + opts.map((x2) => x2.t).join(' | ');
        }
        node = node.parentElement;
      }
    }
    return null;
  }, { l: label, nd: needle });
  if (!r) throw new Error('pickSelectByLabel: "' + label + '" not found');
  if (String(r).startsWith('NO_MATCH')) throw new Error('pickSelectByLabel "' + label + '": ' + r);
  await page.waitForTimeout(1400);
  return r;
}

// The text of the content area only (never the sidebar).
async function contentText(page) {
  return page.evaluate(() => {
    const c = document.querySelector('.app-body-inner');
    return c ? (c.innerText || '').replace(/\s+/g, ' ').trim() : '';
  });
}

// Read every visible label + control so failures are self-describing.
async function dumpFields(page) {
  return page.evaluate(() => {
    const vis = (el) => el.offsetParent !== null && getComputedStyle(el).display !== 'none';
    const norm = (s) => (s || '').replace(/\s+/g, ' ').trim();
    return {
      labels: Array.from(document.querySelectorAll('label, .field-label, .f-label-luxury, .fl-label, .flabel')).filter(vis).map((l) => norm(l.textContent)).filter(Boolean),
      selects: Array.from(document.querySelectorAll('select')).filter(vis).map((s) => Array.from(s.options).map((o) => norm(o.textContent))),
      ss: Array.from(document.querySelectorAll('.ss-wrap')).filter(vis).map((w) => norm(w.querySelector('.ss-input')?.getAttribute('placeholder') || w.querySelector('.ss-selected-text')?.textContent || '')),
      buttons: Array.from(document.querySelectorAll('button')).filter(vis).map((b) => norm(b.innerText)).filter(Boolean),
    };
  });
}

// ---------- reading ----------
async function readTable(page, idx = 0) {
  return page.evaluate((i) => {
    const t = document.querySelectorAll('table')[i];
    if (!t) return null;
    return {
      headers: Array.from(t.querySelectorAll('thead th')).map((th) => th.innerText.trim()),
      rows: Array.from(t.querySelectorAll('tbody tr')).map((tr) =>
        Array.from(tr.querySelectorAll('td')).map((td) => td.innerText.trim())),
    };
  }, idx);
}

async function allTables(page) {
  return page.evaluate(() => Array.from(document.querySelectorAll('table')).map((t) => ({
    headers: Array.from(t.querySelectorAll('thead th')).map((th) => th.innerText.trim()),
    rows: Array.from(t.querySelectorAll('tbody tr')).map((tr) =>
      Array.from(tr.querySelectorAll('td')).map((td) => {
        let v = td.innerText.trim();
        const inputs = td.querySelectorAll('input');
        for (const i of inputs) {
          if (i.type === 'checkbox' || i.type === 'radio') continue;
          v += (v ? '|' : '') + (i.value !== undefined && i.value !== '' ? i.value : '∅');
        }
        return v;
      })),
  })));
}

async function findRow(page, needle, idx = 0) {
  const t = await readTable(page, idx);
  if (!t) return null;
  const n = String(needle);
  return t.rows.find((r) => r.some((c) => c.includes(n))) || null;
}

async function rowCount(page, needle, idx = 0) {
  const t = await readTable(page, idx);
  if (!t) return 0;
  const n = String(needle);
  return t.rows.filter((r) => r.some((c) => c.includes(n))).length;
}

async function messages(page) {
  return page.evaluate(() => {
    const sels = ['.toast-msg', '.toast-luxury', '.toast-card-luxury', '.msg-luxury', '.msg-error', '.msg-success',
      '.field-error', '.form-msg-luxury.err', '.form-msg-luxury.ok', '.alert', '.alert-danger', '.alert-success',
      '.sequence-inline-error'];
    const out = [];
    for (const s of sels) {
      for (const el of document.querySelectorAll(s)) {
        if (el.offsetParent === null) continue;
        const t = (el.innerText || '').replace(/\s+/g, ' ').trim();
        if (t) out.push({ sel: s, text: t });
      }
    }
    // de-dup by text
    const seen = new Set();
    return out.filter((x) => (seen.has(x.text) ? false : (seen.add(x.text), true)));
  });
}

async function statCards(page) {
  return page.evaluate(() => {
    const out = [];
    for (const c of document.querySelectorAll('.stat-card-luxury, .stat-card, .kpi-card, .luxury-stat')) {
      const t = c.innerText.replace(/\s+/g, ' ').trim();
      if (t) out.push(t);
    }
    return out;
  });
}

// ---------- tabs ----------
async function openTab(page, p, label) {
  const t = await ctxNewPage(page);
  const r = await go(t, p, label);
  return { page: t, ...r };
}
async function ctxNewPage(page) {
  const ctx = page.context();
  return await ctx.newPage();
}

module.exports = {
  pw, fs, path, BASE, ROOT, SHOTS, LOGS, PROFILE, SESSION, FINDINGS, TABS,
  log, finding, readFindings, launch, restoreSession, saveSession, collectors,
  settle, go, bodyText, shot, fillByLabel, selectByLabel, clickBtn, tagButton, tagField, hasButton,
  clickText, pickSS, dumpFields, readByLabel, searchList, waitIdle, tryClick,
  setNative, pickSelectByLabel, contentText,
  readTable, allTables, findRow, rowCount, messages, statCards, openTab,
};
