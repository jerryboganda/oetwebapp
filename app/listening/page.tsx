'use client';

import { useEffect, useMemo } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import {
  ArrowRight,
  CalendarDays,
  Clock,
  Headphones,
  ListChecks,
  MapPin,
  Mic,
  PlayCircle,
  Sparkles,
  Target,
  TrendingUp,
  Volume2,
} from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { LearnerSkillSwitcher } from '@/components/domain/learner-skill-switcher';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { InlineAlert } from '@/components/ui/alert';
import { useAuth } from '@/contexts/auth-context';
import { analytics } from '@/lib/analytics';
import { useListeningProfile } from '@/hooks/useListeningProfile';

interface QuickLink {
  title: string;
  description: string;
  href: string;
  icon: typeof Headphones;
  comingSoon?: boolean;
}

const QUICK_LINKS: QuickLink[] = [
  {
    title: 'Pathway',
    description: 'Your personalised 12-week roadmap.',
    href: '/listening/pathway',
    icon: MapPin,
  },
  {
    title: 'Accent training',
    description: 'Targeted drills for British, Australian, and varied accents.',
    href: '/listening',
    icon: Headphones,
    comingSoon: true,
  },
  {
    title: 'Dictation',
    description: 'Build detail capture with short dictation exercises.',
    href: '/listening',
    icon: ListChecks,
    comingSoon: true,
  },
  {
    title: 'Pronunciation',
    description: 'Tune your ear with paired listen-and-repeat practice.',
    href: '/listening',
    icon: Mic,
    comingSoon: true,
  },
  {
    title: 'Strategies',
    description: 'Lessons for note-taking, gist, and distractor recognition.',
    href: '/listening',
    icon: Sparkles,
    comingSoon: true,
  },
];

