import { expect, test, type Locator, type Page } from '@playwright/test';

// Launch handoff UI-1 (2 Oct 2026): on desktop the Writing result card must
// hold all of its text — no label or value may spill out of the card or out of
// a stat tile, including long profession labels. The card is overflow-hidden,
// so this measures box geometry rather than visibility (clipping would hide a
// spill). Hermetic: every Writing read of the result page is route-mocked;
// only the learner session comes from the auth setup project.

const SUBMISSION_ID = 'e2e-results-layout';
const LONG_LABEL = 'Occupational Therapy';
const LONG_VALUE = 'Speech Pathology';

const criterion = (criterionCode: string, score: number, maximumScore: number) => ({
  criterionCode,
  score,
  maximumScore,
  strengthObservation: '',
  limitationObservation: '',
  evidence: [],
  improvementAction: `Next step for ${criterionCode}.`,
});

const correction = (n: number) => ({
  id: `e2e-err-${n}`,
  location: null,
  candidateWording: `the patient have ${n} symptoms`,
  correction: `the patient has ${n} symptoms`,
  category: 'grammar',
  ruleSource: `R12.${n}`,
  whyItMatters: 'Subject-verb agreement.',
  severity: n === 1 ? 'critical' : 'minor',
  confidence: 'high',
  primaryCriterionCode: 'language',
  secondaryCriterionCodes: [],
  startOffset: n,
  endOffset: n + 10,
});

/** A real-shaped graded result: eight corrections so "View all corrections" exists. */
const RESULT_FIXTURE = {
  submission: {
    id: SUBMISSION_ID,
    userId: 'e2e-learner',
    scenarioId: 'e2e-scenario',
    mode: 'practice',
    letterContent: 'Dear Dr Smith,\n\nI am writing to refer Ms Lucy Hall for assessment.\n\nYours sincerely,\nDoctor',
    contentHash: 'e2e',
    wordCount: 180,
    timeSpentSeconds: 2400,
    startedAt: '2026-10-02T09:00:00Z',
    submittedAt: '2026-10-02T09:40:00Z',
    isRevision: false,
    originalSubmissionId: null,
    status: 'graded',
    gradingTier: 'standard',
    inputSource: 'editor',
  },
  grade: {
    id: 'e2e-grade',
    submissionId: SUBMISSION_ID,
    c1Purpose: 3, c2Content: 6, c3Conciseness: 5, c4Genre: 6, c5Organisation: 6, c6Language: 5,
    rawTotal: 31,
    estimatedBand: 31,
    bandLabel: 'B',
    perCriterion: {},
    topThreePriorities: [],
    confidenceFlag: 'high',
    modelUsed: 'e2e',
    canonVersion: 'e2e',
    canonViolations: [],
    gradedAt: '2026-10-02T09:41:00Z',
  },
  report: {
    id: 'e2e-report',
    submissionId: SUBMISSION_ID,
    status: 'CandidateReady',
    profession: 'occupational-therapy',
    letterType: 'routine_referral',
    rulePackVersion: 'e2e',
    modelVersion: 'e2e',
    calibrationSetVersion: 'e2e',
    estimatedPracticeScore: 382,
    scoreLabel: `AI Estimated Practice Score — not an official OET result · ${LONG_LABEL}`,
    gradeBand: 'Grade B',
    scoreRange: null,
    confidenceLabel: LONG_VALUE,
    confidenceRange: null,
    candidateNumericScoreEnabled: true,
    candidateReportVisible: true,
    blockingCodes: [],
    topPriorities: ['AI.language: Check subject-verb agreement.', 'R07.1: State the purpose in the opening.'],
    strengths: [],
    studyPlan: [],
    criteria: [
      criterion('purpose', 3, 3),
      criterion('content', 6, 7),
      criterion('conciseness_clarity', 5, 7),
      criterion('genre_style', 6, 7),
      criterion('organisation_layout', 6, 7),
      criterion('language', 5, 7),
    ],
    errors: Array.from({ length: 8 }, (_, i) => correction(i + 1)),
    facts: [],
    modelAnswer: {
      status: 'Ready',
      modelAnswerText: 'Ms Jane Doe\nCommunity OT Service\n\n2 October 2026\n\nDear Ms Doe,\nRe: Ms Lucy Hall\n\nI am writing to refer Ms Hall for a home assessment.\n\nYours sincerely,\nDoctor',
      correctedCandidateLetter: null,
      whyThisWorks: [],
      groundedFactReferences: [],
      isCandidateVisible: true,
    },
  },
};

