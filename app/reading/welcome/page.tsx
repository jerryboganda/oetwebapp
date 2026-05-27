'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { BookOpen, ClipboardList, Dumbbell, FileCheck2, Star, Trophy } from 'lucide-react';
import { useAuth } from '@/contexts/auth-context';
import { getReadingProfile } from '@/lib/reading-pathway-api';

const JOURNEY_STEPS = [
  {
    icon: ClipboardList,
    label: 'Diagnostic',
    description: 'A short assessment to understand your current level',
  },
  {
    icon: BookOpen,
    label: 'Foundation',
    description: 'Build core reading strategies with guided lessons',
  },
  {
    icon: Dumbbell,
    label: 'Practice',
    description: 'Targeted drills across all three OET parts',
  },
  {
    icon: FileCheck2,
    label: 'Mock Tests',
    description: 'Full timed papers under real exam conditions',
  },
  {
    icon: Trophy,
    label: 'Mastery',
    description: 'Achieve your target band and book with confidence',
  },
];

export default function ReadingWelcomePage() {
  const { isAuthenticated, loading: authLoading } = useAuth();
  const router = useRouter();
  const [checking, setChecking] = useState(true);

  useEffect(() => {
    if (authLoading) return;

    if (!isAuthenticated) {
      router.replace('/sign-in');
      return;
    }

    (async () => {
      try {
        const profile = await getReadingProfile();
        if (profile && profile.currentStage !== 'onboarding') {
          router.replace('/reading');
          return;
        }
      } catch {
        // No profile yet — stay on welcome page
      } finally {
        setChecking(false);
      }
    })();
  }, [authLoading, isAuthenticated, router]);

  if (authLoading || checking) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-white">
        <div className="h-8 w-8 animate-spin rounded-full border-4 border-violet-200 border-t-violet-600" />
      </div>
    );
  }

  return (
    <div className="flex min-h-screen flex-col items-center justify-center bg-gradient-to-b from-violet-50 to-white dark:from-slate-900 dark:to-slate-950 px-4 py-16">
      <div className="w-full max-w-2xl">
        {/* Hero */}
        <div className="mb-10 text-center">
          <div className="mb-4 inline-flex h-16 w-16 items-center justify-center rounded-2xl bg-violet-600 shadow-lg">
            <Star className="h-8 w-8 text-white" aria-hidden />
          </div>
          <h1 className="mb-3 text-4xl font-extrabold tracking-tight text-gray-900">
            Master OET Reading
          </h1>
          <p className="text-lg text-gray-600">
            Your personalised 12-week pathway to OET success
          </p>
        </div>

        {/* Journey steps */}
        <div className="mb-10 rounded-2xl border border-violet-100 bg-white p-6 shadow-sm">
          <p className="mb-5 text-sm font-semibold uppercase tracking-widest text-violet-500">
            Your journey
          </p>
          <ol className="space-y-4">
            {JOURNEY_STEPS.map((step, index) => {
              const Icon = step.icon;
              return (
                <li key={step.label} className="flex items-start gap-4">
                  <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-violet-100 text-violet-700 font-bold text-sm">
                    {index + 1}
                  </div>
                  <div className="flex items-start gap-3">
                    <Icon className="mt-0.5 h-5 w-5 shrink-0 text-violet-500" aria-hidden />
                    <div>
                      <p className="font-semibold text-gray-900">{step.label}</p>
                      <p className="text-sm text-gray-500">{step.description}</p>
                    </div>
                  </div>
                </li>
              );
            })}
          </ol>
        </div>

        {/* CTA */}
        <div className="text-center">
          <button
            type="button"
            onClick={() => router.push('/reading/profile-setup')}
            className="inline-flex items-center gap-2 rounded-full bg-violet-600 px-10 py-4 text-base font-bold tracking-wide text-white shadow-lg transition hover:bg-violet-700 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:ring-offset-2 active:scale-95"
          >
            LET&apos;S BEGIN
          </button>
          <p className="mt-4 text-sm text-gray-400">Takes about 5 minutes to set up</p>
        </div>
      </div>
    </div>
  );
}
