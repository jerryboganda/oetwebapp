'use client';

import { useEffect, useMemo, useState } from 'react';
import {
  BookOpen,
  BookOpenText,
  CalendarDays,
  ClipboardCheck,
  Eye,
  FileText,
  ListChecks,
  PlayCircle,
  ScanSearch,
  Target,
  Timer,
  TrendingUp,
} from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import Link from 'next/link';
import { InlineAlert } from '@/components/ui/alert';
import { Card } from '@/components/ui/card';
import { CardLink } from '@/components/ui/card-link';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { Button } from '@/components/ui/button';
import { LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
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
  icon: LucideIcon;
}

// One Reading identity colour for every card; the icon tells the parts apart.
const HUB_CARDS: HubCard[] = [
  {
    title: 'Practice Part A',
    subtitle: 'Expeditious reading. Match section headings to four short medical texts in 15 minutes.',
    href: '/reading/parts/a',
    accent: 'partA',
    icon: ScanSearch,
  },
  {
    title: 'Practice Part B',
    subtitle: 'Workplace texts. Short workplace notices and excerpts, six 3-option items.',
    href: '/reading/parts/b',
    accent: 'partB',
    icon: FileText,
  },
  {
    title: 'Practice Part C',
    subtitle: 'Long-text comprehension. Two longer texts with detailed 4-option questions.',
    href: '/reading/parts/c',
    accent: 'partC',
    icon: BookOpenText,
  },
  {
    title: 'Full Reading Exam',
    subtitle: '60 minutes • 42 questions • Part A hard-locked, Parts B+C share a 45-minute window.',
    href: '/reading/exam',
    accent: 'exam',
    icon: Timer,
  },
];

