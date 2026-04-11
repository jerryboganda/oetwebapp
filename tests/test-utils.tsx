import React from 'react';
import { render, type RenderOptions } from '@testing-library/react';
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

export function NextRouterProvider({
  children,
  router,
  pathname = '/',
  searchParams = new URLSearchParams(),
  params = {},
}: {
  children: React.ReactNode;
  router?: MockRouterOverrides;
  pathname?: string;
  searchParams?: URLSearchParams;
  params?: Record<string, string | string[]>;
}) {
  const mockRouter = createMockRouter(router);
  return (
    <AppRouterContext.Provider value={mockRouter as any}>
      <PathnameContext.Provider value={pathname}>
        <SearchParamsContext.Provider value={searchParams}>
          <PathParamsContext.Provider value={params}>
            {children}
          </PathParamsContext.Provider>
        </SearchParamsContext.Provider>
      </PathnameContext.Provider>
    </AppRouterContext.Provider>
  );
}

/**
 * Custom render that wraps components in Next.js App Router context providers.
 * Use this for any component that calls useRouter, usePathname, useSearchParams, or useParams.
 */
export function renderWithRouter(
  ui: React.ReactElement,
  options?: RenderOptions & { router?: MockRouterOverrides; pathname?: string; searchParams?: URLSearchParams; params?: Record<string, string | string[]> },
) {
  const { router, pathname, searchParams, params, ...renderOptions } = options ?? {};
  return render(ui, {
    wrapper: ({ children }) => (
      <NextRouterProvider router={router} pathname={pathname} searchParams={searchParams} params={params}>
        {children}
      </NextRouterProvider>
    ),
    ...renderOptions,
  });
}
