import { expect, test, type Page } from '@playwright/test';

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
    revisionInvite: { shouldOffer: false, reason: '' },
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
