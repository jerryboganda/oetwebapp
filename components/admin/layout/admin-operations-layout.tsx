import * as React from 'react';

import { cn } from '@/lib/utils';
import { PageHeader, type PageHeaderProps } from '@/components/admin/ui/page-header';

import { AdminPageShell, type AdminPageShellProps } from './admin-page-shell';

/**
 * AdminOperationsLayout — Template A "Operations Overview"
 *
 * Use for dashboard-style pages where the page surfaces KPIs, signals, and
 * monitoring widgets (e.g. the main `/admin` landing, launch readiness,
 * platform health). Section order is fixed:
 *
 *   1. PageHeader (title + breadcrumbs + actions)
 *   2. KPI strip      (full-width row of metric tiles)
 *   3. Primary grid   (main content — typically the largest tiles)
 *   4. Secondary grid (supporting / supplementary content)
 *
 * See: docs/admin-redesign/axelit-study/03-ADMIN_MACROSTRUCTURE.md §5 Template A
 */

/* ------------------------------------------------------------------ */
/* KPI strip                                                           */
/* ------------------------------------------------------------------ */

export interface KpiStripProps extends React.HTMLAttributes<HTMLDivElement> {}

const KpiStrip = React.forwardRef<HTMLDivElement, KpiStripProps>(
  ({ className, children, ...rest }, ref) => (
    <div
      ref={ref}
      role="group"
      aria-label="Key performance indicators"
      data-slot="kpi-strip"
      className={cn(
        'grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4',
        'gap-3 sm:gap-4',
        className,
      )}
      {...rest}
    >
      {children}
    </div>
  ),
);
KpiStrip.displayName = 'KpiStrip';

/* ------------------------------------------------------------------ */
/* Bento grid                                                          */
/* ------------------------------------------------------------------ */

export interface BentoGridProps extends React.HTMLAttributes<HTMLDivElement> {
  /**
   * Total column count for the grid (responsive override via CSS).
   * Defaults to 12 — `<BentoCell span={...} />` divides cells across these.
   */
  columns?: number;
}

