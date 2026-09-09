'use client';

/**
 * Reading Practice Hub — simplified per the Final Developer Modification
 * Brief (8 Sept 2026), items 6-8.
 *
 * Surfaces exactly two non-exam Reading flows:
 *   1. Untimed Practice — no timer, Part A/B/C or Full Exam, only for papers
 *      the candidate has already opened (the normal 1 Reading credit is
 *      already spent, and reopening never deducts another).
 *   2. Mini-Tests — 5 / 10 / 15-minute timed mixed-Part warm-ups (unchanged).
 *
 * The pathway/drill/error-bank system (Drill weak skills, Error Bank,
 * targeted retests, Practice on paper skill drills) is intentionally removed
 * — see docs/READING-MODULE-A-Z-IMPLEMENTATION-PLAN.md Phase 3 for the prior
 * shape. A concise, data-based AI performance snapshot replaces it.
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import {
  ArrowRight,
  BookOpen,
  Clock,
  Lock,
  Sparkles,
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
  getReadingDrillCatalogue,
  getReadingHome,
  getReadingPerformanceSnapshot,
  startReadingLearningAttempt,
  startReadingMiniTest,
  startReadingPartPracticeAttempt,
  type ReadingDrillCatalogueDto,
  type ReadingHomeDto,
  type ReadingHomePaperDto,
  type ReadingPartCode,
  type ReadingPerformanceSnapshotDto,
} from '@/lib/reading-authoring-api';
import {
  InsufficientCreditsModal,
  isInsufficientCreditsError,
  readInsufficientCreditsMessage,
} from '@/components/domain/InsufficientCreditsModal';
import { showCreditFeedback } from '@/lib/credit-feedback';

const READING_PARTS: ReadingPartCode[] = ['A', 'B', 'C'];

function isPaperAccessible(paper: ReadingHomePaperDto): boolean {
  return paper.entitlement?.allowed !== false;
}

/** Untimed Practice eligibility (item 7): only papers already opened — the
 *  normal 1 Reading credit is already spent — never the full library. */
function isUntimedEligible(paper: ReadingHomePaperDto): boolean {
  return isPaperAccessible(paper) && paper.hasPriorAttempt === true;
}

