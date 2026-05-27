'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';

interface MockTemplate {
  id: string;
  title: string;
  difficulty: number;
  durationSeconds: number;
}

async function listMocks(): Promise<MockTemplate[]> {
  const res = await fetch('/api/proxy?path=/v1/listening-pathway/mocks');
  if (!res.ok) {
    // Fallback: direct API endpoint (cookie-based auth in dev).
    const alt = await fetch('/v1/listening-pathway/mocks').catch(() => null);
    if (alt && alt.ok) return (await alt.json()) as MockTemplate[];
    return [];
  }
  return (await res.json()) as MockTemplate[];
}

async function startMock(mockTemplateId: string) {
  const res = await fetch('/v1/listening-pathway/mocks/start', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ MockTemplateId: mockTemplateId }),
  });
  if (!res.ok) throw new Error(`Failed to start mock (${res.status})`);
  return (await res.json()) as { sessionId: string; totalQuestions: number; durationSeconds: number };
}

export default function ListeningMocksPage() {
  const [mocks, setMocks] = useState<MockTemplate[]>([]);
  const [loading, setLoading] = useState(true);
  const router = useRouter();

  useEffect(() => {
    let cancelled = false;
    listMocks()
      .then((m) => {
        if (!cancelled) {
          setMocks(m);
          setLoading(false);
        }
      })
      .catch(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  async function handleStart(id: string) {
    try {
      const { sessionId } = await startMock(id);
      router.push(`/listening/mocks/${sessionId}`);
    } catch (e) {
      alert(e instanceof Error ? e.message : 'Could not start mock');
    }
  }

  return (
    <main className="mx-auto max-w-5xl px-4 py-12 space-y-8">
      <header>
        <h1 className="text-3xl font-bold tracking-tight">Full Mock Listening Tests</h1>
        <p className="mt-2 text-slate-600">
          45 minutes • 42 questions • Audio plays once. Score scaled to OET 0–500.
        </p>
      </header>

      {loading ? (
        <p className="text-slate-500">Loading available mocks…</p>
      ) : mocks.length === 0 ? (
        <div className="rounded-2xl border border-amber-200 bg-amber-50 p-6 text-amber-900">
          <p className="font-semibold">No mocks available yet.</p>
          <p className="mt-2 text-sm">
            Mock tests will unlock once you complete the diagnostic and finish a few foundation lessons.
          </p>
          <Link
            href="/listening"
            className="mt-4 inline-block rounded-md bg-amber-900 px-4 py-2 text-white text-sm"
          >
            Back to dashboard
          </Link>
        </div>
      ) : (
        <ul className="grid gap-4 md:grid-cols-2">
          {mocks.map((m) => (
            <li
              key={m.id}
              className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm flex flex-col"
            >
              <h2 className="text-lg font-semibold">{m.title}</h2>
              <p className="mt-1 text-sm text-slate-500">
                Difficulty: {'⭐'.repeat(Math.max(1, Math.min(5, m.difficulty)))}
              </p>
              <p className="mt-1 text-sm text-slate-500">
                Duration: {Math.round(m.durationSeconds / 60)} min
              </p>
              <button
                onClick={() => handleStart(m.id)}
                className="mt-4 self-start rounded-md bg-slate-900 px-4 py-2 text-white text-sm"
              >
                Start mock →
              </button>
            </li>
          ))}
        </ul>
      )}
    </main>
  );
}
