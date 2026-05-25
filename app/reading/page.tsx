'use client';

import { useEffect, useMemo, useState } from 'react';
import {
  BookOpen,
  CheckCircle2,
  Clock,
  FileText,
  ListChecks,
  Lock,
  PlayCircle,
  Printer,
  Sparkles,
  Target,
  TrendingUp,
} from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { InlineAlert } from '@/components/ui/alert';
import { MotionItem } from '@/components/ui/motion-primitives';
import { useAuth } from '@/contexts/auth-context';
import { analytics } from '@/lib/analytics';
import {
  getReadingErrorBank,
  getReadingHome,
  type ReadingHomeAttemptDto,
  type ReadingHomeDto,
  type ReadingHomePaperDto,
  type ReadingHomeResultDto,
  type ReadingHomeSafeDrillDto,
} from '@/lib/reading-authoring-api';
import { fetchMockReports } from '@/lib/api';
import type { MockReport } from '@/lib/mock-data';
import { isListeningReadingPassByScaled } from '@/lib/scoring';
import { readErrorMessage } from '@/lib/read-error-message';
import { LearnerPageHero, LearnerSurfaceCard, LearnerSurfaceSectionHeader } from '@/components/domain';
import { PartLaunchpadCard, type PartCode } from '@/components/domain/part-launchpad-card';
import { LearnerEmptyState } from '@/components/domain/learner-empty-state';
import { LearnerSkillSwitcher } from '@/components/domain/learner-skill-switcher';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import type { LearnerSurfaceCardModel } from '@/lib/learner-surface';
import { useReadingProfile } from '@/hooks/useReadingProfile';
import { useDailyPlan } from '@/hooks/useDailyPlan';
import { DashboardHero } from '@/components/reading/DashboardHero';
import { TodayPlan } from '@/components/reading/TodayPlan';
import { StreakBadge } from '@/components/reading/StreakBadge';
import type { DailyPlanItemDto } from '@/lib/reading-pathway-api';


