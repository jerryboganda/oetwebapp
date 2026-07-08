'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { notFound, useParams, useRouter } from 'next/navigation';
import { ArrowLeft, BookOpen, Clock, ListChecks } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { InlineAlert } from '@/components/ui/alert';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { LearnerPageHero } from '@/components/domain';
import { analytics } from '@/lib/analytics';
import {
  getReadingHome,
  startReadingPartPracticeAttempt,
  type ReadingHomeDto,
  type ReadingHomePaperDto,
} from '@/lib/reading-authoring-api';
import { readErrorMessage } from '@/lib/read-error-message';
import {
  InsufficientCreditsModal,
  isInsufficientCreditsError,
  readInsufficientCreditsMessage,
} from '@/components/domain/InsufficientCreditsModal';

// Per the 2026-05-27 OET sample-test alignment, `/reading/parts/[part]` is
// a thin candidate dispatcher: it lists the published Reading papers that
// contain questions for the requested part (A, B, or C), creates a scoped
// backend practice attempt, and hands the user to the existing paper player.
// The legacy /reading/practice hub (Learning / Drills / Mini-Tests / Error
// Bank) stays on disk and reachable by URL but is no longer surfaced from the
// simplified candidate hub.

type PartCode = 'A' | 'B' | 'C';

const PART_DETAILS: Record<PartCode, { title: string; subtitle: string; description: string }> = {
  A: {
    title: 'Practice Part A',
    subtitle: 'Expeditious reading (15 minutes)',
    description:
      'Match section headings to four short medical texts. Trains scanning and skimming under a strict 15-minute Part A window.',
  },
  B: {
    title: 'Practice Part B',
    subtitle: 'Workplace texts',
    description:
      'Six short workplace notices, memos, or guidance excerpts. One three-option multiple-choice question per text.',
  },
  C: {
    title: 'Practice Part C',
    subtitle: 'Long-text comprehension',
    description:
      'Two longer healthcare texts (~800 words each) with eight four-option multiple-choice questions per text.',
  },
};

function normalisePart(raw: string | undefined): PartCode | null {
  if (!raw) return null;
  const upper = raw.toUpperCase();
  return upper === 'A' || upper === 'B' || upper === 'C' ? upper : null;
}

function countForPart(paper: ReadingHomePaperDto, part: PartCode): number {
  if (part === 'A') return paper.partACount;
  if (part === 'B') return paper.partBCount;
  return paper.partCCount;
}

export default function ReadingPartPracticePage() {
  const params = useParams<{ part?: string | string[] }>();
  const raw = Array.isArray(params?.part) ? params?.part?.[0] : params?.part;
  const part = normalisePart(raw);
  if (!part) {
    notFound();
  }

  const router = useRouter();
  const [home, setHome] = useState<ReadingHomeDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [startingPaperId, setStartingPaperId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [insufficientCreditsMessage, setInsufficientCreditsMessage] = useState<string | null>(null);

  useEffect(() => {
    if (!part) return;
    analytics.track('content_view', { page: 'reading-part-practice', part });
    let cancelled = false;
    getReadingHome()
      .then((value) => {
        if (!cancelled) {
          setHome(value);
          setLoading(false);
        }
      })
      .catch((caught) => {
        if (!cancelled) {
          setError(readErrorMessage(caught, 'Could not load Reading papers.'));
          setLoading(false);
        }
      });
    return () => {
      cancelled = true;
    };
  }, [part]);

  if (!part) return null;

  const meta = PART_DETAILS[part];
  const eligiblePapers =
    home?.papers?.filter((paper) => countForPart(paper, part) > 0) ?? [];

  async function handleStart(paper: ReadingHomePaperDto) {
    if (!part) return;
    setStartingPaperId(paper.id);
    setError(null);
    setInsufficientCreditsMessage(null);
    try {
      const started = await startReadingPartPracticeAttempt(paper.id, part);
      router.push(started.playerRoute);
    } catch (caught) {
      if (isInsufficientCreditsError(caught)) {
        setInsufficientCreditsMessage(readInsufficientCreditsMessage(caught));
      } else {
        setError(readErrorMessage(caught, `Could not start Part ${part} practice.`));
      }
    } finally {
      setStartingPaperId(null);
    }
  }

  return (
    <LearnerDashboardShell pageTitle={meta.title}>
      <InsufficientCreditsModal
        open={insufficientCreditsMessage !== null}
        message={insufficientCreditsMessage ?? ''}
        onClose={() => setInsufficientCreditsMessage(null)}
      />
      <main className="space-y-5 sm:space-y-8" data-testid={`reading-part-${part}-dispatcher`}>
        <Link
          href="/reading"
          className="inline-flex items-center gap-2 text-sm font-semibold text-primary hover:underline"
        >
          <ArrowLeft className="h-4 w-4" aria-hidden />
          Back to Reading
        </Link>

        <LearnerPageHero
          eyebrow={`Part ${part}`}
          icon={BookOpen}
          accent="blue"
          title={meta.title}
          description={meta.description}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {loading ? (
          <LearnerSkeleton variant="card-grid" />
        ) : eligiblePapers.length === 0 ? (
          <InlineAlert variant="info">
            No published Reading papers contain Part {part} content yet. Check back soon or attempt
            the diagnostic to unlock more material.
          </InlineAlert>
        ) : (
          <section aria-label={`Available Part ${part} reading papers`}>
            <div className="mb-3">
              <h2 className="text-base font-bold text-navy">
                Pick a paper for Part {part} practice
              </h2>
              <p className="mt-1 text-sm text-muted">
                Each paper boots the reading player in practice mode, scoped to {meta.subtitle}.
              </p>
            </div>

            <ul className="grid gap-4 sm:grid-cols-2">
              {eligiblePapers.map((paper) => {
                const itemCount = countForPart(paper, part);
                const totalMinutes = paper.partATimerMinutes + paper.partBCTimerMinutes;
                return (
                  <li key={paper.id}>
                    <article className="flex h-full flex-col rounded-2xl border border-blue-100 bg-surface p-5 shadow-sm dark:border-blue-900/40">
                      <h3 className="text-base font-bold text-navy">
                        {paper.title}
                      </h3>
                      <p className="mt-1 text-xs text-muted flex flex-wrap items-center gap-x-3 gap-y-1">
                        <span className="inline-flex items-center gap-1">
                          <ListChecks className="h-3 w-3" aria-hidden />
                          {itemCount} Part {part} items
                        </span>
                        <span className="inline-flex items-center gap-1">
                          <Clock className="h-3 w-3" aria-hidden />
                          ~{totalMinutes} min full paper
                        </span>
                      </p>
                      <div className="mt-auto pt-4">
                        <button
                          type="button"
                          onClick={() => handleStart(paper)}
                          disabled={startingPaperId === paper.id}
                          className="rounded-md bg-info px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-info/90"
                        >
                          {startingPaperId === paper.id ? 'Starting...' : `Start Part ${part} practice`}
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
