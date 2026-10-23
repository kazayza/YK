// Capture a Blazor session for the E2E suite.
// Opens a visible Chrome window; YOU type the credentials, the script waits and
// then persists the session (encrypted sessionStorage + cookies) to session.json.
const H = require('./helpers');

(async () => {
  let ctx;
  try {
    ctx = await H.launch();
  } catch (e) {
    H.log('launch(channel=chrome) failed:', e.message);
    const { pw } = H;
    ctx = await pw.chromium.launchPersistentContext(H.PROFILE, {
      headless: false,
      viewport: { width: 1440, height: 900 },
    });
    ctx.setDefaultTimeout(20000);
    ctx.setDefaultNavigationTimeout(60000);
  }
  H.log('browser launched (headed)');

  const page = ctx.pages()[0] || await ctx.newPage();
  const col = H.collectors(page);

  await H.go(page, '/login', 'login');
  await H.shot(page, '00-login');

  const hasForm = await page.evaluate(() =>
    !!(document.querySelector('input[type="password"]') || document.body.innerText.includes('تسجيل الدخول')));
  H.log('login form detected:', hasForm);

  if (hasForm) {
    H.log('>>> LOGIN FORM IS OPEN — type your credentials in the Chrome window.');
    H.log('>>> Waiting up to 20 minutes for a successful login...');
    const deadline = Date.now() + 20 * 60 * 1000;
    let ok = false;
    while (Date.now() < deadline) {
      await page.waitForTimeout(1500);
      if (!page.url().includes('/login')) { ok = true; H.log('>>> LOGIN SUCCEEDED. URL =', page.url()); break; }
    }
    if (!ok) H.log('>>> LOGIN TIMEOUT — no successful login detected.');
  } else {
    H.log('already authenticated? URL =', page.url());
  }

  await H.shot(page, '01-after-login');
  await H.saveSession(ctx, page);
  const d = col.drain();
  H.log('console errors:', d.errors.length, JSON.stringify(d.errors.slice(0, 5)));
  H.log('final URL:', page.url());
  H.log('LOGIN DONE');
  await ctx.close();
  process.exit(0);
})().catch((e) => { H.log('LOGIN FATAL:', e.message, e.stack); process.exit(1); });
