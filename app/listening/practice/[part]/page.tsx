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
import { listeningV2Api, type ListeningPathwayStageView } from '@/lib/listening/v2-api';

// Per the 2026-05-27 OET sample-test alignment, `/listening/practice/[part]`
// is a thin candidate dispatcher: it lets the candidate start a part-scoped
// practice session on the matching server-authoritative V2 pathway stage.
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
      'Two longer extracts: an interview or presentation on a healthcare topic. You answer six four-option multiple-choice questions per extract (12 items total).',
  },
};

function normalisePart(raw: string | undefined): PartCode | null {
  if (!raw) return null;
  const upper = raw.toUpperCase();
  return upper === 'A' || upper === 'B' || upper === 'C' ? upper : null;
}

function stageMatchesPart(stage: ListeningPathwayStageView, part: PartCode): boolean {
  const normalized = stage.stage.toLowerCase().replace(/[-_\s]+/g, '');
  if ((part === 'B' || part === 'C') && normalized.includes('partbc')) return true;
  const partToken = part === 'A' ? 'parta' : part === 'B' ? 'partb' : 'partc';
  return normalized.includes(partToken);
}

export default function ListeningPartPracticePage() {
  const params = useParams<{ part?: string | string[] }>();
  const raw = Array.isArray(params?.part) ? params?.part?.[0] : params?.part;
  const part = normalisePart(raw);
  if (!part) {
    notFound();
  }

  const router = useRouter();
  const [stages, setStages] = useState<ListeningPathwayStageView[]>([]);
  const [loading, setLoading] = useState(true);
  const [starting, setStarting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!part) return;
    analytics.track('content_view', { page: 'listening-part-practice', part });
    let cancelled = false;
    listeningV2Api.myPathway()
      .then((list) => {
        if (!cancelled) {
          setStages(list);
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
  // The listening diagnostic is optional — any learner can start Part A/B/C
  // practice directly, in any order. We launch the matching part-scoped pathway
  // stage as soon as the server exposes a runnable paper for it (actionHref
  // present and not Locked); no diagnostic completion is required.
  const launchStage = stages.find(
    (stage) => stage.actionHref && stage.status !== 'Locked' && stageMatchesPart(stage, part),
  );

  function handleStart() {
    if (!part) return;
    if (!launchStage?.actionHref) {
      setError(`Part ${part} practice isn't available right now. Please try again later.`);
      return;
    }
    setStarting(true);
    setError(null);
    const target = new URL(launchStage.actionHref, window.location.origin);
    target.searchParams.set('mode', 'practice');
    target.searchParams.set('focus', part === 'A' ? 'part-a' : part === 'B' ? 'part-b' : 'part-c');
    target.searchParams.set('part', part);
    router.push(`${target.pathname}${target.search}`);
  }

  return (
    <LearnerDashboardShell pageTitle={meta.title}>
      <main className="space-y-5 sm:space-y-8" data-testid={`listening-part-${part}-dispatcher`}>
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
        ) : !launchStage ? (
          <InlineAlert variant="info">
            No Part {part} listening paper is available yet. Please check back soon.
          </InlineAlert>
        ) : (
          <section aria-label={`Available Part ${part} practice papers`}>
            <div className="mb-3">
              <h2 className="text-base font-bold text-navy">
                Pick a paper for Part {part} practice
              </h2>
              <p className="mt-1 text-sm text-muted">
                This opens your current V2 pathway paper in practice mode with Part {part} focus.
              </p>
            </div>
            <article className="flex flex-col rounded-2xl border border-border bg-surface p-5 shadow-sm sm:max-w-xl">
              <h3 className="text-base font-bold text-navy">
                {meta.subtitle}
              </h3>
              <p className="mt-1 text-xs text-muted">
                Current stage: {launchStage.stage.replace(/[-_]+/g, ' ')}
              </p>
              <div className="mt-4">
                <button
                  type="button"
                  disabled={starting}
                  onClick={handleStart}
                  className="rounded-md bg-primary px-4 py-2 text-sm font-semibold text-white transition-[color,background-color,transform] duration-200 hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600 disabled:opacity-60"
                >
                  {starting ? 'Starting…' : `Start Part ${part} practice`}
                </button>
              </div>
            </article>
          </section>
        )}
      </main>
    </LearnerDashboardShell>
  );
}