export default function ReadingHome() {
  const { isAuthenticated, loading: authLoading } = useAuth();
  const { profile } = useReadingProfile();
  const { plan } = useDailyPlan();

  function handleStartPlanItem(_item: DailyPlanItemDto) {
    // Navigate to appropriate page based on item type — extend as routes are added
    // For now, just navigate to the practice hub
    if (typeof window !== 'undefined') {
      window.location.href = '/reading/practice';
    }
  }

  const [home, setHome] = useState<ReadingHomeDto | null>(null);
  const [mockReports, setMockReports] = useState<MockReport[]>([]);
  const [partErrorCounts, setPartErrorCounts] = useState<Record<PartCode, number>>({ A: 0, B: 0, C: 0 });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [retryCount, setRetryCount] = useState(0);
  const [errorBankError, setErrorBankError] = useState(false);

  useEffect(() => {
    if (authLoading) return;
    if (!isAuthenticated) {
      setLoading(false);
      return;
    }

    let cancelled = false;
    analytics.track('module_entry', { module: 'reading' });

    (async () => {
      try {
        setLoading(true);
        setError(null);
        const [readingHome, reports, errorBank] = await Promise.all([
          getReadingHome(),
          fetchMockReports().catch(() => [] as MockReport[]),
          getReadingErrorBank({ limit: 200 }).then((result) => { setErrorBankError(false); return result; }).catch(() => { setErrorBankError(true); return null; }),
        ]);
        if (cancelled) return;
        setHome(readingHome);
        setMockReports(reports);
        const counts: Record<PartCode, number> = { A: 0, B: 0, C: 0 };
        for (const entry of errorBank?.entries ?? []) {
          const code = entry.partCode as PartCode | undefined;
          if (code === 'A' || code === 'B' || code === 'C') counts[code] += 1;
        }
        setPartErrorCounts(counts);
      } catch (err) {
        if (!cancelled) setError(readErrorMessage(err, 'Failed to load Reading papers.'));
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [authLoading, isAuthenticated, retryCount]);

  const activeAttempts = useMemo(() => home?.activeAttempts ?? [], [home]);
  const latestResult = home?.recentResults[0] ?? null;
  const papers = home?.papers ?? [];
  const recentResults = home?.recentResults ?? [];
  const readingMockReports = useMemo(
    () => mockReports.filter((report) =>
      report.subTests?.some((subtest) => subtest.name?.toLowerCase() === 'reading'),
    ),
    [mockReports],
  );
  const safeDrills = home?.safeDrills ?? [];

  const heroHighlights = useMemo(() => ([
    {
      icon: Target,
      label: 'Ready papers',
      value: loading ? 'Loading...' : `${papers.length} structured`,
    },
    {
      icon: PlayCircle,
      label: 'Active attempt',
      value: activeAttempts.length
        ? activeAttempts.length > 1
          ? `${activeAttempts.length} in progress`
          : `${activeAttempts[0].answeredCount}/${activeAttempts[0].totalQuestions} answered`
        : 'None open',
    },
    {
      icon: TrendingUp,
      label: 'Latest score',
      value: latestResult
        ? latestResult.scaledScore == null
          ? `${latestResult.rawScore}/${latestResult.maxRawScore} practice`
          : `${latestResult.rawScore}/42 | ${latestResult.scaledScore}/500`
        : 'No result yet',
    },
  ]), [activeAttempts, papers.length, latestResult, loading]);

  // Compute daysToExam from profile.examDate
  const daysToExam: number | null = useMemo(() => {
    if (!profile?.examDate) return null;
    const diff = new Date(profile.examDate).getTime() - Date.now();
    return Math.max(0, Math.ceil(diff / (1000 * 60 * 60 * 24)));
  }, [profile]);

  return (
    <LearnerDashboardShell pageTitle="Reading">
      <main className="space-y-10">
        {/* Stage-gated banners */}
        {profile?.currentStage === 'onboarding' ? (
          <Link
            href="/reading/profile-setup"
            className="flex items-center justify-between rounded-xl border border-amber-200 bg-amber-50 dark:border-amber-700 dark:bg-amber-900/20 px-5 py-3 text-sm font-medium text-amber-800 dark:text-amber-300 hover:bg-amber-100 dark:hover:bg-amber-900/30 transition-colors"
          >
            <span>Complete your profile setup to unlock your personalised study plan</span>
            <span aria-hidden="true">→</span>
          </Link>
        ) : null}

        {profile?.currentStage === 'diagnostic' ? (
          <div className="rounded-xl border border-blue-200 bg-blue-50 dark:border-blue-700 dark:bg-blue-900/20 px-5 py-4">
            <p className="text-sm font-semibold text-blue-800 dark:text-blue-200 mb-1">
              Start with your diagnostic test
            </p>
            <p className="text-xs text-blue-700/70 dark:text-blue-300/70 mb-3">
              A 20-minute diagnostic will calibrate your starting point and generate a personalised pathway.
            </p>
            <Link
              href="/reading/diagnostic"
              className="inline-flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-2 text-sm font-semibold text-white hover:bg-blue-700 transition-colors"
            >
              Take your diagnostic test →
            </Link>
          </div>
        ) : null}

        {/* Pathway dashboard cards — shown when profile is loaded and past onboarding */}
        {profile && profile.currentStage !== 'onboarding' ? (
          <div className="space-y-4">
            <DashboardHero
              readinessScore={0}
              predictedScore={0}
              daysToExam={daysToExam}
              streak={0}
            />
            {plan ? (
              <TodayPlan plan={plan} onStartItem={handleStartPlanItem} />
            ) : null}
          </div>
        ) : null}

        <LearnerPageHero
          eyebrow="Module Focus"
          icon={BookOpen}
          accent="blue"
          title="Build reading accuracy before you validate it in mocks"
          description={home?.intro ?? 'Use structured OET Reading papers with the same Part A and Part B/C timing rhythm learners meet in the exam.'}
          highlights={heroHighlights}
        />

        <LearnerSkillSwitcher compact />

        {error ? (
          <div className="flex flex-wrap items-center gap-3">
            <InlineAlert variant="error">{error}</InlineAlert>
            <button
              type="button"
              onClick={() => setRetryCount((c) => c + 1)}
              className="rounded-full border border-danger/30 bg-surface px-3 py-1 text-xs font-medium text-danger hover:bg-danger/5 dark:border-danger/40 dark:hover:bg-danger/10"
            >
              Try again
            </button>
          </div>
        ) : null}

        <div className="flex items-center justify-between gap-3 flex-wrap">
          {/* Streak badge — shown whenever we have any streak data */}
          {plan && plan.streak > 0 ? (
            <StreakBadge streak={plan.streak} />
          ) : <span />}
          <Link
            href="/reading/practice"
            className="inline-flex items-center gap-2 rounded-full border border-blue-200 bg-blue-50 px-4 py-2 text-sm font-medium text-blue-800 hover:bg-blue-100"
          >
            <Sparkles className="h-4 w-4" aria-hidden />
            Open Practice Hub
          </Link>
        </div>

        {loading ? <ReadingHomeSkeleton /> : (
          <>
            {activeAttempts.length > 0 ? (
              <ActiveAttemptsSection attempts={activeAttempts} />
            ) : null}

            <section>
              <LearnerSurfaceSectionHeader
                eyebrow="Structured Papers"
                title="Ready Reading papers"
                description="Each paper follows the canonical 20/6/16 item structure and opens in the timed Reading player."
                className="mb-5"
              />

              {papers.length ? (
                <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                  {papers.map((paper, index) => (
                    <MotionItem key={paper.id} delayIndex={index}>
                      <LearnerSurfaceCard card={paperCard(paper, home?.activeAttempts ?? [])}>
                        <div className="grid grid-cols-3 gap-2 text-center text-xs font-semibold text-muted">
                          <span className="rounded-lg bg-background-light px-2 py-2">A {paper.partACount}</span>
                          <span className="rounded-lg bg-background-light px-2 py-2">B {paper.partBCount}</span>
                          <span className="rounded-lg bg-background-light px-2 py-2">C {paper.partCCount}</span>
                        </div>
                        {/* Attempt info — shows last attempt status + date when available */}
                        {paper.lastAttempt ? (
                          <p className="mt-2 text-xs text-muted">
                            Last attempt:{' '}
                            <span className="font-medium capitalize">
                              {paper.lastAttempt.status.toLowerCase()}
                            </span>
                            {paper.lastAttempt.submittedAt ? ` · ${formatDate(paper.lastAttempt.submittedAt)}` : null}
                          </p>
                        ) : null}
                        {/* TODO: show remaining attempts when API returns attemptCount + cooldownEndsAt */}
                        {/* When cooldown data arrives, render "Available in X min" notice here
                            and disable the primary action button for the duration of the cooldown. */}
                        {home?.policy.allowPaperReadingMode && !(paper.entitlement ? !paper.entitlement.allowed : false) ? (
                          <Link
                            href={`${paper.route}?presentation=paper`}
                            className="mt-3 inline-flex min-h-11 items-center justify-center gap-2 rounded-lg border border-border px-3 py-2 text-xs font-medium text-navy hover:border-border-hover hover:bg-surface"
                          >
                            <Printer className="h-4 w-4" aria-hidden />
                            Paper simulation
                          </Link>
                        ) : null}
                      </LearnerSurfaceCard>
                    </MotionItem>
                  ))}
                </div>
              ) : (
                <EmptyPapersState />
              )}
            </section>

            {safeDrills.length ? (
              <section>
                <LearnerSurfaceSectionHeader
                  eyebrow="Next Actions"
                  title="Targeted Reading practice"
                  description="Evidence-based review and timed-paper actions generated from your latest structured Reading work."
                  className="mb-5"
                />
                <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
                  {safeDrills.map((drill, index) => (
                    <MotionItem key={drill.id ?? `drill-${index}`} delayIndex={index}>
                      <LearnerSurfaceCard card={safeDrillCard(drill)}>
                        {drill.highlights?.length ? (
                          <ul className="space-y-2 text-sm text-navy/80">
                            {drill.highlights.map((highlight) => (
                              <li key={highlight}>{highlight}</li>
                            ))}
                          </ul>
                        ) : null}
                      </LearnerSurfaceCard>
                    </MotionItem>
                  ))}
                </div>
              </section>
            ) : null}

            <section>
              <LearnerSurfaceSectionHeader
                eyebrow="Part Guidance"
                title="Practice under exam conditions"
                description={`Part A is ${home?.policy.partATimerMinutes ?? 15} minutes, followed by ${home?.policy.partBCTimerMinutes ?? 45} shared minutes for Parts B and C.`}
                className="mb-5"
              />
              {errorBankError ? (
                <p className="mb-4 rounded-lg border border-amber-200 bg-amber-50 px-4 py-2 text-sm text-amber-800">
                  Could not load error bank data — counts may be inaccurate.
                </p>
              ) : null}
              <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
                {home ? (['A', 'B', 'C'] as const).map((code, index) => (
                  <MotionItem key={code} delayIndex={index}>
                    <PartLaunchpadCard partCode={code} home={home} errorBankCount={partErrorCounts[code]} />
                  </MotionItem>
                )) : null}
              </div>
            </section>

            <section>
              <LearnerSurfaceSectionHeader
                eyebrow="Recent Results"
                title="Reading evidence"
                description="Standalone Reading attempts show raw score, scaled score, grade, and the review route."
                action={<Link href="/mocks" className="text-sm font-bold text-primary hover:underline">Open Mock Center</Link>}
                className="mb-5"
              />

              {recentResults.length ? (
                <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                  {recentResults.map((result, index) => (
                    <MotionItem key={result.attemptId} delayIndex={index}>
                      <LearnerSurfaceCard card={resultCard(result)} />
                    </MotionItem>
                  ))}
                </div>
              ) : (
                <EmptyStateBox
                  title="No Reading results yet"
                  description="Complete a structured Reading paper to unlock item review and error clusters."
                />
              )}
            </section>

            <section>
              <LearnerSurfaceSectionHeader
                eyebrow="Recent Mock Reports"
                title="Track Reading impact inside full mocks"
                description="See whether Reading performance holds up when it sits inside the full exam load."
                action={<Link href="/mocks" className="text-sm font-bold text-primary hover:underline">Open Mock Center</Link>}
                className="mb-5"
              />

              {readingMockReports.length ? (
                <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
                  {readingMockReports.slice(0, 2).map((report, index) => {
                    const reading = report.subTests.find((subtest) => subtest.name?.toLowerCase() === 'reading');
                    const card: LearnerSurfaceCardModel = {
                      kind: 'evidence',
                      sourceType: 'backend_summary',
                      accent: 'slate',
                      eyebrow: 'Mock Evidence',
                      eyebrowIcon: FileText,
                      title: report.title,
                      description: report.summary,
                      metaItems: [
                        { icon: Clock, label: report.date },
                        { icon: Target, label: `Overall ${report.overallScore}` },
                        ...(reading ? [{ icon: BookOpen, label: `Reading ${(reading as unknown as { score?: number | string }).score ?? '—'}` }] : []),
                      ],
                      primaryAction: {
                        label: 'View Report',
                        href: `/mocks/report/${report.id}`,
                        variant: 'outline',
                      },
                    };
                    return (
                      <MotionItem key={report.id} delayIndex={index}>
                        <LearnerSurfaceCard card={card} />
                      </MotionItem>
                    );
                  })}
                </div>
              ) : (
                <EmptyStateBox
                  title="No mock evidence yet"
                  description="Complete a full mock exam to see Reading transfer inside exam pressure."
                />
              )}
            </section>
          </>
        )}
      </main>
    </LearnerDashboardShell>
  );
}

function ActiveAttemptsSection({ attempts }: { attempts: ReadingHomeAttemptDto[] }) {
  const resumableAttempts = attempts.filter((attempt) => attempt.canResume);
  const expiredAttempts = attempts.filter((attempt) => !attempt.canResume);

  return (
    <section>
      <LearnerSurfaceSectionHeader
        eyebrow="Continue"
        title={attempts.length > 1 ? 'Reading attempts needing attention' : 'Reading attempt needing attention'}
        description="Resume saved work while the timer window is open, or resolve expired attempts with a fresh paper."
        className="mb-5"
      />
      <div className={`grid grid-cols-1 gap-6 ${attempts.length > 1 ? 'md:grid-cols-2' : ''}`}>
        {[...resumableAttempts, ...expiredAttempts].map((attempt, index) => {
          const card: LearnerSurfaceCardModel = {
            kind: 'status',
            sourceType: 'backend_summary',
            accent: attempt.canResume ? 'emerald' : 'amber',
            eyebrow: attempt.canResume ? 'Resume' : 'Expired',
            eyebrowIcon: attempt.canResume ? PlayCircle : Clock,
            title: attempt.paperTitle,
            description: attempt.canResume
              ? 'An in-progress Reading attempt is open. Resume it before the timer window closes.'
              : 'This attempt is past its answer window. Start a fresh attempt so the result evidence stays exam-faithful.',
            metaItems: [
              { icon: ListChecks, label: `${attempt.answeredCount}/${attempt.totalQuestions} answered` },
              { icon: Clock, label: attempt.deadlineAt ? `${attempt.canResume ? 'Ends' : 'Ended'} ${formatTime(attempt.deadlineAt)}` : 'Timer unavailable' },
            ],
            primaryAction: {
              label: attempt.canResume ? 'Resume attempt' : 'Start fresh paper',
              href: attempt.canResume ? attempt.route : `/reading/paper/${attempt.paperId}`,
            },
            statusLabel: attempt.canResume ? 'In progress' : 'Expired',
          };
          return (
            <MotionItem key={attempt.attemptId} delayIndex={index}>
              <LearnerSurfaceCard card={card} />
            </MotionItem>
          );
        })}
      </div>
    </section>
  );
}

function paperCard(paper: ReadingHomePaperDto, attempts: ReadingHomeAttemptDto[]): LearnerSurfaceCardModel {
  const active = attempts.find((attempt) => attempt.paperId === paper.id && attempt.canResume);
  const lastSubmitted = paper.lastAttempt?.status === 'Submitted' ? paper.lastAttempt : null;
  const locked = paper.entitlement ? !paper.entitlement.allowed : false;
  return {
    kind: 'task',
    sourceType: 'backend_task',
    accent: locked ? 'amber' : 'blue',
    eyebrow: locked ? 'Subscription required' : paper.difficulty || 'Reading Paper',
    eyebrowIcon: locked ? Lock : BookOpen,
    title: paper.title,
    description: locked
      ? 'This structured Reading paper is ready, but your current package does not include it yet.'
      : `Full structured Reading paper with ${paper.totalPoints} points and timed Part A/B/C flow.`,
    metaItems: [
      { icon: Clock, label: `${paper.partATimerMinutes + paper.partBCTimerMinutes} mins` },
      { icon: ListChecks, label: `${paper.partACount + paper.partBCount + paper.partCCount} questions` },
      ...(locked && paper.entitlement?.requiredScope ? [{ icon: Lock, label: paper.entitlement.requiredScope }] : []),
    ],
    primaryAction: {
      label: locked ? 'View packages' : active ? 'Resume paper' : 'Start paper',
      href: locked ? '/billing' : active?.route ?? paper.route,
    },
    secondaryAction: !locked && lastSubmitted ? {
      label: 'Review latest result',
      href: lastSubmitted.route,
      variant: 'outline',
    } : undefined,
    statusLabel: locked ? 'Locked' : active ? 'In progress' : undefined,
  };
}

function resultCard(result: ReadingHomeResultDto): LearnerSurfaceCardModel {
  const scaled = result.scaledScore;
  const isPracticeOnly = scaled == null;
  const passed = typeof scaled === 'number' && isListeningReadingPassByScaled(scaled);
  return {
    kind: 'evidence',
    sourceType: 'backend_summary',
    accent: isPracticeOnly ? 'slate' : passed ? 'emerald' : 'amber',
    eyebrow: isPracticeOnly ? 'Practice Evidence' : passed ? 'Pass Evidence' : 'Review Focus',
    eyebrowIcon: passed ? CheckCircle2 : Target,
    title: result.paperTitle,
    description: isPracticeOnly
      ? `${result.rawScore}/${result.maxRawScore} practice marks`
      : `${result.rawScore}/${result.maxRawScore} raw | ${scaled}/500 scaled`,
    metaItems: [
      { icon: FileText, label: isPracticeOnly ? 'No OET grade' : `Grade ${result.gradeLetter}` },
      { icon: Clock, label: result.submittedAt ? formatDate(result.submittedAt) : 'Submitted' },
    ],
    primaryAction: {
      label: 'Open review',
      href: result.route,
      variant: 'outline',
    },
    statusLabel: isPracticeOnly ? 'Practice only' : passed ? 'Passed' : 'Needs work',
  };
}

function safeDrillCard(drill: ReadingHomeSafeDrillDto): LearnerSurfaceCardModel {
  return {
    kind: 'insight',
    sourceType: 'backend_summary',
    accent: 'rose',
    eyebrow: drill.focusLabel,
    eyebrowIcon: Sparkles,
    title: drill.title,
    description: drill.description,
    metaItems: [
      { icon: Clock, label: `${drill.estimatedMinutes} mins` },
    ],
    primaryAction: { label: 'Open action', href: drill.launchRoute },
  };
}

function EmptyPapersState() {
  return (
    <LearnerEmptyState
      icon={BookOpen}
      title="No structured Reading papers are ready yet"
      description="Published papers will appear here after the authoring structure passes the Reading publish gate. Use mocks or your study plan while papers are being prepared."
      primaryAction={{ label: 'Open Mock Center', href: '/mocks' }}
      secondaryAction={{ label: 'View Study Plan', href: '/study-plan' }}
    />
  );
}

function EmptyStateBox({ title, description }: { title: string; description: string }) {
  return (
    <LearnerEmptyState
      compact
      icon={BookOpen}
      title={title}
      description={description}
      primaryAction={{ label: 'Start Reading Paper', href: '/reading' }}
      secondaryAction={{ label: 'Open Mock Center', href: '/mocks' }}
    />
  );
}

function ReadingHomeSkeleton() {
  return <LearnerSkeleton variant="dashboard" />;
}


function formatDate(value: string): string {
  return new Intl.DateTimeFormat('en-GB', { day: '2-digit', month: 'short', year: 'numeric' }).format(new Date(value));
}

function formatTime(value: string): string {
  return new Intl.DateTimeFormat('en-GB', { hour: '2-digit', minute: '2-digit' }).format(new Date(value));
}
