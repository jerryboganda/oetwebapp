import React from 'react';
import { render, type RenderOptions } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AppRouterContext } from 'next/dist/shared/lib/app-router-context.shared-runtime';
import {
  PathnameContext,
  SearchParamsContext,
  PathParamsContext,
} from 'next/dist/shared/lib/hooks-client-context.shared-runtime';

export type MockRouterOverrides = {
  push?: ReturnType<typeof vi.fn>;
  replace?: ReturnType<typeof vi.fn>;
  back?: ReturnType<typeof vi.fn>;
  forward?: ReturnType<typeof vi.fn>;
  refresh?: ReturnType<typeof vi.fn>;
  prefetch?: ReturnType<typeof vi.fn>;
  pathname?: string;
  searchParams?: URLSearchParams;
  params?: Record<string, string | string[]>;
};

export function createMockRouter(overrides: MockRouterOverrides = {}) {
  return {
    push: overrides.push ?? vi.fn(),
    replace: overrides.replace ?? vi.fn(),
    back: overrides.back ?? vi.fn(),
    forward: overrides.forward ?? vi.fn(),
    refresh: overrides.refresh ?? vi.fn(),
    prefetch: overrides.prefetch ?? vi.fn(),
  };
}

/** Fresh, test-scoped QueryClient — no retries so a failing/unmocked fetch fails fast instead of hanging a test. */
function createTestQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false, gcTime: Infinity },
      mutations: { retry: false },
    },
  });
}

export function NextRouterProvider({
  children,
  router,
  pathname = '/',
  searchParams = new URLSearchParams(),
  params = {},
  queryClient,
}: {
  children: React.ReactNode;
  router?: MockRouterOverrides;
  pathname?: string;
  searchParams?: URLSearchParams;
  params?: Record<string, string | string[]>;
  /** Pass an existing client to assert on/seed its cache; otherwise a fresh one is created per render. */
  queryClient?: QueryClient;
}) {
  const mockRouter = createMockRouter(router);
  // Real app-shell chrome (Sidebar/BottomNav/TopNav) reads entitlement/streak/xp
  // via TanStack Query (see hooks/use-enabled-modules.ts, lib/query/hooks.ts),
  // same as it's always wrapped in QueryProvider in app/providers.tsx. Provide
  // one here too so components rendered through this router-only helper don't
  // need every test to hand-roll its own QueryClientProvider.
  const client = queryClient ?? createTestQueryClient();
  return (
    <QueryClientProvider client={client}>
      <AppRouterContext.Provider value={mockRouter as any}>
        <PathnameContext.Provider value={pathname}>
          <SearchParamsContext.Provider value={searchParams}>
            <PathParamsContext.Provider value={params}>
              {children}
            </PathParamsContext.Provider>
          </SearchParamsContext.Provider>
        </PathnameContext.Provider>
      </AppRouterContext.Provider>
    </QueryClientProvider>
  );
}

/**
 * Custom render that wraps components in Next.js App Router context providers
 * plus a QueryClientProvider (see NextRouterProvider). Use this for any
 * component that calls useRouter, usePathname, useSearchParams, useParams, or
 * TanStack Query hooks.
 */
export function renderWithRouter(
  ui: React.ReactElement,
  options?: RenderOptions & {
    router?: MockRouterOverrides;
    pathname?: string;
    searchParams?: URLSearchParams;
    params?: Record<string, string | string[]>;
    queryClient?: QueryClient;
  },
) {
  const { router, pathname, searchParams, params, queryClient, ...renderOptions } = options ?? {};
  return render(ui, {
    wrapper: ({ children }) => (
      <NextRouterProvider router={router} pathname={pathname} searchParams={searchParams} params={params} queryClient={queryClient}>
        {children}
      </NextRouterProvider>
    ),
    ...renderOptions,
  });
}
