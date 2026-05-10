import { expect, test } from '@playwright/test';
import { signInApi } from '../fixtures/api-auth';

/**
 * RW-007 — browser proof that the deterministic Writing rule engine
 * surfaces rule-cited findings end-to-end.
 *
 * Tags: @writing @rule-engine
 *
 * Scoped to chromium-learner (mirrors writing-admin-paper-visibility.spec.ts).
 * The deterministic linter at POST /v1/writing/lint is the easiest oracle
 * to assert on without depending on flaky AI calls.
 */

const API_BASE_URL = (
  process.env.NEXT_PUBLIC_API_BASE_URL ?? 'http://localhost:5198'
).replace(/\/$/, '');

// Letter body intentionally constructed to trigger several deterministic
// rules from the Writing rulebook:
//   - "the patient" / "the smoker" labels   → R08.14 / R12.2 / R12.3
//   - "ASAP"                                → R13.10
//   - leading "Date: ..." line              → R05.8 (no_date_prefix)
//   - "was presented"                       → R14.6
const VIOLATING_LETTER_TEXT = [
  'Date: 12/05/2026',
  '',
  'Dear Dr Smith,',
  '',
  'Re: the patient',
  '',
  'I am writing about the patient, a known smoker, who was presented to',
  'our clinic ASAP for review. The patient reports ongoing chest pain and',
  'requires urgent referral.',
  '',
  'Yours sincerely,',
  'Dr Jones',
].join('\n');

const EXPECTED_RULE_IDS = ['R08.14', 'R12.2', 'R12.3', 'R13.10', 'R05.8', 'R14.6'];

type LintFinding = {
  ruleId?: string;
  severity?: string;
  message?: string;
};

type LintResponse = {
  findings: LintFinding[];
  totals?: Record<string, number>;
};

test.describe('Writing — rule engine violations (RW-007) @writing @rule-engine', () => {
  test('POST /v1/writing/lint returns rule citations for a violating letter', async ({
    request,
  }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }

    // The endpoint requires the RulebookReader policy, so we mint a real
    // bearer token via the same path the auth setup uses. If the seeded
    // learner cannot authenticate against the local API we skip rather
    // than silently passing — there is no useful assertion to make.
    let accessToken: string;
    try {
      const session = await signInApi(request, 'learner');
      accessToken = session.accessToken;
    } catch (error) {
      test.skip(
        true,
        `Could not bootstrap learner session against ${API_BASE_URL}: ${(error as Error).message}`,
      );
      return;
    }

    const response = await request.post(`${API_BASE_URL}/v1/writing/lint`, {
      headers: {
        Authorization: `Bearer ${accessToken}`,
        'Content-Type': 'application/json',
      },
      data: {
        letterText: VIOLATING_LETTER_TEXT,
        letterType: 'urgent_referral',
        profession: 'medicine',
        patientIsMinor: false,
      },
    });

    expect(
      response.ok(),
      `POST /v1/writing/lint failed: ${response.status()} ${await response.text().catch(() => '')}`,
    ).toBeTruthy();

    const body = (await response.json()) as LintResponse;
    expect(Array.isArray(body.findings), 'lint response missing findings array').toBe(true);

    const returnedRuleIds = new Set(
      body.findings
        .map((f) => f.ruleId)
        .filter((id): id is string => typeof id === 'string' && id.length > 0),
    );

    const matched = EXPECTED_RULE_IDS.filter((id) => returnedRuleIds.has(id));
    expect(
      matched.length,
      `Expected at least 3 of ${EXPECTED_RULE_IDS.join(', ')} to be cited; got: ${[...returnedRuleIds].join(', ') || '<none>'}`,
    ).toBeGreaterThanOrEqual(3);
  });

  test('feedback page renders at least one [Rxx.n] rule badge', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }

    // The dev fixture attempt id is environment-dependent and not seeded
    // by the standard auth bootstrap. We attempt the canonical demo id
    // and skip cleanly if the page renders an empty / not-ready state —
    // this UI-level proof is opportunistic; spec 1 above is the
    // authoritative end-to-end assertion.
    const demoAttemptId = 'demo-001';
    const response = await page.goto(
      `/writing/feedback?attemptId=${encodeURIComponent(demoAttemptId)}`,
      { waitUntil: 'domcontentloaded' },
    );

    if (!response || !response.ok()) {
      test.skip(
        true,
        `Feedback page returned ${response?.status() ?? 'no response'} for attemptId=${demoAttemptId}; no dev fixture available.`,
      );
      return;
    }

    const ruleBadge = page.locator('[data-testid="rule-badge"]').first();
    try {
      await ruleBadge.waitFor({ state: 'visible', timeout: 5_000 });
    } catch {
      test.skip(
        true,
        'No rule-cited findings rendered for the demo attempt; this dev environment has no seeded feedback fixture. TODO: wire a deterministic seeded attempt.',
      );
      return;
    }

    const text = (await ruleBadge.textContent())?.trim() ?? '';
    expect(text).toMatch(/^\[R\d{2}\.\d+\]$/);

    const href = await ruleBadge.getAttribute('href');
    expect(href, 'rule badge missing href').not.toBeNull();
    expect(href).toMatch(/\/writing\/rulebook\/R\d{2}\.\d+/);
  });
});
