import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { recoverBrowserSession } from '../fixtures/auth-bootstrap';
import { waitForSessionGuardToClear } from '../fixtures/auth';

async function expectNoSeriousAxeViolations(page: Page) {
  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa'])
    .analyze();

  const blocking = results.violations.filter((violation) => ['critical', 'serious'].includes(violation.impact ?? ''));
  expect(blocking, 'Critical and serious axe violations should remain empty').toEqual([]);
}

/**
 * 2026-05-27 OET Sample-Test Alignment — E2E acceptance suite.
 *
 * Covers the directive from "OET Project Development Requirements" — the
 * candidate-facing workspace is collapsed so it mirrors the official OET
 * computer-based sample test at oet.com/ready/sample-tests/oet-test-on-computer.
 *
 * Verifies:
 *  1. Sidebar trims to 7 learner items (Dashboard, Listening, Reading,
 *     Writing, Mocks, Progress, Billing).
 *  2. Listening hub shows exactly 4 candidate-facing cards and the legacy
 *     "Your audio context" panel is gone.
 *  3. Reading hub shows exactly 4 candidate-facing cards.
 *  4. Mocks page surfaces the 4 canonical mock categories.
 *  5. Reading exam renders the OET-style split-screen at desktop viewports.
 *  6. The "no mocks inside Listening / Reading" rule is enforced via
 *     redirects from /listening/mocks, /listening/exam, /reading/exam.
 */