export default function ListeningHome() {
  const { isAuthenticated, loading: authLoading } = useAuth();
  const router = useRouter();
  const { profile, isLoading, error } = useListeningProfile();

  // Analytics
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

  // Loading / unauthenticated guards
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

  // No profile yet — show welcome CTA
  if (!isLoading && !profile && !error) {
    return (
      <LearnerDashboardShell pageTitle="Listening">
        <main className="space-y-8">
          <LearnerPageHero
            eyebrow="Module focus"
            icon={Headphones}
            accent="purple"
            title="Welcome to the OET Listening module"
            description="Set up your personalised 12-week pathway in under five minutes."
          />
          <div className="rounded-2xl border border-violet-100 bg-violet-50 p-8 text-center shadow-sm">
            <Sparkles className="mx-auto mb-3 h-8 w-8 text-violet-600" aria-hidden />
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

  // Diagnostic stage — primary CTA is to take the diagnostic
  const needsDiagnostic =
    profile.currentStage === 'diagnostic' || profile.currentStage === 'audio_check';

  return (
    <LearnerDashboardShell pageTitle="Listening">
      <main className="space-y-10">
        {/* Stage-gated banner */}
        {needsDiagnostic && (
          <div className="rounded-xl border border-blue-200 bg-blue-50 px-5 py-4 dark:border-blue-700 dark:bg-blue-900/20">
            <p className="mb-1 text-sm font-semibold text-blue-800 dark:text-blue-200">
              Start with your listening diagnostic
            </p>
            <p className="mb-3 text-xs text-blue-700/70 dark:text-blue-300/70">
              A 30-minute diagnostic calibrates your sub-skills, accents, and 12-week plan.
            </p>
            <Link
              href="/listening/diagnostic"
              className="inline-flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-blue-700"
            >
              Take the diagnostic (30 min)
              <ArrowRight className="h-4 w-4" aria-hidden />
            </Link>
          </div>
        )}

        <LearnerPageHero
          eyebrow="Module focus"
          icon={Headphones}
          accent="purple"
          title="Train every part of OET Listening"
          description="Sub-skill drills, accent practice, and full mock tests under exam timing."
          highlights={heroHighlights}
        />

        <LearnerSkillSwitcher compact />

        {/* Today's plan placeholder */}
        <section className="rounded-2xl border border-gray-100 bg-white p-6 shadow-sm dark:border-gray-800 dark:bg-gray-900">
          <div className="mb-3 flex items-center justify-between gap-3 flex-wrap">
            <div>
              <p className="text-xs font-semibold uppercase tracking-widest text-violet-500">
                Today
              </p>
              <h2 className="text-lg font-bold text-gray-900 dark:text-gray-100">Your daily plan</h2>
            </div>
            <span className="rounded-full bg-violet-100 px-3 py-1 text-xs font-bold text-violet-700">
              Coming in Phase 3
            </span>
          </div>
          <div className="rounded-xl border border-dashed border-gray-200 bg-gray-50 p-6 text-center text-sm text-gray-500 dark:border-gray-700 dark:bg-gray-800/50 dark:text-gray-400">
            <Clock className="mx-auto mb-2 h-6 w-6" aria-hidden />
            <p>Your daily plan and adaptive drills will appear here once Phase 3 lands.</p>
            <p className="mt-1 text-xs">
              For now, head into your pathway to see the full 12-week schedule.
            </p>
            <Link
              href="/listening/pathway"
              className="mt-3 inline-flex items-center gap-1 text-sm font-bold text-violet-700 hover:underline"
            >
              View pathway
              <ArrowRight className="h-3.5 w-3.5" aria-hidden />
            </Link>
          </div>
        </section>

        {/* Quick links to other listening surfaces */}
        <section>
          <div className="mb-4">
            <p className="text-xs font-semibold uppercase tracking-widest text-violet-500">
              Explore
            </p>
            <h2 className="text-lg font-bold text-gray-900 dark:text-gray-100">Listening modules</h2>
          </div>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {QUICK_LINKS.map((link) => {
              const Icon = link.icon;
              return (
                <Link
                  key={link.title}
                  href={link.href}
                  aria-disabled={link.comingSoon}
                  className={`group relative flex items-start gap-3 rounded-2xl border p-5 transition-shadow ${
                    link.comingSoon
                      ? 'border-gray-200 bg-gray-50 opacity-70 dark:border-gray-700 dark:bg-gray-800/40'
                      : 'border-violet-100 bg-white hover:shadow-md dark:border-gray-800 dark:bg-gray-900'
                  }`}
                >
                  <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-violet-100 text-violet-700">
                    <Icon className="h-5 w-5" aria-hidden />
                  </div>
                  <div className="flex-1">
                    <div className="flex items-center justify-between gap-2 flex-wrap">
                      <h3 className="text-sm font-bold text-gray-900 dark:text-gray-100">
                        {link.title}
                      </h3>
                      {link.comingSoon && (
                        <span className="rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide text-amber-800">
                          Soon
                        </span>
                      )}
                    </div>
                    <p className="mt-1 text-sm text-gray-500 dark:text-gray-400">{link.description}</p>
                  </div>
                  {!link.comingSoon && (
                    <PlayCircle
                      className="h-4 w-4 text-violet-400 opacity-0 transition-opacity group-hover:opacity-100"
                      aria-hidden
                    />
                  )}
                </Link>
              );
            })}
          </div>
        </section>

        {/* Sub-skill snapshot */}
        <section className="rounded-2xl border border-gray-100 bg-white p-6 shadow-sm dark:border-gray-800 dark:bg-gray-900">
          <div className="mb-3 flex items-center gap-2">
            <Volume2 className="h-4 w-4 text-violet-600" aria-hidden />
            <h2 className="text-base font-bold text-gray-900 dark:text-gray-100">
              Your audio context
            </h2>
          </div>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
            <div className="rounded-xl bg-gray-50 p-3 dark:bg-gray-800/50">
              <p className="text-xs uppercase tracking-wide text-gray-500">British</p>
              <p className="mt-1 text-xl font-extrabold text-gray-900 dark:text-gray-100">
                {profile.comfortBritish}/5
              </p>
            </div>
            <div className="rounded-xl bg-gray-50 p-3 dark:bg-gray-800/50">
              <p className="text-xs uppercase tracking-wide text-gray-500">Australian</p>
              <p className="mt-1 text-xl font-extrabold text-gray-900 dark:text-gray-100">
                {profile.comfortAustralian}/5
              </p>
            </div>
            <div className="rounded-xl bg-gray-50 p-3 dark:bg-gray-800/50">
              <p className="text-xs uppercase tracking-wide text-gray-500">Various</p>
              <p className="mt-1 text-xl font-extrabold text-gray-900 dark:text-gray-100">
                {profile.comfortVarious}/5
              </p>
            </div>
          </div>
        </section>
      </main>
    </LearnerDashboardShell>
  );
}
