'use client';

// Phase 7 of LISTENING-MODULE-PLAN.md — admin Listening analytics dashboard.
// Mirrors the Reading analytics surface conventions (AdminRouteHero +
// AdminRoutePanel + AdminRouteSummaryCard) but consumes the Listening
// admin analytics DTO from `/v1/admin/listening/analytics`.

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { BarChart3, Gauge, Headphones, ListChecks, Target, AlertTriangle } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { Skeleton } from '@/components/ui/skeleton';
import { MotionSection } from '@/components/ui/motion-primitives';
import {
  AdminRouteFreshnessBadge,
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteSummaryCard,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  getListeningAdminAnalytics,
  exportListeningAdminAttempt,
  type ListeningAdminAnalytics,
} from '@/lib/listening-authoring-api';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';

const windowOptions = [7, 30, 90];

function formatPercent(value: number | null | undefined) {
  if (value == null) return '--';
  return `${Number.isInteger(value) ? value.toFixed(0) : value.toFixed(1)}%`;
}

function formatNumber(value: number | null | undefined) {
  if (value == null) return '--';
  return Number.isInteger(value) ? value.toLocaleString() : value.toFixed(1);
}

function AccuracyBar({ value }: { value: number | null }) {
  const width = value == null ? 0 : Math.max(0, Math.min(100, value));
  return (
    <div className="h-2 w-full overflow-hidden rounded-full bg-muted" aria-hidden="true">
      <div className="h-full rounded-full bg-primary" style={{ width: `${width}%` }} />
    </div>
  );
}

