'use client';

import { useCallback, useEffect, useState } from 'react';
import { Wallet } from 'lucide-react';
import { AdminRouteSectionHeader, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { WalletTiersEditor } from '@/components/admin/billing/wallet-tiers-editor';
import {
  fetchAdminWalletTiers,
  replaceAdminWalletTiers,
  type AdminWalletTierInput,
  type AdminWalletTiersResponse,
} from '@/lib/api';
import { analytics } from '@/lib/analytics';

type LoadState = 'loading' | 'success' | 'error';

export default function AdminWalletTiersPage() {
  const [data, setData] = useState<AdminWalletTiersResponse | null>(null);
  const [status, setStatus] = useState<LoadState>('loading');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const response = await fetchAdminWalletTiers();
      setData(response);
      setStatus('success');
      setErrorMessage(null);
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
    void load();
  }, [load]);

  useEffect(() => {
    analytics.track('content_view', { page: 'admin-billing-wallet-tiers' });
  }, []);

  const handleSave = useCallback(async (tiers: AdminWalletTierInput[]) => {
    const updated = await replaceAdminWalletTiers(tiers);
    setData(updated);
  }, []);

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        eyebrow="Billing"
        icon={Wallet}
        accent="navy"
        title="Wallet top-up tiers"
        description="Configure the wallet top-up tiers learners see during checkout. Saving replaces the entire active set."
        actions={
          <Button
            variant="secondary"
            onClick={handleRefresh}
            disabled={status === 'loading'}
          >
            {status === 'loading' ? 'Refreshing…' : 'Refresh'}
          </Button>
        }
      />

      {status === 'loading' ? (
        <div className="space-y-3" data-testid="wallet-tiers-loading">
          <Skeleton height={24} width="40%" />
          <Skeleton height={16} lines={5} />
        </div>
      ) : status === 'error' ? (
        <InlineAlert variant="error" title="Couldn’t load wallet tiers">
          {errorMessage ?? 'An unexpected error occurred.'}
        </InlineAlert>
      ) : data ? (
        <WalletTiersEditor
          initialTiers={data.tiers}
          defaultCurrency={data.currency || 'AUD'}
          source={data.source}
          onSave={handleSave}
        />
      ) : null}
    </AdminRouteWorkspace>
  );
}
