'use client';

// Phase 7 follow-up: per-question deep-dive analytics page.

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { AlertTriangle, ArrowLeft, BarChart3, ListChecks, Target, Headphones } from 'lucide-react';
import { AdminOperationsLayout, KpiStrip, BentoGrid, BentoCell } from '@/components/admin/layout/admin-operations-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  getListeningAdminAnalytics,
  type ListeningAdminAnalytics,
  type ListeningHardestQuestion,
  type ListeningDistractorHeat,
  type ListeningCommonMisspelling,
} from '@/lib/listening-authoring-api';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type DrillTuple = { paperId: string; questionNumber: number };

function readDrillParams(params: Record<string, string | string[] | undefined> | null): DrillTuple | null {
  if (!params) return null;
  const paperIdRaw = params.paperId;
  const numberRaw = params.number;
  const paperId = Array.isArray(paperIdRaw) ? paperIdRaw[0] : paperIdRaw;
  const numberStr = Array.isArray(numberRaw) ? numberRaw[0] : numberRaw;
  if (!paperId || !numberStr) return null;
  if (!/^\d+$/.test(numberStr)) return null;
  const number = Number.parseInt(numberStr, 10);
  if (Number.isNaN(number) || number < 1) return null;
  return { paperId, questionNumber: number };
}

function formatPercent(value: number | null | undefined) {
  if (value == null) return '--';
  return `${Number.isInteger(value) ? value.toFixed(0) : value.toFixed(1)}%`;
}

const windowOptions = [7, 30, 90];

