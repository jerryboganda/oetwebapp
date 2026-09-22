import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('@/lib/auth-client', () => ({
  ensureFreshAccessToken: vi.fn().mockResolvedValue('test-token'),
}));

import {
  fetchPlacementAccommodations,
  grantPlacementAccommodation,
  revokePlacementAccommodation,
} from '../admin-placement';

/**
 * Wire contract for the admin extra-time accommodation endpoints (camelCase
 * JSON). The backend is written against the same shape, so these lock the URL,
 * verb and body of each call plus the tolerant response mapping.
 */

const fetchMock = vi.fn();

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const wire = {
  id: 'pacc_1',
  learnerUserId: 'user-1',
  learnerEmail: 'ali@example.com',
  learnerName: 'Ali Hassan',
  extraTimePercent: 50,
  reference: 'Case ref A-17',
  status: 'active',
  approvedByUserId: 'admin-1',
  approvedByName: 'Dr Admin',
  approvedAt: '2026-09-20T10:11:12.1234567Z',
  revokedByUserId: null,
  revokedByName: null,
  revokedAt: null,
  revokedReason: null,
  uses: [{ sessionId: 'sess-1', appliedAt: '2026-09-21T08:00:00.0000000Z', extraTimePercent: 50 }],
};

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('fetchPlacementAccommodations', () => {
  it('asks for active grants by default and maps the wire shape unchanged', async () => {
    fetchMock.mockResolvedValue(jsonResponse([wire]));

    const rows = await fetchPlacementAccommodations();

    const url = String(fetchMock.mock.calls[0][0]);
    expect(url).toContain('/v1/admin/placement/accommodations?');
    expect(url).toContain('includeRevoked=false');
    expect(url).not.toContain('learner=');
    expect(rows).toEqual([wire]);
  });

  it('sends the learner filter and includeRevoked', async () => {
    fetchMock.mockResolvedValue(jsonResponse([]));

    await fetchPlacementAccommodations({ learner: ' ali@example.com ', includeRevoked: true });

    const url = String(fetchMock.mock.calls[0][0]);
    expect(url).toContain('includeRevoked=true');
    expect(url).toContain('learner=ali%40example.com');
  });

  it('tolerates missing optional fields and an empty body', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse([{ id: 'pacc_2', status: 'Revoked' }]));
    const [sparse] = await fetchPlacementAccommodations({ includeRevoked: true });
    expect(sparse).toMatchObject({
      id: 'pacc_2',
      status: 'revoked',
      extraTimePercent: 0,
      learnerName: null,
      reference: null,
      revokedAt: null,
      uses: [],
    });

    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }));
    expect(await fetchPlacementAccommodations()).toEqual([]);
  });
});

describe('grantPlacementAccommodation', () => {
  it('posts the grant and returns the created accommodation', async () => {
    fetchMock.mockResolvedValue(jsonResponse(wire, 201));

    const created = await grantPlacementAccommodation({
      learnerEmail: 'ali@example.com',
      extraTimePercent: 50,
      reference: 'Case ref A-17',
    });

    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toMatch(/\/v1\/admin\/placement\/accommodations$/);
    expect(init.method).toBe('POST');
    expect(JSON.parse(String(init.body))).toEqual({
      learnerEmail: 'ali@example.com',
      extraTimePercent: 50,
      reference: 'Case ref A-17',
    });
    expect(created).toEqual(wire);
  });

  it('surfaces the server error with its status and code', async () => {
    fetchMock.mockResolvedValue(
      jsonResponse({ code: 'placement_learner_not_found', message: 'No learner matches that email or id.' }, 404),
    );

    await expect(grantPlacementAccommodation({ learnerUserId: 'ghost', extraTimePercent: 25 })).rejects.toMatchObject({
      status: 404,
      code: 'placement_learner_not_found',
      message: 'No learner matches that email or id.',
    });
  });
});

describe('revokePlacementAccommodation', () => {
  it('posts the optional reason to the revoke route', async () => {
    fetchMock.mockResolvedValue(jsonResponse({ ...wire, status: 'revoked' }));

    const revoked = await revokePlacementAccommodation('pacc_1', 'Entered in error');

    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toMatch(/\/v1\/admin\/placement\/accommodations\/pacc_1\/revoke$/);
    expect(init.method).toBe('POST');
    expect(JSON.parse(String(init.body))).toEqual({ reason: 'Entered in error' });
    expect(revoked.status).toBe('revoked');
  });

  it('sends an empty body when no reason is given and encodes the id', async () => {
    fetchMock.mockResolvedValue(jsonResponse({ ...wire, status: 'revoked' }));

    await revokePlacementAccommodation('pacc 1');

    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toContain('/accommodations/pacc%201/revoke');
    expect(JSON.parse(String(init.body))).toEqual({});
  });
});
