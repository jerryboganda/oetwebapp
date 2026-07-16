'use client';

import Link from 'next/link';
import { ArrowLeft, Package } from 'lucide-react';
import { AdminPageShell } from '@/components/admin/layout/admin-page-shell';
import { Button } from '@/components/admin/ui/button';
import { PageHeader } from '@/components/admin/ui/page-header';
import { NoBillingPermission } from '@/components/admin/billing/no-billing-permission';
import { SubscriptionsPackagesEditor } from '@/components/admin/billing/subscriptions-packages-editor';
import { useAuth } from '@/contexts/auth-context';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';

export default function AdminSubscriptionsPackagesPage() {
  const { user } = useAuth();
  const canReadBilling = hasPermission(user?.adminPermissions, AdminPermission.BillingRead, AdminPermission.BillingWrite);

  if (!user) return null;
  if (!canReadBilling) return <NoBillingPermission />;

  return (
    <AdminPageShell>
      <PageHeader
        eyebrow="Billing"
        title="Subscriptions & Packages"
        description="Edit every package card shown to learners on the dashboard Subscriptions & Packages page: names, format line, descriptions, meta chips, badges, tick-list features, best-for text, and section headings."
        icon={<Package aria-hidden className="h-5 w-5" />}
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Billing', href: '/admin/billing' },
          { label: 'Subscriptions & Packages' },
        ]}
        actions={
          <Button asChild variant="ghost" size="sm">
            <Link href="/admin/billing">
              <ArrowLeft className="mr-1.5 h-4 w-4" /> Back to Billing
            </Link>
          </Button>
        }
      />
      <SubscriptionsPackagesEditor />
    </AdminPageShell>
  );
}
