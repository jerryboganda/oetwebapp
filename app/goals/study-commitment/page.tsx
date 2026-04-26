'use client';

import { LearnerPageHero, LearnerSurfaceSectionHeader } from "@/components/domain/learner-surface";
import { LearnerDashboardShell } from "@/components/layout/learner-dashboard-shell";
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { setStudyCommitment } from '@/lib/api';
import { getStudyCommitmentData } from '@/lib/learner-data';
import type { StudyCommitment } from '@/lib/types/learner';
import { Clock, Flame, Shield, Target } from 'lucide-react';
import { useEffect, useState } from 'react';

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

  if (loading) {
    return (
      <LearnerDashboardShell>
        <div className="space-y-4 p-6">
          <Skeleton className="h-10 w-56" />
          <Skeleton className="h-48 w-full rounded-xl" />
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell>
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <LearnerPageHero
        title="Study Commitment"
        description="Set your daily study goal and earn streak protection for consistency."
        icon={<Target className="w-7 h-7" />}
      />

      {error && <InlineAlert variant="error" title="Error">{error}</InlineAlert>}

      {/* Current Status */}
      {commitment && (
        <MotionSection className="mb-6">
          <Card className="p-6">
            <div className="grid grid-cols-2 sm:grid-cols-4 gap-4 text-center">
              <MotionItem>
                <div className="p-3 rounded-lg bg-primary/10">
                  <Clock className="w-5 h-5 text-primary mx-auto mb-1" />
                  <p className="text-xs text-muted">Daily Goal</p>
                  <p className="text-xl font-bold text-navy">{commitment.dailyMinutes}m</p>
                </div>
              </MotionItem>
              <MotionItem>
                <div className="p-3 rounded-lg bg-success/10">
                  <Shield className="w-5 h-5 text-success mx-auto mb-1" />
                  <p className="text-xs text-muted">Freeze Shield</p>
                  <p className="text-xl font-bold text-navy">
                    {commitment.freezeProtections - commitment.freezeProtectionsUsed}/{commitment.freezeProtections}
                  </p>
                </div>
              </MotionItem>
              <MotionItem>
                <div className="p-3 rounded-lg bg-warning/10">
                  <Flame className="w-5 h-5 text-warning mx-auto mb-1" />
                  <p className="text-xs text-muted">Status</p>
                  <Badge variant={commitment.isActive ? 'success' : 'outline'}>
                    {commitment.isActive ? 'Active' : 'Inactive'}
                  </Badge>
                </div>
              </MotionItem>
              <MotionItem>
                <div className="p-3 rounded-lg bg-primary/10">
                  <Target className="w-5 h-5 text-primary mx-auto mb-1" />
                  <p className="text-xs text-muted">Weekly Target</p>
                  <p className="text-xl font-bold text-navy">
                    {Math.round((commitment.dailyMinutes * 7) / 60)}h
                  </p>
                </div>
              </MotionItem>
            </div>
          </Card>
        </MotionSection>
      )}

      {/* Set / Update Commitment */}
      <MotionSection>
        <Card className="p-6">
          <LearnerSurfaceSectionHeader
            icon={<Clock className="w-5 h-5" />}
            title={commitment ? 'Update Your Daily Goal' : 'Set Your Daily Study Goal'}
            description="Choose how many minutes you plan to study each day."
          />
          <div className="mt-4 flex flex-wrap gap-2">
            {PRESET_MINUTES.map((m) => (
              <button
                key={m}
                type="button"
                onClick={() => setSelectedMinutes(m)}
                className={`px-4 py-2 rounded-full text-sm font-medium border transition-colors ${
                  selectedMinutes === m
                    ? 'bg-primary text-white border-primary'
                    : 'bg-surface text-navy border-border-hover hover:border-primary/30'
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
    </LearnerDashboardShell>
  );
}
