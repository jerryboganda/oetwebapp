'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { motion } from 'motion/react';
import { Lock, Star, Volume2 } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Badge, CategoryBadge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Modal } from '@/components/ui/modal';
import {
  fetchRecallsToday,
  fetchRecallsQueue,
  starRecall,
  fetchRecallsAudio,
  fetchVocabularyCategories,
  fetchVocabularyTerms,
  fetchVocabularyRecallSets,
  isApiError,
  type RecallsTodayResponse,
  type RecallsQueueItem,
  type RecallsStarReason,
  type RecallSetSummary,
} from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { playTransientAudio } from '@/lib/recalls-audio';
import { toast } from 'sonner';
import type { VocabularyCategoriesResponse, VocabularyTerm } from '@/lib/types/vocabulary';
import { Pagination } from '@/components/ui/pagination';

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
 * Animated three-bar equalizer shown while a term's pronunciation is playing.
 * Falls back to a static volume glyph when idle (handled by the caller).
 */
function PlayingBars() {
  return (
    <span className="flex h-3.5 items-end gap-[2px]" aria-hidden="true">
      {[0, 1, 2].map((bar) => (
        <motion.span
          key={bar}
          className="w-[2px] rounded-full bg-current"
          initial={{ height: 4 }}
          animate={{ height: [4, 13, 6, 11, 4] }}
          transition={{
            duration: 0.9,
            repeat: Infinity,
            ease: 'easeInOut',
            delay: bar * 0.15,
          }}
        />
      ))}
    </span>
  );
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
  const [catalogPage, setCatalogPage] = useState(1);
  const [catalogPageSize, setCatalogPageSize] = useState(24);
  const [recallSets, setRecallSets] = useState<RecallSetSummary[]>([]);
  const [selectedRecallSet, setSelectedRecallSet] = useState('');
  const catalogPageCount = Math.max(1, Math.ceil(catalogTotal / catalogPageSize));
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [starOpenFor, setStarOpenFor] = useState<string | null>(null);
  // Tracks the term/item id whose pronunciation is currently playing so the
  // audio button can render an animated equalizer instead of the static glyph.
  const [playingId, setPlayingId] = useState<string | null>(null);
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

    fetchVocabularyRecallSets({ examTypeCode: 'oet' })
      .then((data) => setRecallSets(data.sets ?? []))
      .catch(() => {
        // Non-fatal: recall set filter will simply not render.
      });
  }, []);

  useEffect(() => {
    let active = true;
    fetchVocabularyTerms({
      examTypeCode: 'oet',
      category: selectedCategory === 'all' ? undefined : selectedCategory,
      recallSet: selectedRecallSet || undefined,
      page: catalogPage,
      pageSize: catalogPageSize,
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
  }, [selectedCategory, selectedRecallSet, catalogPage, catalogPageSize]);

  function handleCategoryChange(nextCategory: string) {
    if (nextCategory === selectedCategory) return;
    setCatalogLoading(true);
    setCatalogError(null);
    setSelectedCategory(nextCategory);
    setCatalogPage(1);
  }

  function handleRecallSetChange(nextSet: string) {
    if (nextSet === selectedRecallSet) return;
    setCatalogLoading(true);
    setCatalogError(null);
    setSelectedRecallSet(nextSet);
    setCatalogPage(1);
  }

  function goToCatalogPage(nextPage: number) {
    const clamped = Math.min(catalogPageCount, Math.max(1, nextPage));
    if (clamped === catalogPage) return;
    setCatalogLoading(true);
    setCatalogError(null);
    setCatalogPage(clamped);
    if (typeof window !== 'undefined') {
      window.scrollTo({ top: window.scrollY, behavior: 'instant' as ScrollBehavior });
    }
  }

  function bindPlaybackState(audio: HTMLAudioElement, id: string) {
    setPlayingId(id);
    const clear = () => setPlayingId((current) => (current === id ? null : current));
    if (typeof audio.addEventListener === 'function') {
      audio.addEventListener('ended', clear, { once: true });
      audio.addEventListener('pause', clear, { once: true });
      audio.addEventListener('error', clear, { once: true });
    }
  }

  async function playTerm(term: VocabularyTerm) {
    try {
      const url = (await fetchRecallsAudio(term.id, 'normal')).url;
      bindPlaybackState(playTransientAudio(url), term.id);
      analytics.track('recalls_word_audio_played', { termId: term.id });
    } catch (err) {
      setPlayingId((current) => (current === term.id ? null : current));
      if (isApiError(err) && (err.status === 402 || err.status === 403)) {
        analytics.track('recalls_word_audio_blocked', { termId: term.id, status: err.status });
        setShowUpgradeModal(true);
      } else {
        toast.error('Audio not available for this term');
      }
    }
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
    const termId = it.termId;
    try {
      const url = (await fetchRecallsAudio(termId, 'normal')).url;
      bindPlaybackState(playTransientAudio(url), termId);
      analytics.track('recalls_word_audio_played', { termId });
    } catch (err) {
      setPlayingId((current) => (current === termId ? null : current));
      // Backend gates audio behind an active subscription. Surface 402/403 as
      // an upgrade prompt; treat anything else as a quiet best-effort failure.
      if (isApiError(err) && (err.status === 402 || err.status === 403)) {
        analytics.track('recalls_word_audio_blocked', { termId, status: err.status });
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
            {recallSets.length > 0 && (
              <fieldset>
                <legend className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted">Recall set</legend>
                <div className="flex flex-wrap gap-2">
                  <button
                    type="button"
                    aria-pressed={selectedRecallSet === ''}
                    onClick={() => handleRecallSetChange('')}
                    className={`rounded-full border px-3 py-1 text-xs font-medium transition ${
                      selectedRecallSet === ''
                        ? 'border-primary bg-primary text-white dark:bg-violet-700'
                        : 'border-border text-muted hover:border-primary hover:text-primary'
                    }`}
                  >
                    All
                  </button>
                  {recallSets.map((s) => (
                    <button
                      key={s.code}
                      type="button"
                      aria-pressed={selectedRecallSet === s.code}
                      onClick={() => handleRecallSetChange(s.code)}
                      title={s.description}
                      className={`rounded-full border px-3 py-1 text-xs font-medium transition ${
                        selectedRecallSet === s.code
                          ? 'border-primary bg-primary text-white dark:bg-violet-700'
                          : 'border-border text-muted hover:border-primary hover:text-primary'
                      }`}
                    >
                      {s.shortLabel} ({s.termCount})
                    </button>
                  ))}
                </div>
              </fieldset>
            )}
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
                        ? 'border-primary bg-primary text-white dark:bg-violet-700'
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
              <div className="grid gap-3 md:grid-cols-2">
                {catalogTerms.map((term) => {
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
                          playTerm(term);
                        }
                      }}
                      className="group rounded-2xl border border-border bg-surface p-5 transition-[color,background-color,border-color,box-shadow,transform,opacity,filter] duration-200 hover:border-primary/30 hover:shadow-md hoverable:-translate-y-0.5 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                    >
                      <div className="flex flex-wrap items-center gap-2">
                        <h3 className="text-base font-bold text-navy">{term.term}</h3>
                        <button
                          type="button"
                          onClick={() => playTerm(term)}
                          aria-label={`Play pronunciation of ${term.term}`}
                          aria-pressed={playingId === term.id}
                          title="Play pronunciation"
                          className="inline-flex h-7 w-7 items-center justify-center rounded-full bg-primary/10 p-1.5 text-primary transition-colors hover:bg-primary/20 group-hover:bg-primary/15"
                        >
                          {playingId === term.id ? (
                            <PlayingBars />
                          ) : (
                            <Volume2 size={14} strokeWidth={2} className="h-3.5 w-3.5" />
                          )}
                        </button>
                        {(term.examFrequencyCount ?? 1) > 1 && (
                          <span
                            title={`This word appeared ${term.examFrequencyCount} times in the exam`}
                            className="inline-flex items-center gap-0.5 rounded-full bg-amber-100 px-2 py-0.5 text-xs font-semibold text-amber-700 dark:bg-amber-900/30 dark:text-amber-300"
                          >
                            <Star size={12} className="fill-amber-500 text-amber-500" />
                            {term.examFrequencyCount}x
                          </span>
                        )}
                      </div>
                      <div className="mt-2 flex flex-wrap items-center gap-1.5">
                        <CategoryBadge category={term.category} size="sm" />
                      </div>
                      {definitionText && <p className="mt-3 text-sm leading-relaxed text-muted">{definitionText}</p>}
                      {term.exampleSentence && (
                        <p className="mt-2 text-xs italic leading-relaxed text-muted/80">{term.exampleSentence}</p>
                      )}
                    </article>
                  );
                })}
              </div>
              {catalogPageCount > 1 && (
                <Pagination
                  page={catalogPage}
                  pageSize={catalogPageSize}
                  total={catalogTotal}
                  onPageChange={(p) => goToCatalogPage(p)}
                  onPageSizeChange={(size) => { setCatalogPageSize(size); setCatalogPage(1); setCatalogLoading(true); }}
                  pageSizeOptions={[10, 24, 50, 100, 500]}
                  itemLabel="term"
                  resetOnPageSizeChange={false}
                />
              )}
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
                    aria-pressed={playingId === it.termId}
                    className="flex h-10 w-10 items-center justify-center rounded-full p-0"
                  >
                    {playingId === it.termId ? <PlayingBars /> : <Volume2 className="h-4 w-4" />}
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
                    {it.starred && <Badge variant="warning">Starred · {it.starReason ?? '–'}</Badge>}
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
            Nothing in your recall queue yet. Add words from the vocabulary library or complete a Listening drill; wrong
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
              className="inline-flex items-center justify-center rounded-xl bg-primary px-4 py-2 text-sm font-semibold text-white hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600"
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
