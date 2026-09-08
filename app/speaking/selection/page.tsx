'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { ClipboardList, MessageCircleQuestion } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { TaskCard } from '@/components/domain/task-card';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Skeleton } from '@/components/ui/skeleton';
import { EmptyState } from '@/components/ui/empty-error';
import { LearnerSurfaceCard } from '@/components/domain';
import { InlineAlert } from '@/components/ui/alert';
import { analytics } from '@/lib/analytics';
import { WRITING_PROFESSIONS, WRITING_PROFESSION_LABELS } from '@/lib/writing/types';
import {
  speakingCategoryFilterOptions,
  type SpeakingPrimaryCategory,
} from '@/lib/speaking/category-taxonomy';
import {
  listLearnerRolePlayCards,
  type LearnerRolePlayCardSummary,
} from '@/lib/api/speaking-role-play-cards';
import {
  SPEAKING_ASSESSMENT_CRITERIA_HREF,
  SPEAKING_INTRO_QUESTIONS_HREF,
} from '@/lib/speaking-candidate-resources';

// FINAL 2026-09-06 — one shared profession master list (Writing parity, no
// separate hard-coded Speaking list) + the candidate-visible card taxonomy
// as the main category filter. Difficulty is removed completely.
const FILTER_GROUPS: FilterGroup[] = [
  {
    id: 'profession',
    label: 'Profession',
    options: WRITING_PROFESSIONS.map((id) => ({ id, label: WRITING_PROFESSION_LABELS[id] })),
  },
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
      const professionId = single(selection, 'profession');
      const primaryCategory = single(selection, 'category') as SpeakingPrimaryCategory | undefined;
      const response = await listLearnerRolePlayCards({ professionId, primaryCategory });
      if (requestId !== requestIdRef.current) return; // superseded by a newer request

      if (!response || !Array.isArray(response.rolePlayCards) || typeof response.totalCount !== 'number') {
        throw new Error('Malformed response from the server.');
      }

      setCards(response.rolePlayCards);
      // The count is server-derived from the same filters that populate the
      // list — display it verbatim, never compute it on the client.
      setTotalCount(response.totalCount);
      const professionLabel = response.appliedProfessionId
        ? (WRITING_PROFESSION_LABELS[response.appliedProfessionId as keyof typeof WRITING_PROFESSION_LABELS]
          ?? response.appliedProfessionId)
        : null;
      setAppliedProfessionLabel(professionLabel);
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

  // Both groups are single-select (Writing parity): picking an option
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
    <LearnerDashboardShell pageTitle="Select Speaking Task">
      <div className="space-y-6">
        <MotionSection>
          <div className="space-y-1">
            <h2 className="text-lg font-bold text-navy sm:text-xl">Prepare for your OET Speaking</h2>
            <p className="text-[13px] text-muted sm:text-sm">
              Review the assessment criteria and the common introductory questions used across professions.
            </p>
          </div>
          <div className="mt-4 grid grid-cols-1 gap-4 lg:grid-cols-2">
            <LearnerSurfaceCard
              card={{
                kind: 'navigation',
                sourceType: 'frontend_navigation',
                accent: 'purple',
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
              }}
            />
            <LearnerSurfaceCard
              card={{
                kind: 'navigation',
                sourceType: 'frontend_navigation',
                accent: 'purple',
                eyebrow: 'Reference',
                eyebrowIcon: MessageCircleQuestion,
                title: 'Speaking Intro Questions',
                description: '11 common introductory questions with adaptable sample answers for every profession. Personalise the highlighted details.',
                metaItems: [
                  { icon: MessageCircleQuestion, label: '11 questions' },
                  { icon: MessageCircleQuestion, label: 'All professions' },
                ],
                primaryAction: {
                  label: 'Open Intro Questions',
                  href: SPEAKING_INTRO_QUESTIONS_HREF,
                },
              }}
            />
          </div>
        </MotionSection>

        <FilterBar
          groups={FILTER_GROUPS}
          selected={draft}
          onChange={handleDraftChange}
          onClear={handleClear}
        />
        <div className="flex flex-wrap items-center gap-3">
          <button
            type="button"
            onClick={handleApply}
            disabled={!isDirty}
            className="pressable inline-flex items-center justify-center gap-2 rounded-xl bg-primary px-5 py-2.5 text-sm font-semibold text-white shadow-sm transition-colors hover:bg-primary/90 disabled:opacity-40 dark:bg-violet-700 dark:hover:bg-violet-600"
          >
            Apply filters{draftTotal > 0 ? ` (${draftTotal})` : ''}
          </button>
          {appliedTotal > 0 && (
            <button
              type="button"
              onClick={handleClear}
              className="inline-flex items-center justify-center gap-2 rounded-xl border border-border px-5 py-2.5 text-sm font-semibold text-navy transition-colors hover:bg-background-light"
            >
              Clear all filters
            </button>
          )}
        </div>

        {error ? (
          // Retryable error state ONLY — never rendered alongside the empty
          // or loading state below (that contradictory double-render was
          // the bug: an error banner PLUS "No cards available").
          <InlineAlert
            variant="error"
            action={(
              <button
                type="button"
                onClick={() => fetchCards(applied)}
                className="pressable rounded-lg border border-current px-3 py-1 text-xs font-semibold"
              >
                Retry
              </button>
            )}
          >
            {error}
          </InlineAlert>
        ) : loading ? (
          <div className="grid grid-cols-1 gap-4">
            {Array.from({ length: 4 }).map((_, i) => (
              <Skeleton key={i} className="h-28 w-full rounded-xl" />
            ))}
          </div>
        ) : cards.length === 0 ? (
          <EmptyState
            title={appliedTotal > 0 ? 'No cards available for these filters' : 'No speaking cards available'}
            description={
              appliedTotal > 0
                ? 'No published cards match this profession and card type yet. Clear the filters to browse everything.'
                : 'Speaking role plays will appear here once they are published.'
            }
            action={appliedTotal > 0 ? { label: 'Clear all filters', onClick: handleClear } : undefined}
          />
        ) : (
          <>
            <p className="text-sm text-muted" data-testid="speaking-available-count" aria-live="polite">
              <span className="font-bold text-navy">{totalCount ?? cards.length}</span>
              {' '}available Speaking card{(totalCount ?? cards.length) === 1 ? '' : 's'}
              {appliedProfessionLabel ? ` for ${appliedProfessionLabel}` : ''}
              {single(applied, 'category') ? ` · ${single(applied, 'category')}` : ''}.
              {' '}Each card uses 2 AI credits.
            </p>
            <div className="grid grid-cols-1 gap-4">
              {cards.map((card, i) => (
                <MotionItem
                  key={card.cardId}
                  delayIndex={i}
                >
                  <TaskCard
                    id={card.cardId}
                    title={card.scenarioTitle}
                    subtest="Speaking"
                    profession={card.professionId}
                    description={`Focus: ${(card.criteriaFocus ?? []).join(', ') || 'speaking control'} · ${card.primaryCategory ?? 'Other Cards'} · Uses 2 AI credits`}
                    onStart={() => {
                      analytics.track('task_started', { taskId: card.cardId, subtest: 'speaking' });
                      window.location.href = `/speaking/roleplay/${encodeURIComponent(card.cardId)}`;
                    }}
                  />
                </MotionItem>
              ))}
            </div>
          </>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
