'use client';

/**
 * Reading Practice Hub — Phase 3 (a + b).
 *
 * Surfaces every non-exam Reading flow we ship:
 *   1. Learning Mode — full untimed practice on any published paper.
 *   2. Skill Drills — short scoped runs against a Part + skillTag.
 *   3. Mini-Tests — 5 / 10 / 15-minute mixed-Part subsets.
 *   4. Error Bank — questions the learner missed in past graded
 *      attempts, plus a one-click "Retest open misses" launcher.
 *
 * All non-Exam attempts use {@link ReadingAttemptMode} on the backend so
 * Part A hard-lock, the per-paper exam attempt cap, and OET 0-500 scaled
 * conversion are all suppressed. Subsets carry a `ScopeJson` payload so
 * the grader only counts in-scope questions. See
 * `docs/READING-MODULE-A-Z-IMPLEMENTATION-PLAN.md` Phase 3.
 */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import {
  AlertTriangle,
  ArrowRight,
  BookOpen,
  CheckCircle2,
  Clock,
  Lock,
  Sparkles,
  Trash2,
  TrendingUp,
} from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { MotionItem } from '@/components/ui/motion-primitives';
import {
  LearnerPageHero,
  LearnerSurfaceCard,
  LearnerSurfaceSectionHeader,
} from '@/components/domain';
import { LearnerEmptyState } from '@/components/domain/learner-empty-state';
import { useAuth } from '@/contexts/auth-context';
import {
  clearReadingErrorBankEntry,
  getReadingDrillCatalogue,
  getReadingErrorBank,
  getReadingHome,
  getReadingPathway,
  startReadingDrill,
  startReadingErrorBankRetest,
  startReadingLearningAttempt,
  startReadingMiniTest,
  type ReadingDrillCatalogueDto,
  type ReadingErrorBankDto,
  type ReadingHomeDto,
  type ReadingHomePaperDto,
  type ReadingPathwaySnapshot,
} from '@/lib/reading-authoring-api';

function isPaperAccessible(paper: ReadingHomePaperDto): boolean {
  return paper.entitlement?.allowed !== false;
}

function papersWithAccess(papers: ReadingHomePaperDto[]): ReadingHomePaperDto[] {
  return papers.filter(isPaperAccessible);
}

