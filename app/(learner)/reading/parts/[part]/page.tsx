'use client';

import { useEffect, useState } from 'react';
import { notFound, useParams, useRouter } from 'next/navigation';
import { BookOpen, Clock, ListChecks } from 'lucide-react';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { cardClassName } from '@/components/ui/card';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { LearnerPageHero } from '@/components/domain';
import { LearnerSurfaceMetaRow } from '@/components/domain/learner-surface';
import { cn } from '@/lib/utils';
import { analytics } from '@/lib/analytics';
import {
  getReadingHome,
  startReadingPartPracticeAttempt,
  type ReadingHomeDto,
  type ReadingHomePaperDto,
} from '@/lib/reading-authoring-api';
import { ReadingExamFolderBrowser } from '@/components/domain/reading/reading-exam-folder-browser';
import { readErrorMessage } from '@/lib/read-error-message';
import {
  InsufficientCreditsModal,
  isInsufficientCreditsError,
  readInsufficientCreditsMessage,
} from '@/components/domain/InsufficientCreditsModal';
import { showCreditFeedback } from '@/lib/credit-feedback';

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
      showCreditFeedback(started.feedbackMessage);
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
    <>
      <InsufficientCreditsModal
        open={insufficientCreditsMessage !== null}
        message={insufficientCreditsMessage ?? ''}
        onClose={() => setInsufficientCreditsMessage(null)}
      />
      <LearnerPageHero
        eyebrow={`Part ${part}`}
        icon={BookOpen}
        accent="reading"
        title={meta.title}
        description={meta.description}
      />

      {/* A failed load is the error state below; this is for a failed start. */}
      {error && home ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      {loading ? (
        <LearnerSkeleton variant="card-grid" />
      ) : !home ? (
        <ErrorState message={error ?? undefined} />
      ) : eligiblePapers.length === 0 ? (
        <EmptyState
          icon={<BookOpen className="h-8 w-8" aria-hidden />}
          title={`No published Reading papers contain Part ${part} content yet.`}
          description="Check back soon or attempt the diagnostic to unlock more material."
        />
      ) : (
        <section aria-label={`Available Part ${part} reading papers`}>
          <ReadingExamFolderBrowser
            papers={eligiblePapers}
            emptyMessage={`No Part ${part} papers in this series yet. They will appear here after they are published.`}
            renderPaper={(paper) => {
              const itemCount = countForPart(paper, part);
              const partMinutes =
                part === 'A' ? paper.partATimerMinutes : paper.partBCTimerMinutes;
              return (
                <article className={cn(cardClassName({}), 'flex h-full flex-col')}>
                  <h3 className="text-base font-bold text-navy">
                    {paper.title} · Part {part}
                  </h3>
                  <LearnerSurfaceMetaRow
                    size="compact"
                    className="mt-1.5 tabular-nums"
                    items={[
                      { label: `${itemCount} Part ${part} items`, icon: ListChecks },
                      { label: `${partMinutes} min`, icon: Clock },
                    ]}
                  />
                  <div className="mt-auto pt-4">
                    <Button
                      size="sm"
                      onClick={() => handleStart(paper)}
                      disabled={startingPaperId === paper.id}
                    >
                      {startingPaperId === paper.id ? 'Starting...' : `Start Part ${part} practice`}
                    </Button>
                  </div>
                </article>
              );
            }}
          />
        </section>
      )}
    </>
  );
}
