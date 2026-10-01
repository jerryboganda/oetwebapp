'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import {
  Briefcase,
  CalendarDays,
  Clock,
  Eye,
  Headphones,
  ListChecks,
  Lock,
  NotebookPen,
  PlayCircle,
  Presentation,
  Target,
  Timer,
  TrendingUp,
  type LucideIcon,
} from 'lucide-react';
import { CreditsGuideButton, CreditUsageInfoCard, LearnerPageHero } from '@/components/domain';
import { FreeSampleCard } from '@/components/domain/free-sample-card';
import { LearnerSurfaceMetaRow, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { LearnerSkillSwitcher } from '@/components/domain/learner-skill-switcher';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Card, cardClassName } from '@/components/ui/card';
import { CardLink } from '@/components/ui/card-link';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { useAuth } from '@/contexts/auth-context';
import { analytics } from '@/lib/analytics';
import { findFreeSamplePaper } from '@/lib/free-sample';
import { useListeningProfile } from '@/hooks/useListeningProfile';
import {
  getListeningHome,
  startListeningAttempt,
  type ListeningHomeAttemptDto,
  type ListeningHomeDto,
  type ListeningHomePaperDto,
  type ListeningHomeResultDto,
} from '@/lib/listening-api';
import { ListeningExamFolderBrowser } from '@/components/domain/listening/listening-exam-folder-browser';
import { groupListeningExamPapers } from '@/lib/listening-exam-categories';
import { readErrorMessage } from '@/lib/read-error-message';
import {
  ContentLockedNotice,
  isContentLockedError,
  readContentLockedMessage,
} from '@/components/domain/ContentLockedNotice';
import {
  InsufficientCreditsModal,
  isInsufficientCreditsError,
  readInsufficientCreditsMessage,
} from '@/components/domain/InsufficientCreditsModal';
import { showCreditFeedback } from '@/lib/credit-feedback';
import { submitAudioCheck } from '@/lib/listening-pathway-api';

// Per the 2026-05-27 OET sample-test alignment directive, the Listening hub
// shows exactly four candidate-facing entries — three Practice-by-Part cards
// (A / B / C) and one Full Listening Exam card. The only addition is the
// out-of-grid FREE SAMPLE card above them (Free Mocks, 2026-09-22): it is not a
// fifth grid entry and is hidden unless a paper carries the `free-sample` tag.
// Below that grid we now mirror
// the Reading hub's operational dashboard (Reading parity, 2026-06-26): an
// "Available papers" paper library so learners can see and launch every
// published full listening exam in-module, plus recent results — instead of
// being bounced straight to the Mocks centre with nothing to choose from.
interface HubCard {
  title: string;
  subtitle: string;
  href: string;
  accent: 'partA' | 'partB' | 'partC' | 'exam';
  icon: LucideIcon;
}

// One Listening identity colour for every card; the icon tells the parts apart.
const HUB_CARDS: HubCard[] = [
  {
    title: 'Practice Part A',
    subtitle: 'Patient consultations: note-taking from two consultations (24 items).',
    href: '/listening/practice/a',
    accent: 'partA',
    icon: NotebookPen,
  },
  {
    title: 'Practice Part B',
    subtitle: 'Workplace extracts: six short workplace audio extracts (6 items).',
    href: '/listening/practice/b',
    accent: 'partB',
    icon: Briefcase,
  },
  {
    title: 'Practice Part C',
    subtitle: 'Healthcare presentations: two longer extracts with detailed questions (12 items).',
    href: '/listening/practice/c',
    accent: 'partC',
    icon: Presentation,
  },
  {
    title: 'Full Listening Exam',
    subtitle: '45 minutes • 42 questions • audio plays once. Raw practice evidence is retained; owner-approved conversion is shown only when configured.',
    href: '/listening/exam',
    accent: 'exam',
    icon: Timer,
  },
];

const SKILL_ICON_TILE = 'flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-skill-listening/10 text-skill-listening';
const SKILL_CHIP = 'inline-flex w-fit items-center rounded-full border border-skill-listening/20 bg-skill-listening/10 px-2 py-0.5 tile-label text-skill-listening';

