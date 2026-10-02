'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { MotionItem } from '@/components/ui/motion-primitives';
import { BookOpen, ClipboardList, MessageCircleQuestion, Sparkles } from 'lucide-react';
import { TaskCard } from '@/components/domain/task-card';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Skeleton } from '@/components/ui/skeleton';
import { EmptyState } from '@/components/ui/empty-error';
import { LearnerPageHero, LearnerSurfaceCard } from '@/components/domain';
import { FreeSampleLauncher } from '@/components/domain/free-sample-launcher';
import { FREE_SPEAKING_SAMPLE_COPY } from '@/components/domain/speaking/SpeakingRulesConsent';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { analytics } from '@/lib/analytics';
import type { LearnerSurfaceCardModel } from '@/lib/learner-surface';
import { WRITING_PROFESSION_LABELS } from '@/lib/writing/types';
import {
  speakingCategoryFilterOptions,
  type SpeakingPrimaryCategory,
} from '@/lib/speaking/category-taxonomy';
import { CRITERION_LABEL, type SpeakingCriterionCode } from '@/lib/api/speaking-assessments';
import {
  listLearnerRolePlayCards,
  type LearnerRolePlayCardSummary,
} from '@/lib/api/speaking-role-play-cards';
import {
  SPEAKING_ASSESSMENT_CRITERIA_HREF,
  SPEAKING_INTRO_QUESTIONS_HREF,
} from '@/lib/speaking-candidate-resources';

const REFERENCE_CARDS: LearnerSurfaceCardModel[] = [
  {
    kind: 'navigation',
    sourceType: 'frontend_navigation',
    accent: 'speaking',
    eyebrow: 'Reference',
    eyebrowIcon: ClipboardList,
    title: 'Speaking Assessment Criteria',
    description: 'The 9 criteria your role-plays are assessed against, with 4 linguistic bands and 5 clinical indicators. Same for all professions.',
    metaItems: [
      { icon: ClipboardList, label: '9 sections' },
      { icon: ClipboardList, label: 'Language + clinical' },
    ],
    primaryAction: {
      label: 'Open Assessment Criteria',
      href: SPEAKING_ASSESSMENT_CRITERIA_HREF,
    },
  },
  {
    kind: 'navigation',
    sourceType: 'frontend_navigation',
    accent: 'speaking',
    eyebrow: 'Reference',
    eyebrowIcon: MessageCircleQuestion,
    title: 'Speaking Intro Questions',
    description: '12 common introductory questions with adaptable sample answers for every profession. Personalise the highlighted details.',
    metaItems: [
      { icon: MessageCircleQuestion, label: '12 questions' },
      { icon: MessageCircleQuestion, label: 'All professions' },
    ],
    primaryAction: {
      label: 'Open Intro Questions',
      href: SPEAKING_INTRO_QUESTIONS_HREF,
    },
  },
];

function professionLabel(professionId: string) {
  return WRITING_PROFESSION_LABELS[professionId as keyof typeof WRITING_PROFESSION_LABELS] ?? professionId;
}

/** Cards carry criterion codes (`informationGiving`); learners read the criterion's name. */
function criterionLabel(code: string) {
  return CRITERION_LABEL[code as SpeakingCriterionCode] ?? code;
}

// The candidate-visible card taxonomy is the only learner filter. Profession
// is never a learner filter (owner, 23 Sep 2026): the server scopes the
// library to the learner's registered profession. Difficulty is removed.
const FILTER_GROUPS: FilterGroup[] = [
  {
    id: 'category',
    label: 'Card type',
    options: speakingCategoryFilterOptions(),
  },
];

type Selection = Record<string, string[]>;

const EMPTY_SELECTION: Selection = {};

function single(group: Selection, groupId: string): string | undefined {
  return group[groupId]?.[0];
}

