// @vitest-environment node
import { existsSync } from 'node:fs';
import { chromium, type Browser, type Page } from '@playwright/test';
import {
  clearanceProblems, collectContainment, collectOverlap, containmentProblems, overlapProblems, probeTarget, targetProblems,
} from './geometry.mjs';
import { CARD, OBSTACLES, phonePage, scoreCard } from './geometry-fixtures.mjs';

// The in-page collectors only mean something in a real layout engine (jsdom has no geometry). GitHub's ubuntu
// runners ship Google Chrome, so this runs the hermetic detector fixtures (the same ones the Playwright spec
// tests/e2e/learner/writing-qa-detectors.spec.ts uses) in real Chrome inside the vitest job. Without Chrome on
// the machine it is skipped and says so; CI logs show which.
const CHROME = '/opt/google/chrome/chrome';
const hasChrome = existsSync(CHROME);
if (!hasChrome) console.warn(`geometry.browser.test: ${CHROME} not found, real-browser detector checks skipped`);

describe.skipIf(!hasChrome)('geometry collectors + detectors in real Chrome', () => {
  let browser: Browser;
  beforeAll(async () => { browser = await chromium.launch({ channel: 'chrome' }); }, 60_000);
  afterAll(async () => { await browser?.close(); });

  const at = async (width: number, height: number, html: string): Promise<Page> => {
    const page = await browser.newPage({ viewport: { width, height } });
    await page.setContent(html);
    return page;
  };
  const overlapAt = (page: Page, fraction: number) => page.evaluate(collectOverlap, { content: '#main-content', obstacles: OBSTACLES, fraction });

  it('passes a contained card and flags clipped text, tile overflow and a sideways page', async () => {
    const good = await at(1366, 900, scoreCard('Occupational Therapy', 320));
    expect(containmentProblems(await good.evaluate(collectContainment, CARD))).toEqual([]);
    const clipped = await at(1366, 900, scoreCard('Speech Pathology Occupational Therapy Long Label', 140));
    const problems = containmentProblems(await clipped.evaluate(collectContainment, CARD));
    expect(problems.some((p: string) => p.includes('sticks out'))).toBe(true);
    expect(problems.some((p: string) => p.includes('overflows horizontally'))).toBe(true);
    const wide = await at(1366, 900, `${scoreCard('342/500', 300)}<div style="width:2400px">wide</div>`);
    expect(containmentProblems(await wide.evaluate(collectContainment, CARD)).some((p: string) => p.startsWith('the page scrolls horizontally'))).toBe(true);
  }, 30_000);

  it('passes padded content and flags content under the nav or the handle', async () => {
    const padded = await at(390, 844, phonePage({ padding: 120 }));
    const end = await overlapAt(padded, 1);
    expect(end.atEnd).toBe(true);
    expect([...overlapProblems(end), ...clearanceProblems(end)]).toEqual([]);
    const tight = await at(390, 844, phonePage({ padding: 0 }));
    const tightEnd = await overlapAt(tight, 1);
    expect(overlapProblems(tightEnd, ['bottom nav']).length + clearanceProblems(tightEnd).length).toBeGreaterThan(0);
    const handle = await at(390, 844, phonePage({ padding: 120, handle: true }));
    const found: string[] = [];
    for (const fraction of [0, 0.25, 0.5, 0.75, 1]) found.push(...overlapProblems(await overlapAt(handle, fraction), ['handle']));
    expect(found.length).toBeGreaterThan(0);
  }, 30_000);

  it('flags a control left under the nav after scrollIntoView and passes a clear one', async () => {
    const covered = await at(390, 844, phonePage({ padding: 0, lastButton: true }));
    const probe: any = await covered.evaluate(probeTarget, { selector: '#last', index: 0, label: 'View all corrections' });
    expect(probe.found && !probe.topmost).toBe(true);
    expect(targetProblems([probe], (await overlapAt(covered, 1)).obstacles).length).toBeGreaterThan(0);
    const clear = await at(390, 844, phonePage({ padding: 120, lastButton: true }));
    const ok = await clear.evaluate(probeTarget, { selector: '#last', index: 0, label: 'View all corrections' });
    expect(targetProblems([ok], (await overlapAt(clear, 1)).obstacles)).toEqual([]);
  }, 30_000);
});
