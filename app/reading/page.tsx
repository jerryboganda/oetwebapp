'use client';

import { useEffect, useMemo, useState } from 'react';
import { motion } from 'motion/react';
import {
  BookOpen,
  CheckCircle2,
  Clock,
  FileText,
  ListChecks,
  PlayCircle,
  Target,
  TrendingUp,
} from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { useAuth } from '@/contexts/auth-context';
import { analytics } from '@/lib/analytics';
import {
  getReadingHome,
  type ReadingHomeAttemptDto,
  type ReadingHomeDto,
  type ReadingHomePaperDto,
  type ReadingHomeResultDto,
} from '@/lib/reading-authoring-api';
import { formatListeningReadingDisplay, isListeningReadingPassByRaw } from '@/lib/scoring';
import { LearnerPageHero, LearnerSurfaceCard, LearnerSurfaceSectionHeader } from '@/components/domain';
import type { LearnerSurfaceCardModel } from '@/lib/learner-surface';

const partGuides: LearnerSurfaceCardModel[] = [
  {
    kind: 'insight',
    sourceType: 'frontend_insight',
    accent: 'amber',
    eyebrow: 'Part A',
    eyebrowIcon: Clock,
    title: 'Lock exact details first',
    description: 'Use the opening window for rapid extraction across the short texts before moving into the longer timer block.',
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
    title: 'Read workplace purpose',
    description: 'Track the intent of short workplace notices and choose the option supported by the exact wording.',
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
        const readingHome = await getReadingHome();
        if (!cancelled) setHome(readingHome);
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

  const activeAttempt = home?.activeAttempts[0] ?? null;
  const latestResult = home?.recentResults[0] ?? null;
  const heroHighlights = useMemo(() => ([
    {
      icon: Target,
      label: 'Ready papers',
      value: loading ? 'Loading...' : `${home?.papers.length ?? 0} structured`,
    },
    {
      icon: PlayCircle,
      label: 'Active attempt',
      value: activeAttempt ? `${activeAttempt.answeredCount}/${activeAttempt.totalQuestions} answered` : 'None open',
    },
    {
      icon: TrendingUp,
      label: 'Latest score',
      value: latestResult ? `${latestResult.rawScore}/42 | ${latestResult.scaledScore}/500` : 'No result yet',
    },
  ]), [activeAttempt, home?.papers.length, latestResult, loading]);

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

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {loading ? <ReadingHomeSkeleton /> : (
          <>
            {activeAttempt ? <ActiveAttemptSection attempt={activeAttempt} /> : null}

            <section>
              <LearnerSurfaceSectionHeader
                eyebrow="Structured Papers"
                title="Ready Reading papers"
                description="Each paper follows the canonical 20/6/16 item structure and opens in the timed Reading player."
                className="mb-5"
              />

              {home?.papers.length ? (
                <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                  {home.papers.map((paper, index) => (
                    <motion.div
                      key={paper.id}
                      initial={{ opacity: 0, y: 16 }}
                      animate={{ opacity: 1, y: 0 }}
                      transition={{ delay: index * 0.06 }}
                    >
                      <LearnerSurfaceCard card={paperCard(paper, home.activeAttempts)}>
                        <div className="grid grid-cols-3 gap-2 text-center text-xs font-semibold text-muted">
                          <span className="rounded-lg bg-background-light px-2 py-2">A {paper.partACount}</span>
                          <span className="rounded-lg bg-background-light px-2 py-2">B {paper.partBCount}</span>
                          <span className="rounded-lg bg-background-light px-2 py-2">C {paper.partCCount}</span>
                        </div>
                      </LearnerSurfaceCard>
                    </motion.div>
                  ))}
                </div>
              ) : (
                <EmptyState
                  title="No structured Reading papers are ready yet"
                  description="Published papers will appear here after the authoring structure passes the Reading publish gate."
                />
              )}
            </section>

            <section>
              <LearnerSurfaceSectionHeader
                eyebrow="Part Guidance"
                title="Keep the exam rhythm visible"
                description={`Part A is ${home?.policy.partATimerMinutes ?? 15} minutes, followed by ${home?.policy.partBCTimerMinutes ?? 45} shared minutes for Parts B and C.`}
                className="mb-5"
              />
              <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
                {partGuides.map((card) => <LearnerSurfaceCard key={card.title} card={card} />)}
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

              {home?.recentResults.length ? (
                <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                  {home.recentResults.map((result, index) => (
                    <motion.div
                      key={result.attemptId}
                      initial={{ opacity: 0, y: 16 }}
                      animate={{ opacity: 1, y: 0 }}
                      transition={{ delay: 0.12 + index * 0.06 }}
                    >
                      <LearnerSurfaceCard card={resultCard(result)} />
                    </motion.div>
                  ))}
                </div>
              ) : (
                <EmptyState
                  title="No Reading results yet"
                  description="Complete a structured Reading paper to unlock item review and error clusters."
                />
              )}
            </section>
          </>
        )}
      </main>
    </LearnerDashboardShell>
  );
}

