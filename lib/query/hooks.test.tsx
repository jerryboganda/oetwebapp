import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement, type ReactNode } from 'react';

// Mock the underlying API module BEFORE importing the hooks. The hooks pull
// the `fetch*` functions in by reference, so the mock must be in place first.
vi.mock('@/lib/api', () => ({
  fetchDashboardHome: vi.fn(),
  fetchEngagement: vi.fn(),
  fetchReadiness: vi.fn(),
  fetchOnboardingState: vi.fn(),
  fetchStudyPlan: vi.fn(),
}));

import * as api from '@/lib/api';
import {
  queryKeys,
  useOnboardingState,
  useDashboardHome,
  useEngagement,
  useReadiness,
  useStudyPlan,
} from './hooks';

function makeWrapper() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0, staleTime: 0 } },
  });
  const Wrapper = ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client }, children);
  return { client, Wrapper };
}

beforeEach(() => {
  vi.mocked(api.fetchDashboardHome).mockReset();
  vi.mocked(api.fetchEngagement).mockReset();
  vi.mocked(api.fetchReadiness).mockReset();
  vi.mocked(api.fetchOnboardingState).mockReset();
  vi.mocked(api.fetchStudyPlan).mockReset();
});

describe('queryKeys', () => {
  it('exposes stable tuple shapes for every namespace', () => {
    expect(queryKeys.profile._def).toEqual(['profile']);
    expect(queryKeys.profile.self).toEqual(['profile', 'self']);
    expect(queryKeys.profile.onboarding).toEqual(['profile', 'onboarding']);
    expect(queryKeys.dashboard._def).toEqual(['dashboard']);
    expect(queryKeys.dashboard.home).toEqual(['dashboard', 'home']);
    expect(queryKeys.dashboard.engagement).toEqual(['dashboard', 'engagement']);
    expect(queryKeys.readiness._def).toEqual(['readiness']);
    expect(queryKeys.readiness.self).toEqual(['readiness', 'self']);
    expect(queryKeys.studyPlan._def).toEqual(['study-plan']);
    expect(queryKeys.studyPlan.list).toEqual(['study-plan', 'list']);
  });

  it('uses the same identity on every access (frozen factory)', () => {
    expect(queryKeys.dashboard.home).toBe(queryKeys.dashboard.home);
  });
});

describe('useDashboardHome', () => {
  it('returns the data fetched by fetchDashboardHome', async () => {
    vi.mocked(api.fetchDashboardHome).mockResolvedValue({ greeting: 'hi' });
    const { Wrapper } = makeWrapper();
    const { result } = renderHook(() => useDashboardHome(), { wrapper: Wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual({ greeting: 'hi' });
    expect(api.fetchDashboardHome).toHaveBeenCalledTimes(1);
  });

  it('surfaces errors from the underlying fetch', async () => {
    vi.mocked(api.fetchDashboardHome).mockRejectedValue(new Error('boom'));
    const { Wrapper } = makeWrapper();
    const { result } = renderHook(() => useDashboardHome(), { wrapper: Wrapper });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect((result.current.error as Error).message).toBe('boom');
  });
});

describe('useEngagement', () => {
  it('returns engagement data', async () => {
    vi.mocked(api.fetchEngagement).mockResolvedValue({ streak: 7 });
    const { Wrapper } = makeWrapper();
    const { result } = renderHook(() => useEngagement(), { wrapper: Wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual({ streak: 7 });
  });
});

describe('useReadiness', () => {
  it('returns readiness data', async () => {
    vi.mocked(api.fetchReadiness).mockResolvedValue({ score: 92 });
    const { Wrapper } = makeWrapper();
    const { result } = renderHook(() => useReadiness(), { wrapper: Wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual({ score: 92 });
  });
});

describe('useOnboardingState', () => {
  it('returns onboarding state', async () => {
    vi.mocked(api.fetchOnboardingState).mockResolvedValue({ step: 'profile' });
    const { Wrapper } = makeWrapper();
    const { result } = renderHook(() => useOnboardingState(), { wrapper: Wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual({ step: 'profile' });
  });
});

describe('useStudyPlan', () => {
  it('returns study plan data', async () => {
    vi.mocked(api.fetchStudyPlan).mockResolvedValue({ plan: ['lesson-1'] });
    const { Wrapper } = makeWrapper();
    const { result } = renderHook(() => useStudyPlan(), { wrapper: Wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual({ plan: ['lesson-1'] });
  });

  it('honours the `enabled: false` option to suppress fetching', async () => {
    vi.mocked(api.fetchStudyPlan).mockResolvedValue({ plan: [] });
    const { Wrapper } = makeWrapper();
    const { result } = renderHook(() => useStudyPlan({ enabled: false }), { wrapper: Wrapper });

    // Allow microtasks to flush — the query should remain idle.
    await new Promise((r) => setTimeout(r, 10));
    expect(api.fetchStudyPlan).not.toHaveBeenCalled();
    expect(result.current.isLoading).toBe(false);
  });
});
