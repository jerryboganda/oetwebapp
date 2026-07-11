'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import {
  ArrowRight,
  CalendarDays,
  Clock,
  Headphones,
  ListChecks,
  Lock,
  PlayCircle,
  Target,
  TrendingUp,
} from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { CreditsGuideButton, CreditUsageInfoCard, LearnerPageHero } from '@/components/domain';
import { LearnerSkillSwitcher } from '@/components/domain/learner-skill-switcher';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { InlineAlert } from '@/components/ui/alert';
import { useAuth } from '@/contexts/auth-context';
import { analytics } from '@/lib/analytics';
import { useListeningProfile } from '@/hooks/useListeningProfile';
import {
  getListeningHome,
  type ListeningHomeAttemptDto,
  type ListeningHomeDto,
  type ListeningHomePaperDto,
  type ListeningHomeResultDto,
} from '@/lib/listening-api';
import { readErrorMessage } from '@/lib/read-error-message';

// Per the 2026-05-27 OET sample-test alignment directive, the Listening hub
// shows exactly four candidate-facing entries — three Practice-by-Part cards
// (A / B / C) and one Full Listening Exam card. Below that grid we now mirror
// the Reading hub's operational dashboard (Reading parity, 2026-06-26): an
// "Available papers" paper library so learners can see and launch every
// published full listening exam in-module, plus recent results — instead of
// being bounced straight to the Mocks centre with nothing to choose from.
interface HubCard {
  title: string;
  subtitle: string;
  href: string;
  accent: 'partA' | 'partB' | 'partC' | 'exam';
}

const HUB_CARDS: HubCard[] = [
  {
    title: 'Practice Part A',
    subtitle: 'Patient consultations: note-taking from two consultations (24 items).',
    href: '/listening/practice/a',
    accent: 'partA',
  },
  {
    title: 'Practice Part B',
    subtitle: 'Workplace extracts: six short workplace audio extracts (6 items).',
    href: '/listening/practice/b',
    accent: 'partB',
  },
  {
    title: 'Practice Part C',
    subtitle: 'Healthcare presentations: two longer extracts with detailed questions (12 items).',
    href: '/listening/practice/c',
    accent: 'partC',
  },
  {
    title: 'Full Listening Exam',
    subtitle: '45 minutes • 42 questions • audio plays once. Scored on the official OET 0–500 scale.',
    href: '/listening/exam',
    accent: 'exam',
  },
];

const ACCENT_STYLES: Record<HubCard['accent'], { ring: string; badge: string; icon: string; chip: string }> = {
  partA: {
    ring: 'border-violet-200 hover:border-violet-300',
    badge: 'bg-violet-100 text-violet-800',
    icon: 'bg-violet-100 text-violet-700',
    chip: 'Part A',
  },
  partB: {
    ring: 'border-sky-200 hover:border-sky-300',
    badge: 'bg-sky-100 text-sky-800',
    icon: 'bg-sky-100 text-sky-700',
    chip: 'Part B',
  },
  partC: {
    ring: 'border-emerald-200 hover:border-emerald-300',
    badge: 'bg-emerald-100 text-emerald-800',
    icon: 'bg-emerald-100 text-emerald-700',
    chip: 'Part C',
  },
  exam: {
    ring: 'border-amber-200 hover:border-amber-300',
    badge: 'bg-amber-100 text-amber-900',
    icon: 'bg-amber-100 text-amber-800',
    chip: 'Full exam',
  },
};

