'use client';

import type { AiPackageCreditSnapshot } from '@/lib/billing-types';

interface AiCreditSummaryProps {
  snapshot: AiPackageCreditSnapshot | null;
  loading?: boolean;
}

export function AiCreditSummary({ snapshot, loading }: AiCreditSummaryProps) {
  const granted = snapshot?.creditsGranted ?? 0;
  const used = snapshot?.creditsUsed ?? 0;
  const remaining = snapshot?.creditsRemaining ?? 0;

  return (
    <div className="rounded-2xl border border-primary/25 bg-primary/5 p-4" data-testid="ai-credit-summary">
      <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">AI credits</p>
      <p className="mt-1 text-sm text-admin-text-muted">
        Full Course gifts and AI add-ons share this balance. Writing 2, Speaking 1/card, Listening 1, Reading 1.
      </p>
      {loading ? (
        <p className="mt-3 text-sm text-muted">Loading AI credit balance…</p>
      ) : (
        <dl className="mt-3 grid gap-3 sm:grid-cols-3">
          <div className="rounded-xl border border-border/60 bg-admin-bg-subtle p-3">
            <dt className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">Granted / purchased</dt>
            <dd className="mt-1 text-lg font-semibold tabular-nums text-admin-fg-strong">{granted}</dd>
          </div>
          <div className="rounded-xl border border-border/60 bg-admin-bg-subtle p-3">
            <dt className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">Used</dt>
            <dd className="mt-1 text-lg font-semibold tabular-nums text-admin-fg-strong">{used}</dd>
          </div>
          <div className="rounded-xl border border-border/60 bg-admin-bg-subtle p-3">
            <dt className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">Remaining</dt>
            <dd className="mt-1 text-lg font-semibold tabular-nums text-admin-fg-strong">{remaining}</dd>
          </div>
        </dl>
      )}
    </div>
  );
}
