'use client';

import Link from 'next/link';
import { ArrowLeft, Store } from 'lucide-react';
import { AdminPageShell } from '@/components/admin/layout/admin-page-shell';
import { Button } from '@/components/admin/ui/button';
import { PageHeader } from '@/components/admin/ui/page-header';
import { NoBillingPermission } from '@/components/admin/billing/no-billing-permission';
import { StorefrontEditor } from '@/components/admin/billing/storefront-editor';
import { useAuth } from '@/contexts/auth-context';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';

export default function AdminStorefrontPage() {
  const { user } = useAuth();
  const canReadBilling = hasPermission(user?.adminPermissions, AdminPermission.BillingRead, AdminPermission.BillingWrite);

  if (!user) return null;
  if (!canReadBilling) return <NoBillingPermission />;

  return (
    <AdminPageShell>
      <PageHeader
        eyebrow="Billing"
        title="Catalog storefront"
        description="Configure how the Subscriptions and Packages catalog looks: hero copy, category order and visibility, the add-ons legend, section toggles, the footer call-to-action, and per-package marketing (tagline, feature bullets, icon, featured ribbon)."
        icon={<Store aria-hidden className="h-5 w-5" />}
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Billing', href: '/admin/billing' },
          { label: 'Storefront' },
        ]}
        actions={
          <Button asChild variant="ghost" size="sm">
            <Link href="/admin/billing">
              <ArrowLeft className="mr-1.5 h-4 w-4" /> Back to Billing
            </Link>
          </Button>
        }
      />
      <StorefrontEditor />
    </AdminPageShell>
  );
}
