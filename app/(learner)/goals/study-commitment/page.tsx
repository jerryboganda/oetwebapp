'use client';

import { useEffect, useState } from 'react';
import { Target, Clock, Shield, Flame } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { CountUp } from '@/components/ui/count-up';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { getStudyCommitmentData } from '@/lib/learner-data';
import { setStudyCommitment } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { StudyCommitment } from '@/lib/types/learner';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

const PRESET_MINUTES = [15, 30, 45, 60, 90, 120];

export default function StudyCommitmentPage() {
  const [commitment, setCommitment] = useState<StudyCommitment | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastState>(null);
  const [isMutating, setIsMutating] = useState(false);
  const [selectedMinutes, setSelectedMinutes] = useState<number>(30);

  useEffect(() => {
    analytics.track('content_view', { page: 'study-commitment' });
    getStudyCommitmentData()
      .then((c) => {
        setCommitment(c);
        if (c) setSelectedMinutes(c.dailyMinutes);
      })
      .catch(() => setError('Unable to load study commitment data.'))
      .finally(() => setLoading(false));
  }, []);

  async function handleSave() {
    setIsMutating(true);
    try {
      await setStudyCommitment(selectedMinutes);
      const refreshed = await getStudyCommitmentData();
      setCommitment(refreshed);
      setToast({ variant: 'success', message: 'Study commitment updated!' });
      analytics.track('study_commitment_set', { dailyMinutes: selectedMinutes });
    } catch {
      setToast({ variant: 'error', message: 'Failed to update commitment.' });
    } finally {
      setIsMutating(false);
    }
  }

  return (
    <>
      <LearnerPageHero
        title="Study Commitment"
        description="Set your daily study goal and earn streak protection for consistency."
        icon={Target}
      />

      {loading ? (
        <Skeleton className="h-48 w-full rounded-2xl" />
      ) : (
        <>
          {error && <InlineAlert variant="error" title="Error">{error}</InlineAlert>}

          {/* Current Status */}
          {commitment && (
            <MotionSection>
              <Card padding="lg">
                <div className="grid grid-cols-2 gap-4 text-center sm:grid-cols-4">
                  <MotionItem delayIndex={0}>
                    <div className="rounded-xl bg-primary/10 p-3">
                      <Clock className="mx-auto mb-1 h-5 w-5 text-primary" aria-hidden="true" />
                      <p className="tile-label text-muted">Daily Goal</p>
                      <p className="text-xl font-bold tabular-nums text-navy"><CountUp value={commitment.dailyMinutes} suffix="m" /></p>
                    </div>
                  </MotionItem>
                  <MotionItem delayIndex={1}>
                    <div className="rounded-xl bg-success/10 p-3">
                      <Shield className="mx-auto mb-1 h-5 w-5 text-success-strong" aria-hidden="true" />
                      <p className="tile-label text-muted">Freeze Shield</p>
                      <p className="text-xl font-bold tabular-nums text-navy">
                        {commitment.freezeProtections - commitment.freezeProtectionsUsed}/{commitment.freezeProtections}
                      </p>
                    </div>
                  </MotionItem>
                  <MotionItem delayIndex={2}>
                    <div className="rounded-xl bg-warning/10 p-3">
                      <Flame className="mx-auto mb-1 h-5 w-5 text-warning-strong" aria-hidden="true" />
                      <p className="tile-label text-muted">Status</p>
                      <Badge variant={commitment.isActive ? 'success' : 'outline'}>
                        {commitment.isActive ? 'Active' : 'Inactive'}
                      </Badge>
                    </div>
                  </MotionItem>
                  <MotionItem delayIndex={3}>
                    <div className="rounded-xl bg-primary/10 p-3">
                      <Target className="mx-auto mb-1 h-5 w-5 text-primary" aria-hidden="true" />
                      <p className="tile-label text-muted">Weekly Target</p>
                      <p className="text-xl font-bold tabular-nums text-navy">
                        {Math.round((commitment.dailyMinutes * 7) / 60)}h
                      </p>
                    </div>
                  </MotionItem>
                </div>
              </Card>
            </MotionSection>
          )}

          {/* Set / Update Commitment */}
          <MotionSection delayIndex={1}>
            <Card padding="lg">
              <LearnerSurfaceSectionHeader
                icon={Clock}
                title={commitment ? 'Update Your Daily Goal' : 'Set Your Daily Study Goal'}
                description="Choose how many minutes you plan to study each day."
              />
              <div className="mt-4 flex flex-wrap gap-2" role="group" aria-label="Daily study minutes">
                {PRESET_MINUTES.map((m) => (
                  <button
                    key={m}
                    type="button"
                    onClick={() => setSelectedMinutes(m)}
                    aria-pressed={selectedMinutes === m}
                    className={`pressable inline-flex min-h-11 items-center rounded-control border px-4 text-sm font-semibold tabular-nums transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 ${
                      selectedMinutes === m
                        ? 'border-primary bg-primary/10 text-primary'
                        : 'border-border bg-surface text-navy hover:border-primary/30 hover-primary'
                    }`}
                  >
                    {m} min
                  </button>
                ))}
              </div>
              <div className="mt-4">
                <Button onClick={handleSave} disabled={isMutating}>
                  {isMutating ? 'Saving…' : commitment ? 'Update Goal' : 'Set Goal'}
                </Button>
              </div>
              <p className="mt-2 text-xs text-muted">
                You&apos;ll receive 3 streak freeze protections when you set a commitment. Missing a day without protection will reset your streak.
              </p>
            </Card>
          </MotionSection>
        </>
      )}

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </>
  );
}