async function mockWritingResult(page: Page) {
  await page.route(`**/v1/writing/submissions/${SUBMISSION_ID}**`, (route) => {
    const { pathname } = new URL(route.request().url());
    const tail = pathname.slice(pathname.indexOf(SUBMISSION_ID) + SUBMISSION_ID.length);
    const body = tail === '' ? RESULT_FIXTURE.submission
      : tail === '/grade' ? RESULT_FIXTURE.grade
        : tail === '/assessment-v11' ? RESULT_FIXTURE.report
          : tail === '/answer-sheet' ? { answerSheetPdfDownloadPath: null }
            : null;
    if (tail === '/tutor-review') return route.fulfill({ status: 404, contentType: 'application/json', body: '{}' });
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
  });
  await page.route('**/v1/free-samples/writing', (route) => route.fulfill({
    status: 200, contentType: 'application/json', body: '[]',
  }));
}

async function openWritingResult(page: Page) {
  // No reveal transforms mid-measurement.
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await mockWritingResult(page);
  await page.goto(`/writing/submissions/${SUBMISSION_ID}/results`, { waitUntil: 'domcontentloaded' });
  // CountUp has settled once the real value is shown; measure after that.
  await expect(page.getByTestId('ai-estimated-score')).toHaveText('382/500', { timeout: 30_000 });
}

test.describe('Writing result card containment @learner @writing-v2 @responsive', () => {
  for (const width of [1366, 1536]) {
    test(`score card holds every label and value at ${width}px`, async ({ page }, testInfo) => {
      if (testInfo.project.name !== 'chromium-learner') test.skip();
      await page.setViewportSize({ width, height: 900 });
      await openWritingResult(page);

      const card = page.getByTestId('results-score-panel');
      await expect(card).toContainText(LONG_LABEL);
      await expect(card).toContainText(LONG_VALUE);
      await expect(page.getByTestId('grade-value')).toBeVisible();

      const geometry = await card.evaluate((cardEl) => {
        const box = cardEl.getBoundingClientRect();
        const outside: string[] = [];
        for (const el of [cardEl, ...Array.from(cardEl.querySelectorAll('*'))]) {
          const ownText = Array.from(el.childNodes).some((n) => n.nodeType === Node.TEXT_NODE && n.textContent?.trim());
          if (!ownText) continue;
          const range = document.createRange();
          range.selectNodeContents(el);
          const r = range.getBoundingClientRect();
          if (r.width === 0 && r.height === 0) continue;
          if (r.left < box.left - 1 || r.right > box.right + 1 || r.top < box.top - 1 || r.bottom > box.bottom + 1) {
            outside.push(`${el.tagName.toLowerCase()} "${el.textContent?.trim().slice(0, 40)}"`);
          }
        }
        const overflowing = [cardEl, ...Array.from(cardEl.querySelectorAll('[data-testid="results-score-stat"]'))]
          .filter((el) => el.scrollWidth > el.clientWidth + 1)
          .map((el) => `${el.getAttribute('data-testid')} ${el.scrollWidth}>${el.clientWidth}`);
        return {
          outside,
          overflowing,
          tiles: cardEl.querySelectorAll('[data-testid="results-score-stat"]').length,
          documentWidth: document.documentElement.scrollWidth,
          viewportWidth: window.innerWidth,
        };
      });

      expect(geometry.tiles, 'stat tiles').toBeGreaterThanOrEqual(3);
      expect(geometry.outside, `text outside the card @${width}`).toEqual([]);
      expect(geometry.overflowing, `card/tile overflow @${width}`).toEqual([]);
      expect(geometry.documentWidth, `document overflow @${width}`).toBeLessThanOrEqual(geometry.viewportWidth + 1);
    });
  }
});

