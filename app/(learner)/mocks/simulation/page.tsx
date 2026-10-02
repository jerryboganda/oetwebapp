'use client';

import { useEffect, useState } from 'react';
import { Timer, Shield, AlertCircle, CheckCircle2, Lock } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { CountUp } from '@/components/ui/count-up';
import { ErrorState } from '@/components/ui/empty-error';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';
import { cn } from '@/lib/utils';

interface SimConfig {
  examType: string;
  simulationMode: { strictTiming: boolean; noPause: boolean; sequentialSubtests: boolean; noBackNavigation: boolean; showCountdown: boolean; stressIndicators: boolean };
  subtestTimings: Record<string, { durationMinutes: number; sections: number }>;
  totalDurationMinutes: number;
  completedSimulations: number;
  recommendation: string;
  unlocked: boolean;
}

const apiRequest = apiClient.request;

/** Sub-test identity (DESIGN.md §2 skill tokens) for the timing tiles. */
const SUBTEST_TONE: Record<string, string> = {
  listening: 'text-skill-listening',
  reading: 'text-skill-reading',
  writing: 'text-skill-writing',
  speaking: 'text-skill-speaking',
};

export default function ExamSimulationPage() {
  const [config, setConfig] = useState<SimConfig | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    analytics.track('exam_simulation_viewed');
    apiRequest<SimConfig>('/v1/learner/exam-simulation-config').then(setConfig).catch(() => {}).finally(() => setLoading(false));
  }, []);

  return (
    <>
      <LearnerPageHero
        eyebrow="Mocks"
        icon={Timer}
        title="Exam Simulation Mode"
        description="Practice under real exam conditions: strict timing, no pauses, sequential subtests."
      />

      {loading ? (
        <div className="space-y-4" role="status" aria-busy="true" aria-label="Loading simulation settings">
          {Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-32 rounded-2xl" />)}
        </div>
      ) : config ? (
        <>
          <MotionSection>
            <Card className="flex items-start gap-4">
              {config.unlocked
                ? <CheckCircle2 className="h-8 w-8 shrink-0 text-success-strong" aria-hidden="true" />
                : <Lock className="h-8 w-8 shrink-0 text-warning-strong" aria-hidden="true" />}
              <div className="min-w-0">
                <h2 className="text-lg font-semibold text-navy">{config.unlocked ? 'Simulation Mode Unlocked' : 'Simulation Mode Locked'}</h2>
                <p className="mt-1 text-sm text-muted">{config.recommendation}</p>
                <p className="mt-1 text-sm text-muted">
                  Completed simulations: <strong className="tabular-nums text-navy"><CountUp value={config.completedSimulations} /></strong>
                </p>
              </div>
            </Card>
          </MotionSection>

          <MotionSection delayIndex={1}>
            <LearnerSurfaceSectionHeader title="Simulation Rules" className="mb-4" />
            <div className="grid grid-cols-2 gap-3 md:grid-cols-3">
              {Object.entries(config.simulationMode).map(([key, val], index) => (
                <MotionItem key={key} delayIndex={Math.min(index, 5)} className="h-full">
                  <Card padding="sm" className="h-full text-center">
                    {val
                      ? <Shield className="mx-auto mb-2 h-5 w-5 text-primary" aria-hidden="true" />
                      : <AlertCircle className="mx-auto mb-2 h-5 w-5 text-muted" aria-hidden="true" />}
                    <p className="text-sm font-medium capitalize text-navy">{key.replace(/([A-Z])/g, ' $1').trim()}</p>
                    <Badge variant={val ? 'default' : 'outline'} className="mt-1">{val ? 'Active' : 'Off'}</Badge>
                  </Card>
                </MotionItem>
              ))}
            </div>
          </MotionSection>

          <MotionSection delayIndex={2}>
            <LearnerSurfaceSectionHeader title="Subtest Timings" className="mb-4" />
            <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
              {Object.entries(config.subtestTimings).map(([subtest, timing], index) => (
                <MotionItem key={subtest} delayIndex={Math.min(index, 5)} className="h-full">
                  <Card padding="sm" className="h-full text-center">
                    <Timer className={cn('mx-auto mb-2 h-5 w-5', SUBTEST_TONE[subtest.toLowerCase()] ?? 'text-muted')} aria-hidden="true" />
                    <p className="text-lg font-bold tabular-nums text-navy">{timing.durationMinutes} min</p>
                    <p className="text-sm font-medium capitalize text-navy">{subtest}</p>
                    <p className="text-xs tabular-nums text-muted">{timing.sections} section{timing.sections > 1 ? 's' : ''}</p>
                  </Card>
                </MotionItem>
              ))}
            </div>
            <p className="mt-4 text-sm text-muted">
              Total exam duration: <strong className="tabular-nums text-navy">{config.totalDurationMinutes} minutes</strong>
            </p>
          </MotionSection>
        </>
      ) : (
        <ErrorState title="Unable to load simulation configuration" />
      )}
    </>
  );
}
