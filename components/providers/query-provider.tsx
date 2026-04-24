'use client';

import { useState, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider, isServer } from '@tanstack/react-query';

/**
 * App-wide TanStack Query provider.
 *
 * Rationale:
 * - Centralized fetch cache, dedup, background refetch, and stale-while-revalidate
 *   semantics so ~40 ad-hoc `fetch()` call sites can be migrated incrementally
 *   without a big-bang rewrite.
 * - Per-client instance is created inside `useState` to avoid sharing cache across
 *   users in SSR (same pattern Vercel recommends for App Router).
 *
 * Safe defaults for OET:
 * - `staleTime: 30_000` — prevents refetch storms on fast tab switching but stays
 *   fresh enough for dashboards, progress, and notification-adjacent data.
 * - `gcTime: 5 * 60_000` — retain cache for 5 minutes after unmount so
 *   back-navigation is instant.
 * - `refetchOnWindowFocus: false` — OET screens often have timers / audio
 *   streams; refetch-on-focus would cause user-visible flicker during practice.
 * - `retry: 1` — one gentle retry; long retry chains mask real backend errors
 *   (and AGENTS.md mandates visible error paths).
 */
function makeQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        gcTime: 5 * 60_000,
        refetchOnWindowFocus: false,
        retry: 1,
      },
      mutations: {
        retry: 0,
      },
    },
  });
}

let browserQueryClient: QueryClient | undefined;

function getQueryClient(): QueryClient {
  if (isServer) {
    // Always make a new one on the server; never cache across requests.
    return makeQueryClient();
  }
  // Re-use one per browser tab.
  if (!browserQueryClient) {
    browserQueryClient = makeQueryClient();
  }
  return browserQueryClient;
}

export function QueryProvider({ children }: { children: ReactNode }) {
  // `useState` init runs exactly once, memoizing the client per render tree.
  const [client] = useState(() => getQueryClient());
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}
