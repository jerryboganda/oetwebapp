'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { motion } from 'motion/react';
import { Lock, Star, Volume2 } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Badge, CategoryBadge, DifficultyBadge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Modal } from '@/components/ui/modal';
import {
  fetchRecallsToday,
  fetchRecallsQueue,
  starRecall,
  fetchRecallsAudio,
  fetchVocabularyCategories,
  fetchVocabularyTerms,
  isApiError,
  type RecallsTodayResponse,
  type RecallsQueueItem,
  type RecallsStarReason,
} from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { playTransientAudio } from '@/lib/recalls-audio';
import type { VocabularyCategoriesResponse, VocabularyTerm } from '@/lib/types/vocabulary';

const STAR_REASONS: { key: RecallsStarReason; label: string }[] = [
  { key: 'spelling', label: 'Spelling' },
  { key: 'pronunciation', label: 'Pronunciation' },
  { key: 'meaning', label: 'Meaning' },
  { key: 'hearing', label: 'Hearing' },
  { key: 'confused', label: 'Confused' },
];

interface VocabularyTermsPage {
  total: number;
  terms?: VocabularyTerm[];
  items?: VocabularyTerm[];
}

function formatCategoryLabel(category: string, count: number) {
  const label = category
    .replace(/[_-]/g, ' ')
    .replace(/\b\w/g, (char) => char.toUpperCase());
  return `${label} (${count})`;
}

/**
 * /recalls/words — vocabulary-side card list.
 *
 * Surfaces the queued cards (vocab + review) with a star toggle, audio button,
 * and direct link into the runner. The full quiz UX lives at /recalls/cards.
 */
