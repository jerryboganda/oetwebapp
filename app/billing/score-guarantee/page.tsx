'use client';

import { useEffect, useMemo, useRef, useState } from 'react';
import { Calendar, Shield, Target, TrendingUp, Upload } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { getScoreGuaranteeData } from '@/lib/learner-data';
import {
  activateScoreGuarantee,
  fetchFreezeStatus,
} from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { ScoreGuaranteePledge } from '@/lib/types/learner';
import type { LearnerFreezeStatus } from '@/lib/types/freeze';
import {
  BackToBillingLink,
  FREEZE_BLOCKED_MESSAGE,
  FREEZE_UNVERIFIED_MESSAGE,
  isFreezeEffective,
} from '@/components/domain/billing';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

const STATUS_BADGE: Record<
  string,
  { label: string; variant: 'default' | 'success' | 'danger' | 'outline' | 'info' }
> = {
  active: { label: 'Active', variant: 'success' },
  claim_submitted: { label: 'Claim submitted', variant: 'info' },
  claim_approved: { label: 'Approved', variant: 'success' },
  claim_rejected: { label: 'Rejected', variant: 'danger' },
  expired: { label: 'Expired', variant: 'outline' },
};

export default function ScoreGuaranteePage() {
  const [pledge, setPledge] = useState<ScoreGuaranteePledge | null>(null);
  const [freezeState, setFreezeState] = useState<LearnerFreezeStatus | null>(null);
  const [freezeLoadFailed, setFreezeLoadFailed] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastState>(null);
  const [isMutating, setIsMutating] = useState(false);

  const [baselineScore, setBaselineScore] = useState('');
  // Belt-and-braces double-submit guard. Complements the `isMutating`
  // disabled state for cases where rapid clicks fire before React has
  // flushed the disabled prop to the DOM.
  const submittingRef = useRef(false);

  useEffect(() => {
    analytics.track('content_view', { page: 'score-guarantee' });
    Promise.allSettled([getScoreGuaranteeData(), fetchFreezeStatus()])
      .then(([pledgeResult, freezeResult]) => {
        if (pledgeResult.status === 'fulfilled') {
          setPledge(pledgeResult.value);
        } else {
          setError('Unable to load score guarantee data.');
        }
        if (freezeResult.status === 'fulfilled') {
          setFreezeState(freezeResult.value as LearnerFreezeStatus);
          setFreezeLoadFailed(false);
        } else {
          setFreezeState(null);
          setFreezeLoadFailed(true);
        }
      })
      .finally(() => setLoading(false));
  }, []);

  const isFrozen = isFreezeEffective(freezeState);
  const mutationsBlocked = freezeLoadFailed || isFrozen;
  const blockedMessage = freezeLoadFailed ? FREEZE_UNVERIFIED_MESSAGE : FREEZE_BLOCKED_MESSAGE;

  const heroHighlights = useMemo(() => {
    if (!pledge) {
      return [
        { icon: Target, label: 'Eligibility', value: 'Backend checked' },
        { icon: Calendar, label: 'Claims', value: 'Support reviewed' },
      ];
    }
    const badge = STATUS_BADGE[pledge.status];
    return [
      { icon: TrendingUp, label: 'Baseline', value: `${pledge.baselineScore}` },
      {
        icon: Target,
        label: 'Target',
        value: `${pledge.baselineScore + pledge.guaranteedImprovement}`,
      },
      {
        icon: Calendar,
        label: 'Expires',
        value: new Date(pledge.expiresAt).toLocaleDateString(),
      },
      { icon: Shield, label: 'Status', value: badge?.label ?? pledge.status },
    ];
  }, [pledge]);

  async function handleActivate() {
    if (mutationsBlocked) {
      setToast({ variant: 'error', message: blockedMessage });
      return;
    }
    if (submittingRef.current) return;
    const score = parseInt(baselineScore, 10);
    if (Number.isNaN(score) || score < 0 || score > 500) {
      setToast({ variant: 'error', message: 'Enter a valid OET score (0–500).' });
      return;
    }
    submittingRef.current = true;
    setIsMutating(true);
    try {
      await activateScoreGuarantee(score);
      const refreshed = await getScoreGuaranteeData();
      setPledge(refreshed);
      setToast({ variant: 'success', message: 'Score guarantee activated.' });
      analytics.track('score_guarantee_activated', { baselineScore: score });
    } catch {
      setToast({ variant: 'error', message: 'Failed to activate guarantee.' });
    } finally {
      setIsMutating(false);
      submittingRef.current = false;
    }
  }

  if (loading) {
    return (
      <LearnerDashboardShell pageTitle="Score guarantee" backHref="/billing">
        <div className="space-y-6">
          <BackToBillingLink />
          <Skeleton className="h-44 rounded-2xl" />
          <Skeleton className="h-48 rounded-2xl" />
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell pageTitle="Score guarantee" backHref="/billing">
      {toast ? (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      ) : null}

      <div className="space-y-6">
        <BackToBillingLink />

        <LearnerPageHero
          eyebrow="Billing"
          icon={Shield}
          accent="emerald"
          title="Score guarantee"
          description="Score-guarantee eligibility and outcomes are support-reviewed. Claims require official OET result evidence from the eligible pledge window."
          highlights={heroHighlights}
        />

        {isFrozen ? (
          <InlineAlert variant="warning">
            Your account is frozen, so activations and claims are paused. Existing pledges remain visible.
          </InlineAlert>
        ) : null}
        {freezeLoadFailed ? (
          <InlineAlert variant="error">{FREEZE_UNVERIFIED_MESSAGE}</InlineAlert>
        ) : null}
        {error ? (
          <InlineAlert variant="error" title="Couldn't load guarantee">
            {error}
          </InlineAlert>
        ) : null}

        <section
          aria-labelledby="sg-terms-heading"
          className="rounded-2xl border border-border bg-surface p-6 shadow-sm"
        >
          <LearnerSurfaceSectionHeader
            eyebrow="Eligibility"
            icon={Shield}
            title="Guarantee terms and review workflow"
            description="The pledge is intentionally strict so the program can launch without manual exceptions or abuse-prone loopholes."
          />
          <h2 id="sg-terms-heading" className="sr-only">
            Score guarantee eligibility terms
          </h2>
          <ul className="mt-4 list-disc space-y-1 pl-5 text-sm text-muted">
            <li>Available only to learners with an active paid OET subscription when the pledge is activated.</li>
              <li>The baseline score must be a real recent OET result; claims require official OET result proof from the pledge window.</li>
            <li>Claims are reviewed by support before wallet credit is issued, and duplicate or manipulated evidence may be rejected.</li>
            <li>Contact support from the public support page if your result evidence needs manual review or deletion handling.</li>
          </ul>
        </section>

        {!pledge && !error ? (
          <section
            aria-labelledby="sg-activate-heading"
            className="rounded-2xl border border-border bg-surface p-6 shadow-sm"
          >
            <LearnerSurfaceSectionHeader
              eyebrow="Activate"
              icon={Shield}
              title="Request score-guarantee eligibility"
              description="Enter your current OET score so the backend can record the pledge terms available to your subscription."
            />
            <h2 id="sg-activate-heading" className="sr-only">
              Request score-guarantee eligibility
            </h2>
            <div className="mt-5 flex max-w-md flex-col items-stretch gap-3 sm:flex-row sm:items-end">
              <div className="flex-1">
                <label
                  htmlFor="sg-baseline-score"
                  className="mb-1 block text-sm font-medium text-navy"
                >
                  Baseline OET score
                </label>
                <Input
                  id="sg-baseline-score"
                  type="number"
                  min={0}
                  max={500}
                  value={baselineScore}
                  onChange={(e) => setBaselineScore(e.target.value)}
                  placeholder="e.g. 300"
                />
              </div>
              <Button
                variant="primary"
                onClick={handleActivate}
                disabled={isMutating || mutationsBlocked}
                aria-label={
                  mutationsBlocked
                    ? `Activate guarantee (unavailable: ${blockedMessage})`
                    : 'Activate score guarantee'
                }
              >
                {isMutating ? 'Activating…' : 'Activate guarantee'}
              </Button>
            </div>
            <p className="mt-3 text-xs text-muted">
              Requires an active subscription. Exact pledge terms are confirmed by the backend after activation.
            </p>
          </section>
        ) : null}

        {pledge ? (
          <>
            <section
              aria-labelledby="sg-summary-heading"
              className="rounded-2xl border border-border bg-surface p-6 shadow-sm"
            >
              <LearnerSurfaceSectionHeader
                eyebrow="Pledge"
                icon={Shield}
                title="Your score guarantee"
                action={
                  <Badge variant={STATUS_BADGE[pledge.status]?.variant ?? 'default'}>
                    {STATUS_BADGE[pledge.status]?.label ?? pledge.status}
                  </Badge>
                }
              />
              <h2 id="sg-summary-heading" className="sr-only">
                Your score guarantee summary
              </h2>
              <dl className="mt-5 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
                <div className="rounded-2xl border border-border bg-background-light p-4">
                  <dt className="text-[11px] font-black uppercase tracking-widest text-muted">
                    Baseline
                  </dt>
                  <dd className="mt-1 text-2xl font-black text-navy">{pledge.baselineScore}</dd>
                </div>
                <div className="rounded-2xl border border-border bg-background-light p-4">
                  <dt className="text-[11px] font-black uppercase tracking-widest text-muted">
                    Target
                  </dt>
                  <dd className="mt-1 text-2xl font-black text-success">
                    {pledge.baselineScore + pledge.guaranteedImprovement}
                  </dd>
                </div>
                <div className="rounded-2xl border border-border bg-background-light p-4">
                  <dt className="text-[11px] font-black uppercase tracking-widest text-muted">
                    Improvement
                  </dt>
                  <dd className="mt-1 text-2xl font-black text-primary">
                    +{pledge.guaranteedImprovement}
                  </dd>
                </div>
                <div className="rounded-2xl border border-border bg-background-light p-4">
                  <dt className="text-[11px] font-black uppercase tracking-widest text-muted">
                    Expires
                  </dt>
                  <dd className="mt-1 text-sm font-bold text-navy">
                    {new Date(pledge.expiresAt).toLocaleDateString()}
                  </dd>
                </div>
              </dl>
            </section>

            {pledge.status === 'active' ? (
              <section
                aria-labelledby="sg-claim-heading"
                className="rounded-2xl border border-border bg-surface p-6 shadow-sm"
              >
                <LearnerSurfaceSectionHeader
                  eyebrow="Submit a claim"
                  icon={Upload}
                  title="Claim submission requires official result proof"
                  description="Direct claim submission is disabled until an evidence-upload workflow is available. Contact support with your official OET result from the pledge window."
                />
                <h2 id="sg-claim-heading" className="sr-only">
                  Submit a score guarantee claim
                </h2>
                <div className="mt-5 max-w-2xl space-y-3">
                  <InlineAlert variant="info">
                    Email support with your pledge ID, official OET result, and the result date. Claims are not approved without verifiable evidence.
                  </InlineAlert>
                  <Button variant="outline" disabled>
                    Direct claim submission unavailable
                  </Button>
                </div>
              </section>
            ) : null}

            {pledge.status === 'claim_submitted' ? (
              <InlineAlert variant="info" title="Claim under review">
                Your claim is being reviewed by our team. You&apos;ll be notified once a decision is made.
              </InlineAlert>
            ) : null}

            {pledge.status === 'claim_approved' ? (
              <InlineAlert variant="success" title="Claim approved">
                Your score guarantee claim has been approved. A credit has been applied to your wallet.
                {pledge.reviewNote ? <p className="mt-1 text-sm">{pledge.reviewNote}</p> : null}
              </InlineAlert>
            ) : null}

            {pledge.status === 'claim_rejected' ? (
              <InlineAlert variant="error" title="Claim rejected">
                Your claim was not approved.
                {pledge.reviewNote ? <p className="mt-1 text-sm">{pledge.reviewNote}</p> : null}
              </InlineAlert>
            ) : null}
          </>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}
