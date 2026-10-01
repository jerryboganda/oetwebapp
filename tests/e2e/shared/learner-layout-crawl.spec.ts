import { appendFileSync, existsSync, readdirSync } from 'node:fs';
import path from 'node:path';
import { expect, test, type APIRequestContext, type Page, type TestInfo } from '@playwright/test';
import { waitForSessionGuardToClear } from '../fixtures/auth';
import { recoverBrowserSession } from '../fixtures/auth-bootstrap';

/**
 * Learner layout crawl: every static learner route plus seeded results pages,
 * measured at four widths for text that is clipped by an overflow-hidden
 * ancestor, spills past its bordered tile or card, or runs off the viewport.
 * visual-qa.spec.ts checks page-level overflow and cannot see any of these.
 *
 * Report-only by default. Findings go to the job summary and a JSON
 * attachment per chunk. Routes in ENFORCED_ROUTES fail the build, and each UI
 * wave adds the routes it fixed, so the gate only ratchets forward.
 *
 * Each route loads once and is measured at every width by resizing. The whole
 * file runs in one worker, in order, because the learner account allows a
 * single active session.
 */

test.describe.configure({ mode: 'default' });

const CRAWL_WIDTHS = [360, 768, 1024, 1440] as const;
const SCREENSHOT_WIDTHS = new Set<number>([360, 1440]);
const CHUNK_SIZE = 30;

/** Demo-seeded (SeedData.DemoUserData) results pages for learner@oet-prep.dev. */
const SEEDED_ROUTES = [
  '/writing/submissions/11111111-1111-1111-1111-111111111111/results',
  '/writing/submissions/11111111-1111-1111-1111-111111111111',
  '/speaking/results/sa-001',
  '/listening/results/la-001',
  '/mocks/report/mock-report-001',
];

/** Routes whose layout defects fail the build. Each wave adds what it fixed. */
const ENFORCED_ROUTES = new Set<string>([
  // Wave 1: ResultsScorePanel tiles (8 labels/values spilled 61-71px at 1024/1440).
  '/mocks/report/mock-report-001',
]);

/** Hub routes for the right-to-left (Arabic) pass. */
const RTL_ROUTES = [
  '/dashboard', '/listening', '/reading', '/writing', '/speaking', '/mocks', '/progress', '/billing', '/settings',
  '/subscriptions', SEEDED_ROUTES[0],
];

type DefectKind = 'clipped' | 'spill' | 'offscreen' | 'page-overflow';
type Defect = { kind: DefectKind; text: string; where: string; px: number };
type RouteReport = { route: string; landed: string; defects: (Defect & { widths: number[] })[]; truncated: number };