export default function ListeningAnalyticsPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<PageStatus>('loading');
  const [days, setDays] = useState(30);
  const [analytics, setAnalytics] = useState<ListeningAdminAnalytics | null>(null);

  // Audit export controls
  const [exportAttemptId, setExportAttemptId] = useState('');
  const [exportError, setExportError] = useState<string | null>(null);
  const [isExporting, setIsExporting] = useState(false);

  useEffect(() => {
    if (!isAuthenticated || role !== 'admin') return;
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
  }, [days, isAuthenticated, role]);

  async function handleExportAttempt() {
    const id = exportAttemptId.trim();
    if (!id) {
      setExportError('Enter an attempt id.');
      return;
    }
    setExportError(null);
    setIsExporting(true);
    try {
      const payload = await exportListeningAdminAttempt(id);
      const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `listening-attempt-${id}.json`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
    } catch (e) {
      const err = e as { status?: number; message?: string };
      if (err?.status === 404) {
        setExportError(`Attempt "${id}" not found.`);
      } else if (err?.status === 403) {
        setExportError('You do not have permission to export this attempt.');
      } else {
        setExportError(err?.message || 'Export failed. Try again.');
      }
    } finally {
      setIsExporting(false);
    }
  }

  const partAccuracyMap = useMemo(() => {
    const map: Record<string, number | null> = { A: null, B: null, C: null };
    if (!analytics) return map;
    for (const p of analytics.classPartAverages) {
      map[p.partCode] = p.accuracyPercent;
    }
    return map;
  }, [analytics]);

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <AdminRouteWorkspace role="main" aria-label="Listening analytics">
      <AdminRouteHero
        eyebrow="Analytics"
        icon={BarChart3}
        accent="navy"
        title="Listening Analytics"
        description="Class-wide signal from submitted Listening attempts: per-part accuracy, hardest items, distractor heat, and common misspellings."
        aside={(
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
            <div className="mt-3">
              <AdminRouteFreshnessBadge value={new Date().toISOString()} />
            </div>
          </div>
        )}
      />

      {status === 'loading' ? (
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
          {Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-32 rounded-xl" />)}
        </div>
      ) : null}

      {status === 'error' ? (
        <AdminRoutePanel title="Listening analytics unavailable">
          <p className="text-sm text-muted">The analytics service could not be loaded. Try again after the API is available.</p>
        </AdminRoutePanel>
      ) : null}

      {status === 'empty' ? (
        <AdminRoutePanel title="No Listening submissions yet">
          <p className="text-sm text-muted">Once learners submit Listening attempts, this dashboard will populate.</p>
        </AdminRoutePanel>
      ) : null}

      {status === 'success' && analytics ? (
        <div className="space-y-8">
          <MotionSection delayIndex={0}>
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
              <AdminRouteSummaryCard
                label="Submitted Attempts"
                value={analytics.completedAttempts}
                hint={`Last ${analytics.days} days`}
                icon={<ListChecks className="h-5 w-5" />}
              />
              <AdminRouteSummaryCard
                label="Average Scaled"
                value={formatNumber(analytics.averageScaledScore)}
                hint="Anchor: 30/42 ≡ 350/500"
                icon={<Gauge className="h-5 w-5" />}
              />
              <AdminRouteSummaryCard
                label="% Likely Passing"
                value={formatPercent(analytics.percentLikelyPassing)}
                hint="≥ 350 scaled"
                icon={<Target className="h-5 w-5" />}
                tone={analytics.percentLikelyPassing >= 50 ? 'success' : 'warning'}
              />
              <AdminRouteSummaryCard
                label="Hardest Items"
                value={analytics.hardestQuestions.length}
                hint="Items below class avg accuracy"
                icon={<Headphones className="h-5 w-5" />}
              />
            </div>
          </MotionSection>

          <MotionSection delayIndex={1}>
            <AdminRoutePanel title="Class accuracy by part">
              <div className="space-y-3">
                {(['A', 'B', 'C'] as const).map((p) => (
                  <div key={p} className="grid grid-cols-[6rem_minmax(0,1fr)_5rem] items-center gap-3">
                    <div>
                      <p className="text-sm font-semibold text-navy">Part {p}</p>
                      <p className="text-xs text-muted">{p === 'A' ? '24 gap-fill' : p === 'B' ? '6 MCQs' : '12 MCQs'}</p>
                    </div>
                    <AccuracyBar value={partAccuracyMap[p]} />
                    <p className="text-right text-sm font-semibold text-navy">{formatPercent(partAccuracyMap[p])}</p>
                  </div>
                ))}
              </div>
            </AdminRoutePanel>
          </MotionSection>

          <MotionSection delayIndex={2}>
            <AdminRoutePanel title="Hardest questions">
              {analytics.hardestQuestions.length === 0 ? (
                <p className="text-sm text-muted">Not enough attempts yet — at least 3 attempts are required for an item to qualify.</p>
              ) : (
                <div className="space-y-2">
                  {analytics.hardestQuestions.map((q) => (
                    <div key={`${q.paperId}-${q.questionNumber}`} className="flex items-start justify-between gap-3 rounded-lg border border-border bg-background-light p-3">
                      <div className="min-w-0">
                        <p className="text-sm font-semibold text-navy">Q{q.questionNumber} · Part {q.partCode}</p>
                        <p className="truncate text-xs text-muted">{q.paperTitle}</p>
                      </div>
                      <div className="flex items-center gap-3">
                        <Badge variant="muted">{q.attemptCount} attempts</Badge>
                        <span className="text-sm font-semibold text-danger">{formatPercent(q.accuracyPercent)}</span>
                        <Link
                          href={`/admin/analytics/listening/question/${encodeURIComponent(q.paperId)}/${q.questionNumber}`}
                          aria-label={`Deep-dive Q${q.questionNumber} of ${q.paperTitle}`}
                        >
                          <Button type="button" size="sm" variant="outline">
                            Deep dive
                          </Button>
                        </Link>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </AdminRoutePanel>
          </MotionSection>

          <MotionSection delayIndex={3}>
            <AdminRoutePanel title="MCQ distractor heat (top 10 noisiest items)">
              {analytics.distractorHeat.length === 0 ? (
                <p className="text-sm text-muted">No MCQ distractor noise detected in this window.</p>
              ) : (
                <div className="space-y-2">
                  {analytics.distractorHeat.map((d) => (
                    <div key={`${d.paperId}-${d.questionNumber}`} className="rounded-lg border border-border bg-background-light p-3">
                      <div className="flex items-center justify-between">
                        <p className="text-sm font-semibold text-navy">Q{d.questionNumber}</p>
                        <Badge variant="success">Correct: {d.correctAnswer || '—'}</Badge>
                      </div>
                      <ul className="mt-2 space-y-1 text-xs text-muted">
                        {Object.entries(d.wrongAnswerHistogram)
                          .sort((a, b) => b[1] - a[1])
                          .slice(0, 5)
                          .map(([wrong, n]) => (
                            <li key={wrong} className="flex justify-between">
                              <span className="truncate">{wrong}</span>
                              <span className="font-mono">×{n}</span>
                            </li>
                          ))}
                      </ul>
                    </div>
                  ))}
                </div>
              )}
            </AdminRoutePanel>
          </MotionSection>

          <MotionSection delayIndex={4}>
            <AdminRoutePanel title="Common misspellings (Part A short-answer)">
              {analytics.commonMisspellings.length === 0 ? (
                <p className="text-sm text-muted">
                  <AlertTriangle className="mr-1 inline h-4 w-4" /> No spelling-near-miss patterns detected in the window.
                </p>
              ) : (
                <div className="grid gap-2 md:grid-cols-2">
                  {analytics.commonMisspellings.map((s, i) => (
                    <div key={`${s.correctAnswer}-${s.wrongSpelling}-${i}`} className="flex items-center justify-between rounded-lg border border-border bg-background-light p-3">
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
      ) : null}

      <MotionSection delayIndex={5}>
        <AdminRoutePanel title="Audit export">
          <p className="text-sm text-muted">
            Download the full normalized + legacy JSON for a single Listening attempt. The export is recorded as an audit event.
          </p>
          <div className="mt-4 flex flex-col gap-3 sm:flex-row sm:items-end">
            <div className="flex-1">
              <Input
                label="Attempt id"
                value={exportAttemptId}
                onChange={(e) => setExportAttemptId(e.currentTarget.value)}
                placeholder="e.g. 7c2d…"
                error={exportError ?? undefined}
                disabled={isExporting}
                aria-label="Listening attempt id"
              />
            </div>
            <Button
              type="button"
              variant="secondary"
              onClick={handleExportAttempt}
              disabled={isExporting || exportAttemptId.trim().length === 0}
            >
              {isExporting ? 'Exporting…' : 'Export'}
            </Button>
          </div>
        </AdminRoutePanel>
      </MotionSection>
    </AdminRouteWorkspace>
  );
}
