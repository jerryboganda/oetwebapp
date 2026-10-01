'use client';

import { useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import {
  BookOpen,
  Brain,
  CalendarCheck,
  Headphones,
  RefreshCw,
  Trash2,
  Trophy,
  Volume2,
} from 'lucide-react';
import { toast } from 'sonner';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { CountUp } from '@/components/ui/count-up';
import { EmptyState } from '@/components/ui/empty-error';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { ProgressBar } from '@/components/ui/progress';
import { Skeleton } from '@/components/ui/skeleton';
import { StatCard } from '@/components/ui/stat-card';
import { useAuth } from '@/contexts/auth-context';
import {
  addPronunciationCard,
  getPronunciationCards,
  getPronunciationStats,
  removePronunciationCard,
  type PronunciationCardDto,
  type PronunciationStatsDto,
} from '@/lib/listening-pathway-api';

// ─────────────────────────────────────────────────────────────────────────────
// Pronunciation library hub — Phase 4 of OET_LISTENING_MODULE_PATHWAY §15.
//
// Mirrors the Reading vocab hub at app/reading/vocab/page.tsx: header KPIs,
// quick-action CTAs, "Add a word" form, and a grid of every card the learner
// has subscribed to (with SM-2 mastery progress bar + next-review date).
//
// Audio playback is wired through the master row's AudioBritishUrl /
// AudioAustralianUrl. When the audio asset is missing (typical for a fresh
// stub card) the speaker button is rendered disabled.
// ─────────────────────────────────────────────────────────────────────────────

/** DESIGN.md §7: the stat strip sizes to its container. */
const STAT_GRID = 'grid grid-cols-[repeat(auto-fit,minmax(min(100%,8.5rem),1fr))] gap-3';

function formatNextReview(iso: string | null): string {
  if (!iso) return 'soon';
  const at = new Date(iso);
  if (Number.isNaN(at.getTime())) return 'soon';
  const now = new Date();
  const diffDays = Math.round((at.getTime() - now.getTime()) / 86_400_000);
  if (diffDays < 0) return 'due now';
  if (diffDays === 0) return 'today';
  if (diffDays === 1) return 'tomorrow';
  if (diffDays < 7) return `in ${diffDays} days`;
  if (diffDays < 30) return `in ${Math.round(diffDays / 7)} weeks`;
  return `in ${Math.round(diffDays / 30)} months`;
}

function masteryPercent(card: PronunciationCardDto): number {
  // Combine SM-2 repetitions (0..4 ⇒ 0..80%) with the additive retention
  // score (0..100 ⇒ 0..20% boost) so a card with many reps + high retention
  // appears closer to fully mastered.
  const repsContribution = Math.min(card.repetitions, 4) * 20;
  const retentionContribution = Math.round(card.retentionScore / 5);
  return Math.min(100, repsContribution + retentionContribution);
}

export default function PronunciationHubPage() {
  const { isAuthenticated, loading: authLoading } = useAuth();
  const [stats, setStats] = useState<PronunciationStatsDto | null>(null);
  const [cards, setCards] = useState<PronunciationCardDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [newWord, setNewWord] = useState('');
  const [adding, setAdding] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  const audioRef = useRef<HTMLAudioElement | null>(null);

  async function refresh() {
    const [s, list] = await Promise.all([
      getPronunciationStats().catch(() => null),
      getPronunciationCards().catch(() => [] as PronunciationCardDto[]),
    ]);
    setStats(s);
    setCards(list);
  }

  useEffect(() => {
    if (authLoading) return;
    if (!isAuthenticated) {
      setLoading(false);
      return;
    }
    let cancelled = false;
    (async () => {
      try {
        setLoading(true);
        await refresh();
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [authLoading, isAuthenticated]);

  async function handleAdd() {
    const word = newWord.trim();
    if (!word) return;
    setAdding(true);
    try {
      await addPronunciationCard(word, 'manual');
      setNewWord('');
      toast.success(`"${word}" added to your pronunciation deck.`);
      inputRef.current?.focus();
      await refresh();
    } catch {
      toast.error('Could not add word. Please try again.');
    } finally {
      setAdding(false);
    }
  }

  async function handleRemove(card: PronunciationCardDto) {
    try {
      await removePronunciationCard(card.id);
      toast.success(`Removed "${card.word}" from your deck.`);
      await refresh();
    } catch {
      toast.error('Could not remove this card.');
    }
  }

  function handlePlay(url: string | null) {
    if (!url) return;
    if (!audioRef.current) audioRef.current = new Audio();
    audioRef.current.src = url;
    audioRef.current.play().catch(() => {
      toast.error('Audio playback failed.');
    });
  }

  // Counts, not statuses: one neutral tile style, as on the Reading vocab hub.
  const count = (value: number | undefined) => (value == null ? '-' : <CountUp value={value} />);
  const statCards = [
    { label: 'Total Cards', value: count(stats?.total), icon: BookOpen },
    { label: 'Mastered', value: count(stats?.mastered), icon: Trophy },
    { label: 'Due Today', value: count(stats?.dueToday), icon: CalendarCheck },
    { label: 'Struggling', value: count(stats?.struggling), icon: Brain },
  ];

  return (
    <>
      <LearnerPageHero
        eyebrow="SM-2 Spaced Repetition"
        icon={Headphones}
        accent="purple"
        title="Pronunciation Library"
        description="Train your ear on healthcare vocabulary. Listen, repeat, and let SM-2 schedule the next review for maximum retention."
      />

      {/* Stats strip */}
      <section>
        {loading ? (
          <div className={STAT_GRID}>
            {[0, 1, 2, 3].map((i) => (
              <Skeleton key={i} className="h-24 rounded-xl" />
            ))}
          </div>
        ) : (
          <div className={STAT_GRID}>
            {statCards.map(({ label, value, icon: Icon }, index) => (
              <MotionItem key={label} delayIndex={index}>
                <StatCard label={label} value={value} icon={<Icon />} />
              </MotionItem>
            ))}
          </div>
        )}
      </section>

      {/* Quick actions */}
      <section className="flex flex-wrap gap-3">
        <Button asChild size="lg">
          <Link href="/listening/pronunciation/review">
            <RefreshCw className="h-4 w-4" aria-hidden />
            Review Today&apos;s Cards
            {stats?.dueToday ? (
              <span className="ms-1 rounded-full bg-white/20 px-2 py-0.5 text-xs tabular-nums">{stats.dueToday}</span>
            ) : null}
          </Link>
        </Button>
        <Button asChild size="lg" variant="outline">
          <Link href="/listening">
            <BookOpen className="h-4 w-4" aria-hidden />
            Back to Listening Hub
          </Link>
        </Button>
      </section>

      {/* Add a word */}
      <MotionSection>
        <Card padding="lg">
          <h2 id="pronunciation-add-word-title" className="mb-3 text-lg font-bold text-navy">Add a Word</h2>
          <div className="flex gap-3">
            <input
              ref={inputRef}
              type="text"
              aria-labelledby="pronunciation-add-word-title"
              value={newWord}
              onChange={(e) => setNewWord(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') void handleAdd();
              }}
              placeholder="e.g. dyspnoea"
              className="min-h-11 min-w-0 flex-1 rounded-control border border-border bg-background-light px-4 py-2.5 text-sm text-navy placeholder:text-muted focus:border-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/30"
            />
            <Button
              disabled={adding || !newWord.trim()}
              onClick={() => void handleAdd()}
            >
              {adding ? 'Adding…' : 'Add'}
            </Button>
          </div>
          <p className="mt-2 text-xs text-muted">
            New cards are due immediately so you can practise them right after adding.
          </p>
        </Card>
      </MotionSection>

      {/* Card grid */}
      <section aria-label="Your Cards">
        <LearnerSurfaceSectionHeader
          title="Your Cards"
          className="mb-4"
          action={cards.length > 0 ? <p className="text-xs tabular-nums text-muted">{cards.length} total</p> : null}
        />

        {loading ? (
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {[0, 1, 2].map((i) => (
              <Skeleton key={i} className="h-32 rounded-xl" />
            ))}
          </div>
        ) : cards.length === 0 ? (
          <EmptyState
            icon={<Headphones className="h-8 w-8" aria-hidden />}
            title="No pronunciation cards yet"
            description="Add a word above to start training your ear on healthcare vocabulary."
            action={{ label: 'Add Your First Word', onClick: () => inputRef.current?.focus() }}
          />
        ) : (
          <ul className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {cards.map((card, index) => {
              const mastery = masteryPercent(card);
              const audioUrl = card.audioBritishUrl ?? card.audioAustralianUrl ?? null;
              return (
                <li key={card.id}>
                  <MotionItem delayIndex={Math.min(index, 5)} className="h-full">
                    {/* Not clickable itself (its buttons are), so no hover lift. */}
                    <Card className="group h-full">
                      <div className="flex items-start justify-between gap-2">
                        <div className="min-w-0 flex-1">
                          <h3 className="break-words text-lg font-semibold text-navy">
                            {card.word}
                          </h3>
                          {card.pronunciationIpa ? (
                            <p className="mt-0.5 break-words font-mono text-xs text-primary">
                              {card.pronunciationIpa}
                            </p>
                          ) : (
                            <p className="mt-0.5 text-xs text-muted">
                              IPA pending
                            </p>
                          )}
                        </div>
                        <div className="flex shrink-0 items-center gap-1">
                          <button
                            type="button"
                            disabled={!audioUrl}
                            onClick={() => handlePlay(audioUrl)}
                            className="inline-flex h-11 w-11 items-center justify-center rounded-control border border-primary/20 bg-lavender text-primary transition-colors hover:bg-primary/15 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-40"
                            aria-label={`Play pronunciation of ${card.word}`}
                          >
                            <Volume2 className="h-4 w-4" aria-hidden />
                          </button>
                          <button
                            type="button"
                            onClick={() => void handleRemove(card)}
                            className="inline-flex h-11 w-11 items-center justify-center rounded-control border border-border bg-surface text-muted transition-colors hover:border-danger/30 hover:bg-danger/10 hover:text-danger-strong focus-visible:opacity-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary pointer-fine:opacity-0 pointer-fine:group-hover:opacity-100"
                            aria-label={`Remove ${card.word} from deck`}
                          >
                            <Trash2 className="h-4 w-4" aria-hidden />
                          </button>
                        </div>
                      </div>

                      {card.definitionEn ? (
                        <p className="mt-2 line-clamp-2 text-xs text-muted">
                          {card.definitionEn}
                        </p>
                      ) : null}

                      {/* Mastery progress */}
                      <div className="mt-3">
                        <div className="mb-1 flex items-center justify-between text-xs">
                          <span className="font-medium text-muted">Mastery</span>
                          <span className="font-semibold tabular-nums text-primary">{mastery}%</span>
                        </div>
                        <ProgressBar value={mastery} ariaLabel={`${card.word} mastery`} />
                      </div>

                      <div className="mt-3 flex items-center justify-between gap-2 text-xs text-muted">
                        <span className="tabular-nums">{card.repetitions} reviews</span>
                        <span>Next: {formatNextReview(card.nextReviewAt)}</span>
                      </div>
                    </Card>
                  </MotionItem>
                </li>
              );
            })}
          </ul>
        )}
      </section>
    </>
  );
}
