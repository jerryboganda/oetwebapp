'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { notFound, useParams, useRouter } from 'next/navigation';
import { ArrowLeft, Headphones } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { InlineAlert } from '@/components/ui/alert';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { LearnerPageHero } from '@/components/domain';
import { analytics } from '@/lib/analytics';

// Per the 2026-05-27 OET sample-test alignment, `/listening/practice/[part]`
// is a thin candidate dispatcher: it surfaces all available listening papers
// or mocks and lets the candidate start a part-scoped practice session by
// handing `?part=<A|B|C>` to the existing `/listening/player/[id]` runner.
// Pages outside this clean entry point (drills, lessons, strategies,
// pronunciation, dictation) remain on disk and reachable by URL but are
// intentionally hidden from the candidate's primary path.

type PartCode = 'A' | 'B' | 'C';

const PART_DETAILS: Record<PartCode, { title: string; subtitle: string; description: string }> = {
  A: {
    title: 'Practice Part A',
    subtitle: 'Patient consultations',
    description:
      'Two consultations between a healthcare professional and a patient. You take notes while you listen and answer 24 short-response items.',
  },
  B: {
    title: 'Practice Part B',
    subtitle: 'Workplace extracts',
    description:
      'Six short workplace audio extracts. You answer one three-option multiple-choice question per extract (6 items total).',
  },
  C: {
    title: 'Practice Part C',
    subtitle: 'Healthcare presentations',
    description:
      'Two longer extracts — an interview or presentation on a healthcare topic. You answer six four-option multiple-choice questions per extract (12 items total).',
  },
};

interface MockTemplate {
  id: string;
  title: string;
  difficulty?: number;
  durationSeconds?: number;
}

async function listMocks(): Promise<MockTemplate[]> {
  try {
    const res = await fetch('/api/proxy?path=/v1/listening-pathway/mocks');
    if (res.ok) return (await res.json()) as MockTemplate[];
  } catch {
    /* fall through */
  }
  try {
    const alt = await fetch('/v1/listening-pathway/mocks');
    if (alt.ok) return (await alt.json()) as MockTemplate[];
  } catch {
    /* ignore */
  }
  return [];
}

async function startMock(mockTemplateId: string): Promise<{ sessionId: string }> {
  const res = await fetch('/v1/listening-pathway/mocks/start', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ MockTemplateId: mockTemplateId }),
  });
  if (!res.ok) throw new Error(`Failed to start practice session (${res.status})`);
  return (await res.json()) as { sessionId: string };
}

function normalisePart(raw: string | undefined): PartCode | null {
  if (!raw) return null;
  const upper = raw.toUpperCase();
  return upper === 'A' || upper === 'B' || upper === 'C' ? upper : null;
}

export default function ListeningPartPracticePage() {
  const params = useParams<{ part?: string | string[] }>();
  const raw = Array.isArray(params?.part) ? params?.part?.[0] : params?.part;
  const part = normalisePart(raw);
  if (!part) {
    notFound();
  }

  const router = useRouter();
  const [mocks, setMocks] = useState<MockTemplate[]>([]);
  const [loading, setLoading] = useState(true);
  const [startingId, setStartingId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!part) return;
    analytics.track('content_view', { page: 'listening-part-practice', part });
    let cancelled = false;
    listMocks()
      .then((list) => {
        if (!cancelled) {
          setMocks(list);
          setLoading(false);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setLoading(false);
        }
      });
    return () => {
      cancelled = true;
    };
  }, [part]);

  if (!part) return null;

  const meta = PART_DETAILS[part];

  async function handleStart(id: string) {
    if (!part) return;
    setStartingId(id);
    setError(null);
    try {
      const { sessionId } = await startMock(id);
      router.push(`/listening/player/${sessionId}?mode=practice&part=${part}`);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Could not start practice session.');
      setStartingId(null);
    }
  }

  return (
    <LearnerDashboardShell pageTitle={meta.title}>
      <main className="space-y-8" data-testid={`listening-part-${part}-dispatcher`}>
        <Link
          href="/listening"
          className="inline-flex items-center gap-2 text-sm font-semibold text-violet-700 hover:underline"
        >
          <ArrowLeft className="h-4 w-4" aria-hidden />
          Back to Listening
        </Link>

        <LearnerPageHero
          eyebrow={`Part ${part}`}
          icon={Headphones}
          accent="purple"
          title={meta.title}
          description={meta.description}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {loading ? (
          <LearnerSkeleton variant="card-grid" />
        ) : mocks.length === 0 ? (
          <InlineAlert variant="info">
            No practice papers are available yet. Complete the diagnostic to unlock part-scoped
            practice, or check back soon.
          </InlineAlert>
        ) : (
          <section aria-label={`Available Part ${part} practice papers`}>
            <div className="mb-3">
              <h2 className="text-base font-bold text-gray-900 dark:text-gray-100">
                Pick a paper for Part {part} practice
              </h2>
              <p className="mt-1 text-sm text-gray-500">
                Each paper boots the listening player in practice mode. {meta.subtitle} only —
                other parts are skipped.
              </p>
            </div>
            <ul className="grid gap-4 sm:grid-cols-2">
              {mocks.map((mock) => {
                const minutes = mock.durationSeconds ? Math.round(mock.durationSeconds / 60) : null;
                return (
                  <li key={mock.id}>
                    <article className="flex h-full flex-col rounded-2xl border border-violet-100 bg-white p-5 shadow-sm dark:border-gray-800 dark:bg-gray-900">
                      <h3 className="text-base font-bold text-gray-900 dark:text-gray-100">
                        {mock.title}
                      </h3>
                      <p className="mt-1 text-xs text-gray-500">
                        {mock.difficulty
                          ? `Difficulty ${Math.max(1, Math.min(5, mock.difficulty))}/5`
                          : 'Standard difficulty'}
                        {minutes ? ` • ${minutes} min full paper` : ''}
                      </p>
                      <div className="mt-auto pt-4">
                        <button
                          type="button"
                          disabled={startingId === mock.id}
                          onClick={() => handleStart(mock.id)}
                          className="rounded-md bg-violet-600 px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-violet-700 disabled:opacity-60"
                        >
                          {startingId === mock.id ? 'Starting…' : `Start Part ${part} practice →`}
                        </button>
                      </div>
                    </article>
                  </li>
                );
              })}
            </ul>
          </section>
        )}
      </main>
    </LearnerDashboardShell>
  );
}
