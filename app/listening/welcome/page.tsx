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
    description: '23 questions across Parts A, B, and C, mapping every sub-skill and accent.',
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
      <div className="flex min-h-screen items-center justify-center bg-background-light">
        <div className="h-8 w-8 animate-spin rounded-full border-4 border-lavender border-t-primary" />
      </div>
    );
  }

  return (
    <div className="flex min-h-screen flex-col items-center justify-center bg-background-light px-4 py-16">
      <div className="w-full max-w-2xl">
        {/* Hero */}
        <div className="mb-10 text-center">
          <Star className="mx-auto mb-4 h-10 w-10 text-primary" aria-hidden />
          <h1 className="mb-3 text-4xl font-extrabold tracking-tight text-navy">
            Master OET Listening
          </h1>
          <p className="text-lg text-muted">
            Your personalised 12-week pathway to OET Listening success
          </p>
        </div>

        {/* Headphones reminder */}
        <div className="mb-8 flex items-start gap-3 rounded-2xl border border-warning/30 bg-warning/10 p-5">
          <Volume2 className="mt-0.5 h-5 w-5 shrink-0 text-warning" aria-hidden />
          <div>
            <p className="font-semibold text-navy">Headphones recommended</p>
            <p className="mt-0.5 text-sm text-muted">
              For the best experience and to mirror exam conditions, please use over-ear
              headphones in a quiet space throughout the listening pathway.
            </p>
          </div>
        </div>

        {/* Journey steps */}
        <div className="mb-10 rounded-2xl border border-border bg-surface p-6 shadow-sm">
          <p className="mb-5 text-sm font-semibold uppercase tracking-widest text-primary">
            Your journey
          </p>
          <ol className="space-y-4">
            {JOURNEY_STEPS.map((step, index) => {
              const Icon = step.icon;
              return (
                <li key={step.label} className="flex items-start gap-4">
                  <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-lavender text-primary font-bold text-sm">
                    {index + 1}
                  </div>
                  <div className="flex items-start gap-3">
                    <Icon className="mt-0.5 h-5 w-5 shrink-0 text-primary" aria-hidden />
                    <div>
                      <p className="font-semibold text-navy">{step.label}</p>
                      <p className="text-sm text-muted">{step.description}</p>
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
            className="inline-flex items-center gap-2 rounded-full bg-primary px-10 py-4 text-base font-bold tracking-wide text-white shadow-lg transition-colors hover:bg-primary-dark dark:bg-violet-700 dark:hover:bg-violet-600 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 active:scale-95"
            aria-label="Start profile setup"
          >
            LET&apos;S GO
          </button>
          <p className="mt-4 text-sm text-muted">Takes about 5 minutes to set up</p>
        </div>
      </div>
    </div>
  );
}
