'use client';

import { useEffect, useState, useCallback } from 'react';
import { useRouter } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { AsyncStateWrapper } from '@/components/state';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { ReadinessMeter } from '@/components/domain/readiness-meter';
import { WeakestLinkCard } from '@/components/domain/weakest-link-card';
import { CriterionBreakdownCard } from '@/components/domain/criterion-breakdown-card';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Badge, ConfidenceBadge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { ProgressBar } from '@/components/ui/progress';
import { useAnalytics } from '@/hooks/use-analytics';
import { fetchDiagnosticResults, fetchDiagnosticSession } from '@/lib/api';
import type { DiagnosticResult, DiagnosticSession } from '@/lib/mock-data';
import {
  PenLine,
  Mic,
  BookOpen,
  Headphones,
  ShieldCheck,
  ArrowRight,
  TrendingUp,
  AlertTriangle,
  CheckCircle2,
} from 'lucide-react';

const SUBTEST_CONFIG: Record<
  string,
  { icon: typeof PenLine; color: string; bg: string; barColor: 'primary' | 'success' | 'danger' }
> = {
  Writing: { icon: PenLine, color: 'text-info', bg: 'bg-info/10', barColor: 'primary' },
  Speaking: { icon: Mic, color: 'text-primary', bg: 'bg-primary/10', barColor: 'primary' },
  Reading: { icon: BookOpen, color: 'text-success', bg: 'bg-success/10', barColor: 'success' },
  Listening: { icon: Headphones, color: 'text-warning', bg: 'bg-amber-50', barColor: 'primary' },
};