const BentoGrid = React.forwardRef<HTMLDivElement, BentoGridProps>(
  ({ className, columns = 12, style, children, ...rest }, ref) => (
    <div
      ref={ref}
      data-slot="bento-grid"
      style={{
        ['--bento-cols' as string]: String(columns),
        gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))`,
        ...style,
      }}
      className={cn('grid gap-4 sm:gap-5', className)}
      {...rest}
    >
      {children}
    </div>
  ),
);
BentoGrid.displayName = 'BentoGrid';

/* ------------------------------------------------------------------ */
/* Bento cell                                                          */
/* ------------------------------------------------------------------ */

type BentoSpan = {
  default?: number;
  sm?: number;
  md?: number;
  lg?: number;
  xl?: number;
};

export interface BentoCellProps extends React.HTMLAttributes<HTMLDivElement> {
  /**
   * Column span per breakpoint. The grid uses a 12-column track by default,
   * so `{ default: 12, lg: 8 }` renders full-width on mobile and 8/12 on lg.
   */
  span?: BentoSpan | number;
  /** Optional row span (rare). */
  rowSpan?: number;
}

const SPAN_TO_CLASS: Record<string, Record<number, string>> = {
  default: {
    1: 'col-span-1', 2: 'col-span-2', 3: 'col-span-3', 4: 'col-span-4',
    5: 'col-span-5', 6: 'col-span-6', 7: 'col-span-7', 8: 'col-span-8',
    9: 'col-span-9', 10: 'col-span-10', 11: 'col-span-11', 12: 'col-span-12',
  },
  sm: {
    1: 'sm:col-span-1', 2: 'sm:col-span-2', 3: 'sm:col-span-3', 4: 'sm:col-span-4',
    5: 'sm:col-span-5', 6: 'sm:col-span-6', 7: 'sm:col-span-7', 8: 'sm:col-span-8',
    9: 'sm:col-span-9', 10: 'sm:col-span-10', 11: 'sm:col-span-11', 12: 'sm:col-span-12',
  },
  md: {
    1: 'md:col-span-1', 2: 'md:col-span-2', 3: 'md:col-span-3', 4: 'md:col-span-4',
    5: 'md:col-span-5', 6: 'md:col-span-6', 7: 'md:col-span-7', 8: 'md:col-span-8',
    9: 'md:col-span-9', 10: 'md:col-span-10', 11: 'md:col-span-11', 12: 'md:col-span-12',
  },
  lg: {
    1: 'lg:col-span-1', 2: 'lg:col-span-2', 3: 'lg:col-span-3', 4: 'lg:col-span-4',
    5: 'lg:col-span-5', 6: 'lg:col-span-6', 7: 'lg:col-span-7', 8: 'lg:col-span-8',
    9: 'lg:col-span-9', 10: 'lg:col-span-10', 11: 'lg:col-span-11', 12: 'lg:col-span-12',
  },
  xl: {
    1: 'xl:col-span-1', 2: 'xl:col-span-2', 3: 'xl:col-span-3', 4: 'xl:col-span-4',
    5: 'xl:col-span-5', 6: 'xl:col-span-6', 7: 'xl:col-span-7', 8: 'xl:col-span-8',
    9: 'xl:col-span-9', 10: 'xl:col-span-10', 11: 'xl:col-span-11', 12: 'xl:col-span-12',
  },
};

function resolveSpanClasses(span: BentoSpan | number | undefined): string {
  if (span == null) return 'col-span-12';
  if (typeof span === 'number') {
    return SPAN_TO_CLASS.default[span] ?? 'col-span-12';
  }
  const out: string[] = [];
  (['default', 'sm', 'md', 'lg', 'xl'] as const).forEach((bp) => {
    const v = span[bp];
    if (v && SPAN_TO_CLASS[bp][v]) out.push(SPAN_TO_CLASS[bp][v]);
  });
  if (out.length === 0) out.push('col-span-12');
  return out.join(' ');
}

const ROW_SPAN_CLASS: Record<number, string> = {
  1: 'row-span-1', 2: 'row-span-2', 3: 'row-span-3', 4: 'row-span-4',
  5: 'row-span-5', 6: 'row-span-6',
};

const BentoCell = React.forwardRef<HTMLDivElement, BentoCellProps>(
  ({ span, rowSpan, className, children, ...rest }, ref) => (
    <div
      ref={ref}
      data-slot="bento-cell"
      className={cn(
        resolveSpanClasses(span),
        rowSpan ? ROW_SPAN_CLASS[rowSpan] : undefined,
        className,
      )}
      {...rest}
    >
      {children}
    </div>
  ),
);
BentoCell.displayName = 'BentoCell';

/* ------------------------------------------------------------------ */
/* Layout                                                              */
/* ------------------------------------------------------------------ */

export interface AdminOperationsLayoutProps
  extends Omit<AdminPageShellProps, 'children' | 'title'> {
  /** Page title — required, drives `<h1>` in PageHeader. */
  title: PageHeaderProps['title'];
  /** Short page subtitle / lede shown below the title. */
  description?: PageHeaderProps['description'];
  /** Crumb trail above the title. */
  breadcrumbs?: PageHeaderProps['breadcrumbs'];
  /** Right-aligned action buttons in the header. */
  actions?: PageHeaderProps['actions'];
  /** Header eyebrow / kicker label. */
  eyebrow?: PageHeaderProps['eyebrow'];

  /** Full-width KPI strip rendered immediately under the header. */
  kpis?: React.ReactNode;
  /** Primary content grid — usually a BentoGrid with the largest tiles. */
  primaryGrid?: React.ReactNode;
  /** Secondary content grid — supplementary widgets and follow-up tiles. */
  secondaryGrid?: React.ReactNode;

  /** Optional extra slot rendered after the secondary grid (escape hatch). */
  footer?: React.ReactNode;
}

const AdminOperationsLayout = React.forwardRef<
  HTMLDivElement,
  AdminOperationsLayoutProps
>(
  (
    {
      title,
      description,
      breadcrumbs,
      actions,
      eyebrow,
      kpis,
      primaryGrid,
      secondaryGrid,
      footer,
      className,
      ...shellProps
    },
    ref,
  ) => {
    return (
      <AdminPageShell ref={ref} className={className} {...shellProps}>
        <PageHeader
          title={title}
          description={description}
          breadcrumbs={breadcrumbs}
          actions={actions}
          eyebrow={eyebrow}
        />
        {kpis ? <section aria-labelledby="kpi-strip-heading">{kpis}</section> : null}
        {primaryGrid ? (
          <section
            aria-label="Primary content"
            data-slot="operations-primary"
          >
            {primaryGrid}
          </section>
        ) : null}
        {secondaryGrid ? (
          <section
            aria-label="Secondary content"
            data-slot="operations-secondary"
          >
            {secondaryGrid}
          </section>
        ) : null}
        {footer ? <footer data-slot="operations-footer">{footer}</footer> : null}
      </AdminPageShell>
    );
  },
);
AdminOperationsLayout.displayName = 'AdminOperationsLayout';

export { AdminOperationsLayout, KpiStrip, BentoGrid, BentoCell };
