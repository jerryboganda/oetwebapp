'use client';

// Phase 7 of LISTENING-MODULE-PLAN.md — admin Listening analytics dashboard.

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { Gauge, Headphones, ListChecks, Target, AlertTriangle } from 'lucide-react';
import { AdminOperationsLayout, KpiStrip, BentoGrid, BentoCell } from '@/components/admin/layout/admin-operations-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Input } from '@/components/ui/form-controls';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Skeleton } from '@/components/admin/ui/skeleton';
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
    <div className="h-2 w-full overflow-hidden rounded-full bg-admin-bg-subtle" aria-hidden="true">
      <div className="h-full rounded-full bg-[var(--admin-primary)]" style={{ width: `${width}%` }} />
    </div>
  );
}

export default function ListeningAnalyticsPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<PageStatus>('loading');
  const [days, setDays] = useState(30);
  const [analytics, setAnalytics] = useState<ListeningAdminAnalytics | null>(null);

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
      if (err?.status === 404) setExportError(`Attempt "${id}" not found.`);
      else if (err?.status === 403) setExportError('You do not have permission to export this attempt.');
      else setExportError(err?.message || 'Export failed. Try again.');
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

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Analytics', href: '/admin' },
    { label: 'Listening' },
  ];

  return (
    <AdminOperationsLayout
      title="Listening Analytics"
      description="Class-wide signal from submitted Listening attempts: per-part accuracy, hardest items, distractor heat, and common misspellings."
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
        status === 'success' && analytics ? (
          <KpiStrip>
            <KpiTile label="Submitted Attempts" value={analytics.completedAttempts} icon={<ListChecks className="h-4 w-4" />} tone="primary" />
            <KpiTile label="Average Scaled" value={formatNumber(analytics.averageScaledScore)} icon={<Gauge className="h-4 w-4" />} tone="info" />
            <KpiTile
              label="% Likely Passing"
              value={formatPercent(analytics.percentLikelyPassing)}
              icon={<Target className="h-4 w-4" />}
              tone={analytics.percentLikelyPassing >= 50 ? 'success' : 'warning'}
            />
            <KpiTile label="Hardest Items" value={analytics.hardestQuestions.length} icon={<Headphones className="h-4 w-4" />} tone="default" />
          </KpiStrip>
        ) : null
      }
      primaryGrid={
        <div className="space-y-6">
          {status === 'loading' ? (
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
              {Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-32 rounded-admin" />)}
            </div>
          ) : null}

          {status === 'error' ? (
            <Card>
              <CardHeader>
                <CardTitle>Listening analytics unavailable</CardTitle>
              </CardHeader>
              <CardContent>
                <p className="text-sm text-admin-fg-muted">The analytics service could not be loaded. Try again after the API is available.</p>
              </CardContent>
            </Card>
          ) : null}

          {status === 'empty' ? (
            <Card>
              <CardHeader>
                <CardTitle>No Listening submissions yet</CardTitle>
              </CardHeader>
              <CardContent>
                <p className="text-sm text-admin-fg-muted">Once learners submit Listening attempts, this dashboard will populate.</p>
              </CardContent>
            </Card>
          ) : null}

          {status === 'success' && analytics ? (
            <>
              <Card>
                <CardHeader><CardTitle>Class accuracy by part</CardTitle></CardHeader>
                <CardContent>
                  <div className="space-y-3">
                    {(['A', 'B', 'C'] as const).map((p) => (
                      <div key={p} className="grid grid-cols-[6rem_minmax(0,1fr)_5rem] items-center gap-3">
                        <div>
                          <p className="text-sm font-semibold text-admin-fg-strong">Part {p}</p>
                          <p className="text-xs text-admin-fg-muted">{p === 'A' ? '24 gap-fill' : p === 'B' ? '6 MCQs' : '12 MCQs'}</p>
                        </div>
                        <AccuracyBar value={partAccuracyMap[p]} />
                        <p className="text-right text-sm font-semibold text-admin-fg-strong">{formatPercent(partAccuracyMap[p])}</p>
                      </div>
                    ))}
                  </div>
                </CardContent>
              </Card>

              <Card>
                <CardHeader><CardTitle>Hardest questions</CardTitle></CardHeader>
                <CardContent>
                  {analytics.hardestQuestions.length === 0 ? (
                    <p className="text-sm text-admin-fg-muted">Not enough attempts yet. At least 3 attempts are required for an item to qualify.</p>
                  ) : (
                    <div className="space-y-2">
                      {analytics.hardestQuestions.map((q) => (
                        <div key={`${q.paperId}-${q.questionNumber}`} className="flex items-start justify-between gap-3 rounded-admin border border-admin-border bg-admin-bg-surface p-3">
                          <div className="min-w-0">
                            <p className="text-sm font-semibold text-admin-fg-strong">Q{q.questionNumber} · Part {q.partCode}</p>
                            <p className="truncate text-xs text-admin-fg-muted">{q.paperTitle}</p>
                          </div>
                          <div className="flex items-center gap-3">
                            <Badge variant="default" intensity="tinted">{q.attemptCount} attempts</Badge>
                            <span className="text-sm font-semibold text-[var(--admin-danger)]">{formatPercent(q.accuracyPercent)}</span>
                            <Button type="button" size="sm" variant="secondary" asChild>
                              <Link href={`/admin/analytics/listening/question/${encodeURIComponent(q.paperId)}/${q.questionNumber}`} aria-label={`Deep-dive Q${q.questionNumber}`}>
                                Deep dive
                              </Link>
                            </Button>
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </CardContent>
              </Card>

              <BentoGrid>
                <BentoCell span={{ default: 12, xl: 6 }}>
                  <Card>
                    <CardHeader><CardTitle>MCQ distractor heat (top 10 noisiest items)</CardTitle></CardHeader>
                    <CardContent>
                      {analytics.distractorHeat.length === 0 ? (
                        <p className="text-sm text-admin-fg-muted">No MCQ distractor noise detected in this window.</p>
                      ) : (
                        <div className="space-y-2">
                          {analytics.distractorHeat.map((d) => (
                            <div key={`${d.paperId}-${d.questionNumber}`} className="rounded-admin border border-admin-border bg-admin-bg-surface p-3">
                              <div className="flex items-center justify-between">
                                <p className="text-sm font-semibold text-admin-fg-strong">Q{d.questionNumber}</p>
                                <Badge variant="success" intensity="tinted">Correct: {d.correctAnswer || '—'}</Badge>
                              </div>
                              <ul className="mt-2 space-y-1 text-xs text-admin-fg-muted">
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
                    </CardContent>
                  </Card>
                </BentoCell>
                <BentoCell span={{ default: 12, xl: 6 }}>
                  <Card>
                    <CardHeader><CardTitle>Common misspellings (Part A short-answer)</CardTitle></CardHeader>
                    <CardContent>
                      {analytics.commonMisspellings.length === 0 ? (
                        <p className="text-sm text-admin-fg-muted">
                          <AlertTriangle className="mr-1 inline h-4 w-4" /> No spelling-near-miss patterns detected in the window.
                        </p>
                      ) : (
                        <div className="grid gap-2 md:grid-cols-2">
                          {analytics.commonMisspellings.map((s, i) => (
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
            </>
          ) : null}

          <Card>
            <CardHeader><CardTitle>Audit export</CardTitle></CardHeader>
            <CardContent>
              <p className="text-sm text-admin-fg-muted">
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
            </CardContent>
          </Card>
        </div>
      }
    />
  );
}
