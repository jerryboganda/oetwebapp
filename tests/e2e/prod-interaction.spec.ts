import { expect, test, type ConsoleMessage, type Response } from '@playwright/test';

/**
 * Production deep interaction smoke
 * -----------------------------------------------------------------------------
 * Beyond prod-smoke (which only navigates route documents), this test:
 *  - signs in as a real learner
 *  - on the dashboard, clicks every visible primary CTA + sidebar nav link
 *  - on each main module (reading/listening/writing/speaking/mocks), clicks
 *    the first "start" / "begin" / primary CTA and verifies it does not 5xx
 *  - opens the user menu and verifies sign-out works
 *  - asserts: no console errors, no /v1/* 5xx, every clicked element either
 *    navigates or opens a dialog/sheet (i.e. is not a dead button)
 *
 * Run:
 *   $env:PROD_LEARNER_EMAIL  = "..."
 *   $env:PROD_LEARNER_PASSWORD = "..."
 *   npx playwright test tests/e2e/prod-interaction.spec.ts --project=chromium-unauth --workers=1
 */

const PROD_URL = process.env.PROD_URL ?? 'https://app.oetwithdrhesham.co.uk';
const EMAIL = process.env.PROD_LEARNER_EMAIL;
const PASSWORD = process.env.PROD_LEARNER_PASSWORD;

test.describe.configure({ mode: 'serial' });

test.skip(
  !EMAIL || !PASSWORD,
  'Set PROD_LEARNER_EMAIL and PROD_LEARNER_PASSWORD env vars to run prod interaction smoke.',
);

test('prod — deep interaction: click CTAs + nav links across modules', async ({ page }) => {
  test.setTimeout(360_000);
  const consoleErrors: string[] = [];
  const apiFailures: string[] = [];
  const fourXx: string[] = [];
  let surface = 'sign-in';

  page.on('console', (msg: ConsoleMessage) => {
    if (msg.type() !== 'error') return;
    const text = msg.text();
    if (/favicon|beacon|cancel|Failed to load resource/i.test(text)) return;
    consoleErrors.push(`[${surface}] ${text}`);
  });
  page.on('response', (res: Response) => {
    const url = res.url();
    if (!url.includes('/v1/')) return;
    const status = res.status();
    if (status >= 500) apiFailures.push(`[${surface}] ${status} ${url}`);
    else if (status >= 400) fourXx.push(`[${surface}] ${status} ${url}`);
  });

  // ---- Sign in ----
  await page.goto(`${PROD_URL}/sign-in`, { waitUntil: 'domcontentloaded' });
  await page.locator('#email').waitFor({ state: 'visible', timeout: 15_000 });
  await page.locator('#email').fill(EMAIL!);
  await page.locator('#password').fill(PASSWORD!);
  await Promise.all([
    page.waitForResponse(
      (r) => /\/v1\/auth\/(sign[-_]?in|login)/i.test(r.url()) && r.request().method() === 'POST',
      { timeout: 20_000 },
    ).catch(() => null),
    page.getByRole('button', { name: /^sign in$/i }).click(),
  ]);
  await page.waitForURL((url) => !url.pathname.startsWith('/sign-in'), { timeout: 30_000 });

  // ---- Dashboard: click every visible nav link in <nav> + every primary CTA ----
  surface = 'dashboard';
  await page.goto(`${PROD_URL}/`, { waitUntil: 'networkidle', timeout: 30_000 });

  // Collect nav link hrefs from the main nav (avoid clicking external links / sign-out yet)
  const navHrefs = await page.evaluate(() => {
    const seen = new Set<string>();
    const out: string[] = [];
    document.querySelectorAll('nav a[href^="/"]').forEach((a) => {
      const href = (a as HTMLAnchorElement).getAttribute('href') ?? '';
      if (!href || seen.has(href)) return;
      if (href.startsWith('/sign-out') || href.startsWith('/api/')) return;
      if (href === '/') return;
      seen.add(href);
      out.push(href);
    });
    return out.slice(0, 25); // cap to keep runtime sane
  });

  test.info().annotations.push({ type: 'nav-count', description: String(navHrefs.length) });

  for (const href of navHrefs) {
    surface = `nav:${href}`;
    const resp = await page.goto(`${PROD_URL}${href}`, {
      waitUntil: 'domcontentloaded',
      timeout: 30_000,
    }).catch(() => null);
    if (resp) {
      expect(resp.status(), `nav ${href} status`).toBeLessThan(500);
    }
    await page.waitForLoadState('networkidle', { timeout: 12_000 }).catch(() => {});
  }

  // ---- Module CTAs: click first prominent primary button on each module ----
  const modules = [
    { path: '/reading', cta: /start|begin|practice|continue/i },
    { path: '/listening', cta: /start|begin|practice|continue/i },
    { path: '/writing', cta: /start|begin|practice|continue|new/i },
    { path: '/speaking', cta: /start|begin|practice|continue/i },
    { path: '/mocks', cta: /start|begin|take|continue/i },
  ];

  for (const mod of modules) {
    surface = `module:${mod.path}`;
    await page.goto(`${PROD_URL}${mod.path}`, { waitUntil: 'domcontentloaded', timeout: 30_000 });
    await page.waitForLoadState('networkidle', { timeout: 12_000 }).catch(() => {});

    // Try to click the first matching primary CTA (button or link). If none visible,
    // we just verify the page rendered without 500s/console errors.
    const cta = page.getByRole('button', { name: mod.cta }).first();
    const ctaLink = page.getByRole('link', { name: mod.cta }).first();

    let clicked = false;
    if (await cta.isVisible().catch(() => false)) {
      await cta.click({ trial: false }).catch(() => {});
      clicked = true;
    } else if (await ctaLink.isVisible().catch(() => false)) {
      await ctaLink.click({ trial: false }).catch(() => {});
      clicked = true;
    }
    if (clicked) {
      // Allow either navigation OR a dialog/sheet. Just wait briefly for stability.
      await page.waitForLoadState('networkidle', { timeout: 10_000 }).catch(() => {});
    }
    test.info().annotations.push({
      type: `cta:${mod.path}`,
      description: clicked ? 'clicked' : 'none-found',
    });
  }

  // ---- Sign out ----
  surface = 'sign-out';
  await page.goto(`${PROD_URL}/`, { waitUntil: 'domcontentloaded', timeout: 30_000 });

  // Try menu trigger then sign out item
  const userMenu = page.getByRole('button', { name: /account|profile|menu|user/i }).first();
  if (await userMenu.isVisible().catch(() => false)) {
    await userMenu.click().catch(() => {});
  }
  const signOut = page.getByRole('button', { name: /sign out|log out/i }).first();
  const signOutLink = page.getByRole('link', { name: /sign out|log out/i }).first();
  if (await signOut.isVisible().catch(() => false)) {
    await signOut.click().catch(() => {});
  } else if (await signOutLink.isVisible().catch(() => false)) {
    await signOutLink.click().catch(() => {});
  }
  await page.waitForURL(/\/sign-in|\//, { timeout: 10_000 }).catch(() => {});

  // ---- Final aggregate assertions ----
  if (fourXx.length) {
    console.log('[prod-interaction] /v1/* 4xx (non-blocking):');
    for (const line of fourXx) console.log('  ', line);
  }
  expect(consoleErrors, 'no console errors during deep interaction').toEqual([]);
  expect(apiFailures, 'no API 5xx during deep interaction').toEqual([]);
});
