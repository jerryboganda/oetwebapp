'use client';

import { Suspense, useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { CreditCard, FileText, Store } from 'lucide-react';

import { AdminPageShell } from '@/components/admin/layout/admin-page-shell';
import { Button } from '@/components/admin/ui/button';
import { PageHeader } from '@/components/admin/ui/page-header';
import { InlineAlert } from '@/components/ui/alert';
import { NoBillingPermission } from '@/components/admin/billing/no-billing-permission';
import { WalletTiersEditor } from '@/components/admin/billing/wallet-tiers-editor';
import { AiPackageEditor } from '@/components/admin/billing/ai-package-editor';
import { BillingCopyEditor } from '@/components/admin/billing/billing-copy-editor';
import { PlanCatalogEditor } from '@/components/admin/billing/plan-catalog-editor';
import { AddOnCatalogEditor } from '@/components/admin/billing/addon-catalog-editor';
import {
  fetchAdminWalletTiers,
  replaceAdminWalletTiers,
  type AdminWalletTierInput,
  type AdminWalletTiersResponse,
} from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { useAuth } from '@/contexts/auth-context';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';

type TabId = 'plans' | 'addons' | 'ai' | 'wallet' | 'copy';

const TABS: Array<{ id: TabId; label: string }> = [
  { id: 'plans', label: 'Plans' },
  { id: 'addons', label: 'Add-ons' },
  { id: 'ai', label: 'AI Packages' },
  { id: 'wallet', label: 'Wallet Tiers' },
  { id: 'copy', label: 'Page Copy' },
];

function WalletTab({ canWrite }: { canWrite: boolean }) {
  const [data, setData] = useState<AdminWalletTiersResponse | null>(null);
  const [status, setStatus] = useState<'loading' | 'success' | 'error'>('loading');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const response = await fetchAdminWalletTiers();
        if (cancelled) return;
        setData(response);
        setStatus('success');
      } catch (err) {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : 'Failed to load wallet top-up tiers.');
        setStatus('error');
      }
    })();
    return () => { cancelled = true; };
  }, []);

  const handleSave = useCallback(async (tiers: AdminWalletTierInput[]) => {
    const updated = await replaceAdminWalletTiers(tiers);
    setData(updated);
  }, []);

  if (status === 'loading') return <p className="text-sm text-muted">Loading wallet tiers…</p>;
  if (status === 'error') return <InlineAlert variant="error" title="Couldn’t load wallet tiers">{error ?? 'An unexpected error occurred.'}</InlineAlert>;
  if (!data) return null;

  return (
    <WalletTiersEditor
      key={`${data.source}:${data.tiers.length}`}
      initialTiers={data.tiers}
      defaultCurrency={data.currency || 'AUD'}
      source={data.source}
      onSave={handleSave}
      canWrite={canWrite}
    />
  );
}

function PricingHubInner() {
  const { user } = useAuth();
  const searchParams = useSearchParams();
  const canReadBilling = hasPermission(user?.adminPermissions, AdminPermission.BillingRead, AdminPermission.BillingWrite);
  const canWriteCatalog = hasPermission(user?.adminPermissions, AdminPermission.BillingWrite, AdminPermission.BillingCatalogWrite);

  const initialTab = useMemo<TabId>(() => {
    const requested = searchParams?.get('tab');
    return TABS.some((t) => t.id === requested) ? (requested as TabId) : 'plans';
  }, [searchParams]);
  const [activeTab, setActiveTab] = useState<TabId>(initialTab);

  useEffect(() => { analytics.track('content_view', { page: 'admin-billing-pricing' }); }, []);

  if (!canReadBilling) {
    return (
      <AdminPageShell>
        <NoBillingPermission requiredPermission={AdminPermission.BillingRead} />
      </AdminPageShell>
    );
  }

  return (
    <AdminPageShell>
      <PageHeader
        eyebrow="Billing"
        icon={<CreditCard className="h-5 w-5" aria-hidden />}
        title="Pricing"
        description="One place to manage every learner-facing pricing package — plans, add-ons, AI grading packages, wallet top-up tiers — and all the page copy."
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Billing', href: '/admin/billing' },
          { label: 'Pricing' },
        ]}
        actions={
          <div className="flex flex-wrap items-center gap-2">
            <Button asChild variant="secondary">
              <Link href="/admin/billing/storefront">
                <Store className="mr-2 h-4 w-4" /> Catalog storefront
              </Link>
            </Button>
            <Button asChild variant="ghost">
              <Link href="/admin/audit-logs?search=billing">
                <FileText className="mr-2 h-4 w-4" /> Audit log
              </Link>
            </Button>
          </div>
        }
      />

      <div className="mb-6 flex flex-wrap gap-1 border-b border-border" role="tablist">
        {TABS.map((tab) => (
          <button
            key={tab.id}
            type="button"
            role="tab"
            aria-selected={activeTab === tab.id}
            onClick={() => setActiveTab(tab.id)}
            className={`-mb-px rounded-t-lg px-4 py-2.5 text-sm font-semibold transition-colors ${
              activeTab === tab.id
                ? 'border-b-2 border-primary text-primary'
                : 'border-b-2 border-transparent text-muted hover:text-navy'
            }`}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {activeTab === 'plans' ? <PlanCatalogEditor canWrite={canWriteCatalog} /> : null}
      {activeTab === 'addons' ? <AddOnCatalogEditor canWrite={canWriteCatalog} /> : null}
      {activeTab === 'ai' ? <AiPackageEditor canWrite={canWriteCatalog} /> : null}
      {activeTab === 'wallet' ? <WalletTab canWrite={canWriteCatalog} /> : null}
      {activeTab === 'copy' ? <BillingCopyEditor canWrite={canWriteCatalog} /> : null}
    </AdminPageShell>
  );
}

export default function AdminPricingHubPage() {
  return (
    <Suspense fallback={<AdminPageShell><p className="text-sm text-muted">Loading…</p></AdminPageShell>}>
      <PricingHubInner />
    </Suspense>
  );
}