// Launch handoff UI-2 (2 Oct 2026): inside the native app shell on a phone,
// the floating quick-access handle and the bottom nav must never cover the
// report — the last content and "View all corrections" stay >= 8 px clear.

const PHONES = [
  { width: 360, height: 780 },
  { width: 390, height: 844 },
  { width: 430, height: 932 },
] as const;

/**
 * Fakes the Capacitor shell. The app bootstrap stamps `web` on <html> after
 * load, so a MutationObserver keeps re-stamping the native runtime kind.
 */
async function emulateNativeShell(page: Page) {
  await page.addInitScript(() => {
    const stamp = () => {
      const root = document.documentElement;
      if (root && root.dataset.runtimeKind !== 'capacitor-native') root.dataset.runtimeKind = 'capacitor-native';
    };
    stamp();
    new MutationObserver(stamp).observe(document, {
      subtree: true,
      childList: true,
      attributes: true,
      attributeFilter: ['data-runtime-kind'],
    });
  });
}

/** The control is top-most at its centre and its bottom sits >= 8 px above the bottom nav. */
async function expectClearOfBottomNav(page: Page, control: Locator, label: string) {
  const nav = await page.getByRole('navigation', { name: 'Mobile navigation' }).boundingBox();
  const box = await control.boundingBox();
  expect(nav, 'bottom nav box').not.toBeNull();
  expect(box, `${label} box`).not.toBeNull();
  if (!nav || !box) return;
  expect(box.y + box.height, `${label} reaches under the bottom nav`).toBeLessThanOrEqual(nav.y - 8);
  const onTop = await control.evaluate((element) => {
    const rect = element.getBoundingClientRect();
    const hit = document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2);
    return Boolean(hit && (hit === element || element.contains(hit)));
  });
  expect(onTop, `${label} is covered by another element`).toBe(true);
}

test.describe('Writing result on a phone in the native shell @learner @writing-v2 @responsive', () => {
  for (const viewport of PHONES) {
    test(`nothing covers the report at ${viewport.width}px`, async ({ page }, testInfo) => {
      if (testInfo.project.name !== 'chromium-learner') test.skip();
      await page.setViewportSize(viewport);
      await emulateNativeShell(page);
      await openWritingResult(page);

      // Positive control: the shell emulation took effect (the app controls
      // only appear in the menu inside a native shell), and the floating
      // handle stays hidden below lg.
      await page.getByRole('button', { name: 'Open menu' }).click();
      await expect(page.getByTestId('mobile-menu-reload-app')).toBeVisible();
      await expect(page.getByTestId('mobile-menu-check-updates')).toBeVisible();
      await page.getByRole('button', { name: 'Close menu' }).click();
      await expect(page.getByTestId('shell-controls-handle')).toBeHidden();
      await expect(page.getByRole('navigation', { name: 'Mobile navigation' })).toBeVisible();

      const viewAll = page.getByTestId('corrections-view-all');
      // scroll-padding: aligning the control to the bottom keeps it above the nav.
      await viewAll.evaluate((element) => element.scrollIntoView({ block: 'end' }));
      await expectClearOfBottomNav(page, viewAll, `View all corrections @${viewport.width}`);
      await viewAll.click();
      await expect(page.getByTestId('corrections-full-list').getByRole('listitem')).toHaveCount(8);

      // Scrolled to the very end, the last report block and its actions clear the nav.
      await page.locator('#main-content').evaluate((element) => {
        element.scrollTop = element.scrollHeight;
      });
      const lastSection = page.getByTestId('result-section').last();
      await expect(lastSection).toHaveAttribute('data-section', 'next-actions');
      await expectClearOfBottomNav(page, lastSection, `last report section @${viewport.width}`);
      await expectClearOfBottomNav(page, page.getByRole('link', { name: /practiceAgain|Practice this again/ }), `Practice this again @${viewport.width}`);

      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
      expect(overflow, `horizontal overflow @${viewport.width}`).toBeLessThanOrEqual(1);
    });
  }
});
