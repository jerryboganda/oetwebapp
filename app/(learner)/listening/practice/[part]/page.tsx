'use client';

import { useEffect, useState } from 'react';
import { notFound, useParams, useRouter } from 'next/navigation';
import { Clock, Headphones, ListChecks } from 'lucide-react';
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
  getListeningHome,
  startListeningPartPracticeAttempt,
  type ListeningHomeDto,
  type ListeningHomePaperDto,
  type ListeningPartPracticeCode,
} from '@/lib/listening-api';
import { ListeningExamFolderBrowser } from '@/components/domain/listening/listening-exam-folder-browser';
import { readErrorMessage } from '@/lib/read-error-message';
import {
  InsufficientCreditsModal,
  isInsufficientCreditsError,
  readInsufficientCreditsMessage,
} from '@/components/domain/InsufficientCreditsModal';
import { showCreditFeedback } from '@/lib/credit-feedback';

type PartCode = ListeningPartPracticeCode;

const PART_DETAILS: Record<PartCode, { title: string; subtitle: string; description: string; minutes: number }> = {
  A: {
    title: 'Practice Part A',
    subtitle: 'Patient consultations',
    description:
      'Two consultations between a healthcare professional and a patient. You take notes while you listen and answer the Part A items from a published Atlas or Nova paper.',
    minutes: 15,
  },
  B: {
    title: 'Practice Part B',
    subtitle: 'Workplace extracts',
    description:
      'Six short workplace audio extracts. You answer one three-option multiple-choice question per extract from a published Listening paper.',
    minutes: 12,
  },
  C: {
    title: 'Practice Part C',
    subtitle: 'Healthcare presentations',
    description:
      'One or two longer extracts from a published Listening paper. You answer the authored Part C items only — C1 contains Q31–Q36 and C2 contains Q37–Q42 when the source paper includes both extracts.',
    minutes: 15,
  },
};

function normalisePart(raw: string | undefined): PartCode | null {
  if (!raw) return null;
  const upper = raw.toUpperCase();
  return upper === 'A' || upper === 'B' || upper === 'C' ? upper : null;
}

function countForPart(paper: ListeningHomePaperDto, part: PartCode): number {
  if (part === 'A') return paper.partACount ?? 0;
  if (part === 'B') return paper.partBCount ?? 0;
  return paper.partCCount ?? 0;
}

export default function ListeningPartPracticePage() {
  const params = useParams<{ part?: string | string[] }>();
  const raw = Array.isArray(params?.part) ? params?.part?.[0] : params?.part;
  const part = normalisePart(raw);
  if (!part) {
    notFound();
  }

  const router = useRouter();
  const [home, setHome] = useState<ListeningHomeDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [startingPaperId, setStartingPaperId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [insufficientCreditsMessage, setInsufficientCreditsMessage] = useState<string | null>(null);

  useEffect(() => {
    if (!part) return;
    analytics.track('content_view', { page: 'listening-part-practice', part });
    let cancelled = false;
    getListeningHome()
      .then((value) => {
        if (!cancelled) {
          setHome(value);
          setLoading(false);
        }
      })
      .catch((caught) => {
        if (!cancelled) {
          setError(readErrorMessage(caught, 'Could not load Listening papers.'));
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

  async function handleStart(paper: ListeningHomePaperDto) {
    if (!part) return;
    setStartingPaperId(paper.id);
    setError(null);
    setInsufficientCreditsMessage(null);
    try {
      const started = await startListeningPartPracticeAttempt(paper.id, part);
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
        icon={Headphones}
        accent="purple"
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
          icon={<Headphones className="h-8 w-8" aria-hidden />}
          title={`No published Listening papers contain Part ${part} yet`}
        />
      ) : (
        <section aria-label={`Available Part ${part} listening papers`}>
          <ListeningExamFolderBrowser
            papers={eligiblePapers}
            emptyMessage={`No published Listening papers contain Part ${part} yet`}
            renderPaper={(paper) => {
              const itemCount = countForPart(paper, part);
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
                      { label: `${meta.minutes} min`, icon: Clock },
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