/** Every learner page without a dynamic segment, read from app/(learner). */
function staticLearnerRoutes(dir = path.join(process.cwd(), 'app', '(learner)'), prefix = ''): string[] {
  const routes = existsSync(path.join(dir, 'page.tsx')) ? [prefix || '/'] : [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (!entry.isDirectory() || /^[[_@]/.test(entry.name)) continue;
    const segment = entry.name.startsWith('(') ? '' : `/${entry.name}`;
    routes.push(...staticLearnerRoutes(path.join(dir, entry.name), `${prefix}${segment}`));
  }
  return routes;
}

/**
 * Runs in the page. For each visible text node, walks its ancestors for the
 * nearest horizontal clip or scroll container and the nearest box with left
 * and right borders (a tile, card or chip). Reports text that is cut off,
 * spills out of its box, or leaves the viewport. Deliberate ellipsis or
 * line-clamp truncation is counted but not reported. Text in SVG, aria-hidden,
 * sr-only or hidden subtrees is ignored.
 */
function detectLayoutDefects(): { defects: Defect[]; truncated: number } {
  const root = document.getElementById('main-content') ?? document.body;
  const styles = new Map<Element, CSSStyleDeclaration>();
  const style = (el: Element) => {
    let s = styles.get(el);
    if (!s) styles.set(el, (s = getComputedStyle(el)));
    return s;
  };
  const describe = (el: Element) => {
    const tag = el.tagName.toLowerCase();
    const classes = typeof el.className === 'string' ? el.className.split(/\s+/).filter(Boolean).slice(0, 4) : [];
    const testId = el.closest('[data-testid]')?.getAttribute('data-testid');
    return `${testId ? `[${testId}] ` : ''}${tag}${classes.length ? `.${classes.join('.')}` : ''}`;
  };
  const innerEdges = (el: Element) => {
    const box = el.getBoundingClientRect();
    const s = style(el);
    return { left: box.left + parseFloat(s.borderLeftWidth), right: box.right - parseFloat(s.borderRightWidth), width: box.width };
  };

  const defects = new Map<string, Defect>();
  const add = (kind: DefectKind, text: string, el: Element, px: number) => {
    const snippet = text.trim().replace(/\s+/g, ' ').slice(0, 48);
    const where = describe(el);
    const key = `${kind}|${snippet}|${where}`;
    if ((defects.get(key)?.px ?? 0) < px) defects.set(key, { kind, text: snippet, where, px: Math.round(px) });
  };

  let truncated = 0;
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    const text = node.textContent ?? '';
    const parent = node.parentElement;
    if (text.trim().length < 2 || !parent) continue;
    if (parent.closest('svg, [aria-hidden="true"], .sr-only, script, style, noscript, textarea, select, option')) continue;
    if (style(parent).visibility !== 'visible') continue;

    const range = document.createRange();
    range.selectNodeContents(node);
    const rects = Array.from(range.getClientRects()).filter((r) => r.width > 0 && r.height > 0);
    if (rects.length === 0) continue;
    const left = Math.min(...rects.map((r) => r.left));
    const right = Math.max(...rects.map((r) => r.right));

    // Depth of the nearest clip, inner scroller and bordered box; -1 = none.
    let clip: Element | null = null;
    let tile: Element | null = null;
    let scrollerDepth = -1;
    let tileDepth = -1;
    let ignore = false;
    let depth = 0;
    for (let el: Element | null = parent; el && el !== document.body && !ignore; el = el.parentElement, depth += 1) {
      const s = style(el);
      if (s.display === 'none' || s.opacity === '0') ignore = true;
      // #main-content is the page scroller; only inner scrollers make text scrollable.
      if (!clip && scrollerDepth < 0 && el !== root) {
        if (s.overflowX === 'auto' || s.overflowX === 'scroll') scrollerDepth = depth;
        else if (s.overflowX === 'hidden' || s.overflowX === 'clip') {
          if (s.textOverflow === 'ellipsis' || (s.webkitLineClamp && s.webkitLineClamp !== 'none')) {
            truncated += 1;
            ignore = true;
          }
          clip = el;
        }
      }
      if (!tile && el !== root && parseFloat(s.borderLeftWidth) > 0 && parseFloat(s.borderRightWidth) > 0) {
        tile = el;
        tileDepth = depth;
      }
    }
    if (ignore) continue;

    if (clip) {
      const edges = innerEdges(clip);
      const cut = Math.max(right - edges.right, edges.left - left);
      if (edges.width > 2 && cut > 1) {
        add('clipped', text, clip, cut);
        continue;
      }
    }
    // A scroller between the text and its box means the text scrolls inside it.
    if (tile && (scrollerDepth < 0 || scrollerDepth > tileDepth)) {
      const edges = innerEdges(tile);
      const spill = Math.max(right - edges.right, edges.left - left);
      if (spill > 1) {
        add('spill', text, tile, spill);
        continue;
      }
    }
    if (scrollerDepth < 0 && right > window.innerWidth + 1) add('offscreen', text, parent, right - window.innerWidth);
  }

  const doc = document.scrollingElement ?? document.documentElement;
  const main = document.getElementById('main-content');
  const pageOverflow = Math.max(doc.scrollWidth - doc.clientWidth, main ? main.scrollWidth - main.clientWidth : 0);
  if (pageOverflow > 1) {
    defects.set('page', { kind: 'page-overflow', text: '', where: main ? 'main-content' : 'document', px: pageOverflow });
  }

  return { defects: Array.from(defects.values()).sort((a, b) => b.px - a.px).slice(0, 40), truncated };
}

/** Waits for the page, closes the onboarding tour, and scrolls through it so in-view reveals fire. */
async function settle(page: Page) {
  await page.getByRole('main').first().waitFor({ state: 'visible', timeout: 30_000 }).catch(() => undefined);
  await Promise.race([page.waitForLoadState('networkidle'), page.waitForTimeout(4_000)]).catch(() => undefined);
  await page.locator('.driver-popover-close-btn').click({ timeout: 1_000 }).catch(() => undefined);
  await page.evaluate(async () => {
    const main = document.getElementById('main-content');
    const scroller = main && main.scrollHeight > main.clientHeight ? main : document.scrollingElement;
    if (!scroller) return;
    const frame = () => new Promise((resolve) => requestAnimationFrame(resolve));
    // Capped: infinite-scroll pages keep growing.
    for (let y = 0, steps = 0; y < scroller.scrollHeight && steps < 40; y += Math.max(400, scroller.clientHeight), steps += 1) {
      scroller.scrollTop = y;
      await frame();
    }
    scroller.scrollTop = 0;
  });
  await page.waitForTimeout(600);
}

