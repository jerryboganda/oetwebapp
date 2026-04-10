'use client';

import { useEffect, useState } from 'react';
import { Target, Clock, Shield, Flame } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
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
        subtitle="Set your daily study goal and earn streak protection for consistency."
        icon={<Target className="w-7 h-7" />}
      />

      {error && <InlineAlert variant="error" title="Error">{error}</InlineAlert>}

      {/* Current Status */}
      {commitment && (
        <MotionSection className="mb-6">
          <Card className="p-6">
            <div className="grid grid-cols-2 sm:grid-cols-4 gap-4 text-center">
              <MotionItem>
                <div className="p-3 rounded-lg bg-indigo-50 dark:bg-indigo-900/20">
                  <Clock className="w-5 h-5 text-indigo-600 dark:text-indigo-400 mx-auto mb-1" />
                  <p className="text-xs text-gray-500 dark:text-gray-400">Daily Goal</p>
                  <p className="text-xl font-bold text-gray-900 dark:text-gray-100">{commitment.dailyMinutes}m</p>
                </div>
              </MotionItem>
              <MotionItem>
                <div className="p-3 rounded-lg bg-green-50 dark:bg-green-900/20">
                  <Shield className="w-5 h-5 text-green-600 dark:text-green-400 mx-auto mb-1" />
                  <p className="text-xs text-gray-500 dark:text-gray-400">Freeze Shield</p>
                  <p className="text-xl font-bold text-gray-900 dark:text-gray-100">
                    {commitment.freezeProtections - commitment.freezeProtectionsUsed}/{commitment.freezeProtections}
                  </p>
                </div>
              </MotionItem>
              <MotionItem>
                <div className="p-3 rounded-lg bg-orange-50 dark:bg-orange-900/20">
                  <Flame className="w-5 h-5 text-orange-600 dark:text-orange-400 mx-auto mb-1" />
                  <p className="text-xs text-gray-500 dark:text-gray-400">Status</p>
                  <Badge variant={commitment.isActive ? 'success' : 'outline'}>
                    {commitment.isActive ? 'Active' : 'Inactive'}
                  </Badge>
                </div>
              </MotionItem>
              <MotionItem>
                <div className="p-3 rounded-lg bg-purple-50 dark:bg-purple-900/20">
                  <Target className="w-5 h-5 text-purple-600 dark:text-purple-400 mx-auto mb-1" />
                  <p className="text-xs text-gray-500 dark:text-gray-400">Weekly Target</p>
                  <p className="text-xl font-bold text-gray-900 dark:text-gray-100">
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
            subtitle="Choose how many minutes you plan to study each day."
          />
          <div className="mt-4 flex flex-wrap gap-2">
            {PRESET_MINUTES.map((m) => (
              <button
                key={m}
                type="button"
                onClick={() => setSelectedMinutes(m)}
                className={`px-4 py-2 rounded-full text-sm font-medium border transition-colors ${
                  selectedMinutes === m
                    ? 'bg-indigo-600 text-white border-indigo-600'
                    : 'bg-white dark:bg-gray-800 text-gray-700 dark:text-gray-300 border-gray-300 dark:border-gray-600 hover:border-indigo-400'
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
          <p className="mt-2 text-xs text-gray-500 dark:text-gray-400">
            You&apos;ll receive 3 streak freeze protections when you set a commitment. Missing a day without protection will reset your streak.
          </p>
        </Card>
      </MotionSection>
    </LearnerDashboardShell>
  );
}
