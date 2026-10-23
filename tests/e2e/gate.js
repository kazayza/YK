// Quality gate: no emoji, no CDN, no broken icon classes.
// Run:  node tests/e2e/gate.js     -> prints PASS / FAIL and exits 1 on violation.
const fs = require('fs'), path = require('path');
const ROOT = path.resolve(__dirname, '..', '..');
const SCAN_DIRS = ['Components', 'Services', 'Models', 'Helpers', 'wwwroot/js'];
const EXT = new Set(['.razor', '.cs', '.js']);

// pictographs, geometric, dingbats, misc-symbols, regional indicators.
// Arrows (→ ← ↑ ↓ ↳) are TYPOGRAPHY, not emoji — they are legitimate in prose
// (audit descriptions, code comments) and are therefore whitelisted.
const EMOJI = /[\u{1F000}-\u{1FAFF}\u{2300}-\u{23FF}\u{25A0}-\u{25FF}\u{2600}-\u{27BF}\u{2B00}-\u{2BFF}\u{FE0F}\u{1F1E6}-\u{1F1FF}]/u;
// allowed only inside a comment (prose arrows like "x -> y")
const isComment = (l) => /^\s*(\/\/|\*|\/\*)/.test(l);
const CDN = /https?:\/\/(?!localhost|127\.0\.0\.1)[^"'\s)]+/g;
const BAD_CLASS = /bi bi-bi-/g;
// `class="bi bi-x"` is VALID html — but fatal inside a C# string literal, because the
// double quotes terminate the string. Only flag it where it is actually inside one.
const DBL = /class=["']bi bi-/g;

const violations = [];
const walk = (d) => {
  const base = path.join(ROOT, d);
  if (!fs.existsSync(base)) return;
  for (const e of fs.readdirSync(base, { withFileTypes: true })) {
    const f = path.join(base, e.name);
    if (e.isDirectory()) { if (!['bin', 'obj', 'node_modules', 'bootstrap-icons'].includes(e.name)) walk(d + '/' + e.name); }
    else if (EXT.has(path.extname(e.name))) {
      const rel = path.relative(ROOT, f).split(path.sep).join('/');
      fs.readFileSync(f, 'utf8').split('\n').forEach((l, i) => {
        const m = l.match(EMOJI);
        if (m && !isComment(l)) {
          violations.push({ kind: 'emoji', file: rel, line: i + 1, detail: [...new Set(m)].join(' ') + '  ::  ' + l.trim().slice(0, 110) });
        }
        const b = l.match(BAD_CLASS);
        if (b) violations.push({ kind: 'icon-class', file: rel, line: i + 1, detail: l.trim().slice(0, 110) });
        // double class inside a C# string literal
        let idx = 0;
        while (true) {
          const m = DBL.exec(l);
          if (!m) break;
          const q = (l.slice(0, m.index).match(/"/g) || []).length;
          if (q % 2 === 1) violations.push({ kind: 'icon-in-csharp-string', file: rel, line: i + 1, detail: l.trim().slice(0, 110) });
        }
      });
    }
  }
};
for (const d of SCAN_DIRS) walk(d);

// --- CDN check: App.razor + every CSS ---
const cdnFiles = ['Components/App.razor', 'wwwroot/yk-theme-gold.css', 'wwwroot/yk-theme.css', 'wwwroot/yk-hr-overrides.css', 'wwwroot/yk-icons.css', 'wwwroot/yk-gold-pages.css', 'wwwroot/fonts/tajawal.css'];
for (const rel of cdnFiles) {
  const f = path.join(ROOT, rel);
  if (!fs.existsSync(f)) continue;
  fs.readFileSync(f, 'utf8').split('\n').forEach((l, i) => {
    const m = l.match(CDN);
    if (m) violations.push({ kind: 'cdn', file: rel, line: i + 1, detail: m.join(' ') });
  });
}

if (violations.length) {
  console.log('FAIL — ' + violations.length + ' violation(s):');
  const byKind = {};
  for (const v of violations) (byKind[v.kind] = byKind[v.kind] || []).push(v);
  for (const [k, list] of Object.entries(byKind)) {
    console.log('\n[' + k + '] ' + list.length);
    list.slice(0, 25).forEach((v) => console.log('  ' + v.file + ':' + v.line + '  ' + v.detail));
    if (list.length > 25) console.log('  ... +' + (list.length - 25) + ' more');
  }
  process.exit(1);
}
console.log('PASS — no emoji · no CDN · no broken icon classes');