async function crawlRoute(page: Page, request: APIRequestContext, route: string, testInfo: TestInfo, widths: readonly number[]) {
  await page.setViewportSize({ width: widths[0], height: 800 });
  await page.goto(route, { waitUntil: 'domcontentloaded', timeout: 60_000 });
  await waitForSessionGuardToClear(page, { recover: () => recoverBrowserSession(page, request, 'learner', route) });
  // Another learner test may have taken the single active session. Never
  // measure the sign-in page in place of the route: retry, then report it.
  for (let attempt = 0; new URL(page.url()).pathname.startsWith('/sign-in'); attempt += 1) {
    if (attempt === 2) throw new Error(`${route} kept redirecting to /sign-in`);
    await recoverBrowserSession(page, request, 'learner', route);
  }
  await settle(page);

  const landed = new URL(page.url()).pathname;
  const byKey = new Map<string, Defect & { widths: number[] }>();
  let truncated = 0;
  for (const width of widths) {
    await page.setViewportSize({ width, height: width < 768 ? 800 : 900 });
    await page.waitForTimeout(400);
    const result = await page.evaluate(detectLayoutDefects);
    truncated = Math.max(truncated, result.truncated);
    for (const defect of result.defects) {
      const key = `${defect.kind}|${defect.text}|${defect.where}`;
      const seen = byKey.get(key);
      if (seen) {
        seen.widths.push(width);
        seen.px = Math.max(seen.px, defect.px);
      } else {
        byKey.set(key, { ...defect, widths: [width] });
      }
    }
    if (SCREENSHOT_WIDTHS.has(width)) {
      await testInfo.attach(`learner${landed.replace(/\//g, '_')}@${width}.jpg`, {
        body: await page.screenshot({ type: 'jpeg', quality: 60 }),
        contentType: 'image/jpeg',
      });
    }
  }
  const defects = Array.from(byKey.values()).sort((a, b) => b.px - a.px);
  return { route, landed, defects, truncated } satisfies RouteReport;
}

function summarize(title: string, reports: RouteReport[], loadErrors: string[]) {
  const lines = [`### ${title}`, '', '| Route | Defects | Worst |', '|---|---|---|'];
  for (const r of reports.filter((x) => x.defects.length > 0).sort((a, b) => b.defects.length - a.defects.length)) {
    const worst = r.defects[0];
    const text = worst.text.replace(/\|/g, '/');
    lines.push(`| \`${r.landed}\` | ${r.defects.length} | ${worst.kind} ${worst.px}px "${text}" @ ${worst.widths.join('/')} |`);
  }
  const clean = reports.filter((x) => x.defects.length === 0).length;
  lines.push('', `${clean} of ${reports.length} routes clean.`);
  if (loadErrors.length) lines.push('', `Failed to load: ${loadErrors.join(', ')}`);
  return `${lines.join('\n')}\n\n`;
}

const crawled = new Set<string>();

async function runCrawl(
  page: Page,
  request: APIRequestContext,
  testInfo: TestInfo,
  title: string,
  routes: string[],
  widths: readonly number[],
) {
  test.skip(testInfo.project.name !== 'chromium-learner', 'learner layout crawl runs on chromium-learner only');
  test.setTimeout(routes.length * 40_000 + 60_000);
  await page.addInitScript(() => window.sessionStorage.setItem('oet_app_promo_dismissed', 'true'));
  page.on('dialog', (dialog) => {
    (dialog.type() === 'beforeunload' ? dialog.accept() : dialog.dismiss()).catch(() => undefined);
  });
  await recoverBrowserSession(page, request, 'learner', routes[0]);

  const reports: RouteReport[] = [];
  const failures: string[] = [];
  const loadErrors: string[] = [];
  for (const route of routes) {
    let report: RouteReport;
    try {
      report = await crawlRoute(page, request, route, testInfo, widths);
    } catch (error) {
      loadErrors.push(route);
      if (ENFORCED_ROUTES.has(route)) failures.push(`${route} failed to load: ${String(error).slice(0, 200)}`);
      continue;
    }
    // Redirects land several routes on one page; measure it once per pass.
    const key = `${widths.join(',')}|${testInfo.project.use.locale ?? ''}|${report.landed}`;
    if (crawled.has(key)) continue;
    crawled.add(key);
    reports.push(report);
    if (ENFORCED_ROUTES.has(route) && report.defects.length > 0) {
      const found = report.defects.map((d) => `${d.kind} ${d.px}px "${d.text}" (${d.where}) @ ${d.widths.join('/')}`);
      failures.push(`${route}: ${found.join('; ')}`);
    }
  }

  await testInfo.attach(`${title.replace(/\W+/g, '-')}.json`, {
    body: JSON.stringify({ reports, loadErrors }, null, 2),
    contentType: 'application/json',
  });
  if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, summarize(title, reports, loadErrors));
  expect(failures, failures.join('\n')).toEqual([]);
}

const allRoutes = [...SEEDED_ROUTES, ...staticLearnerRoutes()];
const chunks = Array.from({ length: Math.ceil(allRoutes.length / CHUNK_SIZE) }, (_, i) =>
  allRoutes.slice(i * CHUNK_SIZE, (i + 1) * CHUNK_SIZE));

test.describe('Learner layout crawl @visual', () => {
  chunks.forEach((routes, index) => {
    const title = `Learner layout crawl ${index + 1}/${chunks.length}`;
    test(`${title} (${routes.length} routes)`, async ({ page, request }, testInfo) => {
      await runCrawl(page, request, testInfo, title, routes, CRAWL_WIDTHS);
    });
  });
});

test.describe('Learner layout crawl, Arabic RTL @visual', () => {
  test.use({ locale: 'ar' });

  test('RTL hub routes', async ({ page, request }, testInfo) => {
    await runCrawl(page, request, testInfo, 'Learner layout crawl RTL', RTL_ROUTES, [360, 1440]);
  });
});
