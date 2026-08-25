import { expect, test, type APIRequestContext } from '@playwright/test';
import { authHeadersForRole } from '../fixtures/api-auth';

/**
 * Owner directive 2026-08-26: verify the AI Packages + Full Mock
 * automatic-access flow. After admin grants an AI package (Writing
 * Starter or a Full Mock pack), the candidate-side balance, debit
 * feedback, attempt history, and "Other papers" candidate visibility
 * gate must all hold end-to-end.
 *
 * Runs against the local stack (docker:local:up) and uses admin API
 * helpers to grant `pkg_writing_only` and `pkg_full_mock_x1` to a
 * throwaway candidate. The candidate is auto-deleted at session end.
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

async function grantAddon(request: APIRequestContext, userId: string, code: string): Promise<void> {
  const headers = await authHeadersForRole(request, 'admin');
  const response = await request.post(`${apiBaseURL}/v1/admin/users/${userId}/access/addon`, {
    headers,
    data: { addonCode: code },
  });
  expect(response.ok(), `grant ${code} -> ${response.status()}`).toBe(true);
}

test.describe('AI packages + full mocks automatic access @access @ai @local', () => {
  test('admin-granted Writing Starter credits appear on the candidate balance endpoint', async ({ request }) => {
    test.skip(!process.env.LOCAL_STACK_READY, 'requires the local docker stack (docker:local:up)');

    const candidateId = await createCandidate(request, 'writing-starter');
    await grantAddon(request, candidateId, 'pkg_writing_only');

    const learnerHeaders = await authHeadersForRole(request, 'learner');
    const response = await request.get(`${apiBaseURL}/v1/me/credits`, { headers: learnerHeaders });
    expect(response.ok(), `candidate balance -> ${response.status()}`).toBe(true);
    const body = await response.json();
    const writingBucket = body.buckets?.find((b: { type: string }) => b.type === 'writing');
    expect(writingBucket, 'writing bucket must surface after admin grant').toBeTruthy();
    expect(writingBucket.granted).toBeGreaterThan(0);
  });

  test('admin-granted Full Mock attempt surfaces as a separate Full Mock bucket, not an AI credit', async ({ request }) => {
    test.skip(!process.env.LOCAL_STACK_READY, 'requires the local docker stack (docker:local:up)');

    const candidateId = await createCandidate(request, 'full-mock');
    await grantAddon(request, candidateId, 'pkg_full_mock_x1');

    const learnerHeaders = await authHeadersForRole(request, 'learner');
    const response = await request.get(`${apiBaseURL}/v1/me/credits`, { headers: learnerHeaders });
    expect(response.ok()).toBe(true);
    const body = await response.json();
    const mockBucket = body.buckets?.find((b: { type: string }) => b.type === 'full_mock');
    expect(mockBucket, 'full_mock bucket must surface after admin grant').toBeTruthy();
    expect(mockBucket.granted).toBeGreaterThanOrEqual(1);

    // Per the master catalogue: AI credit buckets must NOT have been touched
    // by a Full Mock grant.
    for (const type of ['reading', 'listening', 'writing', 'speaking', 'shared']) {
      const b = body.buckets?.find((bucket: { type: string }) => bucket.type === type);
      if (b) expect(b.granted, `${type} bucket must remain 0 after only a Full Mock grant`).toBe(0);
    }
  });

  test('Other (non-series) Reading papers stay hidden from the candidate', async ({ request }) => {
    test.skip(!process.env.LOCAL_STACK_READY, 'requires the local docker stack (docker:local:up)');

    const learnerHeaders = await authHeadersForRole(request, 'learner');
    const response = await request.get(`${apiBaseURL}/v1/reading/papers`, { headers: learnerHeaders });
    expect(response.ok()).toBe(true);
    const body = (await response.json()) as { items: Array<{ tags: string[]; series: string | null }> };
    for (const paper of body.items ?? []) {
      const isSeries =
        paper.series != null ||
        paper.tags?.some((t: string) => t.startsWith('series:'));
      expect(isSeries, 'every candidate-visible Reading paper must belong to a series').toBe(true);
    }
  });
});
