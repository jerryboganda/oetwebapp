import { expect, test } from '@playwright/test';

// Mission-critical answer-key isolation contract: the learner-side
// session bootstrap endpoint MUST NOT serialize any of the authoring
// fields that would let a learner pre-compute the correct answers from
// the network response. Mirrors the Reading projection rule documented
// in `docs/READING-AUTHORING-PLAN.md` (no `CorrectAnswerJson` /
// `ExplanationMarkdown` / `AcceptedSynonymsJson` on the learner
// surface) — this spec enforces the equivalent for Listening.
//
// Endpoint under test: `GET /v1/listening-papers/papers/{id}/session?
// mode=practice` (see `lib/listening-api.ts` → `getListeningSession`).
// We use `page.request` so the authenticated `learner` storageState
// is automatically attached.
const FORBIDDEN_FIELDS = [
  'isCorrect',
  'correctAnswer',
  'acceptedSynonyms',
  'explanation',
  'whyWrong',
  'transcriptEvidence',
  'distractorCategory',
] as const;

test.describe('Listening answer-key not exposed @learner @listening @security', () => {
  test('learner session payload for lt-001 omits all answer-key fields', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-learner') {
      test.skip();
    }

    testInfo.setTimeout(60_000);

    // Hit the page once first so any same-origin auth cookies / CSRF
    // bootstrap that the API client expects are present on the request
    // context. Without this, the API call below can 401 against a
    // freshly-restored storageState in some browsers.
    await page.goto('/listening', { waitUntil: 'domcontentloaded' });

    const response = await page.request.get(
      '/v1/listening-papers/papers/lt-001/session?mode=practice',
    );
    expect(response.status(), `unexpected status from session endpoint: ${response.status()}`)
      .toBeGreaterThanOrEqual(200);
    expect(response.status()).toBeLessThan(300);

    const raw = await response.text();

    // Use a single combined regex so the failure message reports which
    // forbidden token leaked. Word-boundary anchored on the left to
    // avoid matching e.g. "thisIsCorrectThing" — JSON keys are quoted
    // so an exact `"<field>"` match is the strictest realistic check.
    for (const field of FORBIDDEN_FIELDS) {
      const pattern = new RegExp(`"${field}"`, 'i');
      expect(
        raw,
        `Listening session payload leaked authoring field "${field}". This violates the learner answer-key isolation contract.`,
      ).not.toMatch(pattern);
    }
  });
});
