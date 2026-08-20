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

function formatSkillCredits(unlimited: boolean | undefined, remaining: number | undefined): string {
  if (unlimited) return 'Unlimited';
  return String(remaining ?? 0);
}

export function AiCreditSummary({ snapshot, loading }: AiCreditSummaryProps) {
  const granted = snapshot?.creditsGranted ?? 0;
  const used = snapshot?.creditsUsed ?? 0;
  const writingUnlimited = snapshot?.writingUnlimited === true;
  const speakingUnlimited = snapshot?.speakingUnlimited === true;
  const skillUnlimited = writingUnlimited && speakingUnlimited;
  const sharedCredits = snapshot?.flexibleCredits ?? 0;
  const showShared = !skillUnlimited && sharedCredits > 0;

  return (
    <div className="rounded-2xl border border-primary/25 bg-primary/5 p-4" data-testid="ai-credit-summary">
      <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">AI credits</p>
      <p className="mt-1 text-sm text-admin-text-muted">
        Writing AI credits and Speaking AI credits are separate. Mixed packs such as Quick Check and Exam Prep Pro add shared credits. OET Mastery makes Writing and Speaking unlimited. Reading and Listening are practice tests. Tutor booking stays on its own packages.
      </p>
      {loading ? (
        <p className="mt-3 text-sm text-muted">Loading AI credit balance…</p>
      ) : (
        <>
          <dl className="mt-3 grid gap-3 sm:grid-cols-2">
            <div className="rounded-xl border border-border/60 bg-admin-bg-subtle p-3">
              <dt className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">Writing AI credits</dt>
              <dd className="mt-1 text-lg font-semibold tabular-nums text-admin-fg-strong" data-testid="writing-ai-credits">
                {formatSkillCredits(writingUnlimited, snapshot?.writingOnlyCredits)}
              </dd>
            </div>
            <div className="rounded-xl border border-border/60 bg-admin-bg-subtle p-3">
              <dt className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">Speaking AI credits</dt>
              <dd className="mt-1 text-lg font-semibold tabular-nums text-admin-fg-strong" data-testid="speaking-ai-credits">
                {formatSkillCredits(speakingUnlimited, snapshot?.speakingOnlyCredits)}
              </dd>
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
          {showShared ? (
            <dl className="mt-3 grid gap-3 sm:grid-cols-1">
              <div className="rounded-xl border border-border/60 bg-admin-bg-subtle p-3">
                <dt className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">Shared AI credits</dt>
                <dd className="mt-1 text-lg font-semibold tabular-nums text-admin-fg-strong" data-testid="shared-ai-credits">
                  {sharedCredits}
                </dd>
              </div>
            </dl>
          ) : null}
          {!skillUnlimited ? (
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
                <dd className="mt-1 text-lg font-semibold tabular-nums text-admin-fg-strong" data-testid="finite-remaining">
                  {(snapshot?.writingOnlyCredits ?? 0) + (snapshot?.speakingOnlyCredits ?? 0) + sharedCredits}
                </dd>
              </div>
            </dl>
          ) : null}
        </>
      )}
    </div>
  );
}
