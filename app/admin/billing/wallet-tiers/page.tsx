'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { FileText, Wallet } from 'lucide-react';

import { AdminPageShell } from '@/components/admin/layout/admin-page-shell';
import { Button } from '@/components/admin/ui/button';
import { PageHeader } from '@/components/admin/ui/page-header';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { WalletTiersEditor } from '@/components/admin/billing/wallet-tiers-editor';
import { NoBillingPermission } from '@/components/admin/billing/no-billing-permission';
import {
  fetchAdminWalletTiers,
  replaceAdminWalletTiers,
  type AdminWalletTierInput,
  type AdminWalletTiersResponse,
} from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { useAuth } from '@/contexts/auth-context';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';

type LoadState = 'loading' | 'success' | 'error';

export default function AdminWalletTiersPage() {
  const { user } = useAuth();
  const canReadBilling = hasPermission(user?.adminPermissions, AdminPermission.BillingRead, AdminPermission.BillingWrite);
  const canWriteCatalog = hasPermission(user?.adminPermissions, AdminPermission.BillingWrite, AdminPermission.BillingCatalogWrite);
  const [data, setData] = useState<AdminWalletTiersResponse | null>(null);
  const [status, setStatus] = useState<LoadState>('loading');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [saveMessage, setSaveMessage] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const response = await fetchAdminWalletTiers();
      setData(response);
      setStatus('success');
      setErrorMessage(null);
      setSaveMessage(null);
    } catch (error) {
      console.error('Failed to load admin wallet top-up tiers', error);
      setErrorMessage(error instanceof Error ? error.message : 'Failed to load wallet top-up tiers.');
      setStatus('error');
    }
  }, []);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const response = await fetchAdminWalletTiers();
        if (cancelled) return;
        setData(response);
        setStatus('success');
        setSaveMessage(null);
      } catch (error) {
        if (cancelled) return;
        console.error('Failed to load admin wallet top-up tiers', error);
        setErrorMessage(error instanceof Error ? error.message : 'Failed to load wallet top-up tiers.');
        setStatus('error');
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const handleRefresh = useCallback(() => {
    setStatus('loading');
    setErrorMessage(null);
    setSaveMessage(null);
    void load();
  }, [load]);

  useEffect(() => {
    analytics.track('content_view', { page: 'admin-billing-wallet-tiers' });
  }, []);

  const handleSave = useCallback(async (tiers: AdminWalletTierInput[]) => {
    const updated = await replaceAdminWalletTiers(tiers);
    setData(updated);
    setSaveMessage('Wallet top-up tiers saved.');
  }, []);

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
        icon={<Wallet className="h-5 w-5" aria-hidden />}
        title="Wallet top-up tiers"
        description="Configure the wallet top-up tiers learners see during checkout. Saving replaces the entire active set."
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Billing', href: '/admin/billing' },
          { label: 'Wallet tiers' },
        ]}
        actions={
          <div className="flex flex-wrap gap-2">
            <Button asChild variant="secondary">
              <Link
                href="/admin/audit-logs?search=wallet_tier"
                data-testid="wallet-tiers-audit-log-link"
              >
                <FileText className="mr-2 h-4 w-4" />
                Audit log
              </Link>
            </Button>
            <Button
              variant="secondary"
              onClick={handleRefresh}
              disabled={status === 'loading'}
              loading={status === 'loading'}
            >
              Refresh
            </Button>
          </div>
        }
      />

      {status === 'loading' ? (
        <div className="space-y-3" data-testid="wallet-tiers-loading">
          <Skeleton className="h-6 w-2/5" />
          <Skeleton className="h-4 w-full" />
          <Skeleton className="h-4 w-full" />
          <Skeleton className="h-4 w-3/4" />
        </div>
      ) : status === 'error' ? (
        <InlineAlert variant="error" title="Couldn’t load wallet tiers">
          {errorMessage ?? 'An unexpected error occurred.'}
        </InlineAlert>
      ) : data ? (
        <>
          {saveMessage ? (
            <InlineAlert variant="success" title="Saved">
              {saveMessage}
            </InlineAlert>
          ) : null}
          <WalletTiersEditor
            key={`${data.source}:${data.tiers
              .map(
                (tier) =>
                  `${tier.id ?? 'new'}:${tier.amount}:${tier.credits}:${tier.bonus}:${tier.currency}:${tier.label}:${tier.displayOrder}:${tier.isPopular}:${tier.isActive}`,
              )
              .join('|')}`}
            initialTiers={data.tiers}
            defaultCurrency={data.currency || 'AUD'}
            source={data.source}
            onSave={handleSave}
            canWrite={canWriteCatalog}
          />
        </>
      ) : null}
    </AdminPageShell>
  );
}