export default function ListeningHome() {
  const { isAuthenticated, loading: authLoading } = useAuth();
  const router = useRouter();
  const { profile } = useListeningProfile();

  const [home, setHome] = useState<ListeningHomeDto | null>(null);
  const [homeLoading, setHomeLoading] = useState(true);
  const [homeError, setHomeError] = useState<string | null>(null);
  const [retryCount, setRetryCount] = useState(0);
  const [startingPaperId, setStartingPaperId] = useState<string | null>(null);
  const [lockedMessage, setLockedMessage] = useState<string | null>(null);
  const [insufficientCreditsMessage, setInsufficientCreditsMessage] = useState<string | null>(null);

  useEffect(() => {
    if (!authLoading && isAuthenticated) {
      analytics.track('module_entry', { module: 'listening' });
    }
  }, [authLoading, isAuthenticated]);

  // Redirect unauthenticated visitors to sign-in.
  useEffect(() => {
    if (authLoading) return;
    if (!isAuthenticated) {
      router.replace('/sign-in');
    }
  }, [authLoading, isAuthenticated, router]);

  // Load the listening home payload (published papers + recent activity). This
  // is the same data contract Reading uses (`getReadingHome`), so the learner
  // sees every available full listening exam without leaving the module.
  useEffect(() => {
    if (authLoading || !isAuthenticated) return;
    let cancelled = false;
    (async () => {
      try {
        setHomeLoading(true);
        setHomeError(null);
        const data = await getListeningHome();
        if (!cancelled) setHome(data);
      } catch (err) {
        if (!cancelled) setHomeError(readErrorMessage(err, 'Failed to load your listening papers.'));
      } finally {
        if (!cancelled) setHomeLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [authLoading, isAuthenticated, retryCount]);

  const papers = useMemo(() => home?.papers ?? [], [home]);
  const catalogPapers = useMemo(
    () => groupListeningExamPapers(papers).flatMap((section) => section.papers),
    [papers],
  );
  // The free-sample paper stays in the library too; this only finds it for the card.
  const freeSample = useMemo(() => findFreeSamplePaper(papers), [papers]);
  const freeSampleHref = freeSample
    ? freeSample.lastAttempt && !freeSample.lastAttempt.submittedAt
      ? `/listening/paper/${freeSample.id}?attemptId=${freeSample.lastAttempt.attemptId}`
      : freeSample.route
    : null;
  const activeAttempts = useMemo(() => home?.activeAttempts ?? [], [home]);
  const recentResults = useMemo(() => home?.recentResults ?? [], [home]);
  const latestResult = recentResults[0] ?? null;
  const progressScoreDisplay = home?.progressScoreDisplay ?? latestResult?.scoreDisplay ?? null;

  async function handleStartFullExam(paper: ListeningHomePaperDto) {
    const resumeRoute = paper.lastAttempt && !paper.lastAttempt.submittedAt
      ? `/listening/paper/${paper.id}?attemptId=${paper.lastAttempt.attemptId}`
      : null;
    if (resumeRoute) {
      router.push(resumeRoute);
      return;
    }
    if (paper.requiresSubscription === true) {
      setLockedMessage('This paper requires an active Listening subscription.');
      return;
    }

    setStartingPaperId(paper.id);
    setHomeError(null);
    setLockedMessage(null);
    setInsufficientCreditsMessage(null);
    try {
      // Full exams are gated by the 24h pathway sound check. Auto-refresh it
      // before creating the attempt so a fresh learner (no prior check) or an
      // expired window does not see "Pass the Listening sound check..." while
      // part practice (practice mode) remains ungated.
      try {
        await submitAudioCheck({ outcome: 'clear' });
      } catch {
        // Non-fatal — startListeningAttempt will surface the authoritative error
      }
      let started: Awaited<ReturnType<typeof startListeningAttempt>>;
      try {
        started = await startListeningAttempt(paper.id, 'exam');
      } catch (err) {
        if (isListeningAudioCheckError(err)) {
          await submitAudioCheck({ outcome: 'clear' });
          started = await startListeningAttempt(paper.id, 'exam');
        } else {
          throw err;
        }
      }
      showCreditFeedback(started.feedbackMessage);
      router.push(`/listening/paper/${paper.id}?attemptId=${started.attemptId}`);
    } catch (caught) {
      if (isInsufficientCreditsError(caught)) {
        setInsufficientCreditsMessage(readInsufficientCreditsMessage(caught));
      } else if (isContentLockedError(caught)) {
        setLockedMessage(readContentLockedMessage(caught));
      } else {
        setHomeError(readErrorMessage(caught, 'Could not start the full Listening exam.'));
      }
    } finally {
      setStartingPaperId(null);
    }
  }

  // Cheap derivation — not memoized because wall-clock time is inherently impure.
  const daysToExam: number | null = (() => {
    if (!profile?.examDate) return null;
    // eslint-disable-next-line react-hooks/purity -- intentional: must use wall-clock time
    const diff = new Date(profile.examDate).getTime() - Date.now();
    return Math.max(0, Math.ceil(diff / (1000 * 60 * 60 * 24)));
  })();

  const heroHighlights = useMemo(
    () => [
      {
        icon: Target,
        label: 'Available papers',
        value: homeLoading ? 'Loading…' : `${catalogPapers.length} ready`,
      },
      {
        icon: TrendingUp,
        label: home?.progressScoreDisplayMode === 'latest' || !home?.progressScoreDisplayMode
          ? 'Latest result'
          : `${home.progressScoreDisplayMode} score`,
        value: progressScoreDisplay ?? 'No result yet',
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
    [homeLoading, catalogPapers.length, home?.progressScoreDisplayMode, progressScoreDisplay, daysToExam],
  );

  if (authLoading) {
    return <LearnerSkeleton variant="dashboard" />;
  }

  if (!isAuthenticated) {
    return <InlineAlert variant="info">Please sign in to access the listening module.</InlineAlert>;
  }

  const retry = () => setRetryCount((count) => count + 1);

  return (
    <>
      <InsufficientCreditsModal
        open={insufficientCreditsMessage !== null}
        message={insufficientCreditsMessage ?? ''}
        onClose={() => setInsufficientCreditsMessage(null)}
      />
      <LearnerPageHero
        eyebrow="Module focus"
        icon={Headphones}
        accent="purple"
        title="OET Listening"
        description="Practice each part separately or attempt the full listening exam under official timing."
        highlights={heroHighlights}
      />

      <CreditsGuideButton variant="banner" />

      <LearnerSkillSwitcher compact />

      {/* A failed load shows in the paper library below; this is for a failed start. */}
      {homeError && home ? (
        <InlineAlert
          variant="error"
          action={<Button variant="outline" size="sm" onClick={retry}>Try again</Button>}
        >
          {homeError}
        </InlineAlert>
      ) : null}
      {lockedMessage ? <ContentLockedNotice message={lockedMessage} /> : null}

      {activeAttempts.length > 0 ? <ResumeBanner attempts={activeAttempts} /> : null}

      <MotionSection delayIndex={0}>
        <section aria-label="Practice by Part, or attempt the full exam" data-tour="listening-hub">
          <LearnerSurfaceSectionHeader
            eyebrow="Choose how to practice"
            title="Practice by Part, or attempt the full exam"
            className="mb-4"
          />

          <CreditUsageInfoCard module="listening" className="mb-4" />

          {freeSampleHref ? (
            <FreeSampleCard
              testId="listening-free-mock-card"
              icon={Headphones}
              title="Free Listening Mock"
              description="Try one complete OET Listening mock for free."
              href={freeSampleHref}
              onClick={() => analytics.track('free_sample_click', { module: 'listening' })}
              className="mb-4"
            />
          ) : null}

          <ul
            className="grid grid-cols-1 gap-4 sm:grid-cols-2"
            data-testid="listening-hub-cards"
          >
            {HUB_CARDS.map((card, index) => {
              const Icon = card.icon;
              return (
                <li key={card.href}>
                  <MotionItem delayIndex={Math.min(index, 5)} className="h-full">
                    <CardLink
                      href={card.href}
                      data-testid={`listening-hub-card-${card.accent}`}
                      className="group flex h-full items-start gap-4"
                    >
                      <span className={SKILL_ICON_TILE}>
                        <Icon className="h-5 w-5" aria-hidden />
                      </span>
                      <div className="min-w-0 flex-1">
                        <h3 className="text-sm font-bold text-navy">{card.title}</h3>
                        <p className="mt-1 text-sm text-muted">{card.subtitle}</p>
                      </div>
                      <PlayCircle
                        className="h-4 w-4 shrink-0 self-center text-primary opacity-0 transition-opacity group-hover:opacity-100 group-focus-visible:opacity-100"
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

      {/* Full listening exam library — every published listening paper the
          learner can attempt, surfaced in-module (Reading parity). */}
      <MotionSection delayIndex={1}>
        <section aria-label="Available listening exams">
          <LearnerSurfaceSectionHeader
            eyebrow="Paper library"
            icon={Target}
            title="Available listening exams"
            className="mb-4"
          />
          {homeLoading ? (
            <LearnerSkeleton variant="card-grid" />
          ) : !home ? (
            <ErrorState
              message={homeError ?? undefined}
              onRetry={retry}
              retryLabel="Try again"
            />
          ) : catalogPapers.length === 0 ? (
            <EmptyState
              icon={<Headphones className="h-8 w-8" aria-hidden />}
              title="No published Atlas/Nova Listening papers yet."
            />
          ) : (
            <ListeningExamFolderBrowser
              papers={papers}
              emptyMessage="No published Atlas/Nova Listening papers yet."
              renderPaper={(paper) => (
                <PaperCard
                  paper={paper}
                  starting={startingPaperId === paper.id}
                  onStart={() => void handleStartFullExam(paper)}
                />
              )}
            />
          )}
        </section>
      </MotionSection>

      {recentResults.length > 0 ? (
        <MotionSection delayIndex={2}>
          <section aria-label="Recent results">
            <LearnerSurfaceSectionHeader
              eyebrow="Review"
              icon={TrendingUp}
              title="Recent results"
              className="mb-4"
            />
            <ul className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
              {recentResults.map((result, index) => (
                <li key={result.attemptId}>
                  <MotionItem delayIndex={Math.min(index, 5)} className="h-full">
                    <ResultCard result={result} />
                  </MotionItem>
                </li>
              ))}
            </ul>
          </section>
        </MotionSection>
      ) : null}
    </>
  );
}

function isListeningAudioCheckError(err: unknown): boolean {
  if (typeof err !== 'object' || err === null) return false;
  const e = err as { code?: unknown; message?: unknown; detail?: { code?: unknown; message?: unknown } };
  const code = typeof e.code === 'string' ? e.code : typeof e.detail?.code === 'string' ? e.detail.code : '';
  if (code === 'listening_audio_check_required' || code === 'audio-check-required') return true;
  const msg = typeof e.message === 'string' ? e.message : typeof e.detail?.message === 'string' ? e.detail.message : '';
  return msg.includes('Pass the Listening sound check');
}

function isPartialListeningExam(paper: Pick<ListeningHomePaperDto, 'questionCount' | 'title'>) {
  return (
    paper.questionCount !== 42
    || paper.title.includes('Q37–42 unavailable')
    || paper.title.includes('Q37-42 unavailable')
  );
}

function PaperCard({
  paper,
  starting,
  onStart,
}: {
  paper: ListeningHomePaperDto;
  starting: boolean;
  onStart: () => void;
}) {
  const locked = paper.requiresSubscription === true;
  const partial = isPartialListeningExam(paper);
  const resume = Boolean(paper.lastAttempt && !paper.lastAttempt.submittedAt);
  return (
    <article className={cn(cardClassName({}), 'flex h-full flex-col')}>
      <div className="flex items-start gap-4">
        <span className={SKILL_ICON_TILE}>
          <Headphones className="h-5 w-5" aria-hidden />
        </span>
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h3 className="text-sm font-bold text-navy">{paper.title}</h3>
            {locked ? (
              <Badge variant="warning" className="gap-1">
                <Lock className="h-3 w-3" aria-hidden />
                Premium
              </Badge>
            ) : partial ? (
              <Badge variant="warning">Partial · Q37–42 unavailable</Badge>
            ) : (
              <Badge variant="success">Full exam</Badge>
            )}
          </div>
          <LearnerSurfaceMetaRow
            size="compact"
            className="mt-1.5 tabular-nums"
            items={[
              { label: `${paper.questionCount} questions`, icon: ListChecks },
              { label: `${paper.estimatedDurationMinutes} min`, icon: Clock },
            ]}
          />
          {partial ? (
            <p className="mt-2 text-xs text-muted">
              Questions 37–42 are unavailable in the supplied source. This paper is 36 items (Parts A, B, and C extract 1 only).
            </p>
          ) : null}
        </div>
      </div>
      <div className="mt-auto pt-4">
        <Button
          size="sm"
          onClick={onStart}
          disabled={starting}
        >
          {starting ? 'Starting...' : resume ? 'Resume exam' : locked ? 'View access' : 'Start full exam'}
        </Button>
      </div>
    </article>
  );
}

function ResultCard({ result }: { result: ListeningHomeResultDto }) {
  const scopeLabel = result.partCode
    ? `Part ${result.partCode} practice`
    : ['exam', 'home', 'diagnostic'].includes(result.mode)
      ? 'Full exam'
      : 'Practice';

  return (
    <Card className="flex h-full flex-col">
      <span className={SKILL_CHIP}>{scopeLabel}</span>
      <h3 className="mt-2 text-sm font-semibold text-navy">{result.paperTitle}</h3>
      <p className="mt-1 text-xs tabular-nums text-muted">
        {result.requiresAdminReview ? 'Admin review pending' : result.scoreDisplay}
      </p>
      <div className="mt-auto flex items-center gap-2 pt-3">
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
    </Card>
  );
}

function ResumeBanner({ attempts }: { attempts: ListeningHomeAttemptDto[] }) {
  const resumable = attempts[0];
  if (!resumable) return null;

  return (
    <InlineAlert
      variant="success"
      live="polite"
      title="You have an open Listening attempt"
      action={(
        <Button asChild size="sm">
          <Link href={resumable.route}>
            <ListChecks className="h-4 w-4" aria-hidden />
            Resume attempt
          </Link>
        </Button>
      )}
    >
      {resumable.paperTitle}: {resumable.answeredCount} answered. Resume before the timer window closes.
    </InlineAlert>
  );
}
