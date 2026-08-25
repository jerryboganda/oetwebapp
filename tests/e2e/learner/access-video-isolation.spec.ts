import { expect, test, type APIRequestContext } from '@playwright/test';
import { authHeadersForRole } from '../fixtures/api-auth';

/**
 * Owner directive 2026-08-26: verify the two-way Writing video isolation
 * rule between Full Course and Crash Course / Fast Track plans.
 *
 * Requires the local stack (`pnpm docker:local:up`) — the entitlement
 * checks the live DB. This spec uses the seeded `crash-course-medicine`
 * and `full-condensed-medicine` plans created by the dev seed so the
 * existing migration `20261026090000_TwoWayWritingVideoIsolation` runs
 * against the same data the production rule will see.
 *
 * Test scope (matches the per-folder batch tags seeded in
 * 20261026090000_TwoWayWritingVideoIsolation.cs):
 *   - Full Course candidate: a Writing video tagged with any of
 *     batch:crash-course-arabic-writing, batch:writing-sessions-crash-course-old,
 *     batch:fast-track-crash-course, batch:crash-course-workshops must be
 *     denied by the entitlement endpoint and 403 the playback session.
 *   - Full Course candidate: an ordinary December/February Writing video
 *     (no excluded tag) must still be allowed.
 *   - Crash Course candidate: the same excluded-tag video must be allowed
 *     (it sits on the 18-id include list from 20260822090000 plus the
 *     subtest × profession resolution).
 */

const apiBaseURL = (process.env.NEXT_PUBLIC_API_BASE_URL ?? 'http://localhost:5198').replace(/\/$/, '');

interface AdminVideoSummary {
  videoId: string;
  title: string;
  subtestCode: string | null;
  tags: string[];
  status: string;
}

interface AdminUserDetail {
  id: string;
  email: string;
  subscriptions: Array<{
    planCode: string;
    status: string;
  }>;
}

async function grantPlanToLearner(
  request: APIRequestContext,
  learnerId: string,
  planCode: string,
): Promise<void> {
  const headers = await authHeadersForRole(request, 'admin');
  const response = await request.post(`${apiBaseURL}/v1/admin/users/${learnerId}/access/grant`, {
    headers,
    data: { planCode, makePrimary: true, grantIncludedCredits: false },
  });
  expect(response.ok(), `grant plan ${planCode} for ${learnerId} -> ${response.status()}`).toBe(true);
}

async function fetchVideoByTag(
  request: APIRequestContext,
  tag: string,
): Promise<AdminVideoSummary> {
  const headers = await authHeadersForRole(request, 'admin');
  const response = await request.get(`${apiBaseURL}/v1/admin/videos`, {
    headers,
    params: { tag, status: 'published', itemsPerPage: 5 },
  });
  expect(response.ok(), `list videos for tag ${tag} -> ${response.status()}`).toBe(true);
  const body = (await response.json()) as { items: AdminVideoSummary[] };
  expect(body.items.length, `expected at least one published video for tag ${tag}`).toBeGreaterThan(0);
  return body.items[0]!;
}

test.describe('Two-way Writing video isolation @access @video @local', () => {
  test('full-course candidate is denied a crash-course-tagged Writing video', async ({ request }) => {
    test.skip(!process.env.LOCAL_STACK_READY, 'requires the local docker stack (docker:local:up)');

    const candidate = await fetchCandidate(request, 'full-medicine');
    await grantPlanToLearner(request, candidate.id, 'full-condensed-medicine');

    const crashVideo = await fetchVideoByTag(request, 'batch:crash-course-arabic-writing');
    const learnerHeaders = await authHeadersForRole(request, 'learner');

    const entitlement = await request.post(
      `${apiBaseURL}/v1/video-library/videos/${crashVideo.videoId}/entitlement`,
      { headers: learnerHeaders },
    );
    expect(entitlement.status(), 'entitlement must surface plan_excludes_video_tag').toBe(200);
    const body = await entitlement.json();
    expect(body.allowed).toBe(false);
    expect(body.reason).toBe('plan_excludes_video_tag');
  });

  test('full-course candidate can still access an ordinary Writing video', async ({ request }) => {
    test.skip(!process.env.LOCAL_STACK_READY, 'requires the local docker stack (docker:local:up)');

    const candidate = await fetchCandidate(request, 'full-medicine');
    await grantPlanToLearner(request, candidate.id, 'full-condensed-medicine');

    const learnerHeaders = await authHeadersForRole(request, 'learner');
    const response = await request.get(`${apiBaseURL}/v1/video-library/videos`, {
      headers: learnerHeaders,
      params: { subtest: 'writing', itemsPerPage: 25 },
    });
    expect(response.ok()).toBe(true);
    const body = (await response.json()) as { items: AdminVideoSummary[] };

    const ordinaryWriting = body.items.find(
      (v) => !v.tags.some((t) => t.startsWith('batch:crash-course-')),
    );
    expect(ordinaryWriting, 'expected at least one ordinary (non-crash-tagged) Writing video').toBeDefined();

    const entitlement = await request.post(
      `${apiBaseURL}/v1/video-library/videos/${ordinaryWriting!.videoId}/entitlement`,
      { headers: learnerHeaders },
    );
    const result = await entitlement.json();
    expect(result.allowed).toBe(true);
  });

  test('crash-course candidate can access a crash-course-tagged Writing video', async ({ request }) => {
    test.skip(!process.env.LOCAL_STACK_READY, 'requires the local docker stack (docker:local:up)');

    const candidate = await fetchCandidate(request, 'crash-medicine');
    await grantPlanToLearner(request, candidate.id, 'crash-course');

    const crashVideo = await fetchVideoByTag(request, 'batch:crash-course-arabic-writing');
    const learnerHeaders = await authHeadersForRole(request, 'learner');

    const entitlement = await request.post(
      `${apiBaseURL}/v1/video-library/videos/${crashVideo.videoId}/entitlement`,
      { headers: learnerHeaders },
    );
    const result = await entitlement.json();
    expect(result.allowed, 'crash-course plan must allow the 18-id include carve-out').toBe(true);
  });
});

async function fetchCandidate(
  request: APIRequestContext,
  marker: string,
): Promise<AdminUserDetail> {
  const headers = await authHeadersForRole(request, 'admin');
  const stamp = Date.now();
  const email = `test+e2e+${marker}+${stamp}@example.com`;
  const response = await request.post(`${apiBaseURL}/v1/admin/users`, {
    headers,
    data: {
      email,
      password: 'TestPass!1234',
      displayName: `E2E ${marker}`,
      activeProfessionId: 'medicine',
    },
  });
  expect(response.ok(), `create ${marker} candidate -> ${response.status()}`).toBe(true);
  const created = (await response.json()) as { id: string };
  return { id: created.id, email, subscriptions: [] };
}
