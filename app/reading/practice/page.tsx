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

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import {
  AlertTriangle,
  ArrowRight,
  BookOpen,
  CheckCircle2,
  Clock,
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
      // Default the drill paper picker to the first published paper, if any.
      setDrillPaperId((current) => current ?? homeData?.papers?.[0]?.id ?? null);
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
      const started = await startReadingErrorBankRetest({ limit: 10 });
      router.push(started.playerRoute);
    } catch (err) {
      setErrorMsg(err instanceof Error ? err.message : 'Could not start retest.');
      setBusyKey(null);
    }
  }, [router]);

  // ── Pathway: convert the recommended next action into a real launcher.
  const handlePathwayAction = useCallback(async () => {
    if (!pathway) return;
    const { nextAction } = pathway;
    setBusyKey('pathway');
    setErrorMsg(null);
    try {
      switch (nextAction.kind) {
        case 'start_drill': {
          if (nextAction.paperId && nextAction.drillCode) {
            const started = await startReadingDrill(nextAction.paperId, nextAction.drillCode);
            router.push(started.playerRoute);
            return;
          }
          break;
        }
        case 'start_mini_test': {
          if (nextAction.paperId) {
            const started = await startReadingMiniTest(nextAction.paperId, 10);
            router.push(started.playerRoute);
            return;
          }
          break;
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
  }, [pathway, router]);

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
  const openErrorCount = errorBank?.totals.open ?? 0;

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
                  {busyKey === 'pathway' ? 'Starting…' : pathway.nextAction.label}{' '}
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
              {papers.map((paper, idx) => (
                <MotionItem key={paper.id} delayIndex={idx}>
                  <LearnerSurfaceCard
                    card={{
                      kind: 'navigation',
                      sourceType: 'frontend_navigation',
                      accent: 'blue',
                      eyebrow: 'READING',
                      eyebrowIcon: BookOpen,
                      title: paper.title,
                      description: `Difficulty: ${paper.difficulty} · ${paper.partACount + paper.partBCount + paper.partCCount} questions`,
                      metaItems: [
                        { icon: Clock, label: `${paper.estimatedDurationMinutes ?? 60} min` },
                      ],
                    }}
                  >
                    <div className="mt-4 flex items-center justify-between gap-3">
                      <Badge variant="info">Learning Mode</Badge>
                      <Button
                        variant="primary"
                        size="sm"
                        disabled={startingPaperId === paper.id}
                        onClick={() => void handleStartLearning(paper)}
                      >
                        {startingPaperId === paper.id ? 'Starting…' : 'Start untimed'}{' '}
                        <ArrowRight className="ml-1 h-4 w-4" aria-hidden />
                      </Button>
                    </div>
                  </LearnerSurfaceCard>
                </MotionItem>
              ))}
            </div>
          )}
        </section>

        {/* ── Error Bank ──────────────────────────────────────── */}
        <section>
          <LearnerSurfaceSectionHeader
            eyebrow="Error Bank"
            title={`${openErrorCount} question${openErrorCount === 1 ? '' : 's'} to revisit`}
            description="Questions you missed in past graded attempts. Clear an entry when you're confident — the next time you answer it correctly we clear it automatically."
            className="mb-5"
          />
          {openErrorCount === 0 ? (
            <InlineAlert variant="success">
              <CheckCircle2 className="mr-2 inline h-4 w-4" aria-hidden />
              You have no open Error Bank entries. Submit a graded Reading attempt to start
              tracking missed questions.
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
                  {(errorBank?.entries ?? []).map((entry) => (
                    <tr key={entry.id} className="border-t border-slate-100">
                      <td className="px-4 py-3">
                        {entry.paper ? (
                          <Link
                            className="text-blue-700 hover:underline"
                            href={`/reading/paper/${entry.paper.id}/results`}
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
            <InlineAlert variant="info">
              Drills will appear once at least one Reading paper is published.
            </InlineAlert>
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
                  {papers.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.title}
                    </option>
                  ))}
                </select>
              </div>
              <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
                {drillCatalogue.drills.map((d, idx) => {
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
                            disabled={!drillPaperId || busy}
                            onClick={() => drillPaperId && void handleStartDrill(drillPaperId, d.code)}
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
            <InlineAlert variant="info">
              Mini-tests will appear once at least one Reading paper is published.
            </InlineAlert>
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
                          disabled={!drillPaperId || busy}
                          onClick={() => drillPaperId && void handleStartMiniTest(drillPaperId, m.minutes)}
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
        {openErrorCount > 0 ? (
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
                {busyKey === 'retest' ? 'Building retest…' : `Retest up to 10 open misses`}
                <ArrowRight className="ml-1 h-4 w-4" aria-hidden />
              </Button>
              <span className="text-sm text-slate-600">
                Pulls from the {openErrorCount} open question
                {openErrorCount === 1 ? '' : 's'} above.
              </span>
            </div>
          </section>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}