export default function SpeakingTaskSelection() {
  // `draft` is the pending picker state; `applied` is what the server last
  // rendered. Results + count update together only after Apply.
  const [draft, setDraft] = useState<Selection>(EMPTY_SELECTION);
  const [applied, setApplied] = useState<Selection>(EMPTY_SELECTION);
  const [cards, setCards] = useState<LearnerRolePlayCardSummary[]>([]);
  const [totalCount, setTotalCount] = useState<number | null>(null);
  const [appliedProfessionLabel, setAppliedProfessionLabel] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Stale-request guard: only the most recently issued fetch is allowed to
  // write state. A slow response for a filter the user has since changed
  // away from must never clobber a faster, newer response.
  const requestIdRef = useRef(0);

  const fetchCards = useCallback(async (selection: Selection) => {
    const requestId = ++requestIdRef.current;
    setLoading(true);
    setError(null);
    try {
      const primaryCategory = single(selection, 'category') as SpeakingPrimaryCategory | undefined;
      const response = await listLearnerRolePlayCards({ primaryCategory });
      if (requestId !== requestIdRef.current) return; // superseded by a newer request

      if (!response || !Array.isArray(response.rolePlayCards) || typeof response.totalCount !== 'number') {
        throw new Error('Malformed response from the server.');
      }

      setCards(response.rolePlayCards);
      // The count is server-derived from the same filters that populate the
      // list — display it verbatim, never compute it on the client.
      setTotalCount(response.totalCount);
      setAppliedProfessionLabel(response.appliedProfessionId ? professionLabel(response.appliedProfessionId) : null);
    } catch {
      if (requestId !== requestIdRef.current) return;
      setError('Could not load speaking cards. Please try again.');
    } finally {
      if (requestId === requestIdRef.current) setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchCards(applied);
  }, [applied, fetchCards]);

  // The group is single-select (Writing parity): picking an option
  // replaces the group; picking it again clears the group.
  const handleDraftChange = (groupId: string, optionId: string) => {
    setDraft((prev) => {
      const current = prev[groupId] ?? [];
      const next = current.includes(optionId) ? [] : [optionId];
      return { ...prev, [groupId]: next };
    });
  };

  const handleApply = () => setApplied(draft);

  const handleClear = () => {
    setDraft(EMPTY_SELECTION);
    setApplied(EMPTY_SELECTION);
  };

  const draftTotal = Object.values(draft).reduce((sum, arr) => sum + arr.length, 0);
  const appliedTotal = Object.values(applied).reduce((sum, arr) => sum + arr.length, 0);
  const isDirty = JSON.stringify(draft) !== JSON.stringify(applied);

  return (
    <>
      <LearnerPageHero
        eyebrow="Practice Library"
        icon={BookOpen}
        accent="speaking"
        title="Prepare for your OET Speaking"
        description="Review the assessment criteria and the common introductory questions used across professions."
      />

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        {REFERENCE_CARDS.map((card, index) => (
          <MotionItem key={card.title} delayIndex={index} className="h-full">
            <LearnerSurfaceCard card={card} />
          </MotionItem>
        ))}
      </div>

      <FreeSampleLauncher
        subtest="speaking"
        icon={Sparkles}
        testId="speaking-library-free-sample"
        title="Free Speaking Mock"
        description={FREE_SPEAKING_SAMPLE_COPY}
        className=""
      />

      {/* The picker and its Apply step read as one control. */}
      <div className="space-y-3">
        <FilterBar
          groups={FILTER_GROUPS}
          selected={draft}
          onChange={handleDraftChange}
          onClear={handleClear}
        />
        <div className="flex flex-wrap items-center gap-3">
          <Button type="button" onClick={handleApply} disabled={!isDirty}>
            Apply filters{draftTotal > 0 ? ` (${draftTotal})` : ''}
          </Button>
          {appliedTotal > 0 && (
            <Button type="button" variant="outline" onClick={handleClear}>
              Clear all filters
            </Button>
          )}
        </div>
      </div>

      {error ? (
        // Retryable error state ONLY — never rendered alongside the empty
        // or loading state below (that contradictory double-render was
        // the bug: an error banner PLUS "No cards available").
        <InlineAlert
          variant="error"
          action={(
            <Button type="button" variant="outline" size="sm" onClick={() => fetchCards(applied)}>
              Retry
            </Button>
          )}
        >
          {error}
        </InlineAlert>
      ) : loading ? (
        <div className="grid grid-cols-1 gap-4">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-28 w-full rounded-2xl" />
          ))}
        </div>
      ) : cards.length === 0 ? (
        <EmptyState
          icon={<BookOpen className="h-8 w-8" />}
          title={appliedTotal > 0 ? 'No cards available for these filters' : 'No speaking cards available'}
          description={
            appliedTotal > 0
              ? 'No published cards match this card type yet. Clear the filters to browse everything.'
              : 'Speaking role plays will appear here once they are published.'
          }
          action={appliedTotal > 0 ? { label: 'Clear all filters', onClick: handleClear } : undefined}
        />
      ) : (
        <div className="space-y-4">
          <p className="text-sm text-muted" data-testid="speaking-available-count" aria-live="polite">
            <span className="font-bold tabular-nums text-navy">{totalCount ?? cards.length}</span>
            {' '}available Speaking card{(totalCount ?? cards.length) === 1 ? '' : 's'}
            {appliedProfessionLabel ? ` for ${appliedProfessionLabel}` : ''}
            {single(applied, 'category') ? ` · ${single(applied, 'category')}` : ''}.
            {' '}Each card uses 2 AI credits.
          </p>
          <div className="grid grid-cols-1 gap-4">
            {cards.map((card, i) => {
              const focus = (card.criteriaFocus ?? []).map(criterionLabel).join(', ');
              return (
                <MotionItem key={card.cardId} delayIndex={Math.min(i, 5)}>
                  <TaskCard
                    id={card.cardId}
                    title={card.scenarioTitle}
                    subtest="Speaking"
                    profession={professionLabel(card.professionId)}
                    description={[focus ? `Focus: ${focus}` : null, card.primaryCategory ?? 'Other Cards', 'Uses 2 AI credits'].filter(Boolean).join(' · ')}
                    onStart={() => {
                      analytics.track('task_started', { taskId: card.cardId, subtest: 'speaking' });
                      window.location.href = `/speaking/roleplay/${encodeURIComponent(card.cardId)}`;
                    }}
                  />
                </MotionItem>
              );
            })}
          </div>
        </div>
      )}
    </>
  );
}
