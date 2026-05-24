import * as React from 'react';

import { cn } from '@/lib/utils';
import { Card, CardContent } from '@/components/admin/ui/card';
import { PageHeader, type PageHeaderProps } from '@/components/admin/ui/page-header';

import { AdminPageShell, type AdminPageShellProps } from './admin-page-shell';

/**
 * AdminTableLayout — Template C "Data Table"
 *
 * Use for tabular pages where the focal content is a DataTable
 * (e.g. `/admin/users`, `/admin/audit-logs`, `/admin/billing`).
 * Section order:
 *
 *   1. PageHeader (title + breadcrumbs + actions like "Invite User")
 *   2. Card       (single surface wrapping the table)
 *   3. CardContent renders children verbatim — DataTable handles its own
 *      internal padding, toolbar, pagination, and empty state.
 *
 * See: docs/admin-redesign/axelit-study/03-ADMIN_MACROSTRUCTURE.md §5 Template C
 */

export interface AdminTableLayoutProps
  extends Omit<AdminPageShellProps, 'children' | 'title'> {
  /** Page title — required. */
  title: PageHeaderProps['title'];
  description?: PageHeaderProps['description'];
  breadcrumbs?: PageHeaderProps['breadcrumbs'];
  actions?: PageHeaderProps['actions'];
  eyebrow?: PageHeaderProps['eyebrow'];

  /**
   * Optional banner rendered above the table card. Use for inline filters,
   * tab strips, or status callouts that should sit outside the table surface.
   */
  banner?: React.ReactNode;

  /** Optional extra slot rendered below the table card. */
  footer?: React.ReactNode;

  /** Override the Card className (rare). */
  cardClassName?: string;
  /** Override the CardContent className (rare). */
  contentClassName?: string;

  /** The DataTable (or other table-shaped child) to render inside the card. */
  children: React.ReactNode;
}

const AdminTableLayout = React.forwardRef<
  HTMLDivElement,
  AdminTableLayoutProps
>(
  (
    {
      title,
      description,
      breadcrumbs,
      actions,
      eyebrow,
      banner,
      footer,
      cardClassName,
      contentClassName,
      className,
      children,
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

        {banner ? (
          <div data-slot="table-banner">{banner}</div>
        ) : null}

        <Card
          data-slot="table-card"
          className={cn('overflow-hidden', cardClassName)}
        >
          {/* No padding override — DataTable owns its internal spacing. */}
          <CardContent
            data-slot="table-card-content"
            className={cn('p-0 pt-0', contentClassName)}
          >
            {children}
          </CardContent>
        </Card>

        {footer ? (
          <div data-slot="table-footer">{footer}</div>
        ) : null}
      </AdminPageShell>
    );
  },
);
AdminTableLayout.displayName = 'AdminTableLayout';

export { AdminTableLayout };
