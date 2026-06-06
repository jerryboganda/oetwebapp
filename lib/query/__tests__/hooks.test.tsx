import { describe, it, expect, vi } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { type ReactNode } from 'react';
import { useApiMutation, queryKeys } from '@/lib/query/hooks';

function makeWrapper(client: QueryClient) {
  return function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
  };
}

describe('useApiMutation (FE-006)', () => {
  it('invalidates the provided query keys after a successful mutation', async () => {
    const client = new QueryClient({ defaultOptions: { mutations: { retry: 0 } } });
    const invalidateSpy = vi.spyOn(client, 'invalidateQueries');
    const mutationFn = vi.fn(async (n: number) => n * 2);

    const { result } = renderHook(
      () => useApiMutation(mutationFn, [queryKeys.listening.lessons, queryKeys.dashboard.home]),
      { wrapper: makeWrapper(client) },
    );

    await act(async () => {
      await result.current.mutateAsync(21);
    });

    expect(mutationFn.mock.calls[0][0]).toBe(21);
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: queryKeys.listening.lessons });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: queryKeys.dashboard.home });
  });

  it('does not invalidate when the mutation fails', async () => {
    const client = new QueryClient({ defaultOptions: { mutations: { retry: 0 } } });
    const invalidateSpy = vi.spyOn(client, 'invalidateQueries');
    const mutationFn = vi.fn(async () => {
      throw new Error('boom');
    });

    const { result } = renderHook(
      () => useApiMutation(mutationFn, [queryKeys.listening.lessons]),
      { wrapper: makeWrapper(client) },
    );

    await act(async () => {
      await result.current.mutateAsync(undefined as never).catch(() => {});
    });

    expect(invalidateSpy).not.toHaveBeenCalled();
  });
});