export default function ReadingPracticePage() {
  const { isAuthenticated, loading: authLoading } = useAuth();
  const router = useRouter();
  const [home, setHome] = useState<ReadingHomeDto | null>(null);
  const [errorBank, setErrorBank] = useState<ReadingErrorBankDto | null>(null);
  const [drillCatalogue, setDrillCatalogue] = useState<ReadingDrillCatalogueDto | null>(null);
  const [pathway, setPathway] = useState<ReadingPathwaySnapshot | null>(null);
  const [loading, setLoading] = useState(true);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [startingPaperId, setStartingPaperId] = useState<string | null>(null);
  const [clearingEntryId, setClearingEntryId] = useState<string | null>(null);
  // Single-flight guard for non-Learning launchers — formatted as
  // `${paperId}::${kind}` (or just `kind` for cross-paper retest).
  const [busyKey, setBusyKey] = useState<string | null>(null);
  const [drillPaperId, setDrillPaperId] = useState<string | null>(null);
  const accessiblePapers = useMemo(() => papersWithAccess(home?.papers ?? []), [home?.papers]);

  // ── Deep-link support: /reading/practice?focus=A|B|C&tab=errors ─────────
  const searchParams = useSearchParams();
  const focusParamRaw = searchParams?.get('focus') ?? null;
  const focusPart = focusParamRaw === 'A' || focusParamRaw === 'B' || focusParamRaw === 'C'
    ? focusParamRaw
    : null;
  const tabParam = searchParams?.get('tab') ?? null;

  const refresh = useCallback(async () => {
    if (!isAuthenticated) return;
    setLoading(true);
    setErrorMsg(null);
    try {
      const [homeData, bank, drills] = await Promise.all([
        getReadingHome().catch(() => null),
        getReadingErrorBank({ limit: 50 }).catch(() => null),
        getReadingDrillCatalogue().catch(() => null),
      ]);
      setHome(homeData);
      setErrorBank(bank);
      setDrillCatalogue(drills);
      // Pathway snapshot is best-effort; never blocks the hub.
      void getReadingPathway()
        .then((snap) => setPathway(snap))
        .catch(() => setPathway(null));
      // Default the drill paper picker to the first accessible paper, if any.
      setDrillPaperId((current) => current ?? homeData?.papers?.find((paper) => isPaperAccessible(paper))?.id ?? null);
    } catch (err) {
      setErrorMsg(err instanceof Error ? err.message : 'Could not load practice hub.');
    } finally {
      setLoading(false);
    }
  }, [isAuthenticated]);

  useEffect(() => {
    if (!authLoading && isAuthenticated) {
      void refresh();
    }
  }, [authLoading, isAuthenticated, refresh]);

  const handleStartLearning = useCallback(
    async (paper: ReadingHomePaperDto) => {
      if (!isPaperAccessible(paper)) {
        setErrorMsg('This Reading paper is locked for your current package. Open packages to unlock it.');
        return;
      }
      setStartingPaperId(paper.id);
      setErrorMsg(null);
      try {
        const started = await startReadingLearningAttempt(paper.id);
        router.push(started.playerRoute);
      } catch (err) {
        const message = err instanceof Error ? err.message : 'Could not start learning mode.';
        setErrorMsg(message);
        setStartingPaperId(null);
      }
    },
    [router],
  );

  const handleStartDrill = useCallback(
    async (paperId: string, drillCode: string) => {
      const key = `${paperId}::drill::${drillCode}`;
      setBusyKey(key);
      setErrorMsg(null);
      try {
        const started = await startReadingDrill(paperId, drillCode);
        router.push(started.playerRoute);
      } catch (err) {
        setErrorMsg(err instanceof Error ? err.message : 'Could not start drill.');
        setBusyKey(null);
      }
    },
    [router],
  );

  const handleStartMiniTest = useCallback(
    async (paperId: string, minutes: 5 | 10 | 15) => {
      const key = `${paperId}::mini::${minutes}`;
      setBusyKey(key);
      setErrorMsg(null);
      try {
        const started = await startReadingMiniTest(paperId, minutes);
        router.push(started.playerRoute);
      } catch (err) {
        setErrorMsg(err instanceof Error ? err.message : 'Could not start mini-test.');
        setBusyKey(null);
      }
    },
    [router],
  );

  const handleErrorBankRetest = useCallback(async () => {
    setBusyKey('retest');
    setErrorMsg(null);
    try {
      const started = await startReadingErrorBankRetest({ partCode: focusPart ?? undefined, limit: 10 });
      router.push(started.playerRoute);
    } catch (err) {
      setErrorMsg(err instanceof Error ? err.message : 'Could not start retest.');
      setBusyKey(null);
    }
  }, [focusPart, router]);

  // ── Pathway: convert the recommended next action into a real launcher.
  const handlePathwayAction = useCallback(async () => {
    if (!pathway) return;
    const { nextAction } = pathway;
    setBusyKey('pathway');
    setErrorMsg(null);
    try {
      switch (nextAction.kind) {
        case 'start_drill': {
          if (nextAction.drillCode) {
            const paperId = accessiblePapers.find((paper) => paper.id === nextAction.paperId)?.id
              ?? accessiblePapers[0]?.id
              ?? null;
            if (!paperId) {
              router.push('/billing');
              return;
            }
            const started = await startReadingDrill(paperId, nextAction.drillCode);
            router.push(started.playerRoute);
            return;
          }
          break;
        }
        case 'start_mini_test': {
          const paperId = accessiblePapers.find((paper) => paper.id === nextAction.paperId)?.id
            ?? accessiblePapers[0]?.id
            ?? null;
          if (!paperId) {
            router.push('/billing');
            return;
          }
          const started = await startReadingMiniTest(paperId, 10);
          router.push(started.playerRoute);
          return;
        }
        case 'start_diagnostic':
        case 'start_mock':
        case 'review_results':
        case 'book_exam':
        default: {
          if (nextAction.route) {
            router.push(nextAction.route);
            return;
          }
        }
      }
      // Fallback: route-only navigation if the structured launcher could not run.
      if (nextAction.route) router.push(nextAction.route);
    } catch (err) {
      setErrorMsg(err instanceof Error ? err.message : 'Could not start the recommended step.');
    } finally {
      setBusyKey(null);
    }
  }, [accessiblePapers, pathway, router]);

  const handleClearEntry = useCallback(
    async (entryId: string) => {
      setClearingEntryId(entryId);
      try {
        await clearReadingErrorBankEntry(entryId);
        setErrorBank((prev) =>
          prev
            ? {
                totals: {
                  ...prev.totals,
                  open: Math.max(0, prev.totals.open - 1),
                  resolved: prev.totals.resolved + 1,
                },
                entries: prev.entries.filter((e) => e.id !== entryId),
              }
            : prev,
        );
      } catch (err) {
        setErrorMsg(err instanceof Error ? err.message : 'Could not clear entry.');
      } finally {
        setClearingEntryId(null);
      }
    },
    [],
  );

  const errorBankSectionRef = useRef<HTMLElement | null>(null);
  useEffect(() => {
    if (tabParam === 'errors' && !loading && errorBankSectionRef.current) {
      errorBankSectionRef.current.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
  }, [tabParam, loading]);

  const filteredDrills = useMemo(() => {
    const all = drillCatalogue?.drills ?? [];
    return focusPart ? all.filter((d) => d.partCode === focusPart) : all;
  }, [drillCatalogue, focusPart]);

  const filteredErrorEntries = useMemo(() => {
    const all = errorBank?.entries ?? [];
    return focusPart ? all.filter((e) => e.partCode === focusPart) : all;
  }, [errorBank, focusPart]);

  useEffect(() => {
    if (accessiblePapers.length === 0) return;
    if (!drillPaperId || !accessiblePapers.some((paper) => paper.id === drillPaperId)) {
      setDrillPaperId(accessiblePapers[0]?.id ?? null);
    }
  }, [accessiblePapers, drillPaperId]);

  if (authLoading || (loading && !home && !errorBank)) {
    return (
      <LearnerDashboardShell>
        <div className="space-y-6">
          <Skeleton className="h-32 w-full" />
          <Skeleton className="h-64 w-full" />
        </div>
      </LearnerDashboardShell>
    );
  }

  if (!isAuthenticated) {
    return (
      <LearnerDashboardShell>
        <InlineAlert variant="warning">Sign in to access the Reading practice hub.</InlineAlert>
      </LearnerDashboardShell>
    );
  }

  const papers = home?.papers ?? [];
  const selectedDrillPaperAvailable = accessiblePapers.some((paper) => paper.id === drillPaperId);
  const openErrorCount = errorBank?.totals.open ?? 0;
  const visibleErrorCount = focusPart ? filteredErrorEntries.length : openErrorCount;
  const pathwayNeedsAccessiblePaper = pathway?.nextAction.kind === 'start_drill'
    || pathway?.nextAction.kind === 'start_mini_test';
  const pathwayPackageRequired = Boolean(pathwayNeedsAccessiblePaper && accessiblePapers.length === 0);

  return (
    <LearnerDashboardShell>
      <div className="space-y-10">
        <LearnerPageHero
          eyebrow="Reading"
          title="Practice Hub"
          description="Untimed Learning Mode and your personal Error Bank — review missed questions in your own time without consuming an exam attempt."
          icon={Sparkles}
        />

        {errorMsg ? <InlineAlert variant="error">{errorMsg}</InlineAlert> : null}

        <InlineAlert variant="info">
          Practice mode is <strong>non-standard</strong>. The real OET Reading exam is strictly
          timed and Part A cannot be revisited. Use Practice for teaching; switch to a full
          mock when you need exam fidelity.
        </InlineAlert>

        {/* ── Course pathway recommendation ───────────────────────
            Joins your diagnostic, drill, error-bank, and mock signals
            into a single readiness stage with one concrete next step. */}
        {pathway ? (
          <section aria-label="Course pathway">
            <LearnerSurfaceCard
              card={{
                kind: 'navigation',
                sourceType: 'frontend_navigation',
                accent: pathway.stage === 'exam_ready' ? 'emerald' : 'amber',
                eyebrow: 'YOUR PATHWAY',
                eyebrowIcon: TrendingUp,
                title: pathway.headline,
                description:
                  pathway.weakestSkillTag
                    ? `Weakest skill in your error bank: ${pathway.weakestSkillTag}.`
                    : 'A single recommended next step based on your recent attempts.',
                metaItems: [
                  {
                    icon: CheckCircle2,
                    label: `${pathway.submittedExamAttempts} exam · ${pathway.submittedPracticeAttempts} practice · ${pathway.submittedReadingMockAttempts} mocks`,
                  },
                  pathway.bestScaledScore != null
                    ? { icon: Sparkles, label: `Best ${pathway.bestScaledScore}/500` }
                    : { icon: AlertTriangle, label: `${pathway.openErrorBankCount} open errors` },
                ],
              }}
            >
              <div className="mt-4 flex flex-wrap items-center justify-between gap-3">
                <div className="flex flex-wrap gap-2">
                  {pathway.milestones.map((m) => (
                    <Badge
                      key={m.code}
                      variant={m.achieved ? 'success' : 'info'}
                      title={
                        m.target != null && m.progress != null
                          ? `${m.label} (${m.progress}/${m.target})`
                          : m.label
                      }
                    >
                      {m.achieved ? '✓ ' : ''}
                      {m.label}
                    </Badge>
                  ))}
                </div>
                <Button
                  variant="primary"
                  size="sm"
                  disabled={busyKey === 'pathway'}
                  onClick={() => void handlePathwayAction()}
                >
                  {busyKey === 'pathway' ? 'Starting…' : pathwayPackageRequired ? 'View packages' : pathway.nextAction.label}{' '}
                  <ArrowRight className="ml-1 h-4 w-4" aria-hidden />
                </Button>
              </div>
            </LearnerSurfaceCard>
          </section>
        ) : null}

        {/* ── Learning Mode launcher ──────────────────────────── */}
        <section>
          <LearnerSurfaceSectionHeader
            eyebrow="Learning Mode"
            title="Untimed practice on any published paper"
            description="Walk through a full Reading paper at your own pace. Part A is not hard-locked, and your Learning attempt does not count against the exam attempt cap."
            className="mb-5"
          />
          {papers.length === 0 ? (
            <InlineAlert variant="info">
              No published Reading papers are available yet. Check back after content is released.
            </InlineAlert>
          ) : (
            <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
              {papers.map((paper, idx) => {
                const locked = !isPaperAccessible(paper);
                return (
                  <MotionItem key={paper.id} delayIndex={idx}>
                    <LearnerSurfaceCard
                      card={{
                        kind: 'navigation',
                        sourceType: 'frontend_navigation',
                        accent: locked ? 'amber' : 'blue',
                        eyebrow: locked ? 'PACKAGE REQUIRED' : 'READING',
                        eyebrowIcon: locked ? Lock : BookOpen,
                        title: paper.title,
                        description: locked
                          ? 'This structured Reading paper is ready, but your current package does not include Learning Mode access yet.'
                          : `Difficulty: ${paper.difficulty} · ${paper.partACount + paper.partBCount + paper.partCCount} questions`,
                        metaItems: [
                          { icon: Clock, label: `${paper.estimatedDurationMinutes ?? 60} min` },
                          ...(locked && paper.entitlement?.requiredScope ? [{ icon: Lock, label: paper.entitlement.requiredScope }] : []),
                        ],
                      }}
                    >
                      <div className="mt-4 flex items-center justify-between gap-3">
                        <Badge variant={locked ? 'warning' : 'info'}>{locked ? 'Locked' : 'Learning Mode'}</Badge>
                        {locked ? (
                          <Button asChild variant="outline" size="sm">
                            <Link href="/billing">View packages</Link>
                          </Button>
                        ) : (
                          <Button
                            variant="primary"
                            size="sm"
                            disabled={startingPaperId === paper.id}
                            onClick={() => void handleStartLearning(paper)}
                          >
                            {startingPaperId === paper.id ? 'Starting…' : 'Start untimed'}{' '}
                            <ArrowRight className="ml-1 h-4 w-4" aria-hidden />
                          </Button>
                        )}
                      </div>
                    </LearnerSurfaceCard>
                  </MotionItem>
                );
              })}
            </div>
          )}
        </section>

        {/* ── Error Bank ──────────────────────────────────────── */}
        <section ref={errorBankSectionRef} id="error-bank">
          <LearnerSurfaceSectionHeader
            eyebrow="Error Bank"
            title={`${focusPart ? `Part ${focusPart} \u2014 ` : ''}${focusPart ? filteredErrorEntries.length : openErrorCount} question${(focusPart ? filteredErrorEntries.length : openErrorCount) === 1 ? '' : 's'} to revisit`}
            description={focusPart
              ? `Filtered to Part ${focusPart}. Clear ?focus to see every part.`
              : "Questions you missed in past graded attempts. Clear an entry when you're confident \u2014 the next time you answer it correctly we clear it automatically."}
            className="mb-5"
          />
          {(focusPart ? filteredErrorEntries.length === 0 : openErrorCount === 0) ? (
            <InlineAlert variant="success">
              <CheckCircle2 className="mr-2 inline h-4 w-4" aria-hidden />
              {focusPart
                ? `You have no open Error Bank entries in Part ${focusPart}.`
                : 'You have no open Error Bank entries. Submit a graded Reading attempt to start tracking missed questions.'}
            </InlineAlert>
          ) : (
            <div className="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
              <table className="w-full text-left text-sm">
                <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                  <tr>
                    <th className="px-4 py-3">Paper</th>
                    <th className="px-4 py-3">Part</th>
                    <th className="px-4 py-3">Question</th>
                    <th className="px-4 py-3">Skill</th>
                    <th className="px-4 py-3 text-right">Times wrong</th>
                    <th className="px-4 py-3" />
                  </tr>
                </thead>
                <tbody>
                  {filteredErrorEntries.map((entry) => (
                    <tr key={entry.id} className="border-t border-slate-100">
                      <td className="px-4 py-3">
                        {entry.paper ? (
                          <Link
                            className="text-blue-700 hover:underline"
                            href={`/reading/paper/${entry.paper.id}/results?attemptId=${entry.lastWrongAttemptId}#item-review`}
                          >
                            {entry.paper.title}
                          </Link>
                        ) : (
                          <span className="text-slate-500">—</span>
                        )}
                      </td>
                      <td className="px-4 py-3">
                        <Badge variant="info">Part {entry.partCode}</Badge>
                      </td>
                      <td className="px-4 py-3 max-w-md truncate" title={entry.questionStem ?? ''}>
                        {entry.questionStem ?? <span className="text-slate-400">(stem unavailable)</span>}
                      </td>
                      <td className="px-4 py-3 text-slate-600">
                        {entry.skillTag ?? entry.questionType ?? '—'}
                      </td>
                      <td className="px-4 py-3 text-right">
                        <span className="inline-flex items-center gap-1 text-amber-700">
                          <AlertTriangle className="h-4 w-4" aria-hidden /> {entry.timesWrong}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-right">
                        <Button
                          variant="ghost"
                          size="sm"
                          disabled={clearingEntryId === entry.id}
                          onClick={() => void handleClearEntry(entry.id)}
                          aria-label="Clear from Error Bank"
                        >
                          <Trash2 className="h-4 w-4" aria-hidden />
                          <span className="ml-1">{clearingEntryId === entry.id ? 'Clearing…' : 'Clear'}</span>
                        </Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>

        {/* ── Skill Drills ────────────────────────────────────── */}
        <section>
          <LearnerSurfaceSectionHeader
            eyebrow="Skill Drills"
            title="Targeted Part A / B / C practice"
            description="Short scoped runs against one Part or sub-skill. Drill scores are practice-only — they don't produce an OET 0-500 scaled grade."
            className="mb-5"
          />
          {papers.length === 0 || !drillCatalogue ? (
            <LearnerEmptyState
              compact
              icon={BookOpen}
              title="Drills are not available yet"
              description="Drills will appear once at least one Reading paper is published and eligible for your package."
              primaryAction={{ label: 'Back to Reading', href: '/reading', variant: 'outline' }}
            />
          ) : accessiblePapers.length === 0 ? (
            <LearnerEmptyState
              compact
              icon={Lock}
              title="Practice papers are locked"
              description="Your current package does not include the available structured Reading papers yet."
              primaryAction={{ label: 'View packages', href: '/billing' }}
            />
          ) : (
            <div className="space-y-4">
              <div className="flex items-center gap-3">
                <label className="text-sm font-medium text-slate-700" htmlFor="drill-paper">
                  Practice on paper
                </label>
                <select
                  id="drill-paper"
                  className="rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                  value={drillPaperId ?? ''}
                  onChange={(e) => setDrillPaperId(e.target.value || null)}
                >
                  {accessiblePapers.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.title}
                    </option>
                  ))}
                </select>
              </div>
              <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
                {filteredDrills.map((d, idx) => {
                  const key = drillPaperId ? `${drillPaperId}::drill::${d.code}` : null;
                  const busy = key !== null && busyKey === key;
                  return (
                    <MotionItem key={d.code} delayIndex={idx}>
                      <LearnerSurfaceCard
                        card={{
                          kind: 'navigation',
                          sourceType: 'frontend_navigation',
                          accent: d.partCode === 'A' ? 'blue' : d.partCode === 'B' ? 'purple' : 'emerald',
                          eyebrow: `PART ${d.partCode}${d.skillTag ? ` · ${d.skillTag.toUpperCase()}` : ''}`,
                          eyebrowIcon: TrendingUp,
                          title: d.title,
                          description: d.description,
                          metaItems: [
                            { icon: Clock, label: `${d.minutes} min` },
                            { icon: BookOpen, label: `${d.questionCount} Qs` },
                          ],
                        }}
                      >
                        <div className="mt-4 flex items-center justify-between gap-3">
                          <Badge variant="info">Drill</Badge>
                          <Button
                            variant="primary"
                            size="sm"
                            disabled={!drillPaperId || !selectedDrillPaperAvailable || busy}
                            onClick={() => drillPaperId && selectedDrillPaperAvailable && void handleStartDrill(drillPaperId, d.code)}
                          >
                            {busy ? 'Starting…' : 'Start drill'}
                            <ArrowRight className="ml-1 h-4 w-4" aria-hidden />
                          </Button>
                        </div>
                      </LearnerSurfaceCard>
                    </MotionItem>
                  );
                })}
              </div>
            </div>
          )}
        </section>

        {/* ── Mini-Tests ─────────────────────────────────────── */}
        <section>
          <LearnerSurfaceSectionHeader
            eyebrow="Mini-Tests"
            title="5 / 10 / 15 minute timed warm-ups"
            description="A balanced mix of Part A, B, and C questions sized to the time you've got. Like drills, mini-tests are practice-only and don't produce a scaled score."
            className="mb-5"
          />
          {papers.length === 0 || !drillCatalogue ? (
            <LearnerEmptyState
              compact
              icon={BookOpen}
              title="Mini-tests are not available yet"
              description="Mini-tests will appear once at least one Reading paper is published and eligible for your package."
              primaryAction={{ label: 'Back to Reading', href: '/reading', variant: 'outline' }}
            />
          ) : accessiblePapers.length === 0 ? (
            <LearnerEmptyState
              compact
              icon={Lock}
              title="Mini-tests are locked"
              description="Unlock a structured Reading paper package before starting timed mini-tests."
              primaryAction={{ label: 'View packages', href: '/billing' }}
            />
          ) : (
            <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
              {drillCatalogue.miniTests.map((m, idx) => {
                const key = drillPaperId ? `${drillPaperId}::mini::${m.minutes}` : null;
                const busy = key !== null && busyKey === key;
                return (
                  <MotionItem key={m.minutes} delayIndex={idx}>
                    <LearnerSurfaceCard
                      card={{
                        kind: 'navigation',
                        sourceType: 'frontend_navigation',
                        accent: 'amber',
                        eyebrow: 'MINI-TEST',
                        eyebrowIcon: Clock,
                        title: m.label,
                        description: `Mixed Part A + B + C, ~${m.questionCount} questions.`,
                      }}
                    >
                      <div className="mt-4 flex items-center justify-between gap-3">
                        <Badge variant="info">{m.minutes} min</Badge>
                        <Button
                          variant="primary"
                          size="sm"
                          disabled={!drillPaperId || !selectedDrillPaperAvailable || busy}
                          onClick={() => drillPaperId && selectedDrillPaperAvailable && void handleStartMiniTest(drillPaperId, m.minutes)}
                        >
                          {busy ? 'Starting…' : 'Start'}
                          <ArrowRight className="ml-1 h-4 w-4" aria-hidden />
                        </Button>
                      </div>
                    </LearnerSurfaceCard>
                  </MotionItem>
                );
              })}
            </div>
          )}
        </section>

        {/* ── Error Bank Retest ──────────────────────────────── */}
        {visibleErrorCount > 0 ? (
          <section>
            <LearnerSurfaceSectionHeader
              eyebrow="Targeted retest"
              title="Retest your top open misses"
              description="Spin up a focused practice run against your most recent missed questions. Answering correctly clears them from the Error Bank automatically."
              className="mb-5"
            />
            <div className="flex flex-wrap items-center gap-3">
              <Button
                variant="primary"
                onClick={() => void handleErrorBankRetest()}
                disabled={busyKey === 'retest'}
              >
                {busyKey === 'retest' ? 'Building retest…' : `Retest up to ${Math.min(10, visibleErrorCount)} ${focusPart ? `Part ${focusPart} ` : ''}open miss${Math.min(10, visibleErrorCount) === 1 ? '' : 'es'}`}
                <ArrowRight className="ml-1 h-4 w-4" aria-hidden />
              </Button>
              <span className="text-sm text-slate-600">
                Pulls from the {visibleErrorCount} visible open question
                {visibleErrorCount === 1 ? '' : 's'} above.
              </span>
            </div>
          </section>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}
