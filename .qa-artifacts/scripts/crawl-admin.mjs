// Admin panel QA crawler. Runs against production.
// Captures per-route: screenshot, console errors/warnings, network failures, axe-core a11y violations, basic DOM stats.
import { chromium } from 'playwright';
import fs from 'node:fs';
import path from 'node:path';

const BASE = process.env.QA_BASE || 'https://app.oetwithdrhesham.co.uk';
const EMAIL = process.env.QA_EMAIL || 'manwara575@gmail.com';
const PASSWORD = process.env.QA_PASSWORD || '12345678';
const OUT = '/workspace/.qa-artifacts';
const SHOTS = path.join(OUT, 'screenshots');
const REPORT = path.join(OUT, 'reports', 'crawl.json');
const MAX_ROUTES = parseInt(process.env.QA_MAX_ROUTES || '500', 10);
const ROUTE_TIMEOUT_MS = parseInt(process.env.QA_ROUTE_TIMEOUT || '25000', 10);

const slug = (u) => u.replace(/^https?:\/\/[^/]+/, '').replace(/[^a-z0-9]+/gi, '_').replace(/^_+|_+$/g, '') || 'root';

async function ensureLoggedIn(page) {
  await page.goto(`${BASE}/sign-in`, { waitUntil: 'domcontentloaded', timeout: 30000 });
  // Email field
  const emailSel = 'input[type="email"], input[name="email"], input[id*="email" i]';
  const passSel  = 'input[type="password"], input[name="password"], input[id*="password" i]';
  await page.waitForSelector(emailSel, { timeout: 15000 });
  await page.fill(emailSel, EMAIL);
  await page.fill(passSel, PASSWORD);
  // submit
  const submit = page.locator('button[type="submit"]').first();
  await Promise.all([
    page.waitForLoadState('networkidle', { timeout: 30000 }).catch(() => {}),
    submit.click({ timeout: 10000 }),
  ]);
  // Wait briefly for redirect
  await page.waitForTimeout(2500);
  // Try to land on admin
  await page.goto(`${BASE}/admin`, { waitUntil: 'domcontentloaded', timeout: 30000 });
  const url = page.url();
  if (url.includes('/sign-in')) {
    throw new Error(`Login failed — still on ${url}. Check credentials or MFA gate.`);
  }
  return url;
}

async function discoverAdminLinks(page) {
  // Pull every <a href="/admin..."> on the page (covers sidebar + any in-page links)
  const links = await page.$$eval('a[href^="/admin"], a[href^="/admin/"]', (as) =>
    Array.from(new Set(as.map((a) => a.getAttribute('href')).filter(Boolean))),
  );
  // Also try opening hidden sidebar groups by clicking expanders if present
  return Array.from(new Set(links));
}

async function getAxe(page) {
  // Inject axe-core from CDN once
  await page.addScriptTag({ url: 'https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js' }).catch(() => null);
  const r = await page.evaluate(async () => {
    // @ts-ignore
    if (!window.axe) return { error: 'axe-not-loaded' };
    try {
      // @ts-ignore
      const r = await window.axe.run(document, { resultTypes: ['violations'] });
      return {
        violations: r.violations.map((v) => ({
          id: v.id, impact: v.impact, help: v.help,
          nodes: v.nodes.length,
          sample: v.nodes[0]?.target?.[0] || null,
        })),
      };
    } catch (e) {
      return { error: String(e) };
    }
  });
  return r;
}

