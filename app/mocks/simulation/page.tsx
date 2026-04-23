'use client';

import { useEffect, useState } from 'react';
import { Timer, Shield, AlertCircle, CheckCircle2, Lock } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';

interface SimConfig {
  examType: string;
  simulationMode: { strictTiming: boolean; noPause: boolean; sequentialSubtests: boolean; noBackNavigation: boolean; showCountdown: boolean; stressIndicators: boolean };
  subtestTimings: Record<string, { durationMinutes: number; sections: number }>;
  totalDurationMinutes: number;
  completedSimulations: number;
  recommendation: string;
  unlocked: boolean;
}

async function apiRequest<T = unknown>(path: string, init?: RequestInit): Promise<T> {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}${path}`, { ...init, headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json', ...init?.headers } });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

export default function ExamSimulationPage() {
  const [config, setConfig] = useState<SimConfig | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    analytics.track('exam_simulation_viewed');
    apiRequest<SimConfig>('/v1/learner/exam-simulation-config').then(setConfig).catch(() => {}).finally(() => setLoading(false));
  }, []);

  return (
    <LearnerDashboardShell>
      <LearnerPageHero title="Exam Simulation Mode" description="Practice under real exam conditions — strict timing, no pauses, sequential subtests." />

      <MotionSection className="space-y-6">
        {loading ? (
          <div className="space-y-4">{Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-32 rounded-xl" />)}</div>
        ) : config ? (
          <>
            <MotionItem>
              <Card className="p-6">
                <div className="flex items-start gap-4">
                  {config.unlocked ? <CheckCircle2 className="w-8 h-8 text-success flex-shrink-0" /> : <Lock className="w-8 h-8 text-warning flex-shrink-0" />}
                  <div>
                    <h3 className="text-lg font-semibold">{config.unlocked ? 'Simulation Mode Unlocked' : 'Simulation Mode Locked'}</h3>
                    <p className="text-sm text-muted-foreground mt-1">{config.recommendation}</p>
                    <p className="text-sm text-muted-foreground mt-1">Completed simulations: <strong>{config.completedSimulations}</strong></p>
                  </div>
                </div>
              </Card>
            </MotionItem>

            <LearnerSurfaceSectionHeader title="Simulation Rules" />
            <div className="grid grid-cols-2 md:grid-cols-3 gap-3">
              {Object.entries(config.simulationMode).map(([key, val]) => (
                <MotionItem key={key}>
                  <Card className="p-4 text-center">
                    {val ? <Shield className="w-5 h-5 mx-auto mb-2 text-primary" /> : <AlertCircle className="w-5 h-5 mx-auto mb-2 text-muted-foreground" />}
                    <p className="text-sm font-medium capitalize">{key.replace(/([A-Z])/g, ' $1').trim()}</p>
                    <Badge variant={val ? 'default' : 'outline'} className="mt-1">{val ? 'Active' : 'Off'}</Badge>
                  </Card>
                </MotionItem>
              ))}
            </div>

            <LearnerSurfaceSectionHeader title="Subtest Timings" />
            <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
              {Object.entries(config.subtestTimings).map(([subtest, timing]) => (
                <MotionItem key={subtest}>
                  <Card className="p-4 text-center">
                    <Timer className="w-5 h-5 mx-auto mb-2 text-info" />
                    <p className="text-lg font-bold">{timing.durationMinutes} min</p>
                    <p className="text-sm font-medium capitalize">{subtest}</p>
                    <p className="text-xs text-muted-foreground">{timing.sections} section{timing.sections > 1 ? 's' : ''}</p>
                  </Card>
                </MotionItem>
              ))}
            </div>

            <MotionItem>
              <Card className="p-4 text-center bg-muted/50">
                <p className="text-sm text-muted-foreground">Total exam duration: <strong>{config.totalDurationMinutes} minutes</strong></p>
              </Card>
            </MotionItem>
          </>
        ) : (
          <Card className="p-8 text-center text-muted-foreground"><p>Unable to load simulation configuration.</p></Card>
        )}
      </MotionSection>
    </LearnerDashboardShell>
  );
}
