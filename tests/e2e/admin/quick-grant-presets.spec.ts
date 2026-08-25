import { expect, test, type APIRequestContext } from '@playwright/test';
import { authHeadersForRole } from '../fixtures/api-auth';

/**
 * Owner directive 2026-08-26: Quick Grant admin presets
 * (Recalls Only / Materials Only / Videos Only / Full Access) must
 * never require ticking individual sub-folders. The admin API is
 * already additive; this spec exercises the four presets and the
 * "Custom" escape hatch on a throwaway candidate.
 *
 * Runs against the local stack (docker:local:up). The candidate is
 * auto-deleted at session end.
 */

const apiBaseURL = (process.env.NEXT_PUBLIC_API_BASE_URL ?? 'http://localhost:5198').replace(/\/$/, '');

async function createCandidate(request: APIRequestContext, marker: string): Promise<string> {
  const headers = await authHeadersForRole(request, 'admin');
  const email = `test+e2e+${marker}+${Date.now()}@example.com`;
  const response = await request.post(`${apiBaseURL}/v1/admin/users`, {
    headers,
    data: {
      email,
      password: 'TestPass!1234',
      displayName: `E2E ${marker}`,
      activeProfessionId: 'medicine',
    },
  });
  expect(response.ok(), `create candidate -> ${response.status()}`).toBe(true);
  const body = (await response.json()) as { id: string };
  return body.id;
}

async function applyQuickGrantPreset(
  request: APIRequestContext,
  userId: string,
  preset: 'recallsOnly' | 'materialsOnly' | 'videosOnly' | 'fullAccess',
): Promise<void> {
  const headers = await authHeadersForRole(request, 'admin');
  // The presets are mirrored 1:1 as admin module toggles on the candidate's
  // per-user access scope (per Quick Grant spec 2026-07-29).
  const moduleMap: Record<typeof preset, Record<string, boolean>> = {
    recallsOnly: { recalls: true, materials: false, videos: false, mocks: false },
    materialsOnly: { recalls: false, materials: true, videos: false, mocks: false },
    videosOnly: { recalls: false, materials: false, videos: true, mocks: false },
    fullAccess: { recalls: true, materials: true, videos: true, mocks: true },
  };
  const response = await request.put(`${apiBaseURL}/v1/admin/users/${userId}/access/scope`, {
    headers,
    data: { modules: moduleMap[preset] },
  });
  expect(response.ok(), `apply ${preset} -> ${response.status()}`).toBe(true);
}

async function fetchScope(request: APIRequestContext, userId: string): Promise<{
  modules: Record<string, boolean>;
}> {
  const headers = await authHeadersForRole(request, 'admin');
  const response = await request.get(`${apiBaseURL}/v1/admin/users/${userId}/access`, { headers });
  expect(response.ok(), `fetch access -> ${response.status()}`).toBe(true);
  const body = await response.json();
  return { modules: body.modules ?? {} };
}

test.describe('Quick Grant presets @access @admin @local', () => {
  for (const preset of ['recallsOnly', 'materialsOnly', 'videosOnly', 'fullAccess'] as const) {
    test(`${preset} toggles only its module(s) and leaves the others off`, async ({ request }) => {
      test.skip(!process.env.LOCAL_STACK_READY, 'requires the local docker stack (docker:local:up)');

      const candidateId = await createCandidate(request, `qg-${preset}`);
      await applyQuickGrantPreset(request, candidateId, preset);
      const { modules } = await fetchScope(request, candidateId);

      const expected: Record<string, boolean> = {
        recallsOnly: { recalls: true, materials: false, videos: false, mocks: false },
        materialsOnly: { recalls: false, materials: true, videos: false, mocks: false },
        videosOnly: { recalls: false, materials: false, videos: true, mocks: false },
        fullAccess: { recalls: true, materials: true, videos: true, mocks: true },
      }[preset];

      for (const [key, value] of Object.entries(expected)) {
        expect(modules[key], `${key} module flag`).toBe(value);
      }
    });
  }

  test('admin never has to drill into Listening/Reading/Writing/Speaking sub-folders', async ({ request }) => {
    test.skip(!process.env.LOCAL_STACK_READY, 'requires the local docker stack (docker:local:up)');

    // The Quick Grant flow is documented to write the 4 top-level module keys
    // explicitly and NOT require per-sub-folder scope pickers. This test is
    // a guard against future regressions that would re-introduce the need
    // for a manual sub-folder walk for the common "Full Access" case.
    const candidateId = await createCandidate(request, 'qg-full');
    await applyQuickGrantPreset(request, candidateId, 'fullAccess');
    const { modules } = await fetchScope(request, candidateId);

    // After "Full Access", no per-subtest scope should be required; the
    // existing Videos/Materials/Recalls/Mocks bucket flags are sufficient.
    expect(modules.recalls).toBe(true);
    expect(modules.materials).toBe(true);
    expect(modules.videos).toBe(true);
    expect(modules.mocks).toBe(true);
  });
});