export default function ReadingPracticePage() {
  const { isAuthenticated, loading: authLoading } = useAuth();
  const router = useRouter();
  const [home, setHome] = useState<ReadingHomeDto | null>(null);
  const [drillCatalogue, setDrillCatalogue] = useState<ReadingDrillCatalogueDto | null>(null);
  const [snapshot, setSnapshot] = useState<ReadingPerformanceSnapshotDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  // Single-flight guard, formatted as `${paperId}::${kind}` (mini-test) or
  // `${paperId}::${part}` (untimed practice).
  const [busyKey, setBusyKey] = useState<string | null>(null);
  const [insufficientCreditsMessage, setInsufficientCreditsMessage] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    if (!isAuthenticated) return;
    setLoading(true);
    setErrorMsg(null);
    try {
      const [homeData, drills] = await Promise.all([
        getReadingHome().catch(() => null),
        getReadingDrillCatalogue().catch(() => null),
      ]);
      setHome(homeData);
      setDrillCatalogue(drills);
      void getReadingPerformanceSnapshot().then(setSnapshot).catch(() => setSnapshot(null));
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

  const untimedEligiblePapers = useMemo(
    () => (home?.papers ?? []).filter(isUntimedEligible),
    [home?.papers],
  );

  const handleStartUntimedFullExam = useCallback(
    async (paper: ReadingHomePaperDto) => {
      const key = `${paper.id}::full`;
      setBusyKey(key);
      setErrorMsg(null);
      try {
        const started = await startReadingLearningAttempt(paper.id, { untimed: true });
        showCreditFeedback(started.feedbackMessage);
        router.push(started.playerRoute);
      } catch (err) {
        if (isInsufficientCreditsError(err)) {
          setInsufficientCreditsMessage(readInsufficientCreditsMessage(err));
        } else {
          setErrorMsg(err instanceof Error ? err.message : 'Could not start Untimed Practice.');
        }
      } finally {
        setBusyKey(null);
      }
    },
    [router],
  );

  const handleStartUntimedPart = useCallback(
    async (paper: ReadingHomePaperDto, part: ReadingPartCode) => {
      const key = `${paper.id}::${part}`;
      setBusyKey(key);
      setErrorMsg(null);
      try {
        const started = await startReadingPartPracticeAttempt(paper.id, part, { untimed: true });
        showCreditFeedback(started.feedbackMessage);
        router.push(started.playerRoute);
      } catch (err) {
        if (isInsufficientCreditsError(err)) {
          setInsufficientCreditsMessage(readInsufficientCreditsMessage(err));
        } else {
          setErrorMsg(err instanceof Error ? err.message : 'Could not start Untimed Practice.');
        }
      } finally {
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
        showCreditFeedback(started.feedbackMessage);
        router.push(started.playerRoute);
      } catch (err) {
        if (isInsufficientCreditsError(err)) {
          setInsufficientCreditsMessage(readInsufficientCreditsMessage(err));
        } else {
          setErrorMsg(err instanceof Error ? err.message : 'Could not start mini-test.');
        }
      } finally {
        setBusyKey(null);
      }
    },
    [router],
  );

  if (authLoading || (loading && !home)) {
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

  // Mini-tests need at least one published paper to attach the timed subset
  // to (the mini-test picker below defaults to the first accessible one).
  const accessiblePapers = (home?.papers ?? []).filter(isPaperAccessible);
  const miniTestPaperId = accessiblePapers[0]?.id ?? null;

  return (
    <LearnerDashboardShell>
      <InsufficientCreditsModal
        open={insufficientCreditsMessage !== null}
        message={insufficientCreditsMessage ?? ''}
        onClose={() => setInsufficientCreditsMessage(null)}
      />
      <div className="space-y-6 sm:space-y-10">
        <LearnerPageHero
          eyebrow="Reading"
          title="Practice Hub"
          description="Untimed practice on papers you've already unlocked, plus quick mixed-Part warm-ups."
          icon={Sparkles}
        />

        {errorMsg ? <InlineAlert variant="error">{errorMsg}</InlineAlert> : null}

        {/* ── AI Reading Performance Snapshot ─────────────────────────
            Concise, data-based summary from the candidate's own completed
            attempts — not a pathway/drill system. Hidden until there's
            enough graded history to say something real. */}
        {snapshot?.available ? (
          <InlineAlert variant="info">
            <div className="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm">
              <strong>Weakest area: Part {snapshot.weakestPart}.</strong>
              <span className="text-muted">
                {snapshot.accuracyByPart
                  ?.map((p) => `Part ${p.partCode} ${p.accuracyPct}%`)
                  .join(' • ')}
              </span>
              {snapshot.mainIssue ? (
                <span className="text-muted">Main issue: {snapshot.mainIssue}.</span>
              ) : null}
            </div>
          </InlineAlert>
        ) : null}

        {/* ── Untimed Practice ─────────────────────────────────────── */}
        <section>
          <LearnerSurfaceSectionHeader
            eyebrow="Untimed Practice"
            title="Practice papers you've already unlocked"
            description="Once you've opened a paper, revisit it here at your own pace — Part A, Part B, Part C, or the Full Exam, with no timer and no extra credit."
            className="mb-5"
          />
          {untimedEligiblePapers.length === 0 ? (
            <LearnerEmptyState
              compact
              icon={Lock}
              title="No unlocked papers yet"
              description="Open a Reading paper from the Paper Library first — it will appear here for untimed revisits afterwards."
              primaryAction={{ label: 'Back to Reading', href: '/reading', variant: 'outline' }}
            />
          ) : (
            <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
              {untimedEligiblePapers.map((paper, idx) => (
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
                      metaItems: [{ icon: Clock, label: 'No timer' }],
                    }}
                  >
                    <div className="mt-4 flex flex-wrap items-center gap-2">
                      {READING_PARTS.map((part) => {
                        const key = `${paper.id}::${part}`;
                        return (
                          <Button
                            key={part}
                            variant="outline"
                            size="sm"
                            disabled={busyKey === key}
                            onClick={() => void handleStartUntimedPart(paper, part)}
                          >
                            {busyKey === key ? 'Starting…' : `Part ${part}`}
                          </Button>
                        );
                      })}
                      <Button
                        variant="primary"
                        size="sm"
                        disabled={busyKey === `${paper.id}::full`}
                        onClick={() => void handleStartUntimedFullExam(paper)}
                      >
                        {busyKey === `${paper.id}::full` ? 'Starting…' : 'Full Exam'}
                        <ArrowRight className="ml-1 h-4 w-4" aria-hidden />
                      </Button>
                    </div>
                  </LearnerSurfaceCard>
                </MotionItem>
              ))}
            </div>
          )}
        </section>

        {/* ── Mini-Tests ─────────────────────────────────────── */}
        <section>
          <LearnerSurfaceSectionHeader
            eyebrow="Mini-Tests"
            title="5 / 10 / 15 minute timed warm-ups"
            description="A balanced mix of Part A, B, and C questions sized to the time you've got. Like the untimed papers above, mini-tests are practice-only and don't produce a scaled score."
            className="mb-5"
          />
          {accessiblePapers.length === 0 || !drillCatalogue ? (
            <LearnerEmptyState
              compact
              icon={BookOpen}
              title="Mini-tests are not available yet"
              description="Mini-tests will appear once at least one Reading paper is published and eligible for your package."
              primaryAction={{ label: 'Back to Reading', href: '/reading', variant: 'outline' }}
            />
          ) : (
            <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
              {drillCatalogue.miniTests.map((m, idx) => {
                const key = miniTestPaperId ? `${miniTestPaperId}::mini::${m.minutes}` : null;
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
                          disabled={!miniTestPaperId || busy}
                          onClick={() => miniTestPaperId && void handleStartMiniTest(miniTestPaperId, m.minutes)}
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
      </div>
    </LearnerDashboardShell>
  );
}
