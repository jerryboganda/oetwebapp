import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';

const { mockFetchSignupCatalog } = vi.hoisted(() => ({
  mockFetchSignupCatalog: vi.fn(),
}));

vi.mock('@/lib/auth-client', () => ({
  fetchSignupCatalog: mockFetchSignupCatalog,
}));

import { useSignupCatalog } from '../use-signup-catalog';
import {
  enrollmentSessions as fallbackSessions,
  examTypes as fallbackExamTypes,
  professions as fallbackProfessions,
} from '@/lib/auth/enrollment';

describe('useSignupCatalog', () => {
  beforeEach(() => {
    mockFetchSignupCatalog.mockReset();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('returns the static fallback catalog synchronously on initial render', () => {
    mockFetchSignupCatalog.mockReturnValue(new Promise(() => {})); // never resolves
    const { result } = renderHook(() => useSignupCatalog());

    expect(result.current.examTypes).toBe(fallbackExamTypes);
    expect(result.current.professions).toBe(fallbackProfessions);
    expect(result.current.enrollmentSessions).toBe(fallbackSessions);
    expect(result.current.billingPlans).toEqual([]);
    expect(result.current.externalAuthProviders).toEqual([]);
  });

  it('replaces fallbacks with the server catalog when the request succeeds', async () => {
    const remote = {
      examTypes: [{ id: 'oet', label: 'OET', code: 'OET', description: 'remote' }],
      professions: [{ id: 'nursing', label: 'Nursing', countryTargets: [], examTypeIds: ['oet'], description: '' }],
      sessions: [
        {
          id: 's1', name: 'remote', examTypeId: 'oet', professionIds: ['nursing'],
          priceLabel: '$1', startDate: '2026-01-01', endDate: '2026-02-01',
          deliveryMode: 'online', capacity: 10, seatsRemaining: 5,
        },
      ],
      billingPlans: [{ id: 'p1', label: 'Pro' }] as unknown,
      externalAuthProviders: ['google', 'facebook'],
    };
    mockFetchSignupCatalog.mockResolvedValue(remote);

    const { result } = renderHook(() => useSignupCatalog());

    await waitFor(() => {
      expect(result.current.examTypes).toEqual(remote.examTypes);
    });
    expect(result.current.professions).toEqual(remote.professions);
    expect(result.current.enrollmentSessions).toEqual(remote.sessions);
    expect(result.current.billingPlans).toEqual(remote.billingPlans);
    expect(result.current.externalAuthProviders).toEqual(remote.externalAuthProviders);
  });

  it('uses fallbacks for any catalog property that is not an array', async () => {
    mockFetchSignupCatalog.mockResolvedValue({
      examTypes: 'oops' as unknown,
      professions: null,
      sessions: undefined,
      billingPlans: { not: 'array' } as unknown,
      externalAuthProviders: 42 as unknown,
    });

    const { result } = renderHook(() => useSignupCatalog());

    await waitFor(() => {
      // Wait for effect to flush by checking billingPlans observed (still []).
      expect(mockFetchSignupCatalog).toHaveBeenCalledTimes(1);
    });

    expect(result.current.examTypes).toBe(fallbackExamTypes);
    expect(result.current.professions).toBe(fallbackProfessions);
    expect(result.current.enrollmentSessions).toBe(fallbackSessions);
    expect(result.current.billingPlans).toEqual([]);
    expect(result.current.externalAuthProviders).toEqual([]);
  });

  it('falls back to static catalog when the fetch rejects', async () => {
    mockFetchSignupCatalog.mockRejectedValue(new Error('network down'));

    const { result } = renderHook(() => useSignupCatalog());

    await waitFor(() => {
      expect(mockFetchSignupCatalog).toHaveBeenCalledTimes(1);
    });

    expect(result.current.examTypes).toBe(fallbackExamTypes);
    expect(result.current.professions).toBe(fallbackProfessions);
    expect(result.current.enrollmentSessions).toBe(fallbackSessions);
    expect(result.current.billingPlans).toEqual([]);
    expect(result.current.externalAuthProviders).toEqual([]);
  });

  it('does not call setState when unmounted before fetch resolves (no act warning)', async () => {
    let resolve!: (v: unknown) => void;
    mockFetchSignupCatalog.mockReturnValue(new Promise((r) => { resolve = r; }));

    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
    const { unmount } = renderHook(() => useSignupCatalog());
    unmount();
    resolve({
      examTypes: [{ id: 'x', label: 'x', code: 'x', description: 'x' }],
      professions: [],
      sessions: [],
      billingPlans: [],
      externalAuthProviders: [],
    });
    // give microtasks a chance to flush
    await new Promise((r) => setTimeout(r, 0));

    // No "Can't perform a React state update on an unmounted component" warning.
    const calls = errorSpy.mock.calls.map((c) => String(c[0] ?? ''));
    expect(calls.some((m) => m.includes('unmounted component'))).toBe(false);
    errorSpy.mockRestore();
  });

  it('does not call setState when unmounted before fetch rejects', async () => {
    let reject!: (e: unknown) => void;
    mockFetchSignupCatalog.mockReturnValue(new Promise((_, r) => { reject = r; }));

    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
    const { unmount } = renderHook(() => useSignupCatalog());
    unmount();
    reject(new Error('late'));
    await new Promise((r) => setTimeout(r, 0));

    const calls = errorSpy.mock.calls.map((c) => String(c[0] ?? ''));
    expect(calls.some((m) => m.includes('unmounted component'))).toBe(false);
    errorSpy.mockRestore();
  });
});
