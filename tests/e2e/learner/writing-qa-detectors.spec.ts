import { expect, test, type Page } from '@playwright/test';
import {
  clearanceProblems, collectContainment, collectOverlap, containmentProblems, overlapProblems, probeTarget, targetProblems,
} from '../../../scripts/qa/writing-prod-qa/geometry.mjs';
import { CARD, OBSTACLES, phonePage, scoreCard } from '../../../scripts/qa/writing-prod-qa/geometry-fixtures.mjs';

// WAI-10: proves the live Writing QA harness's geometry/overlap detectors on REAL layouts. Hermetic: every page
// is built with page.setContent (no app, no auth), and the collectors + detectors are the very functions
// scripts/qa/writing-prod-qa/browser.mjs runs against production. Each bad layout must be flagged and each
// good one must pass, so a detector that silently returns nothing fails this spec.

async function overlapAt(page: Page, fraction: number) {
  return page.evaluate(collectOverlap, { content: '#main-content', obstacles: OBSTACLES, fraction });
}

test.describe('Writing QA geometry detectors (hermetic)', () => {
  test.use({ viewport: { width: 1366, height: 900 } });

  test('a contained score card passes', async ({ page }) => {
    await page.setContent(scoreCard('Occupational Therapy', 320));
    expect(containmentProblems(await page.evaluate(collectContainment, CARD))).toEqual([]);
  });

  test('text clipped by overflow-hidden is still flagged by its box geometry', async ({ page }) => {
    await page.setContent(scoreCard('Speech Pathology Occupational Therapy Long Label', 140));
    const problems = containmentProblems(await page.evaluate(collectContainment, CARD));
    expect(problems.some((p: string) => p.includes('sticks out'))).toBe(true);
    expect(problems.some((p: string) => /overflows horizontally/.test(p))).toBe(true);
  });

  test('a page that scrolls sideways is flagged', async ({ page }) => {
    await page.setContent(`${scoreCard('342/500', 300)}<div style="width:2400px">wide</div>`);
    const problems = containmentProblems(await page.evaluate(collectContainment, CARD));
    expect(problems.some((p: string) => p.startsWith('the page scrolls horizontally'))).toBe(true);
  });

  test('a missing card is reported, never passed', async ({ page }) => {
    await page.setContent('<p>no card</p>');
    expect(containmentProblems(await page.evaluate(collectContainment, CARD))).toEqual(['the results score panel was not found']);
  });
});

test.describe('Writing QA overlap detectors at phone width (hermetic)', () => {
  test.use({ viewport: { width: 390, height: 844 } });

  test('content padded clear of the bottom nav passes at the end of the page', async ({ page }) => {
    await page.setContent(phonePage({ padding: 120 }));
    const end = await overlapAt(page, 1);
    expect(end.atEnd).toBe(true);
    expect([...overlapProblems(end), ...clearanceProblems(end)]).toEqual([]);
  });

  test('the last content hidden under the bottom nav is flagged', async ({ page }) => {
    await page.setContent(phonePage({ padding: 0 }));
    const end = await overlapAt(page, 1);
    expect(overlapProblems(end, ['bottom nav']).length + clearanceProblems(end).length).toBeGreaterThan(0);
  });

  test('a floating handle over text is flagged at some scroll offset', async ({ page }) => {
    await page.setContent(phonePage({ padding: 120, handle: true }));
    const found: string[] = [];
    for (const fraction of [0, 0.25, 0.5, 0.75, 1]) found.push(...overlapProblems(await overlapAt(page, fraction), ['handle']));
    expect(found.length).toBeGreaterThan(0);
  });

  test('a control left under the nav after scrollIntoView is flagged; a clear one passes', async ({ page }) => {
    await page.setContent(phonePage({ padding: 0, lastButton: true }));
    const covered = await page.evaluate(probeTarget, { selector: '#last', index: 0, label: 'View all corrections' });
    expect(targetProblems([covered], (await overlapAt(page, 1)).obstacles).length).toBeGreaterThan(0);
    await page.setContent(phonePage({ padding: 120, lastButton: true }));
    const clear = await page.evaluate(probeTarget, { selector: '#last', index: 0, label: 'View all corrections' });
    expect(targetProblems([clear], (await overlapAt(page, 1)).obstacles)).toEqual([]);
  });
});
