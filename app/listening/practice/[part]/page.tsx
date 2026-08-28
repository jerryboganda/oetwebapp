'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { notFound, useParams, useRouter } from 'next/navigation';
import { ArrowLeft, Clock, Headphones, ListChecks } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { InlineAlert } from '@/components/ui/alert';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { LearnerPageHero } from '@/components/domain';
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
    <LearnerDashboardShell pageTitle={meta.title}>
      <InsufficientCreditsModal
        open={insufficientCreditsMessage !== null}
        message={insufficientCreditsMessage ?? ''}
        onClose={() => setInsufficientCreditsMessage(null)}
      />
      <main className="space-y-5 sm:space-y-8" data-testid={`listening-part-${part}-dispatcher`}>
        <Link
          href="/listening"
          className="inline-flex items-center gap-2 text-sm font-semibold text-primary hover:underline"
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
        ) : eligiblePapers.length === 0 ? (
          <InlineAlert variant="info">
            No published Listening papers contain Part {part} yet
          </InlineAlert>
        ) : (
          <section aria-label={`Available Part ${part} listening papers`}>
            <ListeningExamFolderBrowser
              papers={eligiblePapers}
              emptyMessage={`No published Listening papers contain Part ${part} yet`}
              renderPaper={(paper) => {
                const itemCount = countForPart(paper, part);
                return (
                  <article className="flex h-full flex-col rounded-2xl border border-violet-100 bg-surface p-5 shadow-sm dark:border-violet-900/40">
                    <h3 className="text-base font-bold text-navy">
                      {paper.title} · Part {part}
                    </h3>
                    <p className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted">
                      <span className="inline-flex items-center gap-1">
                        <ListChecks className="h-3 w-3" aria-hidden />
                        {itemCount} Part {part} items
                      </span>
                      <span className="inline-flex items-center gap-1">
                        <Clock className="h-3 w-3" aria-hidden />
                        {meta.minutes} min
                      </span>
                    </p>
                    <div className="mt-auto pt-4">
                      <button
                        type="button"
                        onClick={() => handleStart(paper)}
                        disabled={startingPaperId === paper.id}
                        className="rounded-md bg-info px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-info/90 disabled:opacity-70"
                      >
                        {startingPaperId === paper.id ? 'Starting...' : `Start Part ${part} practice`}
                      </button>
                    </div>
                  </article>
                );
              }}
            />
          </section>
        )}
      </main>
    </LearnerDashboardShell>
  );
}
