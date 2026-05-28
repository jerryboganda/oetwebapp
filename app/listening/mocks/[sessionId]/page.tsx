'use client';

import { useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';

export default function ListeningMockSessionPage() {
  const params = useParams<{ sessionId: string }>();
  const sessionId = params?.sessionId ?? '';
  const router = useRouter();
  const [secondsLeft, setSecondsLeft] = useState(45 * 60);
  const [confirmedStart, setConfirmedStart] = useState(false);

  useEffect(() => {
    if (!confirmedStart) return;
    const t = setInterval(() => {
      setSecondsLeft((s) => Math.max(0, s - 1));
    }, 1000);
    return () => clearInterval(t);
  }, [confirmedStart]);

  useEffect(() => {
    if (confirmedStart && secondsLeft === 0) {
      // Auto-submit on timeout — route to results.
      router.push(`/listening/mocks/${sessionId}/results`);
    }
  }, [confirmedStart, secondsLeft, sessionId, router]);

  const minutes = Math.floor(secondsLeft / 60);
  const seconds = secondsLeft % 60;

  if (!confirmedStart) {
    return (
      <main className="mx-auto max-w-2xl px-4 py-12 space-y-6">
        <h1 className="text-3xl font-bold tracking-tight">Full Mock Listening Test</h1>
        <ul className="space-y-2 text-slate-700">
          <li>⏱ 45 minutes — no pauses</li>
          <li>🎧 Audio plays ONCE only</li>
          <li>📝 42 questions (Part A: 24 / Part B: 6 / Part C: 12)</li>
        </ul>
        <p className="rounded-md bg-amber-50 p-4 text-amber-900 text-sm">
          This counts toward your readiness score. Close other tabs, put on headphones, and find a
          quiet 45 minutes.
        </p>
        <button
          onClick={() => setConfirmedStart(true)}
          className="rounded-md bg-slate-900 px-6 py-3 text-white text-sm font-semibold"
        >
          I am ready — Begin
        </button>
      </main>
    );
  }

  return (
    <main className="mx-auto max-w-4xl px-4 py-8 space-y-6">
      <header className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Mock in progress</h1>
        <span className="font-mono text-lg">
          {minutes}:{seconds.toString().padStart(2, '0')}
        </span>
      </header>
      <div className="rounded-2xl border border-slate-200 bg-white p-8 text-center">
        <p className="text-slate-700">
          The strict full-length Audio Player ships in Phase 2 of the Listening module rollout. For
          now this stub records that the mock was started so the grading + readiness pipeline can be
          tested end-to-end.
        </p>
        <button
          onClick={() => router.push(`/listening/mocks/${sessionId}/results`)}
          className="mt-6 rounded-md bg-slate-900 px-4 py-2 text-white text-sm"
        >
          Submit mock now →
        </button>
      </div>
    </main>
  );
}
