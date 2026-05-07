'use client';

import { useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import {
  ArrowRight,
  CheckCircle2,
  Clock,
  FileText,
  Headphones,
  Mic,
  PenTool,
  ShieldCheck,
  Sparkles,
  Stethoscope,
  Target,
} from 'lucide-react';

import { LearnerDashboardShell } from '@/components/layout';
import {
  LearnerPageHero,
  LearnerSurfaceCard,
  LearnerSurfaceSectionHeader,
  OetStatementOfResultsCard,
} from '@/components/domain';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { MotionSection } from '@/components/ui/motion-primitives';

import {
  ApiError,
  fetchMockDiagnosticEntitlement,
  fetchMockDiagnosticStudyPath,
  fetchMockOptions,
  fetchMockReport,
} from '@/lib/api';
import type {
  MockBundleOption,
  MockDiagnosticEntitlement,
  MockReport,
} from '@/lib/mock-data';

type ApiRecord = Record<string, unknown>;
import {
  isMockReportStatementOfResultsReady,
  mockReportToStatementOfResults,
} from '@/lib/adapters/oet-sor-adapter';
import { oetGradeFromScaled } from '@/lib/scoring';
import { analytics } from '@/lib/analytics';

// ─── Types for the diagnostic study-path payload ───────────────────────────
type SubtestCode = 'listening' | 'reading' | 'writing' | 'speaking';

interface DiagnosticStudyPathItem {
  id: string;
  title: string;
  subtest: string | null;
  dueDate: string | null;
  durationMinutes: number | null;
  rationale: string | null;
  route: string;
}

interface DiagnosticStudyPath {
  diagnosticCompleted: boolean;
  reportId: string | null;
  weakness: { subtest: string | null; criterion: string | null; description: string | null } | null;
  generatedAt: string | null;
  items: DiagnosticStudyPathItem[];
  fallback: { title: string; route: string; description: string } | null;
}

interface DiagnosticBundleSummary {
  id: string;
  title: string;
  estimatedDurationMinutes: number;
  difficulty?: string;
}

const SUBTEST_META: Record<SubtestCode, { label: string; icon: typeof Headphones; route: string; accent: string }> = {
  listening: { label: 'Listening', icon: Headphones, route: '/listening', accent: 'text-primary' },
  reading: { label: 'Reading', icon: FileText, route: '/reading', accent: 'text-info' },
  writing: { label: 'Writing', icon: PenTool, route: '/writing', accent: 'text-rose-600' },
  speaking: { label: 'Speaking', icon: Mic, route: '/speaking', accent: 'text-primary' },
};

function isSubtestCode(value: string | null | undefined): value is SubtestCode {
  return value === 'listening' || value === 'reading' || value === 'writing' || value === 'speaking';
}

function ragForScaled(scaled: number | null | undefined): { variant: 'success' | 'warning' | 'danger' | 'muted'; label: string } {
  if (scaled == null || !Number.isFinite(scaled)) return { variant: 'muted', label: 'Pending' };
  const grade = oetGradeFromScaled(scaled);
  if (grade === 'A' || grade === 'B') return { variant: 'success', label: `Grade ${grade}` };
  if (grade === 'C+' || grade === 'C') return { variant: 'warning', label: `Grade ${grade}` };
  return { variant: 'danger', label: `Grade ${grade}` };
}

function mapStudyPath(record: ApiRecord): DiagnosticStudyPath {
  const items = Array.isArray(record.items) ? (record.items as ApiRecord[]) : [];
  const weaknessRecord = (record.weakness ?? null) as ApiRecord | null;
  const fallbackRecord = (record.fallback ?? null) as ApiRecord | null;
  return {
    diagnosticCompleted: Boolean(record.diagnosticCompleted),
    reportId: typeof record.reportId === 'string' ? record.reportId : null,
    weakness: weaknessRecord
      ? {
          subtest: typeof weaknessRecord.subtest === 'string' ? weaknessRecord.subtest : null,
          criterion: typeof weaknessRecord.criterion === 'string' ? weaknessRecord.criterion : null,
          description: typeof weaknessRecord.description === 'string' ? weaknessRecord.description : null,
        }
      : null,
    generatedAt: typeof record.generatedAt === 'string' ? record.generatedAt : null,
    items: items.map((item): DiagnosticStudyPathItem => ({
      id: String(item.id ?? ''),
      title: String(item.title ?? 'Practice item'),
      subtest: typeof item.subtest === 'string' ? item.subtest : null,
      dueDate: typeof item.dueDate === 'string' ? item.dueDate : null,
      durationMinutes: typeof item.durationMinutes === 'number' ? item.durationMinutes : null,
      rationale: typeof item.rationale === 'string' ? item.rationale : null,
      route: typeof item.route === 'string' && item.route.length > 0 ? item.route : '/dashboard',
    })),
    fallback: fallbackRecord
      ? {
          title: String(fallbackRecord.title ?? 'Start a diagnostic mock first'),
          route: String(fallbackRecord.route ?? '/mocks/setup?type=diagnostic'),
          description: String(fallbackRecord.description ?? ''),
        }
      : null,
  };
}

function bundlesFromOptions(bundles: MockBundleOption[]): DiagnosticBundleSummary[] {
  return bundles
    .filter((bundle) => bundle.mockType === 'diagnostic')
    .map((bundle) => ({
      id: bundle.id,
      title: bundle.title,
      estimatedDurationMinutes: bundle.estimatedDurationMinutes,
      difficulty: bundle.difficulty,
    }));
}

export default function DiagnosticMockPage() {
  const router = useRouter();
  const [entitlement, setEntitlement] = useState<MockDiagnosticEntitlement | null>(null);
  const [studyPath, setStudyPath] = useState<DiagnosticStudyPath | null>(null);
  const [report, setReport] = useState<MockReport | null>(null);
  const [bundles, setBundles] = useState<DiagnosticBundleSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    analytics.track('page_viewed', { page: 'mock_diagnostic_landing' });

    (async () => {
      try {
        const [entitlementResult, studyPathResult, optionsResult] = await Promise.allSettled([
          fetchMockDiagnosticEntitlement(),
          fetchMockDiagnosticStudyPath(),
          fetchMockOptions(),
        ]);

        if (cancelled) return;

        if (entitlementResult.status === 'fulfilled') {
          setEntitlement(entitlementResult.value);
        }

        let resolvedStudyPath: DiagnosticStudyPath | null = null;
        if (studyPathResult.status === 'fulfilled') {
          resolvedStudyPath = mapStudyPath(studyPathResult.value);
          setStudyPath(resolvedStudyPath);
        } else if (studyPathResult.reason instanceof ApiError && studyPathResult.reason.status === 404) {
          setStudyPath({
            diagnosticCompleted: false,
            reportId: null,
            weakness: null,
            generatedAt: null,
            items: [],
            fallback: null,
          });
        }

        if (optionsResult.status === 'fulfilled') {
          setBundles(bundlesFromOptions(optionsResult.value.availableBundles));
        }

        if (resolvedStudyPath?.reportId) {
          try {
            const fetchedReport = await fetchMockReport(resolvedStudyPath.reportId);
            if (!cancelled) setReport(fetchedReport);
          } catch {
            // Report load is best-effort; the result section degrades gracefully.
          }
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Could not load diagnostic data.');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, []);

  const canStart = entitlement?.allowed !== false;
  const startHref = '/mocks/setup?type=diagnostic';
  const hasResult = Boolean(report && studyPath?.diagnosticCompleted);

  const sorReady = useMemo(() => (report ? isMockReportStatementOfResultsReady(report) : false), [report]);
  const sorData = useMemo(
    () => (report && sorReady ? mockReportToStatementOfResults({ report, profession: report.profession ?? undefined }) : null),
    [report, sorReady],
  );

  return (
    <LearnerDashboardShell>
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Diagnostic mock"
          icon={Stethoscope}
          accent="primary"
          title="Diagnostic mock & personalised study path"
          description="A single calibrated mock that benchmarks your current Listening, Reading, Writing and Speaking levels and generates a focused remediation plan you can work through one drill at a time."
          highlights={[
            { label: 'Duration', value: '≈ 3 hours', icon: Clock },
            { label: 'Bands set', value: 'L · R · W · S', icon: Target },
            { label: 'Output', value: 'Study path', icon: Sparkles },
          ]}
        />

        {error ? <InlineAlert variant="error" title="Couldn’t load diagnostic">{error}</InlineAlert> : null}

        {loading ? (
          <div className="space-y-4">
            <Skeleton className="h-32 w-full rounded-2xl" />
            <Skeleton className="h-48 w-full rounded-2xl" />
          </div>
        ) : hasResult && report && studyPath ? (
          <ResultState
            report={report}
            studyPath={studyPath}
            sorData={sorData}
            sorReady={sorReady}
            onNavigate={(href) => router.push(href)}
          />
        ) : (
          <LandingState
            entitlement={entitlement}
            canStart={canStart}
            startHref={startHref}
            bundles={bundles}
            fallback={studyPath?.fallback ?? null}
            onNavigate={(href) => router.push(href)}
          />
        )}

        <PracticeDisclaimer />
      </div>
    </LearnerDashboardShell>
  );
}

// ─── Landing state ────────────────────────────────────────────────────────
function LandingState({
  entitlement,
  canStart,
  startHref,
  bundles,
  fallback,
  onNavigate,
}: {
  entitlement: MockDiagnosticEntitlement | null;
  canStart: boolean;
  startHref: string;
  bundles: DiagnosticBundleSummary[];
  fallback: DiagnosticStudyPath['fallback'];
  onNavigate: (href: string) => void;
}) {
  const reasonMessage = entitlement?.message ?? entitlement?.reason ?? null;

  return (
    <MotionSection className="space-y-6">
      <Card>
        <CardHeader>
          <CardTitle>How the diagnostic works</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4 text-sm leading-6 text-muted">
          <p>
            You’ll sit one timed mock that mirrors the real exam shape: Listening (Parts A–C), Reading
            (Parts A–C), one Writing referral letter and one Speaking role-play. We then convert your
            raw scores to OET scaled scores using the official anchors and produce a per-subtest plan.
          </p>
          <ul className="grid gap-2 sm:grid-cols-2">
            <li className="flex items-start gap-2"><CheckCircle2 className="mt-0.5 h-4 w-4 text-success" /> Calibrated, single-attempt baseline</li>
            <li className="flex items-start gap-2"><CheckCircle2 className="mt-0.5 h-4 w-4 text-success" /> Per-skill RAG band &amp; grade</li>
            <li className="flex items-start gap-2"><CheckCircle2 className="mt-0.5 h-4 w-4 text-success" /> Targeted drills queued automatically</li>
            <li className="flex items-start gap-2"><CheckCircle2 className="mt-0.5 h-4 w-4 text-success" /> Re-baseline whenever your plan allows</li>
          </ul>

          {!canStart ? (
            <InlineAlert variant="warning" title="Diagnostic not currently available">
              {reasonMessage ?? 'Your current plan does not include a diagnostic mock. Upgrade or contact support to enable it.'}
            </InlineAlert>
          ) : null}

          <div className="flex flex-wrap items-center gap-3 pt-2">
            <Button
              variant="primary"
              disabled={!canStart}
              onClick={() => onNavigate(startHref)}
            >
              Start diagnostic
              <ArrowRight className="ml-2 h-4 w-4" />
            </Button>
            <Button variant="ghost" onClick={() => onNavigate('/mocks')}>
              Back to mocks
            </Button>
          </div>
        </CardContent>
      </Card>

      {bundles.length > 0 ? (
        <section className="space-y-3">
          <LearnerSurfaceSectionHeader
            eyebrow="Available bundles"
            title="Diagnostic bundles ready to launch"
            description="Pick a bundle on the setup page; we’ll lock the timer and section order to match the real exam."
          />
          <div className="grid gap-3 sm:grid-cols-2">
            {bundles.slice(0, 4).map((bundle) => (
              <LearnerSurfaceCard
                key={bundle.id}
                card={{
                  kind: 'navigation',
                  sourceType: 'frontend_navigation',
                  title: bundle.title,
                  description: bundle.difficulty ? `Difficulty: ${bundle.difficulty}` : 'Calibrated diagnostic bundle',
                  metaItems: [
                    { label: `Duration ${bundle.estimatedDurationMinutes || '≈180'} min`, icon: Clock },
                  ],
                  primaryAction: {
                    label: 'Use this bundle',
                    href: `${startHref}&bundleId=${encodeURIComponent(bundle.id)}`,
                  },
                }}
              />
            ))}
          </div>
        </section>
      ) : null}

      {fallback ? (
        <InlineAlert variant="info" title={fallback.title}>
          {fallback.description}
        </InlineAlert>
      ) : null}
    </MotionSection>
  );
}

// ─── Result + Study path state ────────────────────────────────────────────
function ResultState({
  report,
  studyPath,
  sorData,
  sorReady,
  onNavigate,
}: {
  report: MockReport;
  studyPath: DiagnosticStudyPath;
  sorData: ReturnType<typeof mockReportToStatementOfResults> | null;
  sorReady: boolean;
  onNavigate: (href: string) => void;
}) {
  return (
    <MotionSection className="space-y-6">
      {sorReady && sorData ? (
        <OetStatementOfResultsCard data={sorData} />
      ) : (
        <InlineAlert variant="info" title="Provisional result">
          One or more subtests are still being graded or queued for teacher review. The full Statement
          of Results will appear here as soon as every band score is final.
        </InlineAlert>
      )}

      <Card>
        <CardHeader>
          <CardTitle>Per-subtest breakdown</CardTitle>
        </CardHeader>
        <CardContent>
          <div className="grid gap-3 sm:grid-cols-2">
            {(['listening', 'reading', 'writing', 'speaking'] as const).map((code) => {
              const subtest = report.subTests.find(
                (s) => s.id === code || (typeof s.name === 'string' && s.name.toLowerCase() === code),
              );
              const meta = SUBTEST_META[code];
              const Icon = meta.icon;
              const scaled = subtest?.scaledScore ?? null;
              const rag = ragForScaled(scaled);
              return (
                <div
                  key={code}
                  className="flex items-center justify-between rounded-2xl border border-border bg-surface p-4"
                >
                  <div className="flex items-center gap-3">
                    <span className={`inline-flex h-9 w-9 items-center justify-center rounded-xl bg-background-light ${meta.accent}`}>
                      <Icon className="h-4 w-4" />
                    </span>
                    <div>
                      <p className="text-sm font-semibold text-navy">{meta.label}</p>
                      <p className="text-xs text-muted">
                        {scaled != null ? `Scaled ${scaled}` : 'Pending'}
                      </p>
                    </div>
                  </div>
                  <div className="flex items-center gap-2">
                    <Badge variant={rag.variant}>{rag.label}</Badge>
                    <Button variant="ghost" size="sm" onClick={() => onNavigate(meta.route)}>
                      Practise
                    </Button>
                  </div>
                </div>
              );
            })}
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <Sparkles className="h-4 w-4 text-primary" />
            Your personalised study path
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          {studyPath.items.length === 0 ? (
            <p className="text-sm text-muted">
              No drills are queued yet — your study path will populate as soon as the diagnostic is
              fully marked.
            </p>
          ) : (
            <ol className="space-y-2">
              {studyPath.items.map((item, index) => {
                const subtest = isSubtestCode(item.subtest) ? item.subtest : null;
                const meta = subtest ? SUBTEST_META[subtest] : null;
                const Icon = meta?.icon;
                return (
                  <li
                    key={item.id || `${index}-${item.title}`}
                    className="flex items-start gap-3 rounded-2xl border border-border bg-surface p-4"
                  >
                    <span className="mt-0.5 inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-primary/10 text-xs font-bold text-primary">
                      {index + 1}
                    </span>
                    <div className="min-w-0 flex-1">
                      <div className="flex flex-wrap items-center gap-2">
                        {Icon && meta ? (
                          <span className={`inline-flex items-center gap-1 text-xs font-semibold ${meta.accent}`}>
                            <Icon className="h-3.5 w-3.5" />
                            {meta.label}
                          </span>
                        ) : null}
                        <p className="text-sm font-semibold text-navy">{item.title}</p>
                        {item.durationMinutes ? (
                          <span className="text-xs text-muted">· {item.durationMinutes} min</span>
                        ) : null}
                      </div>
                      {item.rationale ? (
                        <p className="mt-1 text-xs leading-5 text-muted">{item.rationale}</p>
                      ) : null}
                    </div>
                    <Button variant="primary" size="sm" onClick={() => onNavigate(item.route)}>
                      Open
                      <ArrowRight className="ml-1 h-3.5 w-3.5" />
                    </Button>
                  </li>
                );
              })}
            </ol>
          )}
        </CardContent>
      </Card>
    </MotionSection>
  );
}

function PracticeDisclaimer() {
  return (
    <div className="rounded-2xl border border-border bg-background-light p-4 text-xs leading-5 text-muted">
      <div className="flex items-start gap-2">
        <ShieldCheck className="mt-0.5 h-4 w-4 shrink-0 text-muted" />
        <p>
          <strong className="font-semibold text-navy">Practice only.</strong> This diagnostic and any
          generated Statement of Results are for personal preparation. They are not affiliated with,
          endorsed by, or interchangeable with the official OET awarded by Cambridge Boxhill Language
          Assessment.
        </p>
      </div>
    </div>
  );
}
