'use client';

import { useEffect, useMemo, useState } from 'react';
import {
  ArrowRight,
  BookOpen,
  CalendarDays,
  ClipboardCheck,
  Clock,
  Eye,
  ListChecks,
  PlayCircle,
  Target,
  TrendingUp,
} from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import Link from 'next/link';
import { InlineAlert } from '@/components/ui/alert';
import { MotionSection } from '@/components/ui/motion-primitives';
import { Button } from '@/components/ui/button';
import { useAuth } from '@/contexts/auth-context';
import { analytics } from '@/lib/analytics';
import {
  getReadingHome,
  type ReadingHomeAttemptDto,
  type ReadingHomeDto,
  type ReadingHomePaperDto,
  type ReadingHomeResultDto,
} from '@/lib/reading-authoring-api';
import { listMyReadingAssignments, type ReadingAssignmentDto } from '@/lib/reading-tutor-api';
import { readErrorMessage } from '@/lib/read-error-message';
import { CreditsGuideButton, CreditUsageInfoCard, LearnerPageHero } from '@/components/domain';
import { FreeSampleCard } from '@/components/domain/free-sample-card';
import { findFreeSamplePaper } from '@/lib/free-sample';
import { LearnerSkillSwitcher } from '@/components/domain/learner-skill-switcher';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { useReadingProfile } from '@/hooks/useReadingProfile';

// The primary decision surface stays aligned with the OET sample-test pattern:
// three Practice-by-Part cards plus one Full Reading Exam card. The only
// addition is the out-of-grid FREE SAMPLE card above them (Free Mocks,
// 2026-09-22) — not a fifth grid entry, and hidden unless a paper carries the
// `free-sample` tag. Operational
// context such as assigned work, available papers, and recent results appears
// below that grid so learners can resume and review without diluting the first
// choice they need to make.

interface HubCard {
  title: string;
  subtitle: string;
  href: string;
  accent: 'partA' | 'partB' | 'partC' | 'exam';
}

const HUB_CARDS: HubCard[] = [
  {
    title: 'Practice Part A',
    subtitle: 'Expeditious reading. Match section headings to four short medical texts in 15 minutes.',
    href: '/reading/parts/a',
    accent: 'partA',
  },
  {
    title: 'Practice Part B',
    subtitle: 'Workplace texts. Short workplace notices and excerpts, six 3-option items.',
    href: '/reading/parts/b',
    accent: 'partB',
  },
  {
    title: 'Practice Part C',
    subtitle: 'Long-text comprehension. Two longer texts with detailed 4-option questions.',
    href: '/reading/parts/c',
    accent: 'partC',
  },
  {
    title: 'Full Reading Exam',
    subtitle: '60 minutes • 42 questions • Part A hard-locked, Parts B+C share a 45-minute window.',
    href: '/reading/exam',
    accent: 'exam',
  },
];

const ACCENT_STYLES: Record<HubCard['accent'], { ring: string; badge: string; icon: string; chip: string }> = {
  partA: {
    ring: 'border-blue-200 hover:border-blue-300 dark:border-blue-900/60 dark:hover:border-blue-700',
    badge: 'bg-blue-100 text-blue-800 dark:bg-blue-950/60 dark:text-blue-300',
    icon: 'text-blue-600 dark:text-blue-400',
    chip: 'Part A',
  },
  partB: {
    ring: 'border-sky-200 hover:border-sky-300 dark:border-sky-900/60 dark:hover:border-sky-700',
    badge: 'bg-sky-100 text-sky-800 dark:bg-sky-950/60 dark:text-sky-300',
    icon: 'text-sky-600 dark:text-sky-400',
    chip: 'Part B',
  },
  partC: {
    ring: 'border-emerald-200 hover:border-emerald-300 dark:border-emerald-900/60 dark:hover:border-emerald-700',
    badge: 'bg-emerald-100 text-emerald-800 dark:bg-emerald-950/60 dark:text-emerald-300',
    icon: 'text-emerald-600 dark:text-emerald-400',
    chip: 'Part C',
  },
  exam: {
    ring: 'border-amber-200 hover:border-amber-300 dark:border-amber-900/60 dark:hover:border-amber-700',
    badge: 'bg-amber-100 text-amber-900 dark:bg-amber-950/60 dark:text-amber-300',
    icon: 'text-amber-600 dark:text-amber-400',
    chip: 'Full exam',
  },
};