export default function ListeningQuestionDeepDivePage() {
  const params = useParams<{ paperId?: string | string[]; number?: string | string[] }>();
  const drill = useMemo(() => readDrillParams(params), [params]);
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<PageStatus>('loading');
  const [days, setDays] = useState(30);
  const [analytics, setAnalytics] = useState<ListeningAdminAnalytics | null>(null);
  const routeIsValid = drill !== null;

  useEffect(() => {
    if (!isAuthenticated || role !== 'admin' || !routeIsValid) return;
    let cancelled = false;
    (async () => {
      setStatus('loading');
      try {
        const result = await getListeningAdminAnalytics(days);
        if (cancelled) return;
        setAnalytics(result);
        setStatus(result.completedAttempts > 0 ? 'success' : 'empty');
      } catch (e) {
        console.error(e);
        if (!cancelled) setStatus('error');
      }
    })();
    return () => { cancelled = true; };
  }, [days, isAuthenticated, role, routeIsValid]);

  const filtered = useMemo(() => {
    if (!analytics || !drill) {
      return { hardest: null, distractor: null, misspellings: [] as ListeningCommonMisspelling[] };
    }
    const hardest: ListeningHardestQuestion | null =
      analytics.hardestQuestions.find((q) => q.paperId === drill.paperId && q.questionNumber === drill.questionNumber) ?? null;
    const distractor: ListeningDistractorHeat | null =
      analytics.distractorHeat.find((d) => d.paperId === drill.paperId && d.questionNumber === drill.questionNumber) ?? null;
    const correctAnswer = distractor?.correctAnswer?.toLowerCase().trim() ?? '';
    const misspellings = correctAnswer
      ? analytics.commonMisspellings.filter((m) => m.correctAnswer.toLowerCase().trim() === correctAnswer)
      : [];
    return { hardest, distractor, misspellings };
  }, [analytics, drill]);

  if (!isAuthenticated || role !== 'admin') return null;

  if (!drill) {
    return (
      <AdminOperationsLayout
        title="Question deep-dive"
        description="Drill into a single Listening question to see accuracy, distractor heat, and common misspellings."
        eyebrow="Analytics"
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Listening', href: '/admin/analytics/listening' },
          { label: 'Question' },
        ]}
        primaryGrid={
          <InlineAlert variant="warning">
            Invalid question route — provide both paperId and a numeric question number.
          </InlineAlert>
        }
      />
    );
  }

  const { hardest, distractor, misspellings } = filtered;
  const wrongHistogramRows = distractor
    ? Object.entries(distractor.wrongAnswerHistogram).sort((a, b) => b[1] - a[1])
    : [];

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Listening', href: '/admin/analytics/listening' },
    { label: `Q${drill.questionNumber}` },
  ];

  return (
    <AdminOperationsLayout
      title={`Q${drill.questionNumber} · ${hardest?.paperTitle ?? 'Listening question'}`}
      description={`Per-question deep-dive over the last ${days} days. Class-wide signal filtered to this single item.`}
      eyebrow="Analytics"
      breadcrumbs={breadcrumbs}
      actions={
        <div className="flex flex-wrap items-center gap-2">
          {windowOptions.map((option) => (
            <Button
              key={option}
              type="button"
              size="sm"
              variant={days === option ? 'primary' : 'secondary'}
              aria-pressed={days === option}
              onClick={() => setDays(option)}
            >
              {option}d
            </Button>
          ))}
        </div>
      }
      kpis={
        status === 'success' && (hardest || distractor) ? (
          <KpiStrip>
            <KpiTile label="Accuracy" value={formatPercent(hardest?.accuracyPercent ?? null)} icon={<BarChart3 className="h-4 w-4" />} tone="primary" />
            <KpiTile label="Part" value={hardest?.partCode ?? '—'} icon={<Headphones className="h-4 w-4" />} tone="info" />
            <KpiTile label="Correct answer" value={distractor?.correctAnswer ?? '—'} icon={<ListChecks className="h-4 w-4" />} tone="success" />
            <KpiTile label="Attempts" value={hardest ? hardest.attemptCount : 0} icon={<Target className="h-4 w-4" />} tone="default" />
          </KpiStrip>
        ) : null
      }
      primaryGrid={
        <div className="space-y-6" data-testid="listening-question-deep-dive">
          <div className="flex items-center gap-3">
            <Button variant="ghost" size="sm" asChild>
              <Link href="/admin/analytics/listening">
                <ArrowLeft className="h-4 w-4" /> Back to Listening analytics
              </Link>
            </Button>
            <p className="text-xs text-admin-fg-muted">paperId · {drill.paperId}</p>
          </div>

          {status === 'loading' ? (
            <div className="grid gap-4 md:grid-cols-3">
              <Skeleton className="h-24 rounded-admin" />
              <Skeleton className="h-24 rounded-admin" />
              <Skeleton className="h-24 rounded-admin" />
            </div>
          ) : status === 'error' ? (
            <InlineAlert variant="error">Could not load Listening analytics.</InlineAlert>
          ) : status === 'empty' || (!hardest && !distractor && misspellings.length === 0) ? (
            <InlineAlert variant="info">
              No class data for this question in the last {days} days. Try a longer window, or confirm the question id is correct.
            </InlineAlert>
          ) : (
            <BentoGrid>
              <BentoCell span={{ default: 12, xl: 6 }}>
                <Card>
                  <CardHeader><CardTitle>Wrong-answer histogram</CardTitle></CardHeader>
                  <CardContent>
                    {wrongHistogramRows.length === 0 ? (
                      <p className="text-sm text-admin-fg-muted">No wrong answers recorded for this question in the window.</p>
                    ) : (
                      <div className="space-y-2" data-testid="listening-question-distractor-list">
                        {wrongHistogramRows.map(([wrong, count]) => (
                          <div key={wrong} className="flex items-center justify-between rounded-admin border border-admin-border bg-admin-bg-surface p-3">
                            <span className="truncate text-sm font-semibold text-admin-fg-strong">{wrong || '(blank)'}</span>
                            <Badge variant="default" intensity="tinted">×{count}</Badge>
                          </div>
                        ))}
                      </div>
                    )}
                  </CardContent>
                </Card>
              </BentoCell>

              <BentoCell span={{ default: 12, xl: 6 }}>
                <Card>
                  <CardHeader><CardTitle>Common misspellings of the correct answer</CardTitle></CardHeader>
                  <CardContent>
                    {misspellings.length === 0 ? (
                      <p className="text-sm text-admin-fg-muted">
                        <AlertTriangle className="mr-1 inline h-4 w-4" /> No spelling-near-miss patterns detected for &ldquo;{distractor?.correctAnswer ?? '—'}&rdquo; in the last {days} days.
                      </p>
                    ) : (
                      <div className="grid gap-2 md:grid-cols-2" data-testid="listening-question-misspellings">
                        {misspellings.map((s, i) => (
                          <div key={`${s.correctAnswer}-${s.wrongSpelling}-${i}`} className="flex items-center justify-between rounded-admin border border-admin-border bg-admin-bg-surface p-3">
                            <div className="min-w-0">
                              <p className="truncate text-sm font-semibold text-admin-fg-strong">{s.correctAnswer}</p>
                              <p className="truncate text-xs text-admin-fg-muted">→ {s.wrongSpelling}</p>
                            </div>
                            <Badge variant="default" intensity="tinted">×{s.count}</Badge>
                          </div>
                        ))}
                      </div>
                    )}
                  </CardContent>
                </Card>
              </BentoCell>
            </BentoGrid>
          )}
        </div>
      }
    />
  );
}
