'use client';

import { LearnerDashboardShell } from "@/components/layout/learner-dashboard-shell";
import { AsyncStateWrapper } from "@/components/state/async-state-wrapper";
import { InlineAlert } from '@/components/ui/alert';
import { StatusBadge, type StatusType } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { MotionSection } from '@/components/ui/motion-primitives';
import { useAnalytics } from '@/hooks/use-analytics';
import { fetchDiagnosticSession, startDiagnostic } from '@/lib/api';
import type { DiagnosticSession, DiagnosticSubTest } from '@/lib/mock-data';
import {
    ArrowRight, BookOpen, CheckCircle2, Clock, Headphones, Mic, PenLine, Play,
    RotateCcw
} from 'lucide-react';
import { useRouter } from 'next/navigation';
import { useCallback, useEffect, useState } from 'react';

const SUBTEST_CONFIG: Record<
  string,
  { icon: typeof PenLine; color: string; bg: string; route: string }
> = {
  Writing: { icon: PenLine, color: 'text-info', bg: 'bg-info/10', route: '/diagnostic/writing' },
  Speaking: { icon: Mic, color: 'text-primary', bg: 'bg-primary/10', route: '/diagnostic/speaking' },
  Reading: { icon: BookOpen, color: 'text-success', bg: 'bg-success/10', route: '/diagnostic/reading' },
  Listening: { icon: Headphones, color: 'text-warning', bg: 'bg-amber-50', route: '/diagnostic/listening' },
};

export default function DiagnosticHubPage() {
  const router = useRouter();
  const { track } = useAnalytics();
  const [session, setSession] = useState<DiagnosticSession | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();

  const load = useCallback(async () => {
    try {
      setLoading(true);
      setError(undefined);
      const data = await fetchDiagnosticSession();
      // If session hasn't started, auto-start it
      if (data.status === 'not_started') {
        const started = await startDiagnostic();
        setSession(started);
      } else {
        setSession(data);
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load diagnostic session');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  const completedCount = session?.subTests.filter((s) => s.status === 'completed').length ?? 0;
  const totalCount = session?.subTests.length ?? 4;
  const allComplete = completedCount === totalCount;

  const handleStartSubTest = (sub: DiagnosticSubTest) => {
    const config = SUBTEST_CONFIG[sub.subTest];
    track('diagnostic_subtest_started', { subTest: sub.subTest });
    router.push(config.route);
  };

  const status: 'loading' | 'error' | 'success' =
    loading ? 'loading' : error ? 'error' : 'success';

  return (
    <LearnerDashboardShell pageTitle="Diagnostic Hub">
      <div className="space-y-6">
        <AsyncStateWrapper status={status} onRetry={load} errorMessage={error}>
          {session && (
            <>
              {/* Progress header */}
              <MotionSection delayIndex={0}>
              <div className="text-center space-y-2">
                <h1 className="text-xl sm:text-2xl font-bold text-navy">
                  {allComplete ? 'All Sub-Tests Complete!' : 'Choose a Sub-Test'}
                </h1>
                <p className="text-sm text-muted">
                  {allComplete
                    ? 'View your diagnostic results below.'
                    : `${completedCount} of ${totalCount} completed — complete them in any order.`}
                </p>
                {/* Progress bar */}
                <div className="w-full max-w-sm mx-auto h-2 bg-border rounded-full overflow-hidden">
                  <div
                    className="h-full bg-primary rounded-full transition-all duration-500"
                    style={{ width: `${(completedCount / totalCount) * 100}%` }}
                  />
                </div>
              </div>
              </MotionSection>

              {/* Sub-test cards */}
              <MotionSection delayIndex={1}>
              <div className="grid gap-4 sm:grid-cols-2">
                {session.subTests.map((sub) => {
                  const config = SUBTEST_CONFIG[sub.subTest];
                  const Icon = config.icon;
                  const isComplete = sub.status === 'completed';
                  const isInProgress = sub.status === 'in_progress';

                  return (
                    <Card
                      key={sub.subTest}
                      className={isComplete ? 'border-success/30 bg-success/10' : ''}
                    >
                      <div className="flex items-start gap-4">
                        <div
                          className={`w-12 h-12 rounded-xl ${config.bg} ${config.color} flex items-center justify-center shrink-0`}
                        >
                          {isComplete ? (
                            <CheckCircle2 className="w-6 h-6 text-success" />
                          ) : (
                            <Icon className="w-6 h-6" />
                          )}
                        </div>
                        <div className="flex-1 min-w-0">
                          <div className="flex items-center gap-2 mb-1">
                            <p className="text-sm font-bold text-navy">{sub.subTest}</p>
                            <StatusBadge status={sub.status as StatusType} />
                          </div>
                          <p className="text-xs text-muted flex items-center gap-1">
                            <Clock className="w-3 h-3" />
                            {sub.estimatedDuration}
                          </p>
                          {sub.completedAt && (
                            <p className="text-xs text-success mt-1">
                              Completed {new Date(sub.completedAt).toLocaleDateString()}
                            </p>
                          )}
                        </div>
                      </div>
                      {!isComplete && (
                        <div className="mt-4 pt-3 border-t border-border">
                          <Button
                            size="sm"
                            fullWidth
                            onClick={() => handleStartSubTest(sub)}
                            className="gap-1.5"
                          >
                            {isInProgress ? (
                              <>
                                <RotateCcw className="w-3.5 h-3.5" /> Resume
                              </>
                            ) : (
                              <>
                                <Play className="w-3.5 h-3.5" /> Start
                              </>
                            )}
                          </Button>
                        </div>
                      )}
                    </Card>
                  );
                })}
              </div>
              </MotionSection>

              {/* Resume warning */}
              {session.subTests.some((s) => s.status === 'in_progress') && (
                <InlineAlert variant="info" dismissible>
                  You have a sub-test in progress. Your work has been saved — pick up where you left off.
                </InlineAlert>
              )}

              {/* View Results CTA */}
              {allComplete && (
                <div className="flex justify-center pt-2">
                  <Button
                    size="lg"
                    onClick={() => {
                      track('diagnostic_completed', { sessionId: session.id });
                      router.push('/diagnostic/results');
                    }}
                    className="gap-2 shadow-lg"
                  >
                    View Results
                    <ArrowRight className="w-4 h-4" />
                  </Button>
                </div>
              )}
            </>
          )}
        </AsyncStateWrapper>
      </div>
    </LearnerDashboardShell>
  );
}