function ActiveAttemptSection({ attempt }: { attempt: ReadingHomeAttemptDto }) {
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
    <section>
      <LearnerSurfaceSectionHeader
        eyebrow="Continue"
        title="Active Reading attempt"
        description="Resume your saved work from the structured player."
        className="mb-5"
      />
      <LearnerSurfaceCard card={card} />
    </section>
  );
}

function paperCard(paper: ReadingHomePaperDto, attempts: ReadingHomeAttemptDto[]): LearnerSurfaceCardModel {
  const active = attempts.find((attempt) => attempt.paperId === paper.id && attempt.canResume);
  const lastSubmitted = paper.lastAttempt?.status === 'Submitted' ? paper.lastAttempt : null;
  return {
    kind: 'task',
    sourceType: 'backend_task',
    accent: 'blue',
    eyebrow: paper.difficulty || 'Reading Paper',
    eyebrowIcon: BookOpen,
    title: paper.title,
    description: `Full structured Reading paper with ${paper.totalPoints} points and timed Part A/B/C flow.`,
    metaItems: [
      { icon: Clock, label: `${paper.partATimerMinutes + paper.partBCTimerMinutes} mins` },
      { icon: ListChecks, label: `${paper.partACount + paper.partBCount + paper.partCCount} questions` },
    ],
    primaryAction: {
      label: active ? 'Resume paper' : 'Start paper',
      href: active?.route ?? paper.route,
    },
    secondaryAction: lastSubmitted ? {
      label: 'Review latest result',
      href: lastSubmitted.route,
      variant: 'outline',
    } : undefined,
    statusLabel: active ? 'In progress' : undefined,
  };
}

function resultCard(result: ReadingHomeResultDto): LearnerSurfaceCardModel {
  const passed = isListeningReadingPassByRaw(result.rawScore);
  return {
    kind: 'evidence',
    sourceType: 'backend_summary',
    accent: passed ? 'emerald' : 'amber',
    eyebrow: passed ? 'Pass Evidence' : 'Review Focus',
    eyebrowIcon: passed ? CheckCircle2 : Target,
    title: result.paperTitle,
    description: formatListeningReadingDisplay(result.rawScore),
    metaItems: [
      { icon: FileText, label: `Grade ${result.gradeLetter}` },
      { icon: Clock, label: result.submittedAt ? formatDate(result.submittedAt) : 'Submitted' },
    ],
    primaryAction: {
      label: 'Open review',
      href: result.route,
      variant: 'outline',
    },
    statusLabel: passed ? 'Passed' : 'Needs work',
  };
}

function EmptyState({ title, description }: { title: string; description: string }) {
  return (
    <div className="rounded-[20px] border border-dashed border-border bg-surface px-5 py-8 text-center">
      <p className="text-base font-bold text-navy">{title}</p>
      <p className="mt-2 text-sm text-muted">{description}</p>
    </div>
  );
}

function ReadingHomeSkeleton() {
  return (
    <div className="space-y-8">
      <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
        {[1, 2].map((item) => <Skeleton key={item} className="h-56 rounded-[24px]" />)}
      </div>
      <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
        {[1, 2, 3].map((item) => <Skeleton key={item} className="h-48 rounded-[24px]" />)}
      </div>
    </div>
  );
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
