'use client';

import type { AiPackageCreditSnapshot } from '@/lib/billing-types';

interface AiCreditSummaryProps {
  snapshot: AiPackageCreditSnapshot | null;
  loading?: boolean;
}

function formatAllowance(value: number | null | undefined): string {
  if (value === null || value === undefined) return 'Unlimited';
  return String(value);
}

export function AiCreditSummary({ snapshot, loading }: AiCreditSummaryProps) {
  const granted = snapshot?.creditsGranted ?? 0;
  const used = snapshot?.creditsUsed ?? 0;
  const remaining = snapshot?.creditsRemaining ?? 0;

  return (
    <div className="rounded-2xl border border-primary/25 bg-primary/5 p-4" data-testid="ai-credit-summary">
      <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">AI credits</p>
      <p className="mt-1 text-sm text-admin-text-muted">
        Full Course gifts and Writing/Speaking AI packs share this credit balance. Reading and Listening add-ons grant practice tests on the same account. Tutor booking stays on its own packages.
      </p>
      {loading ? (
        <p className="mt-3 text-sm text-muted">Loading AI credit balance…</p>
      ) : (
        <>
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
          <dl className="mt-3 grid gap-3 sm:grid-cols-2">
            <div className="rounded-xl border border-border/60 bg-admin-bg-subtle p-3">
              <dt className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">Reading tests</dt>
              <dd className="mt-1 text-lg font-semibold tabular-nums text-admin-fg-strong">{formatAllowance(snapshot?.readingTestsRemaining)}</dd>
            </div>
            <div className="rounded-xl border border-border/60 bg-admin-bg-subtle p-3">
              <dt className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">Listening tests</dt>
              <dd className="mt-1 text-lg font-semibold tabular-nums text-admin-fg-strong">{formatAllowance(snapshot?.listeningTestsRemaining)}</dd>
            </div>
          </dl>
        </>
      )}
    </div>
  );
}
