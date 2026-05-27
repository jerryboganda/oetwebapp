'use client';

import { useEffect, useMemo } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import {
  ArrowRight,
  CalendarDays,
  Headphones,
  PlayCircle,
  Target,
  TrendingUp,
} from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { LearnerSkillSwitcher } from '@/components/domain/learner-skill-switcher';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { InlineAlert } from '@/components/ui/alert';
import { useAuth } from '@/contexts/auth-context';
import { analytics } from '@/lib/analytics';
import { useListeningProfile } from '@/hooks/useListeningProfile';

// Per the 2026-05-27 OET sample-test alignment directive, the Listening hub
// shows exactly four candidate-facing entries — three Practice-by-Part cards
// (A / B / C) and one Full Listening Exam card. The Pathway, Drills, Lessons,
// Strategies, Pronunciation, Dictation, Accent Training, and "Your audio
// context" surfaces remain on disk and are still reachable by direct URL for
// admin/QA, but they are intentionally hidden from the learner's primary path.
interface HubCard {
  title: string;
  subtitle: string;
  href: string;
  accent: 'partA' | 'partB' | 'partC' | 'exam';
}

const HUB_CARDS: HubCard[] = [
  {
    title: 'Practice Part A',
    subtitle: 'Patient consultations — note-taking from two consultations (24 items).',
    href: '/listening/practice/a',
    accent: 'partA',
  },
  {
    title: 'Practice Part B',
    subtitle: 'Workplace extracts — six short workplace audio extracts (6 items).',
    href: '/listening/practice/b',
    accent: 'partB',
  },
  {
    title: 'Practice Part C',
    subtitle: 'Healthcare presentations — two longer extracts with detailed questions (12 items).',
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
  const { profile, isLoading, error } = useListeningProfile();

  useEffect(() => {
    if (!authLoading && isAuthenticated) {
      analytics.track('module_entry', { module: 'listening' });
    }
  }, [authLoading, isAuthenticated]);

  // Stage-driven routing — push the learner to the appropriate next step.
  useEffect(() => {
    if (authLoading || isLoading) return;
    if (!isAuthenticated) {
      router.replace('/sign-in');
      return;
    }
    if (!profile) return;
    if (profile.currentStage === 'onboarding') {
      router.replace('/listening/welcome');
      return;
    }
    if (profile.currentStage === 'audio_check') {
      router.replace('/listening/audio-check');
      return;
    }
  }, [authLoading, isLoading, isAuthenticated, profile, router]);

  const daysToExam: number | null = useMemo(() => {
    if (!profile?.examDate) return null;
    const diff = new Date(profile.examDate).getTime() - Date.now();
    return Math.max(0, Math.ceil(diff / (1000 * 60 * 60 * 24)));
  }, [profile]);

  const heroHighlights = useMemo(
    () => [
      {
        icon: Target,
        label: 'Target band',
        value: profile?.targetBand ? `OET ${profile.targetBand}` : 'Not set',
      },
      {
        icon: TrendingUp,
        label: 'Readiness',
        value:
          profile?.currentReadinessScore !== null && profile?.currentReadinessScore !== undefined
            ? `${profile.currentReadinessScore}%`
            : 'Diagnostic pending',
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
    [profile, daysToExam],
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

  if (!isLoading && !profile && !error) {
    return (
      <LearnerDashboardShell pageTitle="Listening">
        <main className="space-y-8">
          <LearnerPageHero
            eyebrow="Module focus"
            icon={Headphones}
            accent="purple"
            title="Welcome to the OET Listening module"
            description="Set up your personalised pathway in under five minutes."
          />
          <div className="rounded-2xl border border-violet-100 bg-violet-50 p-8 text-center shadow-sm">
            <p className="text-lg font-bold text-violet-900">Get started</p>
            <p className="mt-1 text-sm text-violet-700">
              We&apos;ll ask a few quick questions, run an audio check, and tailor your plan.
            </p>
            <Link
              href="/listening/welcome"
              className="mt-5 inline-flex items-center gap-2 rounded-full bg-violet-600 px-8 py-3 text-sm font-bold text-white shadow-md transition hover:bg-violet-700"
            >
              Start your listening journey
              <ArrowRight className="h-4 w-4" aria-hidden />
            </Link>
          </div>
        </main>
      </LearnerDashboardShell>
    );
  }

  if (error) {
    return (
      <LearnerDashboardShell pageTitle="Listening">
        <InlineAlert variant="error">
          Could not load your listening profile. Please refresh the page or try again later.
        </InlineAlert>
      </LearnerDashboardShell>
    );
  }

  if (isLoading || !profile) {
    return (
      <LearnerDashboardShell pageTitle="Listening">
        <LearnerSkeleton variant="dashboard" />
      </LearnerDashboardShell>
    );
  }

  const needsDiagnostic =
    profile.currentStage === 'diagnostic' || profile.currentStage === 'audio_check';

  return (
    <LearnerDashboardShell pageTitle="Listening">
      <main className="space-y-10">
        {needsDiagnostic && (
          <div className="rounded-xl border border-blue-200 bg-blue-50 px-5 py-4 dark:border-blue-700 dark:bg-blue-900/20">
            <p className="mb-1 text-sm font-semibold text-blue-800 dark:text-blue-200">
              Start with your listening diagnostic
            </p>
            <p className="mb-3 text-xs text-blue-700/70 dark:text-blue-300/70">
              A short diagnostic calibrates your sub-skills, accents, and recommended plan.
            </p>
            <Link
              href="/listening/diagnostic"
              className="inline-flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-blue-700"
            >
              Take the diagnostic
              <ArrowRight className="h-4 w-4" aria-hidden />
            </Link>
          </div>
        )}

        <LearnerPageHero
          eyebrow="Module focus"
          icon={Headphones}
          accent="purple"
          title="OET Listening"
          description="Practice each part separately or attempt the full listening exam under official timing."
          highlights={heroHighlights}
        />

        <LearnerSkillSwitcher compact />

        <section aria-labelledby="listening-hub-heading">
          <div className="mb-4">
            <p className="text-xs font-semibold uppercase tracking-widest text-violet-500">
              Choose how to practice
            </p>
            <h2 id="listening-hub-heading" className="text-lg font-bold text-gray-900 dark:text-gray-100">
              Practice by Part, or attempt the full exam
            </h2>
          </div>

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
                    className={`group relative flex h-full items-start gap-4 rounded-2xl border bg-white p-5 transition-shadow hover:shadow-md dark:bg-gray-900 ${accent.ring}`}
                  >
                    <div className={`flex h-11 w-11 shrink-0 items-center justify-center rounded-xl ${accent.icon}`}>
                      <Headphones className="h-5 w-5" aria-hidden />
                    </div>
                    <div className="flex-1">
                      <div className="flex items-center justify-between gap-2 flex-wrap">
                        <h3 className="text-sm font-bold text-gray-900 dark:text-gray-100">
                          {card.title}
                        </h3>
                        <span className={`rounded-full px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide ${accent.badge}`}>
                          {accent.chip}
                        </span>
                      </div>
                      <p className="mt-1 text-sm text-gray-500 dark:text-gray-400">{card.subtitle}</p>
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
      </main>
    </LearnerDashboardShell>
  );
}
