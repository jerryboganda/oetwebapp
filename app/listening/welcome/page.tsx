'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import {
  Headphones,
  ClipboardList,
  ActivitySquare,
  Dumbbell,
  FileCheck2,
  Trophy,
  Star,
  Volume2,
} from 'lucide-react';
import { useAuth } from '@/contexts/auth-context';
import { getListeningProfile } from '@/lib/listening-pathway-api';

const JOURNEY_STEPS = [
  {
    icon: ClipboardList,
    label: 'Profile setup',
    description: 'Tell us your target band, exam date, profession, and study habits.',
  },
  {
    icon: Headphones,
    label: 'Audio check',
    description: 'A quick playback test to confirm your headphones are exam-ready.',
  },
  {
    icon: ActivitySquare,
    label: 'Diagnostic',
    description: '23 questions across Parts A, B, and C — maps every sub-skill and accent.',
  },
  {
    icon: Dumbbell,
    label: 'Foundation & practice',
    description: 'Targeted lessons + adaptive drills focused on your weakest sub-skills.',
  },
  {
    icon: FileCheck2,
    label: 'Mock tests',
    description: 'Full timed listening mocks under real exam conditions.',
  },
  {
    icon: Trophy,
    label: 'Mastery',
    description: 'Land three consecutive passes and walk into exam day with confidence.',
  },
];

export default function ListeningWelcomePage() {
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
        const profile = await getListeningProfile();
        if (profile && profile.currentStage !== 'onboarding') {
          router.replace('/listening');
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
            Master OET Listening
          </h1>
          <p className="text-lg text-gray-600">
            Your personalised 12-week pathway to OET Listening success
          </p>
        </div>

        {/* Headphones reminder */}
        <div className="mb-8 flex items-start gap-3 rounded-2xl border border-amber-200 bg-amber-50 p-5">
          <Volume2 className="mt-0.5 h-5 w-5 shrink-0 text-amber-600" aria-hidden />
          <div>
            <p className="font-semibold text-amber-900">Headphones recommended</p>
            <p className="mt-0.5 text-sm text-amber-800">
              For the best experience and to mirror exam conditions, please use over-ear
              headphones in a quiet space throughout the listening pathway.
            </p>
          </div>
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
            onClick={() => router.push('/listening/profile-setup')}
            className="inline-flex items-center gap-2 rounded-full bg-violet-600 px-10 py-4 text-base font-bold tracking-wide text-white shadow-lg transition hover:bg-violet-700 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:ring-offset-2 active:scale-95"
            aria-label="Let's go — start profile setup"
          >
            LET&apos;S GO
          </button>
          <p className="mt-4 text-sm text-gray-400">Takes about 5 minutes to set up</p>
        </div>
      </div>
    </div>
  );
}