export default function ReadingHome() {
  const { isAuthenticated, loading: authLoading } = useAuth();
  const { profile } = useReadingProfile();

  const [home, setHome] = useState<ReadingHomeDto | null>(null);
  const [assignments, setAssignments] = useState<ReadingAssignmentDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [retryCount, setRetryCount] = useState(0);

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
        const [readingHome, readingAssignments] = await Promise.all([
          getReadingHome(),
          listMyReadingAssignments().catch(() => [] as ReadingAssignmentDto[]),
        ]);
        if (cancelled) return;
        setHome(readingHome);
        setAssignments(readingAssignments);
      } catch (err) {
        if (!cancelled) setError(readErrorMessage(err, 'Failed to load Reading workspace.'));
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [authLoading, isAuthenticated, retryCount]);

  const activeAttempts = useMemo(() => home?.activeAttempts ?? [], [home]);
  // The free-sample paper stays in the library too; this only finds it for the
  // card. Resume-aware: an open attempt on it wins over a fresh start.
  const freeSample = useMemo(() => findFreeSamplePaper(home?.papers ?? []), [home]);
  const freeSampleHref = freeSample
    ? (activeAttempts.find((attempt) => attempt.paperId === freeSample.id && attempt.canResume)?.route
      ?? freeSample.route)
    : null;
  const latestResult = home?.recentResults?.[0] ?? null;
  const totalPapers = home?.papers?.length ?? 0;

  const daysToExam: number | null = useMemo(() => {
    if (!profile?.examDate) return null;
    const diff = new Date(profile.examDate).getTime() - Date.now();
    return Math.max(0, Math.ceil(diff / (1000 * 60 * 60 * 24)));
  }, [profile]);

  const heroHighlights = useMemo(
    () => [
      {
        icon: Target,
        label: 'Available papers',
        value: loading ? 'Loading…' : `${totalPapers} ready`,
      },
      {
        icon: TrendingUp,
        label: 'Latest result',
        value: latestResult
          ? latestResult.scaledScore == null
            ? `${latestResult.rawScore}/${latestResult.maxRawScore} practice`
            : `${latestResult.rawScore}/${latestResult.maxRawScore} • ${latestResult.scaledScore}/500`
          : 'No result yet',
      },
      {
        icon: CalendarDays,
        label: 'Exam',
        value:
          daysToExam === null
            ? 'Not scheduled'
            : daysToExam === 0
              ? 'Today'
              : `${daysToExam} days`,
      },
    ],
    [daysToExam, latestResult, loading, totalPapers],
  );

  return (
    <>
      <div className="space-y-6 sm:space-y-10">
        <LearnerPageHero
          eyebrow="Module focus"
          icon={BookOpen}
          accent="blue"
          title="OET Reading"
          description="Practice each part separately or attempt the full reading exam under official timing."
          highlights={heroHighlights}
        />

        <CreditsGuideButton variant="banner" />

        <LearnerSkillSwitcher compact />

        {error ? (
          <div className="flex flex-wrap items-center gap-3">
            <InlineAlert variant="error">{error}</InlineAlert>
            <Button
              variant="outline"
              size="sm"
              onClick={() => setRetryCount((count) => count + 1)}
              className="border-danger/30 bg-surface text-danger hover:bg-danger/5 dark:border-danger/40 dark:hover:bg-danger/10"
            >
              Try again
            </Button>
          </div>
        ) : null}

        {activeAttempts.length > 0 ? <ResumeBanner attempts={activeAttempts} /> : null}

        <MotionSection delayIndex={0}>
          <section aria-labelledby="reading-hub-heading" data-tour="reading-hub">
            <div className="mb-4">
              <p className="eyebrow text-primary">
                Choose how to practice
              </p>
              <h2 id="reading-hub-heading" className="text-lg font-bold text-navy">
                Practice by Part, or attempt the full exam
              </h2>
            </div>
  
            <CreditUsageInfoCard module="reading" className="mb-4" />
  
            {freeSampleHref ? (
              <FreeSampleCard
                testId="reading-free-mock-card"
                icon={BookOpen}
                title="Free Reading Mock"
                description="Try one complete OET Reading mock for free."
                href={freeSampleHref}
                onClick={() => analytics.track('free_sample_click', { module: 'reading' })}
                className="mb-4"
              />
            ) : null}
  
            {loading ? (
              <LearnerSkeleton variant="card-grid" />
            ) : (
              <ul
                className="grid grid-cols-1 gap-4 sm:grid-cols-2"
                data-testid="reading-hub-cards"
              >
                {HUB_CARDS.map((card) => {
                  const accent = ACCENT_STYLES[card.accent];
                  return (
                    <li key={card.href}>
                      <Link
                        href={card.href}
                        data-testid={`reading-hub-card-${card.accent}`}
                        className={`group relative flex h-full items-start gap-4 rounded-2xl border bg-surface p-5 shadow-sm transition-[border-color,box-shadow] duration-200 hover:shadow-clinical focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 ${accent.ring}`}
                      >
                        <BookOpen className={`mt-0.5 h-6 w-6 shrink-0 ${accent.icon}`} aria-hidden />
                        <div className="flex-1">
                          <div className="flex items-center justify-between gap-2 flex-wrap">
                            <h3 className="text-sm font-bold text-navy">
                              {card.title}
                            </h3>
                            <span className={`rounded-full px-2 py-0.5 text-3xs font-bold uppercase tracking-wide ${accent.badge}`}>
                              {accent.chip}
                            </span>
                          </div>
                          <p className="mt-1 text-sm text-muted">{card.subtitle}</p>
                        </div>
                        <PlayCircle
                          className="h-4 w-4 self-center text-primary opacity-0 transition-opacity group-hover:opacity-100 group-focus-visible:opacity-100"
                          aria-hidden
                        />
                      </Link>
                    </li>
                  );
                })}
              </ul>
            )}
          </section>
        </MotionSection>

        {!loading && home ? (
          <ReadingSecondaryDashboard
            assignments={assignments}
            papers={home.papers}
            recentResults={home.recentResults}
          />
        ) : null}
      </div>
    </>
  );
}

