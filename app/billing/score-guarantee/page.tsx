'use client';

import { useEffect, useState } from 'react';
import { Shield, CheckCircle2, Clock, AlertTriangle, Upload } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { getScoreGuaranteeData } from '@/lib/learner-data';
import { activateScoreGuarantee, submitScoreGuaranteeClaim } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { ScoreGuaranteePledge } from '@/lib/types/learner';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

const STATUS_BADGE: Record<string, { label: string; variant: 'default' | 'success' | 'destructive' | 'outline' }> = {
  active: { label: 'Active', variant: 'success' },
  claim_submitted: { label: 'Claim Submitted', variant: 'default' },
  claim_approved: { label: 'Approved', variant: 'success' },
  claim_rejected: { label: 'Rejected', variant: 'destructive' },
  expired: { label: 'Expired', variant: 'outline' },
};

export default function ScoreGuaranteePage() {
  const [pledge, setPledge] = useState<ScoreGuaranteePledge | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastState>(null);
  const [isMutating, setIsMutating] = useState(false);

  // Activation form
  const [baselineScore, setBaselineScore] = useState('');
  // Claim form
  const [actualScore, setActualScore] = useState('');
  const [claimNote, setClaimNote] = useState('');

  useEffect(() => {
    analytics.track('content_view', { page: 'score-guarantee' });
    getScoreGuaranteeData()
      .then(setPledge)
      .catch(() => setError('Unable to load score guarantee data.'))
      .finally(() => setLoading(false));
  }, []);

  async function handleActivate() {
    const score = parseInt(baselineScore, 10);
    if (!score || score < 0 || score > 500) {
      setToast({ variant: 'error', message: 'Enter a valid OET score (0–500).' });
      return;
    }
    setIsMutating(true);
    try {
      await activateScoreGuarantee(score);
      const refreshed = await getScoreGuaranteeData();
      setPledge(refreshed);
      setToast({ variant: 'success', message: 'Score guarantee activated!' });
      analytics.track('score_guarantee_activated', { baselineScore: score });
    } catch {
      setToast({ variant: 'error', message: 'Failed to activate guarantee.' });
    } finally {
      setIsMutating(false);
    }
  }

  async function handleClaim() {
    const score = parseInt(actualScore, 10);
    if (!score || score < 0 || score > 500) {
      setToast({ variant: 'error', message: 'Enter your actual OET score (0–500).' });
      return;
    }
    setIsMutating(true);
    try {
      await submitScoreGuaranteeClaim(score, undefined, claimNote || undefined);
      const refreshed = await getScoreGuaranteeData();
      setPledge(refreshed);
      setToast({ variant: 'success', message: 'Claim submitted for review.' });
      analytics.track('score_guarantee_claim_submitted', { actualScore: score });
    } catch {
      setToast({ variant: 'error', message: 'Failed to submit claim.' });
    } finally {
      setIsMutating(false);
    }
  }

  if (loading) {
    return (
      <LearnerDashboardShell>
        <div className="space-y-4 p-6">
          <Skeleton className="h-10 w-64" />
          <Skeleton className="h-48 w-full rounded-xl" />
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell>
      {toast && <Toast variant={toast.variant} onDismiss={() => setToast(null)}>{toast.message}</Toast>}

      <LearnerPageHero
        title="Score Guarantee"
        subtitle="We guarantee a 50-point improvement or your money back."
        icon={<Shield className="w-7 h-7" />}
      />

      {error && <InlineAlert variant="error" title="Error">{error}</InlineAlert>}

      {/* No pledge yet — activation form */}
      {!pledge && !error && (
        <MotionSection>
          <Card className="p-6">
            <LearnerSurfaceSectionHeader
              icon={<Shield className="w-5 h-5" />}
              title="Activate Your Score Guarantee"
              subtitle="Enter your current OET score to activate the 50-point improvement guarantee."
            />
            <div className="mt-4 flex items-end gap-3 max-w-md">
              <div className="flex-1">
                <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Baseline OET Score</label>
                <Input
                  type="number"
                  min={0}
                  max={500}
                  value={baselineScore}
                  onChange={(e) => setBaselineScore(e.target.value)}
                  placeholder="e.g. 300"
                />
              </div>
              <Button onClick={handleActivate} disabled={isMutating}>
                {isMutating ? 'Activating…' : 'Activate Guarantee'}
              </Button>
            </div>
            <p className="mt-3 text-xs text-gray-500 dark:text-gray-400">
              Requires an active subscription. Guarantee is valid for 180 days.
            </p>
          </Card>
        </MotionSection>
      )}

      {/* Active pledge display */}
      {pledge && (
        <>
          <MotionSection>
            <Card className="p-6">
              <div className="flex items-center justify-between mb-4">
                <h3 className="font-semibold text-gray-900 dark:text-gray-100">Your Score Guarantee</h3>
                <Badge variant={STATUS_BADGE[pledge.status]?.variant ?? 'default'}>
                  {STATUS_BADGE[pledge.status]?.label ?? pledge.status}
                </Badge>
              </div>

              <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
                <MotionItem>
                  <div className="text-center p-3 rounded-lg bg-gray-50 dark:bg-gray-800/50">
                    <p className="text-xs text-gray-500 dark:text-gray-400">Baseline</p>
                    <p className="text-2xl font-bold text-gray-900 dark:text-gray-100">{pledge.baselineScore}</p>
                  </div>
                </MotionItem>
                <MotionItem>
                  <div className="text-center p-3 rounded-lg bg-gray-50 dark:bg-gray-800/50">
                    <p className="text-xs text-gray-500 dark:text-gray-400">Target</p>
                    <p className="text-2xl font-bold text-green-600 dark:text-green-400">
                      {pledge.baselineScore + pledge.guaranteedImprovement}
                    </p>
                  </div>
                </MotionItem>
                <MotionItem>
                  <div className="text-center p-3 rounded-lg bg-gray-50 dark:bg-gray-800/50">
                    <p className="text-xs text-gray-500 dark:text-gray-400">Improvement</p>
                    <p className="text-2xl font-bold text-indigo-600 dark:text-indigo-400">+{pledge.guaranteedImprovement}</p>
                  </div>
                </MotionItem>
                <MotionItem>
                  <div className="text-center p-3 rounded-lg bg-gray-50 dark:bg-gray-800/50">
                    <p className="text-xs text-gray-500 dark:text-gray-400">Expires</p>
                    <p className="text-sm font-medium text-gray-700 dark:text-gray-300">
                      {new Date(pledge.expiresAt).toLocaleDateString()}
                    </p>
                  </div>
                </MotionItem>
              </div>
            </Card>
          </MotionSection>

          {/* Claim form — only when active */}
          {pledge.status === 'active' && (
            <MotionSection className="mt-6">
              <Card className="p-6">
                <LearnerSurfaceSectionHeader
                  icon={<Upload className="w-5 h-5" />}
                  title="Submit a Claim"
                  subtitle="If you took the OET and didn't improve by 50 points, submit your actual score."
                />
                <div className="mt-4 space-y-3 max-w-md">
                  <div>
                    <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Actual OET Score</label>
                    <Input
                      type="number"
                      min={0}
                      max={500}
                      value={actualScore}
                      onChange={(e) => setActualScore(e.target.value)}
                      placeholder="Your official OET score"
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Note (optional)</label>
                    <Input
                      value={claimNote}
                      onChange={(e) => setClaimNote(e.target.value)}
                      placeholder="Any additional details"
                    />
                  </div>
                  <Button onClick={handleClaim} disabled={isMutating}>
                    {isMutating ? 'Submitting…' : 'Submit Claim'}
                  </Button>
                </div>
              </Card>
            </MotionSection>
          )}

          {/* Claim result messages */}
          {pledge.status === 'claim_submitted' && (
            <MotionSection className="mt-6">
              <InlineAlert variant="info" title="Claim Under Review">
                Your claim is being reviewed by our team. You&apos;ll be notified once a decision is made.
              </InlineAlert>
            </MotionSection>
          )}

          {pledge.status === 'claim_approved' && (
            <MotionSection className="mt-6">
              <InlineAlert variant="success" title="Claim Approved">
                Your score guarantee claim has been approved. A credit has been applied to your wallet.
                {pledge.reviewNote && <p className="mt-1 text-sm">{pledge.reviewNote}</p>}
              </InlineAlert>
            </MotionSection>
          )}

          {pledge.status === 'claim_rejected' && (
            <MotionSection className="mt-6">
              <InlineAlert variant="error" title="Claim Rejected">
                Your claim was not approved.
                {pledge.reviewNote && <p className="mt-1 text-sm">{pledge.reviewNote}</p>}
              </InlineAlert>
            </MotionSection>
          )}
        </>
      )}
    </LearnerDashboardShell>
  );
}