export default function RecallsWordsPage() {
  const [today, setToday] = useState<RecallsTodayResponse | null>(null);
  const [items, setItems] = useState<RecallsQueueItem[] | null>(null);
  const [catalogTerms, setCatalogTerms] = useState<VocabularyTerm[]>([]);
  const [catalogTotal, setCatalogTotal] = useState(0);
  const [catalogLoading, setCatalogLoading] = useState(true);
  const [catalogError, setCatalogError] = useState<string | null>(null);
  const [categoryFilters, setCategoryFilters] = useState([{ key: 'all', label: 'All categories' }]);
  const [selectedCategory, setSelectedCategory] = useState('all');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [starOpenFor, setStarOpenFor] = useState<string | null>(null);
  // Per PRD Phase 2 §2 the click-to-hear feature is paid-only. The backend is
  // authoritative (returns 402/403 for free learners) — we surface that as an
  // upgrade modal instead of a silent failure so candidates understand why.
  const [showUpgradeModal, setShowUpgradeModal] = useState(false);

  useEffect(() => {
    analytics.track('recalls_words_viewed');
    Promise.all([fetchRecallsToday(), fetchRecallsQueue(40)])
      .then(([t, q]) => {
        setToday(t);
        setItems(q);
      })
      .catch(() => setError('Could not load your recalls list.'))
      .finally(() => setLoading(false));

    fetchVocabularyCategories({ examTypeCode: 'oet' })
      .then((categories) => {
        const categoryPage = categories as VocabularyCategoriesResponse;
        setCategoryFilters([
          { key: 'all', label: 'All categories' },
          ...(categoryPage.categories ?? []).map((item) => ({
            key: item.category,
            label: formatCategoryLabel(item.category, item.termCount),
          })),
        ]);
      })
      .catch(() => {
        // Non-fatal: the matrix still works with the all-categories fallback.
      });
  }, []);

  useEffect(() => {
    let active = true;
    fetchVocabularyTerms({
      examTypeCode: 'oet',
      category: selectedCategory === 'all' ? undefined : selectedCategory,
      page: 1,
      pageSize: 24,
    })
      .then((response) => {
        if (!active) return;
        const page = response as VocabularyTermsPage;
        setCatalogTerms(page.terms ?? page.items ?? []);
        setCatalogTotal(page.total ?? 0);
      })
      .catch(() => {
        if (!active) return;
        setCatalogTerms([]);
        setCatalogTotal(0);
        setCatalogError('Could not load the vocabulary matrix.');
      })
      .finally(() => {
        if (active) setCatalogLoading(false);
      });

    return () => {
      active = false;
    };
  }, [selectedCategory]);

  function handleCategoryChange(nextCategory: string) {
    if (nextCategory === selectedCategory) return;
    setCatalogLoading(true);
    setCatalogError(null);
    setSelectedCategory(nextCategory);
  }

  function playTerm(term: VocabularyTerm) {
    const src = term.audioUrl
      ? term.audioUrl
      : term.audioMediaAssetId
        ? `/v1/media/${term.audioMediaAssetId}/content`
        : null;
    if (!src) return;
    const a = new Audio(src);
    a.play().catch(() => {});
  }

  async function handleStar(it: RecallsQueueItem, reason: RecallsStarReason) {
    setStarOpenFor(null);
    setItems((prev) =>
      prev ? prev.map((p) => (p.id === it.id ? { ...p, starred: true, starReason: reason } : p)) : prev,
    );
    try {
      await starRecall(it.kind, it.id, true, reason);
    } catch {
      setItems((prev) =>
        prev ? prev.map((p) => (p.id === it.id ? { ...p, starred: it.starred, starReason: it.starReason } : p)) : prev,
      );
    }
  }

  async function handleUnstar(it: RecallsQueueItem) {
    setItems((prev) =>
      prev ? prev.map((p) => (p.id === it.id ? { ...p, starred: false, starReason: null } : p)) : prev,
    );
    try {
      await starRecall(it.kind, it.id, false);
    } catch {
      setItems((prev) =>
        prev ? prev.map((p) => (p.id === it.id ? { ...p, starred: true, starReason: it.starReason } : p)) : prev,
      );
    }
  }

  async function handlePlay(it: RecallsQueueItem) {
    if (it.kind !== 'vocab' || !it.termId) return;
    try {
      const url = (await fetchRecallsAudio(it.termId, 'normal')).url;
      playTransientAudio(url);
      analytics.track('recalls_word_audio_played', { termId: it.termId });
    } catch (err) {
      // Backend gates audio behind an active subscription. Surface 402/403 as
      // an upgrade prompt; treat anything else as a quiet best-effort failure.
      if (isApiError(err) && (err.status === 402 || err.status === 403)) {
        analytics.track('recalls_word_audio_blocked', { termId: it.termId, status: err.status });
        setShowUpgradeModal(true);
      }
    }
  }

  return (
    <LearnerDashboardShell>
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Recalls / Words"
          title="Your active vocabulary"
          description="Every term you've added, every card seeded from your drills, all in one starrable, drillable list."
          icon={Star}
          highlights={[
            { icon: Star, label: 'Starred', value: `${today?.starred ?? 0}` },
            { icon: Star, label: 'Due today', value: `${today?.dueToday ?? 0}` },
            { icon: Star, label: 'Mastered', value: `${today?.mastered ?? 0}` },
          ]}
        />

        {error && <InlineAlert variant="warning">{error}</InlineAlert>}

        <LearnerSurfaceSectionHeader
          eyebrow="Catalog matrix"
          title="Browse active vocabulary by category"
          description="Use the functional category filter to inspect the active recall vocabulary catalog."
        />

        <section className="space-y-4 rounded-2xl border border-border bg-surface p-4">
          <div className="space-y-3">
            <fieldset>
              <legend className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted">Functional category</legend>
              <div className="flex flex-wrap gap-2">
                {categoryFilters.map((filter) => (
                  <button
                    key={filter.key}
                    type="button"
                    aria-pressed={selectedCategory === filter.key}
                    onClick={() => handleCategoryChange(filter.key)}
                    className={`rounded-full border px-3 py-1 text-xs font-medium transition ${
                      selectedCategory === filter.key
                        ? 'border-primary bg-primary text-white'
                        : 'border-border text-muted hover:border-primary hover:text-primary'
                    }`}
                  >
                    {filter.label}
                  </button>
                ))}
              </div>
            </fieldset>
          </div>

          {catalogError && <InlineAlert variant="warning">{catalogError}</InlineAlert>}

          {catalogLoading ? (
            <div className="grid gap-3 md:grid-cols-2">
              {Array.from({ length: 4 }).map((_, i) => (
                <Skeleton key={i} className="h-24 rounded-xl" />
              ))}
            </div>
          ) : catalogTerms.length > 0 ? (
            <div className="space-y-3">
              <div className="text-sm text-muted">
                Showing {catalogTerms.length} of {catalogTotal} active terms for this matrix slice.
              </div>
              <div className="grid gap-3 md:grid-cols-2">
                {catalogTerms.map((term) => {
                  const hasAudio = Boolean(term.audioUrl || term.audioMediaAssetId);
                  const definitionText =
                    term.definition && !/^\s*\(\s*pending\b/i.test(term.definition) ? term.definition : null;
                  return (
                    <article
                      key={term.id}
                      role="group"
                      tabIndex={0}
                      onKeyDown={(event) => {
                        if (event.key === ' ' || event.code === 'Space') {
                          event.preventDefault();
                          if (hasAudio) playTerm(term);
                        }
                      }}
                      className="rounded-xl border border-border bg-background/70 p-4 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                    >
                      <div className="flex flex-wrap items-center gap-2">
                        <h3 className="font-semibold text-navy">{term.term}</h3>
                        {hasAudio ? (
                          <button
                            type="button"
                            onClick={() => playTerm(term)}
                            aria-label={`Play pronunciation of ${term.term}`}
                            title="Play pronunciation"
                            className="inline-flex h-7 w-7 items-center justify-center rounded-full bg-primary/10 p-1.5 text-primary hover:bg-primary/15"
                          >
                            <Volume2 size={14} strokeWidth={2} className="h-3.5 w-3.5" />
                          </button>
                        ) : (
                          <button
                            type="button"
                            disabled
                            aria-disabled="true"
                            aria-label={`Audio not yet available for ${term.term}`}
                            title="Audio not yet available"
                            className="inline-flex h-7 w-7 items-center justify-center rounded-full bg-primary/10 p-1.5 text-primary opacity-40 cursor-not-allowed"
                          >
                            <Volume2 size={14} strokeWidth={2} className="h-3.5 w-3.5" />
                          </button>
                        )}
                        <CategoryBadge category={term.category} size="sm" />
                        <DifficultyBadge difficulty={term.difficulty} size="sm" />
                      </div>
                      {definitionText && <p className="mt-2 text-sm text-muted">{definitionText}</p>}
                      <p className="mt-2 text-xs italic text-muted">{term.exampleSentence}</p>
                      {hasAudio ? (
                        <button
                          type="button"
                          onClick={() => playTerm(term)}
                          className="mt-2 inline-flex items-center gap-1.5 text-xs font-medium text-primary hover:text-primary-dark focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary rounded"
                        >
                          <Volume2 size={12} className="h-3 w-3" aria-hidden="true" />
                          <span>Play pronunciation</span>
                        </button>
                      ) : (
                        <span
                          className="mt-2 inline-flex items-center gap-1.5 text-xs text-muted opacity-60 cursor-not-allowed"
                          title="Audio not yet available"
                        >
                          <Volume2 size={12} className="h-3 w-3" aria-hidden="true" />
                          <span>Audio not yet available</span>
                        </span>
                      )}
                    </article>
                  );
                })}
              </div>
            </div>
          ) : (
            <div className="rounded-xl border border-dashed border-border p-4 text-center text-sm text-muted">
              No active terms match this category yet.
            </div>
          )}
        </section>

        <LearnerSurfaceSectionHeader
          eyebrow="Queue"
          title="Today's recall queue"
          description="Starred cards float to the top. Click play to hear the British pronunciation."
        />

        {loading ? (
          <div className="space-y-2">
            {Array.from({ length: 6 }).map((_, i) => (
              <Skeleton key={i} className="h-16 rounded-xl" />
            ))}
          </div>
        ) : items && items.length > 0 ? (
          <ul className="divide-y divide-border rounded-2xl border border-border bg-surface">
            {items.map((it) => (
              <li key={`${it.kind}:${it.id}`} className="flex items-center gap-3 p-3">
                {it.kind === 'vocab' ? (
                  <Button
                    type="button"
                    variant="primary"
                    onClick={() => handlePlay(it)}
                    aria-label={`Play ${it.title}`}
                    className="flex h-10 w-10 items-center justify-center rounded-full p-0"
                  >
                    <Volume2 className="h-4 w-4" />
                  </Button>
                ) : (
                  <div className="flex h-10 w-10 items-center justify-center rounded-full bg-info/10 text-info">
                    <Star className="h-4 w-4" aria-hidden="true" />
                  </div>
                )}
                <div className="flex-1">
                  <div className="flex items-center gap-2">
                    {it.kind === 'vocab' ? (
                      // PRD Phase 2 §2: clicking the word itself plays audio.
                      <button
                        type="button"
                        onClick={() => handlePlay(it)}
                        className="rounded font-semibold text-navy hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                        aria-label={`Play pronunciation of ${it.title}`}
                      >
                        {it.title}
                      </button>
                    ) : (
                      <span className="font-semibold text-navy">{it.title}</span>
                    )}
                    <Badge variant={it.kind === 'vocab' ? 'info' : 'default'}>{it.kind}</Badge>
                    {it.starred && <Badge variant="warning">Starred · {it.starReason ?? '—'}</Badge>}
                  </div>
                  {it.subtitle && <div className="text-xs text-muted">{it.subtitle}</div>}
                </div>
                <div className="relative">
                  {it.starred ? (
                    <button
                      type="button"
                      onClick={() => handleUnstar(it)}
                      className="rounded-full border border-border px-3 py-1 text-xs font-medium text-muted hover:border-warning hover:text-warning"
                    >
                      Unstar
                    </button>
                  ) : (
                    <button
                      type="button"
                      onClick={() => setStarOpenFor((cur) => (cur === it.id ? null : it.id))}
                      onKeyDown={(event) => {
                        if (event.key === 'Escape') setStarOpenFor(null);
                      }}
                      aria-haspopup="menu"
                      aria-expanded={starOpenFor === it.id}
                      aria-controls={`star-reason-menu-${it.id}`}
                      className="rounded-full border border-border px-3 py-1 text-xs font-medium text-muted hover:border-warning hover:text-warning"
                    >
                      Star
                    </button>
                  )}
                  {starOpenFor === it.id && (
                    <motion.div
                      initial={{ opacity: 0, y: -4 }}
                      animate={{ opacity: 1, y: 0 }}
                      id={`star-reason-menu-${it.id}`}
                      role="menu"
                      className="absolute right-0 top-full z-10 mt-1 w-44 overflow-hidden rounded-xl border border-border bg-surface shadow-lg"
                    >
                      {STAR_REASONS.map((r) => (
                        <button
                          key={r.key}
                          type="button"
                          role="menuitem"
                          onClick={() => handleStar(it, r.key)}
                          className="block w-full px-3 py-2 text-left text-sm text-navy hover:bg-lavender/40"
                        >
                          {r.label}
                        </button>
                      ))}
                    </motion.div>
                  )}
                </div>
              </li>
            ))}
          </ul>
        ) : (
          <div className="rounded-2xl border border-border bg-surface p-6 text-center text-sm text-muted">
            Nothing in your recall queue yet. Add words from the vocabulary library or complete a Listening drill — wrong
            free-text answers seed cards automatically.
          </div>
        )}
      </div>

      <Modal
        open={showUpgradeModal}
        onClose={() => setShowUpgradeModal(false)}
        title="Unlock click-to-hear pronunciation"
        size="sm"
      >
        <div className="space-y-4">
          <div className="flex h-12 w-12 items-center justify-center rounded-full bg-primary/10 text-primary">
            <Lock className="h-5 w-5" />
          </div>
          <p className="text-sm text-muted">
            Pronunciation audio for recall words is part of the paid plan. Upgrade to listen to every term in your recall
            list, hear British clinical pronunciation, and unlock the full Recalls drill set.
          </p>
          <div className="flex flex-col gap-2 sm:flex-row sm:justify-end">
            <Button variant="secondary" onClick={() => setShowUpgradeModal(false)}>
              Not now
            </Button>
            <Link
              href="/billing/upgrade"
              className="inline-flex items-center justify-center rounded-xl bg-primary px-4 py-2 text-sm font-semibold text-white hover:bg-primary-dark"
              onClick={() => setShowUpgradeModal(false)}
            >
              View upgrade options
            </Link>
          </div>
        </div>
      </Modal>
    </LearnerDashboardShell>
  );
}
