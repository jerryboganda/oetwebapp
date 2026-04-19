'use client';

import Link from 'next/link';
import { cloneElement, isValidElement } from 'react';
import type { ComponentProps, ElementType, HTMLAttributes, ReactNode } from 'react';
import { Sparkles } from 'lucide-react';
import { Card } from '@/components/ui/card';
import { Tabs, type Tab } from '@/components/ui/tabs';
import { Breadcrumbs, type BreadcrumbItem } from '@/components/ui/breadcrumbs';
import { LearnerPageHero, LearnerSurfaceCard, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import type {
  LearnerPageHeroModel,
  LearnerPageHeroHighlight,
  LearnerSurfaceAccent,
  LearnerSurfaceAction,
  LearnerSurfaceCardModel,
} from '@/lib/learner-surface';
import { cn } from '@/lib/utils';

// ═════════════════════════════════════════════════════════════════════════════
// Admin route-surface primitives.
//
// Design contract: DESIGN.md §4 (Component Stylings) + §5 (Layout Principles).
// These are the ONLY primitives admin pages should use. They delegate to the
// learner surface primitives so the admin console feels like the same product,
// per DESIGN.md §1 ("every other page should feel like the same product").
//
// Visual rhythm:
//   AdminRouteWorkspace  → outermost container, space-y rhythm between blocks
//   AdminRouteHero       → page-opening hero (uses full LearnerPageHero)
//   AdminRouteSectionHeader → same as hero but for sub-pages (variants below)
//   AdminRouteSummaryCard → single KPI card (delegates to LearnerSurfaceCard)
//   AdminRoutePanel      → content section card with title + optional actions
//   AdminRouteStatRow    → horizontal 3-up stat strip used inside panels
//   AdminRouteFreshnessBadge → "Updated …" metadata
// ═════════════════════════════════════════════════════════════════════════════

type LearnerSurfaceSectionHeaderProps = ComponentProps<typeof LearnerSurfaceSectionHeader>;

const toneAccentMap: Record<'default' | 'success' | 'warning' | 'danger' | 'info', LearnerSurfaceAccent> = {
  default: 'navy',
  success: 'emerald',
  warning: 'amber',
  danger: 'rose',
  info: 'blue',
};

export function AdminRouteWorkspace({ className, children, ...props }: HTMLAttributes<HTMLDivElement>) {
  // space-y-6 (not 8) to match learner dashboard cadence at similar widths.
  return (
    <div className={cn('space-y-6', className)} {...props}>
      {children}
    </div>
  );
}

export function AdminRouteHero(props: LearnerPageHeroModel) {
  return <LearnerPageHero {...props} />;
}

export function AdminRouteSectionHeader({
  action,
  actions,
  meta,
  eyebrow = 'Admin Workspace',
  icon = Sparkles,
  accent = 'navy',
  highlights,
  className,
  title,
  description,
}: LearnerSurfaceSectionHeaderProps & {
  actions?: ReactNode;
  meta?: string;
  icon?: ElementType | ReactNode;
  accent?: LearnerSurfaceAccent;
  highlights?: LearnerPageHeroHighlight[];
}) {
  const hasActions = Boolean(action) || Boolean(actions);
  const aside = hasActions || meta ? (
    <div className="rounded-2xl border border-border bg-background-light p-4 shadow-sm">
      {hasActions ? (
        <div className="flex flex-wrap items-center gap-2">
          {action}
          {actions}
        </div>
      ) : null}
      {meta ? (
        <p className={cn('text-xs font-semibold uppercase tracking-[0.14em] text-muted', hasActions && 'mt-3')}>
          {meta}
        </p>
      ) : null}
    </div>
  ) : undefined;

  return (
    <div className={className}>
      <LearnerPageHero
        title={title}
        description={description ?? ''}
        eyebrow={eyebrow}
        icon={icon}
        accent={accent}
        highlights={highlights}
        aside={aside}
      />
    </div>
  );
}

export function AdminRouteFreshnessBadge({ value }: { value?: string | null }) {
  return (
    <span className="text-xs text-muted">
      {value ? `Updated ${new Date(value).toLocaleString()}` : 'Freshness unavailable'}
    </span>
  );
}

export function AdminRouteSummaryCard({
  label,
  value,
  hint,
  accent,
  icon,
  tone = 'default',
  statusLabel,
  primaryAction,
  secondaryAction,
  className,
}: {
  label: string;
  value: string | number;
  hint?: string;
  accent?: LearnerSurfaceAccent;
  icon?: ElementType | ReactNode;
  tone?: 'default' | 'success' | 'warning' | 'danger' | 'info';
  statusLabel?: string;
  primaryAction?: LearnerSurfaceAction;
  secondaryAction?: LearnerSurfaceAction;
  className?: string;
}) {
  const normalizedIcon =
    !icon ? undefined
    : typeof icon === 'function' || typeof icon === 'string' ? (icon as ElementType)
    : isValidElement<{ className?: string }>(icon)
      ? ({ className: iconClassName }: { className?: string }) =>
          cloneElement(icon, {
            className: cn(icon.props.className, iconClassName),
          })
      : undefined;

  const card: LearnerSurfaceCardModel = {
    kind: 'status',
    sourceType: 'frontend_status',
    eyebrow: label,
    eyebrowIcon: normalizedIcon,
    title: String(value),
    description: hint ?? '',
    accent: accent ?? toneAccentMap[tone],
    statusLabel,
    primaryAction,
    secondaryAction,
  };

  return <LearnerSurfaceCard card={card} className={className} />;
}

export interface AdminRoutePanelProps {
  title?: string;
  description?: string;
  eyebrow?: string;
  /** Optional header actions shown on the right of the panel title. */
  actions?: ReactNode;
  children: ReactNode;
  className?: string;
  contentClassName?: string;
  /** Reduces vertical padding for denser panels (e.g. data tables). */
  dense?: boolean;
}

export function AdminRoutePanel({
  title,
  description,
  eyebrow,
  actions,
  children,
  className,
  contentClassName,
  dense = false,
}: AdminRoutePanelProps) {
  const hasHeader = Boolean(title || description || actions || eyebrow);
  return (
    <Card className={cn('overflow-hidden', className)} padding={dense ? 'md' : 'lg'}>
      <div className={cn('space-y-5', dense && 'space-y-4')}>
        {hasHeader ? (
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div className="space-y-1.5">
              {eyebrow ? (
                <p className="text-xs font-semibold uppercase tracking-[0.14em] text-muted">
                  {eyebrow}
                </p>
              ) : null}
              {title ? (
                <h2 className="text-xl font-bold tracking-tight text-navy">{title}</h2>
              ) : null}
              {description ? (
                <p className="max-w-3xl text-sm leading-6 text-muted">{description}</p>
              ) : null}
            </div>
            {actions ? (
              <div className="flex flex-wrap items-center gap-2">{actions}</div>
            ) : null}
          </div>
        ) : null}
        <div className={cn('space-y-4', contentClassName)}>{children}</div>
      </div>
    </Card>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// AdminRouteStatRow
//
// A horizontal strip of 2–5 labelled figures, sized so it breathes inside a
// panel instead of looking like a telemetry row. Replaces the pattern of:
//
//   <div className="grid gap-4 sm:grid-cols-3">
//     <div><p className="text-sm text-muted">Label</p><p className="text-xl">…</p></div>
//     …
//
// which was repeated across 20+ admin pages inconsistently.
// ─────────────────────────────────────────────────────────────────────────────

export interface AdminRouteStatRowItem {
  label: string;
  value: string | number;
  hint?: string;
  /** Optional coloured dot to the left of the label. */
  tone?: 'default' | 'success' | 'warning' | 'danger' | 'info';
}

const toneDotClass: Record<NonNullable<AdminRouteStatRowItem['tone']>, string> = {
  default: 'bg-slate-400',
  success: 'bg-emerald-500',
  warning: 'bg-amber-500',
  danger: 'bg-rose-500',
  info: 'bg-blue-500',
};

export function AdminRouteStatRow({ items, className }: { items: AdminRouteStatRowItem[]; className?: string }) {
  const cols =
    items.length === 2 ? 'sm:grid-cols-2'
    : items.length === 3 ? 'sm:grid-cols-3'
    : items.length === 4 ? 'sm:grid-cols-2 lg:grid-cols-4'
    : items.length === 5 ? 'sm:grid-cols-2 lg:grid-cols-5'
    : 'sm:grid-cols-3';
  return (
    <dl className={cn('grid gap-5', cols, className)}>
      {items.map((item, i) => (
        <div key={`${item.label}-${i}`} className="space-y-1">
          <dt className="flex items-center gap-1.5 text-xs font-semibold uppercase tracking-[0.08em] text-muted">
            {item.tone && item.tone !== 'default' ? (
              <span className={cn('h-1.5 w-1.5 rounded-full', toneDotClass[item.tone])} aria-hidden />
            ) : null}
            <span>{item.label}</span>
          </dt>
          <dd className="text-2xl font-bold text-navy leading-tight">{item.value}</dd>
          {item.hint ? (
            <dd className="text-xs text-muted leading-snug">{item.hint}</dd>
          ) : null}
        </div>
      ))}
    </dl>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// AdminRouteTabs — admin-flavoured wrapper around the shared Tabs primitive.
// Provides the same tokenised visual language as the learner Tabs but with
// an explicit `ariaLabel` hint for admin contexts.
// ─────────────────────────────────────────────────────────────────────────────

export interface AdminRouteTabsProps {
  tabs: Tab[];
  activeTab: string;
  onChange: (id: string) => void;
  className?: string;
  scrollable?: boolean;
  'aria-label'?: string;
}

export function AdminRouteTabs({ tabs, activeTab, onChange, className, scrollable, 'aria-label': ariaLabel }: AdminRouteTabsProps) {
  return (
    <div role="region" aria-label={ariaLabel} className={cn('flex w-full', className)}>
      <Tabs tabs={tabs} activeTab={activeTab} onChange={onChange} scrollable={scrollable} />
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// AdminTableCellLink — primary-coloured link for DataTable cells.
// Replaces raw <button className="text-primary hover:underline"> patterns
// across content / content-hierarchy / user tables.
// ─────────────────────────────────────────────────────────────────────────────

export interface AdminTableCellLinkProps {
  href?: string;
  onClick?: () => void;
  children: ReactNode;
  className?: string;
  /** When true renders muted rather than primary (for secondary cell links). */
  muted?: boolean;
}

export function AdminTableCellLink({ href, onClick, children, className, muted = false }: AdminTableCellLinkProps) {
  const base = cn(
    'inline-flex items-center gap-1 text-left font-medium transition-colors',
    muted ? 'text-muted hover:text-navy' : 'text-primary hover:text-primary-dark',
    'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 rounded-sm',
    className,
  );
  if (href) {
    return (
      <Link href={href} className={base}>
        {children}
      </Link>
    );
  }
  return (
    <button type="button" onClick={onClick} className={base}>
      {children}
    </button>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// AdminRoutePanelFooter — standardized footer for data panels.
//
// Design contract: DESIGN.md §15.5. Every admin panel backed by external
// data uses this to surface freshness / window / source consistently.
// ─────────────────────────────────────────────────────────────────────────────

export interface AdminRoutePanelFooterProps {
  updatedAt?: string | Date | null;
  window?: string;
  source?: string;
  className?: string;
  actions?: ReactNode;
}

function formatUpdatedAt(value: string | Date | null | undefined) {
  if (!value) return 'Freshness unavailable';
  try {
    const d = typeof value === 'string' ? new Date(value) : value;
    if (Number.isNaN(d.getTime())) return 'Freshness unavailable';
    return `Updated ${d.toLocaleString()}`;
  } catch {
    return 'Freshness unavailable';
  }
}

export function AdminRoutePanelFooter({
  updatedAt,
  window,
  source,
  className,
  actions,
}: AdminRoutePanelFooterProps) {
  const hasAnything = updatedAt || window || source || actions;
  if (!hasAnything) return null;
  return (
    <div
      className={cn(
        'mt-2 flex flex-col gap-2 border-t border-border pt-3 text-xs text-muted sm:flex-row sm:items-center sm:justify-between',
        className,
      )}
    >
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        {updatedAt ? <span>{formatUpdatedAt(updatedAt)}</span> : null}
        {window ? (
          <span className="inline-flex items-center gap-1">
            <span aria-hidden className="inline-block h-1 w-1 rounded-full bg-muted/60" />
            Window {window}
          </span>
        ) : null}
        {source ? (
          <span className="inline-flex items-center gap-1">
            <span aria-hidden className="inline-block h-1 w-1 rounded-full bg-muted/60" />
            Source {source}
          </span>
        ) : null}
      </div>
      {actions ? <div className="flex items-center gap-2">{actions}</div> : null}
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// AdminRouteBreadcrumbs — re-export for consistency when building pages.
// Admin layout supplies trail via TopNav; pages rarely need this, but content
// editors and wizard flows may render in-body breadcrumbs.
// ─────────────────────────────────────────────────────────────────────────────

export function AdminRouteBreadcrumbs(props: { items: BreadcrumbItem[]; className?: string }) {
  return <Breadcrumbs {...props} showHome homeHref="/admin" />;
}
