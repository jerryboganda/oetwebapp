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
  getReadingHome,
  type ReadingHomeAttemptDto,
  type ReadingHomeDto,
  type ReadingHomePaperDto,
  type ReadingHomeResultDto,
  type ReadingHomeSafeDrillDto,
} from '@/lib/reading-authoring-api';
import { fetchMockReports } from '@/lib/api';
import type { MockReport } from '@/lib/mock-data';
import { formatListeningReadingDisplay, isListeningReadingPassByRaw } from '@/lib/scoring';
import { LearnerPageHero, LearnerSurfaceCard, LearnerSurfaceSectionHeader } from '@/components/domain';
import { LearnerEmptyState } from '@/components/domain/learner-empty-state';
import { LearnerSkillSwitcher } from '@/components/domain/learner-skill-switcher';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import type { LearnerSurfaceCardModel } from '@/lib/learner-surface';

const partGuides: LearnerSurfaceCardModel[] = [
  {
    kind: 'insight',
    sourceType: 'frontend_insight',
    accent: 'amber',
    eyebrow: 'Part A',
    eyebrowIcon: Clock,
    title: 'Lock exact details first',
    description: 'Use the opening window for rapid extraction across the four medical texts (varied length — Text C may include large tables) before moving into the longer timer block.',
    metaItems: [
      { icon: Target, label: '20 items' },
      { icon: Clock, label: 'Strict timer' },
    ],
  },
  {
    kind: 'insight',
    sourceType: 'frontend_insight',
    accent: 'blue',
    eyebrow: 'Part B',
    eyebrowIcon: FileText,
    title: 'Read the purpose of each extract',
    description: 'Short extracts from different healthcare contexts — policies, notices, guidelines, clinical communications. Choose the option supported by the exact wording.',
    metaItems: [
      { icon: Target, label: '6 items' },
      { icon: ListChecks, label: '3 options' },
    ],
  },
  {
    kind: 'insight',
    sourceType: 'frontend_insight',
    accent: 'indigo',
    eyebrow: 'Part C',
    eyebrowIcon: TrendingUp,
    title: 'Control inference choices',
    description: 'Separate stated evidence from tempting distractors while holding the main argument of each passage.',
    metaItems: [
      { icon: Target, label: '16 items' },
      { icon: ListChecks, label: '4 options' },
    ],
  },
];

export default function ReadingHome() {
  const { isAuthenticated, loading: authLoading } = useAuth();
  const [home, setHome] = useState<ReadingHomeDto | null>(null);
  const [mockReports, setMockReports] = useState<MockReport[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

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
        const [readingHome, reports] = await Promise.all([
          getReadingHome(),
          fetchMockReports().catch(() => [] as MockReport[]),
        ]);
        if (cancelled) return;
        setHome(readingHome);
        setMockReports(reports);
      } catch (err) {
        if (!cancelled) setError(readErrorMessage(err, 'Failed to load Reading papers.'));
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [authLoading, isAuthenticated]);

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

  return (
    <LearnerDashboardShell pageTitle="Reading">
      <main className="space-y-10">
        <LearnerPageHero
          eyebrow="Module Focus"
          icon={BookOpen}
          accent="blue"
          title="Build reading accuracy before you validate it in mocks"
          description={home?.intro ?? 'Use structured OET Reading papers with the same Part A and Part B/C timing rhythm learners meet in the exam.'}
          highlights={heroHighlights}
        />

        <LearnerSkillSwitcher compact />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <div className="flex justify-end">
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
              <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
                {partGuides.map((card, index) => (
                  <MotionItem key={card.title} delayIndex={index}>
                    <LearnerSurfaceCard card={card} />
                  </MotionItem>
                ))}
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
  return (
    <section>
      <LearnerSurfaceSectionHeader
        eyebrow="Continue"
        title={attempts.length > 1 ? 'Active Reading attempts' : 'Active Reading attempt'}
        description="Resume your saved work from the structured player."
        className="mb-5"
      />
      <div className={`grid grid-cols-1 gap-6 ${attempts.length > 1 ? 'md:grid-cols-2' : ''}`}>
        {attempts.map((attempt, index) => {
          const card: LearnerSurfaceCardModel = {
            kind: 'status',
            sourceType: 'backend_summary',
            accent: 'emerald',
            eyebrow: 'Resume',
            eyebrowIcon: PlayCircle,
            title: attempt.paperTitle,
            description: 'An in-progress Reading attempt is open. Resume it before the timer window closes.',
            metaItems: [
              { icon: ListChecks, label: `${attempt.answeredCount}/${attempt.totalQuestions} answered` },
              { icon: Clock, label: attempt.deadlineAt ? `Ends ${formatTime(attempt.deadlineAt)}` : 'Timer active' },
            ],
            primaryAction: {
              label: 'Resume attempt',
              href: attempt.route,
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
  const isPracticeOnly = result.scaledScore == null;
  const passed = !isPracticeOnly && isListeningReadingPassByRaw(result.rawScore);
  return {
    kind: 'evidence',
    sourceType: 'backend_summary',
    accent: isPracticeOnly ? 'slate' : passed ? 'emerald' : 'amber',
    eyebrow: isPracticeOnly ? 'Practice Evidence' : passed ? 'Pass Evidence' : 'Review Focus',
    eyebrowIcon: passed ? CheckCircle2 : Target,
    title: result.paperTitle,
    description: isPracticeOnly
      ? `${result.rawScore}/${result.maxRawScore} practice marks`
      : formatListeningReadingDisplay(result.rawScore),
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

function readErrorMessage(err: unknown, fallback: string): string {
  const detail = (err as { detail?: { message?: string; error?: string } })?.detail;
  return detail?.message ?? detail?.error ?? (err instanceof Error ? err.message : fallback);
}

function formatDate(value: string): string {
  return new Intl.DateTimeFormat('en-GB', { day: '2-digit', month: 'short', year: 'numeric' }).format(new Date(value));
}

function formatTime(value: string): string {
  return new Intl.DateTimeFormat('en-GB', { hour: '2-digit', minute: '2-digit' }).format(new Date(value));
}