test.describe('OET sample-test alignment — learner workspace', () => {
  test.beforeEach(async ({ page, request }) => {
    await page.goto('/');
    await waitForSessionGuardToClear(page, {
      recover: () => recoverBrowserSession(page, request, 'learner', '/'),
    });
  });

  test('sidebar exposes the canonical learner nav items', async ({ page }) => {
    const nav = page.getByRole('navigation', { name: /main navigation/i });
    await expect(nav).toBeVisible();

    // Per the 2026-05-27 alignment + the follow-up that restored Recalls to the
    // learner study menu, the learner sidebar shows exactly these entries.
    const expected = ['Dashboard', 'Listening', 'Reading', 'Writing', 'Mocks', 'Recalls', 'Progress', 'Billing'];
    for (const label of expected) {
      await expect(nav.getByRole('link', { name: new RegExp(`^${label}$`, 'i') })).toBeVisible();
    }

    // Items intentionally hidden from the learner workspace (still reachable by
    // URL / visible to admin + tutor workspaces).
    const hiddenFromLearner = ['Study Plan', 'Speaking', 'Readiness', 'History', 'Escalations', 'Grammar', 'Live Classes', 'Video Lessons', 'Strategies', 'AI Conversation'];
    for (const label of hiddenFromLearner) {
      await expect(nav.getByRole('link', { name: new RegExp(`^${label}$`, 'i') })).toHaveCount(0);
    }
  });

  test('Listening hub shows exactly four 4-card hub entries in canonical order', async ({ page }) => {
    await page.goto('/listening');

    const grid = page.getByTestId('listening-hub-cards');
    await expect(grid).toBeVisible();

    const cards = grid.locator('a[data-testid^="listening-hub-card-"]');
    await expect(cards).toHaveCount(4);

    await expect(cards.nth(0)).toHaveAttribute('href', '/listening/practice/a');
    await expect(cards.nth(1)).toHaveAttribute('href', '/listening/practice/b');
    await expect(cards.nth(2)).toHaveAttribute('href', '/listening/practice/c');
    await expect(cards.nth(3)).toHaveAttribute('href', '/listening/exam');

    // Owner directive §3 — the candidate-facing audio-context panel is removed.
    await expect(page.getByText(/your audio context/i)).toHaveCount(0);

    // Owner directive §3 — legacy "Coming Soon" drill/lesson/strategy chips are gone.
    await expect(page.getByText(/coming soon/i, { exact: false })).toHaveCount(0);
  });

  test('Reading hub shows exactly four 4-card hub entries in canonical order', async ({ page }) => {
    await page.goto('/reading');

    const grid = page.getByTestId('reading-hub-cards');
    await expect(grid).toBeVisible();

    const cards = grid.locator('a[data-testid^="reading-hub-card-"]');
    await expect(cards).toHaveCount(4);

    await expect(cards.nth(0)).toHaveAttribute('href', '/reading/parts/a');
    await expect(cards.nth(1)).toHaveAttribute('href', '/reading/parts/b');
    await expect(cards.nth(2)).toHaveAttribute('href', '/reading/parts/c');
    await expect(cards.nth(3)).toHaveAttribute('href', '/reading/exam');

    // Owner directive §5 — Reading must not look like a package marketplace.
    await expect(page.getByText(/view packages/i)).toHaveCount(0);
    await expect(page.getByText(/recent mock reports/i)).toHaveCount(0);
  });

  test('Mocks page surfaces the four canonical mock categories', async ({ page }) => {
    await page.goto('/mocks');

    const categories = page.getByTestId('mocks-categories');
    await expect(categories).toBeVisible();

    await expect(page.getByTestId('mocks-cat-listening')).toBeVisible();
    await expect(page.getByTestId('mocks-cat-reading')).toBeVisible();
    await expect(page.getByTestId('mocks-cat-writing')).toBeVisible();
    await expect(page.getByTestId('mocks-cat-combined')).toBeVisible();
  });

  test('Reading exam runs in the stacked passage-and-questions layout at desktop viewports', async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    // Probe a known-published paper from the Reading hub. The dispatcher route
    // surfaces eligible papers and clicking one boots the player.
    await page.goto('/reading/parts/a');

    // If at least one paper renders, follow it into the player to assert the
    // stacked layout. Otherwise this path can't be verified end-to-end
    // because no content was seeded — skip rather than fail.
    const startButton = page.getByRole('button', { name: /start part a practice/i }).first();
    if (await startButton.isVisible({ timeout: 5_000 }).catch(() => false)) {
      await startButton.click();
      await expect(page.getByTestId('reading-stacked-scroll')).toBeVisible({ timeout: 10_000 });
      await expect(page.getByTestId('reading-group').first()).toBeVisible();
      await expect(page.getByTestId('reading-question-card').first()).toBeVisible();
    } else {
      test.info().annotations.push({ type: 'skip', description: 'No published Reading paper available; stacked layout path not exercised.' });
    }
  });

  test('full-mock entry points outside /mocks redirect into the canonical Mocks tab', async ({ page }) => {
    // Owner directive §6 — full mocks must live only in /mocks.
    await page.goto('/listening/exam');
    await expect(page).toHaveURL(/\/mocks(?:\?|$)/);
    expect(page.url()).toContain('subtest=listening');

    await page.goto('/reading/exam');
    await expect(page).toHaveURL(/\/mocks(?:\?|$)/);
    expect(page.url()).toContain('subtest=reading');

    await page.goto('/listening/mocks');
    await expect(page).toHaveURL(/\/mocks(?:\?|$)/);
    expect(page.url()).toContain('subtest=listening');
  });

  test('mocks page honours the subtest deep-link with a scoped banner and clear control', async ({ page }) => {
    // Regression: the `?subtest=` deep-link from each module's "Full Exam" surface was previously
    // ignored, dumping learners on the full unfiltered mock center. The page must now scope the view.
    await page.goto('/mocks?subtest=listening');

    const banner = page.getByTestId('mocks-scope-banner');
    await expect(banner).toBeVisible();
    await expect(banner).toContainText(/Full Listening Mock/i);

    // The matching category card is marked active.
    await expect(page.getByTestId('mocks-cat-listening')).toHaveAttribute('aria-current', 'true');

    // The escape hatch clears the scope back to the full center.
    await page.getByTestId('mocks-scope-clear').click();
    await expect(page).toHaveURL(/\/mocks(?:$|\?)/);
    expect(page.url()).not.toContain('subtest=');
    await expect(page.getByTestId('mocks-scope-banner')).toHaveCount(0);
  });
});
