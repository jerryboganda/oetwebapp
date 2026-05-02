'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { motion } from 'motion/react';
import { Lock, Star, Volume2 } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Modal } from '@/components/ui/modal';
import {
  fetchRecallsToday,
  fetchRecallsQueue,
  starRecall,
  fetchRecallsAudio,
  isApiError,
  type RecallsTodayResponse,
  type RecallsQueueItem,
  type RecallsStarReason,
} from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { playTransientAudio } from '@/lib/recalls-audio';

const STAR_REASONS: { key: RecallsStarReason; label: string }[] = [
  { key: 'spelling', label: 'Spelling' },
  { key: 'pronunciation', label: 'Pronunciation' },
  { key: 'meaning', label: 'Meaning' },
  { key: 'hearing', label: 'Hearing' },
  { key: 'confused', label: 'Confused' },
];

/**
 * /recalls/words — vocabulary-side card list.
 *
 * Surfaces the queued cards (vocab + review) with a star toggle, audio button,
 * and direct link into the runner. The full quiz UX lives at /recalls/cards.
 */
export default function RecallsWordsPage() {
  const [today, setToday] = useState<RecallsTodayResponse | null>(null);
  const [items, setItems] = useState<RecallsQueueItem[] | null>(null);
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
  }, []);

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
                    <Star className="h-4 w-4" />
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
                      className="rounded-full border border-border px-3 py-1 text-xs font-medium text-muted hover:border-warning hover:text-warning"
                    >
                      Star
                    </button>
                  )}
                  {starOpenFor === it.id && (
                    <motion.div
                      initial={{ opacity: 0, y: -4 }}
                      animate={{ opacity: 1, y: 0 }}
                      className="absolute right-0 top-full z-10 mt-1 w-44 overflow-hidden rounded-xl border border-border bg-surface shadow-lg"
                    >
                      {STAR_REASONS.map((r) => (
                        <button
                          key={r.key}
                          type="button"
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
