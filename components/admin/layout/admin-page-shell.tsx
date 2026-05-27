'use client';

import * as React from 'react';

import { cn } from '@/lib/utils';
import { EmptyState } from '@/components/admin/ui/empty-state';

/**
 * AdminPageShell — minimal inner content shell shared by every admin layout template.
 *
 * NOTE: This does NOT replace `app/admin/layout.tsx` (which owns the sidebar,
 * topbar, and route-level chrome). It wraps the main content region only and
 * provides four cross-cutting concerns:
 *   1. Consistent container padding and max-width (1440px).
 *   2. Skip-to-content link (a11y — first focusable element in the page body).
 *   3. Suspense fallback so layouts can lazily render their content slots.
 *   4. Error boundary that catches per-page exceptions and surfaces an
 *      EmptyState in error variant rather than blowing up the shell.
 *
 * See: docs/admin-redesign/axelit-study/03-ADMIN_MACROSTRUCTURE.md §5
 * See: docs/admin-redesign/axelit-study/12-REFACTORING-PLAYBOOK.md §4
 */

const SHELL_CONTAINER = [
  'mx-auto w-full max-w-[1440px]',
  'px-4 sm:px-6 lg:px-8',
  'py-6 sm:py-8',
  'space-y-6',
] as const;

const SKIP_LINK_CLASSES = [
  // Hidden until focused — restores the visible jump target for keyboard users.
  'sr-only focus:not-sr-only',
  'focus:absolute focus:left-4 focus:top-4 focus:z-50',
  'focus:rounded-admin-lg focus:bg-admin-bg-surface',
  'focus:px-4 focus:py-2 focus:shadow-admin-lg',
  'focus:text-sm focus:font-medium focus:text-admin-primary',
  'focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]',
  'focus:ring-offset-2 focus:ring-offset-[var(--admin-bg-page)]',
] as const;

/* ------------------------------------------------------------------ */
/* Error boundary                                                      */
/* ------------------------------------------------------------------ */

type ErrorBoundaryProps = {
  children: React.ReactNode;
  fallback?: React.ReactNode;
};

type ErrorBoundaryState = {
  hasError: boolean;
  error?: Error;
};

class AdminPageShellErrorBoundary extends React.Component<
  ErrorBoundaryProps,
  ErrorBoundaryState
> {
  state: ErrorBoundaryState = { hasError: false };

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, info: React.ErrorInfo) {
    // Surface to console in dev; production telemetry hook lives upstream.
    if (typeof window !== 'undefined') {
      // eslint-disable-next-line no-console
      console.error('[AdminPageShell] caught error', error, info);
    }
  }

  render() {
    if (this.state.hasError) {
      if (this.props.fallback) return this.props.fallback;
      return (
        <EmptyState
          variant="error"
          size="lg"
          title="Something went wrong"
          description="An unexpected error occurred while rendering this page. Try reloading or contact support if the problem persists."
          primaryAction={{
            label: 'Reload page',
            onClick: () => {
              if (typeof window !== 'undefined') window.location.reload();
            },
          }}
          errorRef={this.state.error?.message}
        />
      );
    }
    return this.props.children;
  }
}

/* ------------------------------------------------------------------ */
/* Suspense fallback                                                   */
/* ------------------------------------------------------------------ */

function DefaultLoadingFallback() {
  return (
    <div
      role="status"
      aria-live="polite"
      aria-label="Loading page content"
      className="space-y-4 motion-safe:animate-pulse motion-reduce:animate-none"
    >
      <div className="h-9 w-1/3 rounded-admin bg-admin-bg-subtle" />
      <div className="h-4 w-2/3 rounded-admin bg-admin-bg-subtle" />
      <div className="mt-6 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {Array.from({ length: 4 }).map((_, i) => (
          <div key={i} className="h-28 rounded-admin-lg bg-admin-bg-subtle" />
        ))}
      </div>
      <div className="h-64 rounded-admin-lg bg-admin-bg-subtle" />
    </div>
  );
}

/* ------------------------------------------------------------------ */
/* Public component                                                    */
/* ------------------------------------------------------------------ */

export interface AdminPageShellProps
  extends React.HTMLAttributes<HTMLDivElement> {
  children: React.ReactNode;
  /** Override the default skeleton shown during Suspense resolution. */
  loadingFallback?: React.ReactNode;
  /** Override the default error fallback rendered by the boundary. */
  errorFallback?: React.ReactNode;
  /** Disable the skip-to-content link (rarely needed). */
  hideSkipLink?: boolean;
  /** Override the inner `<main>` id used by the skip link target. */
  mainId?: string;
  /**
   * Optional accessible label applied to the inner `<main>` element. Use this
   * when the page's primary heading lives elsewhere in the document so screen
   * readers (and a11y-aware Playwright/RTL queries) can still identify the
   * landmark by name. Defaults to "Admin page" if not provided.
   */
  mainAriaLabel?: string;
}

const AdminPageShell = React.forwardRef<HTMLDivElement, AdminPageShellProps>(
  (
    {
      children,
      className,
      loadingFallback,
      errorFallback,
      hideSkipLink = false,
      mainId = 'admin-main-content',
      mainAriaLabel,
      ...rest
    },
    ref,
  ) => {
    return (
      <div ref={ref} data-slot="admin-page-shell" {...rest}>
        {hideSkipLink ? null : (
          <a href={`#${mainId}`} className={cn(SKIP_LINK_CLASSES)}>
            Skip to main content
          </a>
        )}
        <main
          id={mainId}
          tabIndex={-1}
          aria-label={mainAriaLabel ?? 'Admin page'}
          className={cn(SHELL_CONTAINER, 'focus:outline-none', className)}
        >
          <AdminPageShellErrorBoundary fallback={errorFallback}>
            <React.Suspense fallback={loadingFallback ?? <DefaultLoadingFallback />}>
              {children}
            </React.Suspense>
          </AdminPageShellErrorBoundary>
        </main>
      </div>
    );
  },
);
AdminPageShell.displayName = 'AdminPageShell';

export { AdminPageShell };
