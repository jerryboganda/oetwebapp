import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';

/**
 * Writing V2 - WCAG 2.2 AA scans for the high-traffic learner surfaces.
 * Tags: @writing-v2 @a11y
 *
 * Acceptance bar: zero `critical` and zero `serious` impact violations.
 * `minor` / `moderate` violations are logged for follow-up but do not fail
 * the test, matching tests/e2e/shared/accessibility.spec.ts.
 *
 * Scope: chromium-learner only. The pages are public to authenticated
 * learners; running on the chromium shard is sufficient signal.
 */

const PAGES: Array<{ path: string; label: string; headingPattern: RegExp }> = [
  {
    path: '/writing/welcome',
    label: 'writing welcome',
    headingPattern: /welcome . let'?s set up your writing pathway/i,
  },
  {
    path: '/writing/diagnostic',
    label: 'writing diagnostic briefing',
    headingPattern: /a 50-minute baseline of your six writing criteria/i,
  },
  {
    path: '/writing/canon',
    label: 'writing canon library',
    headingPattern: /dr ahmed's writing rules in one place/i,
  },
  {
    path: '/writing/skill-tree',
    label: 'writing skill tree',
    headingPattern: /w1-w8 . the eight skills every oet letter rests on/i,
  },
  {
    path: '/writing/stats',
    label: 'writing stats dashboard',
    headingPattern: /track every dimension of your writing progress/i,
  },
];

async function scanPage(page: Page, label: string, testInfo: import('@playwright/test').TestInfo) {
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']).analyze();

  // Attach the full violation report so QA can triage minor/moderate without
  // re-running the suite.
  await testInfo.attach(`axe-report-${label.replace(/\s+/g, '-')}.json`, {
    body: JSON.stringify(results, null, 2),
    contentType: 'application/json',
  });

  const blocking = results.violations.filter(
    (v) => v.impact === 'critical' || v.impact === 'serious',
  );

  // Build a readable summary for failure output. Each violation has an id,
  // impact, help text, and one or more nodes; surface them all so the fix
  // path is obvious from CI logs alone.
  if (blocking.length > 0) {
    const summary = blocking
      .map((v) => {
        const nodes = v.nodes
          .slice(0, 3)
          .map((n) => `    - ${n.target.join(' ')}: ${n.failureSummary?.split('\n').join(' | ') ?? ''}`)
          .join('\n');
        return `  [${v.impact}] ${v.id} - ${v.help}\n${nodes}`;
      })
      .join('\n\n');
    expect(
      blocking,
      `Found ${blocking.length} blocking (critical/serious) axe violations on ${label}:\n${summary}`,
    ).toEqual([]);
  }
}

test.describe('Writing V2 a11y @writing-v2 @a11y', () => {
  test.describe.configure({ mode: 'serial' });

  for (const target of PAGES) {
    test(`${target.label} has no critical/serious WCAG AA violations`, async (
      { page },
      testInfo,
    ) => {
      if (testInfo.project.name !== 'chromium-learner') {
        test.skip();
      }

      await page.goto(target.path, { waitUntil: 'domcontentloaded' });
      // Wait for at least one heading matching the page identity so the scan
      // runs against fully-mounted content rather than the loading state.
      await expect(
        page.getByRole('heading', { name: target.headingPattern }).first(),
      ).toBeVisible({ timeout: 30_000 });

      await scanPage(page, target.label, testInfo);
    });
  }
});