export default function ListeningHome() {
  const { isAuthenticated, loading: authLoading } = useAuth();
  const router = useRouter();
  const { profile } = useListeningProfile();

  const [home, setHome] = useState<ListeningHomeDto | null>(null);
  const [homeLoading, setHomeLoading] = useState(true);
  const [homeError, setHomeError] = useState<string | null>(null);
  const [retryCount, setRetryCount] = useState(0);

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
  const activeAttempts = useMemo(() => home?.activeAttempts ?? [], [home]);
  const recentResults = useMemo(() => home?.recentResults ?? [], [home]);
  const latestResult = recentResults[0] ?? null;

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
        value: homeLoading ? 'Loading…' : `${papers.length} ready`,
      },
      {
        icon: TrendingUp,
        label: 'Latest result',
        value: latestResult ? latestResult.scoreDisplay : 'No result yet',
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
    [homeLoading, papers.length, latestResult, daysToExam],
  );

  if (authLoading) {
    return (
      <LearnerDashboardShell pageTitle="Listening">
        <LearnerSkeleton variant="dashboard" />
      </LearnerDashboardShell>
    );
  }

  if (!isAuthenticated) {
    return (
      <LearnerDashboardShell pageTitle="Listening">
        <InlineAlert variant="info">Please sign in to access the listening module.</InlineAlert>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell pageTitle="Listening">
      <main className="space-y-6 sm:space-y-10">
        <LearnerPageHero
          eyebrow="Module focus"
          icon={Headphones}
          accent="purple"
          title="OET Listening"
          description="Practice each part separately or attempt the full listening exam under official timing."
          highlights={heroHighlights}
        />

        <div className="flex justify-end">
          <CreditsGuideButton />
        </div>

        <LearnerSkillSwitcher compact />

        {homeError ? (
          <div className="flex flex-wrap items-center gap-3">
            <InlineAlert variant="error">{homeError}</InlineAlert>
            <button
              type="button"
              onClick={() => setRetryCount((count) => count + 1)}
              className="rounded-full border border-danger/30 bg-surface px-3 py-1 text-xs font-medium text-danger hover:bg-danger/5 dark:border-danger/40 dark:hover:bg-danger/10"
            >
              Try again
            </button>
          </div>
        ) : null}

        {activeAttempts.length > 0 ? <ResumeBanner attempts={activeAttempts} /> : null}

        <section aria-labelledby="listening-hub-heading" data-tour="listening-hub">
          <div className="mb-4">
            <p className="text-xs font-semibold uppercase tracking-widest text-violet-500">
              Choose how to practice
            </p>
            <h2 id="listening-hub-heading" className="text-lg font-bold text-navy">
              Practice by Part, or attempt the full exam
            </h2>
          </div>

          <CreditUsageInfoCard module="listening" className="mb-4" />

          <ul
            className="grid grid-cols-1 gap-4 sm:grid-cols-2"
            data-testid="listening-hub-cards"
          >
            {HUB_CARDS.map((card) => {
              const accent = ACCENT_STYLES[card.accent];
              return (
                <li key={card.href}>
                  <Link
                    href={card.href}
                    data-testid={`listening-hub-card-${card.accent}`}
                    className={`group relative flex h-full items-start gap-4 rounded-2xl border bg-surface p-5 transition-shadow hover:shadow-md ${accent.ring}`}
                  >
                    <div className={`flex h-11 w-11 shrink-0 items-center justify-center rounded-xl ${accent.icon}`}>
                      <Headphones className="h-5 w-5" aria-hidden />
                    </div>
                    <div className="flex-1">
                      <div className="flex items-center justify-between gap-2 flex-wrap">
                        <h3 className="text-sm font-bold text-navy">
                          {card.title}
                        </h3>
                        <span className={`rounded-full px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide ${accent.badge}`}>
                          {accent.chip}
                        </span>
                      </div>
                      <p className="mt-1 text-sm text-muted">{card.subtitle}</p>
                    </div>
                    <PlayCircle
                      className="h-4 w-4 self-center text-violet-400 opacity-0 transition-opacity group-hover:opacity-100"
                      aria-hidden
                    />
                  </Link>
                </li>
              );
            })}
          </ul>
        </section>

        {/* Full listening exam library — every published listening paper the
            learner can attempt, surfaced in-module (Reading parity). */}
        <section aria-labelledby="listening-papers-heading">
          <div className="mb-4 flex items-end justify-between gap-3">
            <div>
              <p className="flex items-center gap-1.5 text-xs font-semibold uppercase tracking-widest text-violet-500">
                <Target className="h-3.5 w-3.5" aria-hidden />
                Paper library
              </p>
              <h2 id="listening-papers-heading" className="text-lg font-bold text-navy">
                Available listening exams
              </h2>
            </div>
          </div>

          {homeLoading ? (
            <LearnerSkeleton variant="card-grid" />
          ) : papers.length > 0 ? (
            <ul className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              {papers.map((paper) => (
                <li key={paper.id}>
                  <PaperCard paper={paper} />
                </li>
              ))}
            </ul>
          ) : (
            <div className="rounded-2xl border border-dashed border-border bg-surface px-4 py-6 text-sm text-muted">
              No full listening exams are published yet. Your tutor will add papers here as they become
              available — meanwhile you can practise Part&nbsp;A, B, or C above.
            </div>
          )}
        </section>

        {recentResults.length > 0 ? (
          <section aria-labelledby="listening-results-heading">
            <div className="mb-4">
              <p className="flex items-center gap-1.5 text-xs font-semibold uppercase tracking-widest text-violet-500">
                <TrendingUp className="h-3.5 w-3.5" aria-hidden />
                Review
              </p>
              <h2 id="listening-results-heading" className="text-lg font-bold text-navy">
                Recent results
              </h2>
            </div>
            <ul className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
              {recentResults.slice(0, 6).map((result) => (
                <li key={result.attemptId}>
                  <ResultCard result={result} />
                </li>
              ))}
            </ul>
          </section>
        ) : null}
      </main>
    </LearnerDashboardShell>
  );
}