async function crawl() {
  fs.mkdirSync(SHOTS, { recursive: true });
  fs.mkdirSync(path.dirname(REPORT), { recursive: true });
  const browser = await chromium.launch({ headless: true });
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, ignoreHTTPSErrors: true });
  const page = await ctx.newPage();

  console.log('[crawler] logging in…');
  const landedUrl = await ensureLoggedIn(page);
  console.log(`[crawler] landed on ${landedUrl}`);

  // First-pass discovery
  const first = await discoverAdminLinks(page);
  console.log(`[crawler] discovered ${first.length} admin links from /admin`);

  // Expand: visit /admin/content to get child-section links, then merge
  const seeds = ['/admin', '/admin/content', '/admin/billing', '/admin/users', '/admin/review-ops', '/admin/ai-config', '/admin/ai-providers', '/admin/ai-usage', '/admin/rulebooks', '/admin/audit-logs', '/admin/settings', '/admin/freeze', '/admin/flags', '/admin/webhooks', '/admin/notifications', '/admin/escalations', '/admin/marketplace-review', '/admin/private-speaking', '/admin/launch-readiness', '/admin/alerts', '/admin/analytics/quality', '/admin/community', '/admin/criteria', '/admin/signup-catalog', '/admin/writing/options', '/admin/writing/ai-draft'];
  const found = new Set(first);
  for (const s of seeds) {
    try {
      await page.goto(`${BASE}${s}`, { waitUntil: 'domcontentloaded', timeout: 20000 });
      await page.waitForTimeout(700);
      const more = await discoverAdminLinks(page);
      for (const m of more) found.add(m);
    } catch (e) { /* swallow */ }
  }
  // Normalise: keep paths (drop query/hash, drop dynamic detail pages we can't fill)
  const norm = (h) => (h || '').split('#')[0].split('?')[0];
  const routes = Array.from(found).map(norm).filter((h) => h.startsWith('/admin')).filter((h) => !/\/(new|edit|create)$/i.test(h));
  routes.sort();
  console.log(`[crawler] will visit ${routes.length} unique admin routes (capped at ${MAX_ROUTES})`);
  const toVisit = routes.slice(0, MAX_ROUTES);

  const findings = [];
  for (let i = 0; i < toVisit.length; i++) {
    const url = toVisit[i];
    const full = `${BASE}${url}`;
    const t0 = Date.now();
    const consoleMsgs = [];
    const netFails = [];
    const onConsole = (m) => {
      const t = m.type();
      if (t === 'error' || t === 'warning') consoleMsgs.push({ type: t, text: m.text().slice(0, 500) });
    };
    const onResponse = (resp) => {
      const s = resp.status();
      if (s >= 400) netFails.push({ status: s, url: resp.url().slice(0, 300), method: resp.request().method() });
    };
    page.on('console', onConsole);
    page.on('response', onResponse);

    let status = 'ok', err = null, axeRes = null, title = '', h1 = '', mainText = '';
    try {
      const resp = await page.goto(full, { waitUntil: 'domcontentloaded', timeout: ROUTE_TIMEOUT_MS });
      await page.waitForLoadState('networkidle', { timeout: 8000 }).catch(() => {});
      // Wait an extra moment for client hydration / data fetches
      await page.waitForTimeout(1200);
      title = await page.title().catch(() => '');
      h1 = await page.locator('h1').first().textContent({ timeout: 1500 }).catch(() => '') || '';
      mainText = (await page.locator('main, [role="main"]').first().textContent({ timeout: 1500 }).catch(() => '') || '').replace(/\s+/g, ' ').trim().slice(0, 400);
      axeRes = await getAxe(page).catch((e) => ({ error: String(e) }));
      // Detect "blank page" or "error page" heuristics
      const finalUrl = page.url();
      if (finalUrl.includes('/sign-in')) status = 'redirected-to-signin';
      else if (resp && resp.status() >= 400) status = `http-${resp.status()}`;
      else if (!h1 && mainText.length < 30) status = 'possibly-blank';
      else if (/something went wrong|application error|client-side exception|error 500|error 404|not authorized|access denied/i.test(mainText)) status = 'error-content';
      const png = path.join(SHOTS, slug(url) + '.png');
      await page.screenshot({ path: png, fullPage: true }).catch(() => {});
    } catch (e) {
      status = 'navigation-failed';
      err = String(e).slice(0, 400);
    } finally {
      page.off('console', onConsole);
      page.off('response', onResponse);
    }

    findings.push({
      url, status, err, title, h1, snippet: mainText,
      durMs: Date.now() - t0,
      consoleErrors: consoleMsgs.filter((m) => m.type === 'error').length,
      consoleWarnings: consoleMsgs.filter((m) => m.type === 'warning').length,
      consoleSample: consoleMsgs.slice(0, 5),
      networkFailures: netFails.length,
      networkSample: netFails.slice(0, 8),
      axeViolations: (axeRes && axeRes.violations) ? axeRes.violations.length : (axeRes?.error ? -1 : 0),
      axeSample: axeRes?.violations?.slice(0, 5) || [],
    });
    if ((i + 1) % 10 === 0 || i + 1 === toVisit.length) {
      console.log(`[crawler] ${i + 1}/${toVisit.length} done`);
      fs.writeFileSync(REPORT, JSON.stringify({ base: BASE, routes: toVisit, findings, partial: i + 1 < toVisit.length }, null, 2));
    }
  }
  fs.writeFileSync(REPORT, JSON.stringify({ base: BASE, routes: toVisit, findings, partial: false, generatedAt: new Date().toISOString() }, null, 2));
  console.log(`[crawler] done. report: ${REPORT}`);
  await browser.close();
}

crawl().catch((e) => { console.error(e); process.exit(1); });
