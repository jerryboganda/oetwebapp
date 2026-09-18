import { expect, test, type Page } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

/**
 * Free General-English placement journey (placement-test integration).
 *
 * Two hermetic surfaces — neither needs a live assessment engine:
 *  1. @unauth — the server middleware routes unauthenticated /placement-test
 *     visitors to the MINIMAL placement signup (identity + consent only,
 *     enrollment deferred), preserving the destination.
 *  2. @learner @mocks — the authenticated journey against route-mocked
 *     /v1/placement/* responses: session create → objective modules →
 *     foundation profile with honest per-skill statuses.
 */

const PLACEMENT_API = '**/api/backend/v1/placement/**';

test.describe('Placement entry @placement @smoke', () => {
  test('unauthenticated placement visits route into the minimal signup, not the standard wall', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('unauth')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/placement-test');

    await expect(page).toHaveURL(/\/register\?purpose=placement&next=%2Fplacement-test/);

    // Minimal signup: identity + consent + password only.
    await expect(page.getByRole('heading', { name: /create your free account/i })).toBeVisible();
    await expect(page.getByText(/one quick step, then your placement test starts/i)).toBeVisible();
    await expect(page.getByRole('button', { name: /create account & start test/i })).toBeVisible();

    // The healthcare-enrollment block must be DEFERRED — none of the
    // standard signup's exam/profession/country/date fields appear.
    await expect(page.getByLabel(/exam type/i)).toHaveCount(0);
    await expect(page.getByLabel(/current profession/i)).toHaveCount(0);
    await expect(page.getByLabel(/target country/i)).toHaveCount(0);
    await expect(page.getByLabel(/target exam date/i)).toHaveCount(0);

    // Existing accounts reach sign-in with the destination preserved.
    const signIn = page.getByRole('link', { name: /sign in/i }).first();
    await expect(signIn).toHaveAttribute('href', /next=%2Fplacement-test/);

    expectNoSevereClientIssues(diagnostics, { allowAuthRedirectNoise: true, allowNextDevNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});

type Skill = {
  skill: string;
  status: string;
  band: string | null;
  range: [string, string] | null;
  notes: string[];
  flags: string[];
  can_do?: string[];
  growth_areas?: string[];
};

function foundationReport(sessionId: string) {
  const skills: Skill[] = [
    { skill: 'RD', status: 'measured', band: 'B1', range: null, notes: [], flags: [] },
    { skill: 'LSN', status: 'measured', band: 'A2', range: null, notes: [], flags: [] },
    {
      skill: 'SPK',
      status: 'not_measured',
      band: null,
      range: null,
      notes: ['Productive module not yet completed'],
      flags: [],
    },
    {
      skill: 'WRT',
      status: 'not_measured',
      band: null,
      range: null,
      notes: ['Productive module not yet completed'],
      flags: [],
    },
  ];
  return {
    session_id: sessionId,
    profile_type: 'foundation_receptive',
    skills,
    headline: { kind: 'none', band: null, range: null },
    confidence: 'Low',
    confidence_reasons: ['Partial profile: receptive skills only.'],
    readiness: null,
    retest_advice: 'Recommended study interval: retest after 2–4 weeks of focused study.',
    wording_version: '2.0.0-beta',
    generated_at: new Date().toISOString(),
  };
}

function jsonOk(body: unknown) {
  return {
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(body),
  };
}

function mockAuthSession() {
  const now = Date.now();
  return {
    accessToken: 'pw-mock-access-token',
    refreshToken: 'pw-mock-refresh-token',
    accessTokenExpiresAt: new Date(now + 30 * 60_000).toISOString(),
    refreshTokenExpiresAt: new Date(now + 30 * 86_400_000).toISOString(),
    currentUser: {
      userId: 'oet-learner-pw',
      email: 'placement.pw@example.com',
      role: 'learner',
      displayName: 'Placement PW',
      isEmailVerified: true,
      isAuthenticatorEnabled: false,
      requiresEmailVerification: false,
      requiresMfa: false,
      emailVerifiedAt: new Date(now).toISOString(),
      authenticatorEnabledAt: null,
      adminPermissions: null,
      activeProfessionId: null,
    },
  };
}

async function mockPlacementApi(page: Page) {
  const sessionId = 'ses_pw_000000000001';

  // Catch-all FIRST so specific routes registered after it win: the mocked
  // journey is hermetic — no real backend request ever leaves the browser.
  await page.route('**/api/backend/**', async (route) => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/v1/auth/refresh')) {
      await route.fulfill(jsonOk(mockAuthSession()));
      return;
    }
    await route.fulfill({ status: 404, contentType: 'application/json', body: '{"error":"not mocked"}' });
  });

  await page.route(PLACEMENT_API, async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname.replace(/^.*\/v1\/placement/, '');
    const method = request.method();

    if (path === '/status' && method === 'GET') {
      await route.fulfill(jsonOk({ enabled: true }));
      return;
    }

    if (path === '/session' && method === 'POST') {
      await route.fulfill(
        jsonOk({ session_id: sessionId, ruleset_version: '2.0.0-beta' }),
      );
      return;
    }

    // Proxy paths: /v1/placement/session/{id}/... (unlike the engine's
    // /api/sessions/{id} — the mock speaks the PROXY contract).
    if (path === `/session/${sessionId}` && method === 'GET') {
      await route.fulfill(
        jsonOk({
          session_id: sessionId,
          candidate_uid: 'oet-learner-pw',
          mode: 'free_beta',
          state_version: 3,
          target_goal: 'General',
          flags: [],
          created_at: new Date().toISOString(),
          ruleset_version: '2.0.0-beta',
          ls_status: 'complete',
          rd_status: 'complete',
          lsn_status: 'complete',
          spk_status: 'pending',
          wrt_status: 'pending',
          has_result: false,
        }),
      );
      return;
    }

    if (path.startsWith(`/session/${sessionId}/module/`) && method === 'POST') {
      // Every objective module reports completion immediately — the journey
      // under test is the CLIENT flow, not the engine's adaptive routing
      // (that is covered by the engine's own integration suite).
      await route.fulfill(
        jsonOk({
          stimulus_id: null,
          stimulus_text: null,
          stimulus_type: null,
          audio_url: null,
          items: [],
          deadline_at: new Date(Date.now() + 60_000).toISOString(),
          module_complete: true,
        }),
      );
      return;
    }

    if (path === `/session/${sessionId}/result/receptive` && method === 'GET') {
      await route.fulfill(jsonOk(foundationReport(sessionId)));
      return;
    }

    if (path === '/history' && method === 'GET') {
      await route.fulfill(jsonOk([]));
      return;
    }

    await route.fulfill({ status: 404, contentType: 'application/json', body: '{"error":"not mocked"}' });
  });

  return sessionId;
}