const SKILL_CHIP = 'inline-flex w-fit items-center rounded-full border border-skill-reading/20 bg-skill-reading/10 px-2 py-0.5 tile-label text-skill-reading';

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
      <LearnerPageHero
        eyebrow="Module focus"
        icon={BookOpen}
        accent="reading"
        title="OET Reading"
        description="Practice each part separately or attempt the full reading exam under official timing."
        highlights={heroHighlights}
      />

      <CreditsGuideButton variant="banner" />

      <LearnerSkillSwitcher compact />

      {error ? (
        <InlineAlert
          variant="error"
          action={(
            <Button variant="outline" size="sm" onClick={() => setRetryCount((count) => count + 1)}>
              Try again
            </Button>
          )}
        >
          {error}
        </InlineAlert>
      ) : null}

      {activeAttempts.length > 0 ? <ResumeBanner attempts={activeAttempts} /> : null}

      <MotionSection delayIndex={0}>
        <section aria-label="Practice by Part, or attempt the full exam" data-tour="reading-hub">
          <LearnerSurfaceSectionHeader
            eyebrow="Choose how to practice"
            title="Practice by Part, or attempt the full exam"
            className="mb-4"
          />

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

          {/* The four entry cards are static, so they never wait on the API. */}
          <ul
            className="grid grid-cols-1 gap-4 sm:grid-cols-2"
            data-testid="reading-hub-cards"
          >
            {HUB_CARDS.map((card, index) => {
              const Icon = card.icon;
              return (
                <li key={card.href}>
                  <MotionItem delayIndex={Math.min(index, 5)} className="h-full">
                    <CardLink
                      href={card.href}
                      data-testid={`reading-hub-card-${card.accent}`}
                      className="group flex h-full items-start gap-4"
                    >
                      <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-skill-reading/10 text-skill-reading">
                        <Icon className="h-5 w-5" aria-hidden />
                      </span>
                      <div className="min-w-0 flex-1">
                        <h3 className="text-sm font-bold text-navy">{card.title}</h3>
                        <p className="mt-1 text-sm text-muted">{card.subtitle}</p>
                      </div>
                      <PlayCircle
                        className="h-4 w-4 shrink-0 self-center text-primary opacity-0 transition-opacity group-hoverable:opacity-100 group-focus-visible:opacity-100"
                        aria-hidden
                      />
                    </CardLink>
                  </MotionItem>
                </li>
              );
            })}
          </ul>
        </section>
      </MotionSection>

      {loading ? (
        <LearnerSkeleton variant="card-grid" />
      ) : home ? (
        <ReadingSecondaryDashboard
          assignments={assignments}
          papers={home.papers}
          recentResults={home.recentResults}
        />
      ) : null}
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
    <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
      <MotionItem delayIndex={0} className="h-full">
        <Card className="h-full">
          <DashboardPanelHeader
            icon={ClipboardCheck}
            eyebrow="Practice"
            title="Practice Hub"
            href="/reading/practice"
          />
          {assignments.length > 0 ? (
            <ul className="mt-3 divide-y divide-border">
              {assignments.slice(0, 3).map((assignment) => (
                <li key={assignment.id} className="py-3 text-sm last:pb-0">
                  <p className="font-semibold capitalize text-navy">{assignment.kind.replace(/_/g, ' ')}</p>
                  <p className="mt-1 text-xs text-muted">
                    Due {formatOptionalDate(assignment.dueAt)} · {assignment.status}
                  </p>
                  {assignment.note ? <p className="mt-2 text-xs text-muted">{assignment.note}</p> : null}
                </li>
              ))}
            </ul>
          ) : (
            <PanelEmpty icon={ClipboardCheck}>No active Reading assignments.</PanelEmpty>
          )}
        </Card>
      </MotionItem>

      <MotionItem delayIndex={1} className="h-full">
        <Card className="h-full">
          <DashboardPanelHeader
            icon={Target}
            eyebrow="Paper library"
            title="Available papers"
            href="/reading/exam"
          />
          {papers.length > 0 ? (
            <ul className="mt-3 divide-y divide-border">
              {papers.slice(0, 3).map((paper) => (
                <li key={paper.id} className="py-1.5 last:pb-0">
                  <Link
                    href={paper.route}
                    className="hover-primary -mx-2 block rounded-control px-2 py-2 text-sm transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                  >
                    <span className="font-semibold text-navy">{paper.title}</span>
                    <span className="mt-1 block text-xs tabular-nums text-muted">
                      {paper.partACount}+{paper.partBCount}+{paper.partCCount} items · {paper.estimatedDurationMinutes} min
                    </span>
                  </Link>
                </li>
              ))}
            </ul>
          ) : (
            <PanelEmpty icon={Target}>Published Reading papers will appear here.</PanelEmpty>
          )}
        </Card>
      </MotionItem>

      <MotionItem delayIndex={2} className="h-full">
        <Card className="h-full">
          <DashboardPanelHeader
            icon={TrendingUp}
            eyebrow="Review"
            title="Recent results"
            href="/reading/stats"
          />
          {recentResults.length > 0 ? (
            <ul className="mt-3 divide-y divide-border">
              {recentResults.map((result) => (
                <li key={result.attemptId} className="py-3 text-sm last:pb-0">
                  <span className={SKILL_CHIP}>
                    {result.partCode ? `Part ${result.partCode}` : 'Full exam'}
                  </span>
                  <p className="mt-2 font-semibold text-navy">{result.paperTitle}</p>
                  <p className="mt-1 text-xs tabular-nums text-muted">
                    {result.rawScore}/{result.maxRawScore}
                    {result.requiresAdminReview
                      ? ' · admin review pending'
                      : result.scaledScore == null
                        ? ' practice'
                        : ` · ${result.scaledScore}/500 · ${result.gradeLetter}`}
                  </p>
                  <div className="mt-3 flex items-center gap-2">
                    <Button asChild size="sm" variant="outline" className="flex-1">
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
                </li>
              ))}
            </ul>
          ) : (
            <PanelEmpty icon={TrendingUp}>Submit a Reading attempt to unlock review.</PanelEmpty>
          )}
        </Card>
      </MotionItem>
    </div>
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
      <div className="min-w-0">
        <p className="eyebrow text-muted">{eyebrow}</p>
        <h2 className="mt-1 flex items-center gap-2 text-base font-bold text-navy">
          <Icon className="h-4 w-4 shrink-0 text-primary" aria-hidden />
          {title}
        </h2>
      </div>
      <Button asChild variant="ghost" size="sm" className="-me-2 shrink-0 text-primary">
        <Link href={href}>View</Link>
      </Button>
    </div>
  );
}

/** A panel's own empty state: an icon and its one line, inside the panel card. */
function PanelEmpty({ icon: Icon, children }: { icon: LucideIcon; children: string }) {
  return (
    <div role="status" className="mt-4 flex items-center gap-3 rounded-control border border-dashed border-border px-3 py-4 text-sm text-muted">
      <Icon className="h-4 w-4 shrink-0" aria-hidden />
      <p>{children}</p>
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
    <InlineAlert
      variant="success"
      live="polite"
      title="You have an open Reading attempt"
      action={(
        <Button asChild size="sm">
          <Link href={resumable.route}>
            <ListChecks className="h-4 w-4" aria-hidden />
            Resume attempt
          </Link>
        </Button>
      )}
    >
      {resumable.paperTitle}: {resumable.answeredCount}/{resumable.totalQuestions} answered. Resume
      before the timer window closes.
    </InlineAlert>
  );
}
