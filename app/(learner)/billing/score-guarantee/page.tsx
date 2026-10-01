'use client';

import { useEffect, useRef, useState } from 'react';
import { Shield, Upload } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { CountUp } from '@/components/ui/count-up';
import { Input } from '@/components/ui/form-controls';
import { MotionSection } from '@/components/ui/motion-primitives';
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

  async function handleActivate() {
    if (mutationsBlocked) {
      setToast({ variant: 'error', message: blockedMessage });
      return;
    }
    if (submittingRef.current) return;
    const score = parseInt(baselineScore, 10);
    if (Number.isNaN(score) || score < 0 || score > 500) {
      setToast({ variant: 'error', message: 'Enter a valid OET score (0-500).' });
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
      <>
        <Skeleton className="h-44 rounded-2xl" />
        <Skeleton className="h-48 rounded-2xl" />
      </>
    );
  }

  return (
    <>
      {toast ? (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      ) : null}

      <LearnerPageHero
        eyebrow="Billing"
        icon={Shield}
        accent="emerald"
        title="Score guarantee"
        description="Score-guarantee eligibility and outcomes are support-reviewed. Claims require official OET result evidence from the eligible pledge window."
        aside={<BackToBillingLink />}
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

      <MotionSection className="space-y-4">
        <LearnerSurfaceSectionHeader
          eyebrow="Eligibility"
          icon={Shield}
          title="Guarantee terms and review workflow"
          description="The pledge is intentionally strict so the program can launch without manual exceptions or abuse-prone loopholes."
        />
        <Card padding="lg">
          <ul className="list-disc space-y-1 ps-5 text-sm text-muted">
            <li>Available only to learners with an active paid OET subscription when the pledge is activated.</li>
            <li>The baseline score must be a real recent OET result; claims require official OET result proof from the pledge window.</li>
            <li>Claims are reviewed by support before wallet credit is issued, and duplicate or manipulated evidence may be rejected.</li>
            <li>Contact support from the public support page if your result evidence needs manual review or deletion handling.</li>
          </ul>
        </Card>
      </MotionSection>

      {!pledge && !error ? (
        <MotionSection delayIndex={1} className="space-y-4">
          <LearnerSurfaceSectionHeader
            eyebrow="Activate"
            icon={Shield}
            title="Request score-guarantee eligibility"
            description="Enter your current OET score so the backend can record the pledge terms available to your subscription."
          />
          <Card padding="lg">
            <div className="flex max-w-md flex-col items-stretch gap-3 sm:flex-row sm:items-end">
              <div className="flex-1">
                <Input
                  id="sg-baseline-score"
                  label="Baseline OET score"
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
          </Card>
        </MotionSection>
      ) : null}

      {pledge ? (
        <MotionSection delayIndex={1} className="space-y-4">
          <LearnerSurfaceSectionHeader
            eyebrow="Pledge"
            icon={Shield}
            title="Your score guarantee"
            action={
              <Badge variant={STATUS_BADGE[pledge.status]?.variant ?? 'default'} className="self-start sm:self-auto">
                {STATUS_BADGE[pledge.status]?.label ?? pledge.status}
              </Badge>
            }
          />
          <dl className="grid grid-cols-[repeat(auto-fit,minmax(min(100%,8.5rem),1fr))] gap-3">
            <Card>
              <dt className="tile-label text-muted">Baseline</dt>
              <dd className="mt-1 text-2xl font-bold text-navy">
                <CountUp value={pledge.baselineScore} />
              </dd>
            </Card>
            <Card>
              <dt className="tile-label text-muted">Target</dt>
              <dd className="mt-1 text-2xl font-bold text-success-strong">
                <CountUp value={pledge.baselineScore + pledge.guaranteedImprovement} />
              </dd>
            </Card>
            <Card>
              <dt className="tile-label text-muted">Improvement</dt>
              <dd className="mt-1 text-2xl font-bold tabular-nums text-primary">
                +<CountUp value={pledge.guaranteedImprovement} />
              </dd>
            </Card>
            <Card>
              <dt className="tile-label text-muted">Expires</dt>
              <dd className="mt-1 text-sm font-bold tabular-nums text-navy">
                {new Date(pledge.expiresAt).toLocaleDateString()}
              </dd>
            </Card>
          </dl>
        </MotionSection>
      ) : null}

      {pledge?.status === 'active' ? (
        <MotionSection delayIndex={2} className="space-y-4">
          <LearnerSurfaceSectionHeader
            eyebrow="Submit a claim"
            icon={Upload}
            title="Claim submission requires official result proof"
            description="Direct claim submission is disabled until an evidence-upload workflow is available. Contact support with your official OET result from the pledge window."
          />
          <InlineAlert
            variant="info"
            live="polite"
            className="max-w-2xl"
            action={(
              <Button variant="outline" size="sm" disabled>
                Direct claim submission unavailable
              </Button>
            )}
          >
            Email support with your pledge ID, official OET result, and the result date. Claims are not approved without verifiable evidence.
          </InlineAlert>
        </MotionSection>
      ) : null}

      {pledge?.status === 'claim_submitted' ? (
        <InlineAlert variant="info" title="Claim under review">
          Your claim is being reviewed by our team. You&apos;ll be notified once a decision is made.
        </InlineAlert>
      ) : null}

      {pledge?.status === 'claim_approved' ? (
        <InlineAlert variant="success" title="Claim approved">
          Your score guarantee claim has been approved. A credit has been applied to your wallet.
          {pledge.reviewNote ? <p className="mt-1 text-sm">{pledge.reviewNote}</p> : null}
        </InlineAlert>
      ) : null}

      {pledge?.status === 'claim_rejected' ? (
        <InlineAlert variant="error" title="Claim rejected">
          Your claim was not approved.
          {pledge.reviewNote ? <p className="mt-1 text-sm">{pledge.reviewNote}</p> : null}
        </InlineAlert>
      ) : null}
    </>
  );
}
