'use client';

// Phase 7 follow-up: per-question deep-dive analytics page.
// Drills into a single (paperId, questionNumber) tuple. Filters the
// existing /v1/admin/listening/analytics response client-side — no new
// backend endpoint required. Linked from the hardest-questions table on
// the main listening analytics page.

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { AlertTriangle, ArrowLeft, BarChart3, ListChecks, Target, Headphones } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { MotionSection } from '@/components/ui/motion-primitives';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteSummaryCard,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
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
    return () => {
      cancelled = true;
    };
  }, [days, isAuthenticated, role, routeIsValid]);

  const filtered = useMemo(() => {
    if (!analytics || !drill) {
      return { hardest: null, distractor: null, misspellings: [] as ListeningCommonMisspelling[] };
    }
    const hardest: ListeningHardestQuestion | null =
      analytics.hardestQuestions.find(
        (q) => q.paperId === drill.paperId && q.questionNumber === drill.questionNumber,
      ) ?? null;
    const distractor: ListeningDistractorHeat | null =
      analytics.distractorHeat.find(
        (d) => d.paperId === drill.paperId && d.questionNumber === drill.questionNumber,
      ) ?? null;
    // Misspellings are not keyed by question id in the analytics DTO — they
    // are aggregated class-wide by correctAnswer. We surface only the
    // distractor.correctAnswer's misspelling row so the page is still
    // contextual when the question is a Part A gap-fill.
    const correctAnswer = distractor?.correctAnswer?.toLowerCase().trim() ?? '';
    const misspellings = correctAnswer
      ? analytics.commonMisspellings.filter(
          (m) => m.correctAnswer.toLowerCase().trim() === correctAnswer,
        )
      : [];
    return { hardest, distractor, misspellings };
  }, [analytics, drill]);

  if (!isAuthenticated || role !== 'admin') return null;

  if (!drill) {
    return (
      <AdminRouteWorkspace role="main" aria-label="Listening question deep dive">
        <AdminRouteHero
          eyebrow="Analytics"
          icon={Target}
          accent="navy"
          title="Question deep-dive"
          description="Drill into a single Listening question to see accuracy, distractor heat, and common misspellings."
        />
        <MotionSection delayIndex={0}>
          <InlineAlert variant="warning">
            Invalid question route — provide both paperId and a numeric question number.
          </InlineAlert>
        </MotionSection>
      </AdminRouteWorkspace>
    );
  }

  const { hardest, distractor, misspellings } = filtered;
  const wrongHistogramRows = distractor
    ? Object.entries(distractor.wrongAnswerHistogram).sort((a, b) => b[1] - a[1])
    : [];

  return (
    <AdminRouteWorkspace role="main" aria-label="Listening question deep dive">
      <AdminRouteHero
        eyebrow="Analytics"
        icon={Target}
        accent="navy"
        title={`Q${drill.questionNumber} · ${hardest?.paperTitle ?? 'Listening question'}`}
        description={`Per-question deep-dive over the last ${days} days. Class-wide signal filtered to this single item.`}
        aside={
          <div className="rounded-2xl border border-border bg-background-light p-4 shadow-sm">
            <div className="flex flex-wrap items-center gap-2">
              {windowOptions.map((option) => (
                <Button
                  key={option}
                  type="button"
                  size="sm"
                  variant={days === option ? 'primary' : 'outline'}
                  aria-pressed={days === option}
                  onClick={() => setDays(option)}
                >
                  {option}d
                </Button>
              ))}
            </div>
            <p className="mt-3 text-xs text-muted">paperId · {drill.paperId}</p>
          </div>
        }
      />

      <MotionSection delayIndex={0}>
        <div className="flex items-center gap-3">
          <Link href="/admin/analytics/listening">
            <Button variant="ghost" size="sm" className="gap-1">
              <ArrowLeft className="h-4 w-4" /> Back to Listening analytics
            </Button>
          </Link>
        </div>
      </MotionSection>

      {status === 'loading' ? (
        <MotionSection delayIndex={1}>
          <div className="grid gap-4 md:grid-cols-3">
            <Skeleton className="h-24" />
            <Skeleton className="h-24" />
            <Skeleton className="h-24" />
          </div>
        </MotionSection>
      ) : status === 'error' ? (
        <MotionSection delayIndex={1}>
          <InlineAlert variant="error">Could not load Listening analytics.</InlineAlert>
        </MotionSection>
      ) : status === 'empty' || (!hardest && !distractor && misspellings.length === 0) ? (
        <MotionSection delayIndex={1}>
          <InlineAlert variant="info">
            No class data for this question in the last {days} days. Try a longer window, or confirm the question id is correct.
          </InlineAlert>
        </MotionSection>
      ) : (
        <div className="space-y-6" data-testid="listening-question-deep-dive">
          <MotionSection delayIndex={1}>
            <div className="grid gap-4 md:grid-cols-3">
              <AdminRouteSummaryCard
                label="Accuracy"
                value={formatPercent(hardest?.accuracyPercent ?? null)}
                hint={hardest ? `${hardest.attemptCount} attempts` : 'Not yet flagged hard'}
                icon={<BarChart3 className="h-5 w-5" />}
              />
              <AdminRouteSummaryCard
                label="Part"
                value={hardest?.partCode ?? '—'}
                hint={hardest?.partCode === 'A' ? '24 gap-fill' : hardest?.partCode === 'B' ? '6 MCQs' : '12 MCQs'}
                icon={<Headphones className="h-5 w-5" />}
              />
              <AdminRouteSummaryCard
                label="Correct answer"
                value={distractor?.correctAnswer ?? '—'}
                hint={wrongHistogramRows.length > 0 ? `${wrongHistogramRows.length} wrong-answer buckets` : 'No wrong-answer signal'}
                icon={<ListChecks className="h-5 w-5" />}
              />
            </div>
          </MotionSection>

          <MotionSection delayIndex={2}>
            <AdminRoutePanel title="Wrong-answer histogram">
              {wrongHistogramRows.length === 0 ? (
                <p className="text-sm text-muted">No wrong answers recorded for this question in the window.</p>
              ) : (
                <div className="space-y-2" data-testid="listening-question-distractor-list">
                  {wrongHistogramRows.map(([wrong, count]) => (
                    <div
                      key={wrong}
                      className="flex items-center justify-between rounded-lg border border-border bg-background-light p-3"
                    >
                      <span className="truncate text-sm font-semibold text-navy">{wrong || '(blank)'}</span>
                      <Badge variant="muted">×{count}</Badge>
                    </div>
                  ))}
                </div>
              )}
            </AdminRoutePanel>
          </MotionSection>

          <MotionSection delayIndex={3}>
            <AdminRoutePanel title="Common misspellings of the correct answer">
              {misspellings.length === 0 ? (
                <p className="text-sm text-muted">
                  <AlertTriangle className="mr-1 inline h-4 w-4" /> No spelling-near-miss patterns detected for &ldquo;
                  {distractor?.correctAnswer ?? '—'}&rdquo; in the last {days} days.
                </p>
              ) : (
                <div className="grid gap-2 md:grid-cols-2" data-testid="listening-question-misspellings">
                  {misspellings.map((s, i) => (
                    <div
                      key={`${s.correctAnswer}-${s.wrongSpelling}-${i}`}
                      className="flex items-center justify-between rounded-lg border border-border bg-background-light p-3"
                    >
                      <div className="min-w-0">
                        <p className="truncate text-sm font-semibold text-navy">{s.correctAnswer}</p>
                        <p className="truncate text-xs text-muted">→ {s.wrongSpelling}</p>
                      </div>
                      <Badge variant="muted">×{s.count}</Badge>
                    </div>
                  ))}
                </div>
              )}
            </AdminRoutePanel>
          </MotionSection>
        </div>
      )}
    </AdminRouteWorkspace>
  );
}
