'use client';

import * as React from 'react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { ChevronDown, Menu } from 'lucide-react';

import { cn } from '@/lib/utils';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
  CardAction,
} from '@/components/admin/ui/card';
import { PageHeader, type PageHeaderProps } from '@/components/admin/ui/page-header';

import { AdminPageShell, type AdminPageShellProps } from './admin-page-shell';

/**
 * AdminSettingsLayout — Template D "Settings / Form"
 *
 * Use for settings-style pages with multiple grouped sections sharing a
 * navigation rail (e.g. `/admin/settings`, `/admin/profile`, edit forms with
 * tabbed sub-sections). Section order:
 *
 *   1. PageHeader (title + breadcrumbs + actions)
 *   2. Two-column grid:
 *      - Left: <SettingsNav /> (sticky, w-64) on >= lg
 *              collapses to a top-anchored disclosure on smaller widths.
 *      - Right: children — typically a stack of <SettingsSection /> cards.
 *
 * See: docs/admin-redesign/axelit-study/03-ADMIN_MACROSTRUCTURE.md §5 Template D
 */

/* ------------------------------------------------------------------ */
/* SettingsNav                                                         */
/* ------------------------------------------------------------------ */

export type SettingsNavItem = {
  label: string;
  href: string;
  badge?: React.ReactNode;
  icon?: React.ReactNode;
  /** Force matching even when the path is deeper than `href`. */
  matchPrefix?: string;
  /** Disable the link. */
  disabled?: boolean;
};

export interface SettingsNavProps
  extends Omit<React.HTMLAttributes<HTMLElement>, 'title'> {
  items: SettingsNavItem[];
  /** Optional section heading shown above the list. */
  title?: string;
  /** Override the active link detection. */
  activeHref?: string;
}

function isItemActive(
  item: SettingsNavItem,
  pathname: string | null,
  override?: string,
): boolean {
  if (override) return override === item.href;
  if (!pathname) return false;
  const match = item.matchPrefix ?? item.href;
  if (pathname === item.href) return true;
  return pathname.startsWith(match.endsWith('/') ? match : `${match}/`);
}

const SettingsNav = React.forwardRef<HTMLElement, SettingsNavProps>(
  ({ items, title, activeHref, className, ...rest }, ref) => {
    const pathname = usePathname();

    return (
      <nav
        ref={ref}
        aria-label={title ?? 'Settings sections'}
        data-slot="settings-nav"
        className={cn('flex flex-col gap-1', className)}
        {...rest}
      >
        {title ? (
          <h2 className="px-3 pb-2 text-xs font-semibold uppercase tracking-wider text-admin-fg-muted">
            {title}
          </h2>
        ) : null}
        <ul className="flex flex-col gap-1">
          {items.map((item) => {
            const active = isItemActive(item, pathname, activeHref);
            const baseClasses = cn(
              'group relative flex items-center justify-between gap-2',
              'rounded-admin px-3 py-2 text-sm font-medium',
              'transition-[background-color,color,box-shadow]',
              'duration-150 ease-[cubic-bezier(0.4,0,0.6,1)]',
              'motion-reduce:transition-none',
              'focus-visible:outline-none focus-visible:ring-2',
              'focus-visible:ring-[var(--admin-primary)] focus-visible:ring-offset-2',
              'focus-visible:ring-offset-[var(--admin-bg-page)]',
              active
                ? [
                    'bg-[var(--admin-state-selected)] text-admin-fg-strong',
                  ]
                : [
                    'text-admin-fg-default',
                    '[@media(hover:hover)]:hover:bg-[var(--admin-state-hover)]',
                    '[@media(hover:hover)]:hover:text-admin-fg-strong',
                  ],
              item.disabled && 'opacity-50 pointer-events-none',
            );

            return (
              <li key={`${item.href}-${item.label}`}>
                {item.disabled ? (
                  <span aria-disabled="true" className={baseClasses}>
                    <span className="inline-flex items-center gap-2 min-w-0">
                      {item.icon ? (
                        <span className="shrink-0" aria-hidden="true">
                          {item.icon}
                        </span>
                      ) : null}
                      <span className="truncate">{item.label}</span>
                    </span>
                    {item.badge ? <span className="shrink-0">{item.badge}</span> : null}
                  </span>
                ) : (
                  <Link
                    href={item.href}
                    aria-current={active ? 'page' : undefined}
                    className={baseClasses}
                  >
                    <span className="inline-flex items-center gap-2 min-w-0">
                      {item.icon ? (
                        <span className="shrink-0" aria-hidden="true">
                          {item.icon}
                        </span>
                      ) : null}
                      <span className="truncate">{item.label}</span>
                    </span>
                    {item.badge ? <span className="shrink-0">{item.badge}</span> : null}
                  </Link>
                )}
              </li>
            );
          })}
        </ul>
      </nav>
    );
  },
);
SettingsNav.displayName = 'SettingsNav';

/* ------------------------------------------------------------------ */
/* SettingsSection                                                     */
/* ------------------------------------------------------------------ */

export interface SettingsSectionProps
  extends Omit<React.HTMLAttributes<HTMLElement>, 'title'> {
  /** Section heading rendered inside the CardHeader. */
  title: React.ReactNode;
  /** Supporting description rendered below the title. */
  description?: React.ReactNode;
  /** Right-aligned action slot inside the CardHeader (e.g. "Save"). */
  actions?: React.ReactNode;
  /** Stable id for the heading — defaults to a slugified title. */
  id?: string;
  /** Override the CardContent class. */
  contentClassName?: string;
}

