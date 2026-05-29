'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { Headphones, Volume2, AlertTriangle, RotateCcw, LifeBuoy } from 'lucide-react';
import { useAuth } from '@/contexts/auth-context';
import { submitAudioCheck, type AudioCheckPayload } from '@/lib/listening-pathway-api';
import { AudioCheck } from '@/components/listening/AudioCheck';

type CheckPhase = 'intro' | 'checking' | 'result';
type CheckOutcome = AudioCheckPayload['outcome'];

const TROUBLESHOOTING_TIPS = [
  'Make sure your volume is turned up on both the device and your headphones.',
  'Try a different pair of headphones or switch from speakers to headphones.',
  'Close other apps that might be using your audio (calls, music, video).',
  'Check that the right output device is selected in your system settings.',
];

export default function ListeningAudioCheckPage() {
  const { isAuthenticated, loading: authLoading } = useAuth();
  const router = useRouter();

  const [phase, setPhase] = useState<CheckPhase>('intro');
  const [outcome, setOutcome] = useState<CheckOutcome | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!authLoading && !isAuthenticated) {
      router.replace('/sign-in');
    }
  }, [authLoading, isAuthenticated, router]);

  if (authLoading || !isAuthenticated) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-background-light">
        <div className="h-8 w-8 animate-spin rounded-full border-4 border-lavender border-t-primary" />
      </div>
    );
  }

  const handleResult = async (result: CheckOutcome, volumeLevel?: number) => {
    setOutcome(result);
    setSubmitting(true);
    setError(null);
    try {
      const payload: AudioCheckPayload = { outcome: result };
      if (typeof volumeLevel === 'number') payload.volumeLevel = volumeLevel;
      const response = await submitAudioCheck(payload);
      setPhase('result');
      if (response.success) {
        router.push('/listening/diagnostic');
      }
    } catch {
      setError('We could not save your audio check. Please try again.');
      setPhase('result');
    } finally {
      setSubmitting(false);
    }
  };

  const restart = () => {
    setOutcome(null);
    setError(null);
    setPhase('intro');
  };

  return (
    <div className="flex min-h-screen flex-col items-center justify-center bg-background-light px-4 py-16">
      <div className="w-full max-w-xl">
        {/* Intro */}
        {phase === 'intro' && (
          <div className="rounded-2xl border border-border bg-surface p-8 shadow-sm">
            <div className="mb-6 text-center">
              <Headphones className="mx-auto mb-3 h-8 w-8 text-primary" aria-hidden />
              <h1 className="text-2xl font-extrabold text-navy">Audio check</h1>
              <p className="mt-2 text-sm text-muted">
                Before your diagnostic, we&apos;ll play a short clip so you can confirm everything sounds
                clear.
              </p>
            </div>

            <ol className="mb-8 space-y-3 text-sm text-muted">
              <li className="flex gap-3">
                <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-lavender text-xs font-bold text-primary">
                  1
                </span>
                <span>Put on your headphones and find a quiet spot.</span>
              </li>
              <li className="flex gap-3">
                <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-lavender text-xs font-bold text-primary">
                  2
                </span>
                <span>Set your device volume to a comfortable mid-level.</span>
              </li>
              <li className="flex gap-3">
                <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-lavender text-xs font-bold text-primary">
                  3
                </span>
                <span>Play the clip and tell us how it sounds.</span>
              </li>
            </ol>

            <button
              type="button"
              onClick={() => setPhase('checking')}
              className="w-full rounded-xl bg-primary py-4 text-base font-bold text-white transition-colors hover:bg-primary-dark dark:bg-violet-700 dark:hover:bg-violet-600 active:scale-95"
            >
              Start audio check
            </button>
          </div>
        )}

        {/* Checking */}
        {phase === 'checking' && (
          <div className="rounded-2xl border border-border bg-surface p-8 shadow-sm">
            <h1 className="mb-6 text-center text-xl font-extrabold text-navy">
              Play the clip and pick the result
            </h1>
            <AudioCheck
              onResult={handleResult}
              disabled={submitting}
            />
            {submitting && (
              <p className="mt-4 flex items-center justify-center gap-2 text-sm text-muted">
                <span className="h-4 w-4 animate-spin rounded-full border-2 border-lavender border-t-primary" />
                Saving your result…
              </p>
            )}
          </div>
        )}

        {/* Result */}
        {phase === 'result' && outcome !== null && (
          <div className="rounded-2xl border border-border bg-surface p-8 shadow-sm">
            {outcome === 'clear' && (
              <div className="text-center">
                <Volume2 className="mx-auto mb-4 h-8 w-8 text-success" aria-hidden />
                <h2 className="text-2xl font-extrabold text-navy">All clear</h2>
                <p className="mt-2 text-sm text-muted">
                  Audio is working. Taking you to the diagnostic…
                </p>
              </div>
            )}

            {(outcome === 'quiet' || outcome === 'failed') && (
              <div>
                <div className="mb-6 text-center">
                  <AlertTriangle className="mx-auto mb-3 h-8 w-8 text-warning" aria-hidden />
                  <h2 className="text-2xl font-extrabold text-navy">
                    {outcome === 'quiet' ? 'Audio seems quiet' : 'Audio not working'}
                  </h2>
                  <p className="mt-2 text-sm text-muted">
                    Listening relies on clear audio. Let&apos;s fix this before you start the diagnostic.
                  </p>
                </div>

                <div className="mb-6 rounded-xl border border-warning/20 bg-warning/10 p-5">
                  <p className="mb-3 text-sm font-semibold text-navy">Try the following:</p>
                  <ul className="space-y-2 text-sm text-muted">
                    {TROUBLESHOOTING_TIPS.map((tip) => (
                      <li key={tip} className="flex gap-2">
                        <span aria-hidden>•</span>
                        <span>{tip}</span>
                      </li>
                    ))}
                  </ul>
                </div>

                <div className="flex flex-col gap-3 sm:flex-row">
                  <button
                    type="button"
                    onClick={restart}
                    className="flex flex-1 items-center justify-center gap-2 rounded-xl bg-primary py-3 text-sm font-bold text-white transition-[color,background-color,transform] duration-200 hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600"
                  >
                    <RotateCcw className="h-4 w-4" aria-hidden />
                    Retry audio check
                  </button>
                  <a
                    href="mailto:support@oetlearner.com"
                    className="flex flex-1 items-center justify-center gap-2 rounded-xl border border-border py-3 text-sm font-semibold text-navy transition-colors hover:bg-background-light"
                  >
                    <LifeBuoy className="h-4 w-4" aria-hidden />
                    Contact support
                  </a>
                </div>
              </div>
            )}

            {error && (
              <p
                className="mt-4 rounded-xl border border-danger/20 bg-danger/10 px-4 py-3 text-sm text-danger"
                role="alert"
              >
                {error}
              </p>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
