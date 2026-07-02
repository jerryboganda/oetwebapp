'use client';

import { useEffect, useState, type ReactNode } from 'react';
import Link from 'next/link';
import { Loader2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { fetchAdminMockBundle } from '@/lib/api';
import { WizardShell, type WizardMockBundle } from './WizardShell';

// The wizard layout used to fetch the bundle in a server component, but the
// admin API client resolves its bearer token from web storage — client-only.
// Fetch here (in the browser) instead, so every wizard step works.
export function WizardShellLoader({
  bundleId,
  children,
}: {
  bundleId: string;
  children: ReactNode;
}) {
  const [bundle, setBundle] = useState<WizardMockBundle | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setBundle(null);
    setError(null);
    (async () => {
      try {
        const next = (await fetchAdminMockBundle(bundleId)) as WizardMockBundle;
        if (cancelled) return;
        if (!next?.id) {
          setError('Mock bundle not found.');
          return;
        }
        setBundle(next);
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Could not load the mock bundle.');
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [bundleId]);

  if (error) {
    return (
      <div className="rounded-2xl border border-border bg-surface p-6 text-sm">
        <p className="font-bold text-navy">Could not open this mock draft</p>
        <p className="mt-1 text-muted">{error}</p>
        <Button asChild variant="outline" className="mt-4">
          <Link href="/admin/content/mocks/wizard">Back to the wizard home</Link>
        </Button>
      </div>
    );
  }

  if (!bundle) {
    return (
      <div className="flex items-center gap-2 rounded-2xl border border-border bg-surface p-6 text-sm text-muted">
        <Loader2 className="h-4 w-4 animate-spin" /> Loading mock draft…
      </div>
    );
  }

  return <WizardShell bundle={bundle}>{children}</WizardShell>;
}