export default function DiagnosticResultsPage() {
  const router = useRouter();
  const { track } = useAnalytics();

  const [results, setResults] = useState<DiagnosticResult[]>([]);
  const [session, setSession] = useState<DiagnosticSession | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  const [expandedSubTest, setExpandedSubTest] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      setLoading(true);
      setError(undefined);
      const [res, sess] = await Promise.all([
        fetchDiagnosticResults(),
        fetchDiagnosticSession(),
      ]);
      setResults(res);
      setSession(sess);
      track('evaluation_viewed', { mode: 'diagnostic' });
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load diagnostic results');
    } finally {
      setLoading(false);
    }
  }, [track]);

  useEffect(() => { load(); }, [load]);

  const overallReadiness = results.length
    ? Math.round(results.reduce((sum, r) => sum + r.readiness, 0) / results.length)
    : 0;

  const weakest = results.length
    ? results.reduce((min, r) => (r.readiness < min.readiness ? r : min), results[0])
    : null;

  const pendingSubTests = session?.subTests.filter((s) => s.status !== 'completed') ?? [];
  const isPartial = pendingSubTests.length > 0;

  const status: 'loading' | 'error' | 'success' | 'partial' =
    loading ? 'loading' : error ? 'error' : isPartial ? 'partial' : 'success';

  return (
    <LearnerDashboardShell pageTitle="Diagnostic Results">
      <div className="space-y-8">
        <AsyncStateWrapper
          status={status}
          onRetry={load}
          errorMessage={error}
          partialMessage={`${pendingSubTests.length} sub-test${pendingSubTests.length > 1 ? 's' : ''} still pending. Results below are based on completed sections only.`}
        >
          {/* Trust notice */}
          <InlineAlert variant="info" className="border-primary/20 bg-primary/5">
            <div className="flex items-start gap-3">
              <ShieldCheck className="w-5 h-5 text-primary shrink-0 mt-0.5" />
              <div>
                <p className="font-bold text-navy text-sm">Training Estimates Only</p>
                <p className="text-xs text-muted mt-1">
                  These results are AI-generated estimates to guide your study plan.
                  They are <strong>not</strong> official OET scores.
                </p>
              </div>
            </div>
          </InlineAlert>

          {/* Overall readiness hero */}
          <MotionSection delayIndex={0}>
          <Card className="text-center py-8">
            <ReadinessMeter
              value={overallReadiness}
              label="Overall Test Readiness"
              sublabel={`Based on ${results.length} sub-test${results.length !== 1 ? 's' : ''}`}
              size={140}
            />
          </Card>
          </MotionSection>

          {/* Sub-test result cards */}
          <MotionSection delayIndex={1}>
          <div className="grid gap-4 sm:grid-cols-2">
            {results.map((result) => {
              const config = SUBTEST_CONFIG[result.subTest];
              const Icon = config.icon;
              const isExpanded = expandedSubTest === result.subTest;

              return (
                <Card key={result.subTest} className="flex flex-col">
                  {/* Header */}
                  <div className="flex items-start gap-3 mb-3">
                    <div
                      className={`w-10 h-10 rounded-lg ${config.bg} ${config.color} flex items-center justify-center shrink-0`}
                    >
                      <Icon className="w-5 h-5" />
                    </div>
                    <div className="flex-1 min-w-0">
                      <p className="text-sm font-bold text-navy">{result.subTest}</p>
                      <div className="flex items-center gap-2 mt-1">
                        <Badge variant="default">{result.scoreRange}</Badge>
                        <ConfidenceBadge level={result.confidence.toLowerCase() as 'high' | 'medium' | 'low'} />
                      </div>
                    </div>
                  </div>

                  {/* Readiness bar */}
                  <div className="mb-3">
                    <div className="flex items-center justify-between text-xs mb-1">
                      <span className="text-muted">Readiness</span>
                      <span className="font-bold text-navy">{result.readiness}%</span>
                    </div>
                    <ProgressBar
                      value={result.readiness}
                      color={result.readiness >= 70 ? 'success' : result.readiness >= 50 ? 'primary' : 'danger'}
                    />
                  </div>

                  {/* Strengths & Issues */}
                  <div className="space-y-2 mb-3">
                    {result.strengths.slice(0, 2).map((s, i) => (
                      <p key={i} className="text-xs text-navy flex items-start gap-1.5">
                        <CheckCircle2 className="w-3.5 h-3.5 text-success shrink-0 mt-0.5" />
                        {s}
                      </p>
                    ))}
                    {result.issues.slice(0, 2).map((s, i) => (
                      <p key={i} className="text-xs text-navy flex items-start gap-1.5">
                        <AlertTriangle className="w-3.5 h-3.5 text-warning shrink-0 mt-0.5" />
                        {s}
                      </p>
                    ))}
                  </div>

                  {/* Expand criterion breakdown */}
                  {result.criterionBreakdown.length > 0 && (
                    <div className="mt-auto pt-3 border-t border-border">
                      <button
                        onClick={() =>
                          setExpandedSubTest(isExpanded ? null : result.subTest)
                        }
                        className="text-xs font-semibold text-primary hover:underline"
                      >
                        {isExpanded ? 'Hide Breakdown' : 'View Criterion Breakdown'}
                      </button>
                      {isExpanded && (
                        <div className="mt-3 space-y-3">
                          {result.criterionBreakdown.map((c) => (
                            <CriterionBreakdownCard
                              key={c.name}
                              criterion={c.name}
                              score={Math.round((c.score / c.maxScore) * 100)}
                              grade={c.grade}
                              explanation={c.explanation}
                            />
                          ))}
                        </div>
                      )}
                    </div>
                  )}
                </Card>
              );
            })}
          </div>
          </MotionSection>

          {/* Weakest link card */}
          {weakest && (
            <MotionSection delayIndex={2}>
            <WeakestLinkCard
              criterion={weakest.issues[0] ?? 'General skills'}
              subtest={weakest.subTest}
              description={`${weakest.subTest} had the lowest readiness at ${weakest.readiness}%. Focus here first for maximum improvement.`}
              score={weakest.scoreRange}
            />
            </MotionSection>
          )}

          {/* Action plan */}
          <MotionSection delayIndex={3}>
          <Card className="border-primary/20 bg-primary/5">
            <div className="flex items-start gap-4">
              <div className="w-12 h-12 rounded-xl bg-primary/10 text-primary flex items-center justify-center shrink-0">
                <TrendingUp className="w-6 h-6" />
              </div>
              <div className="flex-1">
                <h3 className="text-sm font-bold text-navy mb-1">Your First Study Week</h3>
                <p className="text-xs text-muted mb-3">
                  Based on your diagnostic results, we&apos;ve created a personalised study plan targeting your
                  weakest areas first. Your plan adapts as you complete tasks and improve.
                </p>
                <Button
                  size="md"
                  onClick={() => router.push('/study-plan')}
                  className="gap-1.5"
                >
                  View Study Plan <ArrowRight className="w-3.5 h-3.5" />
                </Button>
              </div>
            </div>
          </Card>
          </MotionSection>

          {/* Quick actions */}
          <div className="flex flex-wrap gap-3 justify-center">
            <Button variant="outline" onClick={() => router.push('/')}>
              Go to Dashboard
            </Button>
            <Button variant="outline" onClick={() => router.push('/readiness')}>
              View Full Readiness
            </Button>
          </div>
        </AsyncStateWrapper>
      </div>
    </LearnerDashboardShell>
  );
}