function slugify(input: React.ReactNode): string {
  if (typeof input === 'string') {
    return input
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/(^-|-$)/g, '');
  }
  return `section-${Math.random().toString(36).slice(2, 8)}`;
}

const SettingsSection = React.forwardRef<HTMLElement, SettingsSectionProps>(
  (
    { title, description, actions, id, className, contentClassName, children, ...rest },
    ref,
  ) => {
    const headingId = id ?? slugify(title);
    return (
      <section
        ref={ref}
        aria-labelledby={headingId}
        data-slot="settings-section"
        className={cn('scroll-mt-24', className)}
        {...rest}
      >
        <Card>
          <CardHeader>
            <div className="min-w-0">
              <CardTitle id={headingId}>{title}</CardTitle>
              {description ? <CardDescription>{description}</CardDescription> : null}
            </div>
            {actions ? <CardAction>{actions}</CardAction> : null}
          </CardHeader>
          <CardContent className={cn('pt-0', contentClassName)}>
            {children}
          </CardContent>
        </Card>
      </section>
    );
  },
);
SettingsSection.displayName = 'SettingsSection';

/* ------------------------------------------------------------------ */
/* Mobile nav (collapsible disclosure shown < lg)                      */
/* ------------------------------------------------------------------ */

function MobileSettingsNav({
  sidebar,
  navLabel,
}: {
  sidebar: React.ReactNode;
  navLabel: string;
}) {
  const [open, setOpen] = React.useState(false);
  const panelId = React.useId();

  return (
    <div className="lg:hidden">
      <button
        type="button"
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => setOpen((v) => !v)}
        className={cn(
          'flex w-full items-center justify-between gap-2',
          'rounded-admin-lg border border-admin-border bg-admin-bg-surface',
          'px-4 py-3 text-sm font-medium text-admin-fg-default',
          'shadow-admin-sm',
          'focus-visible:outline-none focus-visible:ring-2',
          'focus-visible:ring-[var(--admin-primary)] focus-visible:ring-offset-2',
          'focus-visible:ring-offset-[var(--admin-bg-page)]',
        )}
      >
        <span className="inline-flex items-center gap-2">
          <Menu className="h-4 w-4" aria-hidden="true" />
          {navLabel}
        </span>
        <ChevronDown
          aria-hidden="true"
          className={cn(
            'h-4 w-4 transition-transform duration-200 motion-reduce:transition-none',
            open && 'rotate-180',
          )}
        />
      </button>
      <div
        id={panelId}
        hidden={!open}
        className={cn(
          'mt-2 rounded-admin-lg border border-admin-border bg-admin-bg-surface',
          'p-2 shadow-admin-sm',
        )}
      >
        {sidebar}
      </div>
    </div>
  );
}

/* ------------------------------------------------------------------ */
/* Layout                                                              */
/* ------------------------------------------------------------------ */

export interface AdminSettingsLayoutProps
  extends Omit<AdminPageShellProps, 'children' | 'title'> {
  /** Page title — required. */
  title: PageHeaderProps['title'];
  description?: PageHeaderProps['description'];
  breadcrumbs?: PageHeaderProps['breadcrumbs'];
  actions?: PageHeaderProps['actions'];
  eyebrow?: PageHeaderProps['eyebrow'];

  /** Navigation rail (typically <SettingsNav />). Required for two-column layout. */
  sidebar?: React.ReactNode;
  /** Label used by the mobile disclosure trigger. */
  mobileNavLabel?: string;
  /** Override the sidebar column width (default w-64). */
  sidebarWidthClassName?: string;
  /** Override the content stack spacing (default space-y-6). */
  contentClassName?: string;

  /** Sections — typically a stack of <SettingsSection /> cards. */
  children: React.ReactNode;

  /**
   * Backward-compat: decorative icon shown next to the page title. Accepted
   * here so legacy call sites compile but currently rendered as a no-op —
   * the PageHeader eyebrow carries the same semantic meaning in the new DS.
   */
  icon?: React.ReactNode;
  /** Backward-compat: optional href for a "back" affordance in the header. */
  backHref?: string;
}

const AdminSettingsLayout = React.forwardRef<
  HTMLDivElement,
  AdminSettingsLayoutProps
>(
  (
    {
      title,
      description,
      breadcrumbs,
      actions,
      eyebrow,
      sidebar,
      mobileNavLabel = 'Sections',
      sidebarWidthClassName,
      contentClassName,
      className,
      children,
      // Accepted for backward-compat; see prop docs.
      icon: _icon,
      backHref: _backHref,
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

        {sidebar ? (
          <div
            data-slot="settings-grid"
            className="flex flex-col gap-6 lg:flex-row lg:items-start lg:gap-8"
          >
            {/* Mobile (collapsed disclosure) */}
            <MobileSettingsNav sidebar={sidebar} navLabel={mobileNavLabel} />

            {/* Desktop (sticky rail) */}
            <aside
              data-slot="settings-sidebar"
              className={cn(
                'hidden lg:block shrink-0',
                'sticky top-24 self-start',
                sidebarWidthClassName ?? 'lg:w-64',
              )}
            >
              {sidebar}
            </aside>

            <div
              data-slot="settings-content"
              className={cn('flex-1 min-w-0 space-y-6', contentClassName)}
            >
              {children}
            </div>
          </div>
        ) : (
          <div
            data-slot="settings-content"
            className={cn('space-y-6', contentClassName)}
          >
            {children}
          </div>
        )}
      </AdminPageShell>
    );
  },
);
AdminSettingsLayout.displayName = 'AdminSettingsLayout';

export { AdminSettingsLayout, SettingsNav, SettingsSection };