function ReadingSecondaryDashboard({
  assignments,
  papers,
  recentResults,
}: {
  assignments: ReadingAssignmentDto[];
  papers: ReadingHomePaperDto[];
  recentResults: ReadingHomeResultDto[];
}) {
  return (
    <section aria-labelledby="reading-workspace-heading" className="grid gap-4 lg:grid-cols-3">
      <div className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
        <DashboardPanelHeader
          icon={ClipboardCheck}
          eyebrow="Practice"
          title="Practice Hub"
          href="/reading/practice"
        />
        {assignments.length > 0 ? (
          <ul className="mt-4 space-y-3">
            {assignments.slice(0, 3).map((assignment) => (
              <li key={assignment.id} className="rounded-xl border border-border/70 bg-background-light p-3 text-sm">
                <p className="font-semibold text-navy">{assignment.kind.replace(/_/g, ' ')}</p>
                <p className="mt-1 text-xs text-muted">
                  Due {formatOptionalDate(assignment.dueAt)} · {assignment.status}
                </p>
                {assignment.note ? <p className="mt-2 text-xs text-muted">{assignment.note}</p> : null}
              </li>
            ))}
          </ul>
        ) : (
          <p className="mt-4 rounded-xl border border-dashed border-border px-3 py-4 text-sm text-muted">
            No active Reading assignments.
          </p>
        )}
      </div>

      <div className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
        <DashboardPanelHeader
          icon={Target}
          eyebrow="Paper library"
          title="Available papers"
          href="/reading/exam"
        />
        {papers.length > 0 ? (
          <ul className="mt-4 space-y-3">
            {papers.slice(0, 3).map((paper) => (
              <li key={paper.id}>
                <Link href={paper.route} className="block rounded-xl border border-border/70 bg-background-light p-3 text-sm transition-colors hover:border-primary/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary">
                  <span className="font-semibold text-navy">{paper.title}</span>
                  <span className="mt-1 block text-xs text-muted">
                    {paper.partACount}+{paper.partBCount}+{paper.partCCount} items · {paper.estimatedDurationMinutes} min
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        ) : (
          <p className="mt-4 rounded-xl border border-dashed border-border px-3 py-4 text-sm text-muted">
            Published Reading papers will appear here.
          </p>
        )}
      </div>

      <div className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
        <DashboardPanelHeader
          icon={TrendingUp}
          eyebrow="Review"
          title="Recent results"
          href="/reading/stats"
        />
        {recentResults.length > 0 ? (
          <ul className="mt-4 space-y-3">
            {recentResults.map((result) => (
              <li key={result.attemptId}>
                <article className="rounded-xl border border-border/70 bg-background-light p-3 text-sm">
                  <span className="inline-flex rounded-full bg-blue-100 px-2 py-0.5 tile-label text-blue-700 dark:bg-blue-950/60 dark:text-blue-300">
                    {result.partCode ? `Part ${result.partCode}` : 'Full exam'}
                  </span>
                  <p className="mt-2 font-semibold text-navy">{result.paperTitle}</p>
                  <p className="mt-1 text-xs text-muted">
                    {result.rawScore}/{result.maxRawScore}
                    {result.requiresAdminReview
                      ? ' · admin review pending'
                      : result.scaledScore == null
                        ? ' practice'
                        : ` · ${result.scaledScore}/500 · ${result.gradeLetter}`}
                  </p>
                  <div className="mt-3 flex items-center gap-2">
                    <Button asChild size="sm" variant="outline" className="flex-1 bg-surface">
                      <Link href={result.route}>
                        <Eye className="h-3.5 w-3.5" aria-hidden />
                        Review
                      </Link>
                    </Button>
                    <Button asChild size="sm" className="flex-1">
                      <Link href={result.practiceRoute}>
                        <PlayCircle className="h-3.5 w-3.5" aria-hidden />
                        Practice
                      </Link>
                    </Button>
                  </div>
                </article>
              </li>
            ))}
          </ul>
        ) : (
          <p className="mt-4 rounded-xl border border-dashed border-border px-3 py-4 text-sm text-muted">
            Submit a Reading attempt to unlock review.
          </p>
        )}
      </div>
    </section>
  );
}

function DashboardPanelHeader({
  icon: Icon,
  eyebrow,
  title,
  href,
}: {
  icon: LucideIcon;
  eyebrow: string;
  title: string;
  href: string;
}) {
  return (
    <div className="flex items-start justify-between gap-3">
      <div>
        <p className="flex items-center gap-1.5 eyebrow text-primary">
          <Icon className="h-3.5 w-3.5" aria-hidden />
          {eyebrow}
        </p>
        <h2 id={title === 'Practice Hub' ? 'reading-workspace-heading' : undefined} className="mt-1 text-base font-bold text-navy">
          {title}
        </h2>
      </div>
      <Link href={href} className="text-xs font-semibold text-primary hover:text-primary-dark">
        View
      </Link>
    </div>
  );
}

function formatOptionalDate(iso: string | null): string {
  if (!iso) return 'not set';
  try {
    return new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' });
  } catch {
    return iso;
  }
}

function ResumeBanner({ attempts }: { attempts: ReadingHomeAttemptDto[] }) {
  const resumable = attempts.find((attempt) => attempt.canResume);
  if (!resumable) return null;

  return (
    <div className="rounded-xl border border-emerald-200 bg-emerald-50 px-5 py-4 dark:border-emerald-700 dark:bg-emerald-900/20">
      <p className="mb-1 flex items-center gap-2 text-sm font-semibold text-emerald-800 dark:text-emerald-200">
        <Clock className="h-4 w-4" aria-hidden />
        You have an open Reading attempt
      </p>
      <p className="mb-3 text-xs text-emerald-700/80 dark:text-emerald-300/70">
        {resumable.paperTitle}: {resumable.answeredCount}/{resumable.totalQuestions} answered. Resume
        before the timer window closes.
      </p>
      <Button asChild size="sm" className="bg-emerald-600 hover:bg-emerald-700 dark:bg-emerald-700 dark:hover:bg-emerald-600">
        <Link href={resumable.route}>
          <ListChecks className="h-4 w-4" aria-hidden />
          Resume attempt
        </Link>
      </Button>
    </div>
  );
}