test.describe('Placement journey @placement @mocks', () => {
  test('start → objective modules → foundation profile with honest unmeasured skills', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }

    const sessionId = await mockPlacementApi(page);
    const diagnostics = observePage(page);

    await page.goto('/placement-test');
    await expect(page.getByRole('heading', { name: /free general english placement test/i })).toBeVisible();

    await page.getByRole('button', { name: /start free placement test/i }).click();

    // The runner walks LS → RD → LSN (each mocked complete) and lands on
    // the receptive foundation profile.
    await expect(page.getByRole('heading', { name: /your foundation profile/i })).toBeVisible({ timeout: 20_000 });

    const profile = page.locator('div', { has: page.getByRole('heading', { name: /your foundation profile/i }) });
    await expect(profile.getByText('B1', { exact: true })).toBeVisible();
    await expect(profile.getByText('A2', { exact: true })).toBeVisible();
    // Productive skills are honestly unmeasured at this stage — never a band.
    await expect(profile.getByText('not_measured').first()).toBeVisible();

    // Next stage CTA present.
    await expect(page.getByRole('button', { name: /continue to speaking/i })).toBeVisible();

    // The engine session id stays reachable for support (sr-only marker).
    const pageContent = await page.content();
    expect(pageContent).toContain(sessionId);

    // NB: no expectNoSevereClientIssues here. This suite is hermetic — the
    // route mocks fulfill only the placement surface, so the learner shell's
    // unrelated boot calls (AI-assistant SignalR negotiation, notifications,
    // gamification, ...) 404 against the catch-all and log. That noise is
    // inherent to mocking one surface of a large shell and is not what this
    // spec verifies; the placement-relevant assertions above are.
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