function PaperCard({ paper }: { paper: ListeningHomePaperDto }) {
  const locked = paper.requiresSubscription === true;
  return (
    <Link
      href={paper.route}
      className="group flex h-full items-start gap-4 rounded-2xl border border-border bg-surface p-5 transition-shadow hover:border-violet-300 hover:shadow-md"
    >
      <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-amber-100 text-amber-800">
        <Headphones className="h-5 w-5" aria-hidden />
      </div>
      <div className="flex-1">
        <div className="flex items-center justify-between gap-2 flex-wrap">
          <h3 className="text-sm font-bold text-navy">{paper.title}</h3>
          {locked ? (
            <span className="inline-flex items-center gap-1 rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide text-amber-900">
              <Lock className="h-3 w-3" aria-hidden />
              Premium
            </span>
          ) : (
            <span className="rounded-full bg-emerald-100 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide text-emerald-800">
              Full exam
            </span>
          )}
        </div>
        <p className="mt-1 flex items-center gap-2 text-xs text-muted">
          <span>{paper.questionCount} questions</span>
          <span aria-hidden>·</span>
          <span className="inline-flex items-center gap-1">
            <Clock className="h-3.5 w-3.5" aria-hidden />
            {paper.estimatedDurationMinutes} min
          </span>
        </p>
      </div>
      <ArrowRight
        className="h-4 w-4 self-center text-violet-400 opacity-0 transition-opacity group-hover:opacity-100"
        aria-hidden
      />
    </Link>
  );
}

function ResultCard({ result }: { result: ListeningHomeResultDto }) {
  return (
    <Link
      href={result.route}
      className="block rounded-2xl border border-border bg-surface p-4 text-sm transition-colors hover:border-violet-300"
    >
      <span className="font-semibold text-navy">{result.paperTitle}</span>
      <span className="mt-1 block text-xs text-muted">{result.scoreDisplay}</span>
    </Link>
  );
}

function ResumeBanner({ attempts }: { attempts: ListeningHomeAttemptDto[] }) {
  const resumable = attempts[0];
  if (!resumable) return null;

  return (
    <div className="rounded-xl border border-emerald-200 bg-emerald-50 px-5 py-4 dark:border-emerald-700 dark:bg-emerald-900/20">
      <p className="mb-1 flex items-center gap-2 text-sm font-semibold text-emerald-800 dark:text-emerald-200">
        <Clock className="h-4 w-4" aria-hidden />
        You have an open Listening attempt
      </p>
      <p className="mb-3 text-xs text-emerald-700/80 dark:text-emerald-300/70">
        {resumable.paperTitle}: {resumable.answeredCount} answered. Resume before the timer window closes.
      </p>
      <Link
        href={resumable.route}
        className="inline-flex items-center gap-2 rounded-lg bg-emerald-600 px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-emerald-700"
      >
        <ListChecks className="h-4 w-4" aria-hidden />
        Resume attempt
      </Link>
    </div>
  );
}
