'use client';

import { useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import { ArrowRight, BookOpen, Brain, CalendarCheck, RefreshCw, Trophy } from 'lucide-react';
import { toast } from 'sonner';
import { isApiError } from '@/lib/api';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { CountUp } from '@/components/ui/count-up';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { StatCard } from '@/components/ui/stat-card';
import { useAuth } from '@/contexts/auth-context';
import {
  addVocabWord,
  getVocabDue,
  getVocabStats,
  type VocabItemDto,
  type VocabStatsDto,
} from '@/lib/reading-pathway-api';

export default function VocabHubPage() {
  const { isAuthenticated, loading: authLoading } = useAuth();
  const [stats, setStats] = useState<VocabStatsDto | null>(null);
  const [dueItems, setDueItems] = useState<VocabItemDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [newWord, setNewWord] = useState('');
  const [addingWord, setAddingWord] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (authLoading) return;
    if (!isAuthenticated) { setLoading(false); return; }

    let cancelled = false;
    (async () => {
      try {
        setLoading(true);
        const [s, due] = await Promise.all([getVocabStats(), getVocabDue()]);
        if (cancelled) return;
        setStats(s);
        setDueItems(due);
      } catch {
        // stats are non-blocking
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [authLoading, isAuthenticated]);

  async function handleAddWord() {
    const word = newWord.trim();
    if (!word) return;
    setAddingWord(true);
    try {
      await addVocabWord(word, 'manual');
      setNewWord('');
      toast.success(`"${word}" added to your deck.`);
      inputRef.current?.focus();
      const [s, due] = await Promise.all([getVocabStats(), getVocabDue()]);
      setStats(s);
      setDueItems(due);
    } catch (error) {
      if (isApiError(error) && (error.status === 503 || error.code === 'vocabulary_generation_unavailable')) {
        toast.error(`A definition for "${word}" is unavailable right now. No card was stored. Try again later.`);
      } else {
        toast.error('Could not add word. Please try again.');
      }
    } finally {
      setAddingWord(false);
    }
  }

  // One neutral tile style: these are counts, not statuses, so no status tints.
  const countValue = (value: number | undefined) => (value == null ? '–' : <CountUp value={value} />);
  const statCards = [
    { label: 'Total Words', value: countValue(stats?.total), icon: BookOpen },
    { label: 'Mastered', value: countValue(stats?.mastered), icon: Trophy },
    { label: 'Due Today', value: countValue(stats?.dueToday), icon: CalendarCheck },
    { label: 'Avg Retention', value: stats ? <CountUp value={Math.round(stats.averageRetention)} suffix="%" /> : '–', icon: Brain },
  ];

  return (
    <>
      <LearnerPageHero
        eyebrow="SM-2 Spaced Repetition"
        icon={BookOpen}
        title="Vocabulary Builder"
        description="Build your OET medical vocabulary with evidence-based spaced repetition. Review daily to maximise retention."
      />

      {/* Stats strip: sized to its container, so the tiles always fill the row. */}
      <section>
        {loading ? (
          <div className="grid grid-cols-[repeat(auto-fit,minmax(min(100%,8.5rem),1fr))] gap-3">
            {[0, 1, 2, 3].map((i) => (
              <Skeleton key={i} className="h-24 rounded-xl" />
            ))}
          </div>
        ) : (
          <div className="grid grid-cols-[repeat(auto-fit,minmax(min(100%,8.5rem),1fr))] gap-3">
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
          <Link href="/reading/vocab/review">
            <RefreshCw className="h-4 w-4" aria-hidden />
            Review Today&apos;s Cards
            {stats?.dueToday ? (
              <span className="ms-1 rounded-full bg-white/20 px-2 py-0.5 text-xs tabular-nums">
                {stats.dueToday}
              </span>
            ) : null}
          </Link>
        </Button>
        <Button asChild size="lg" variant="outline">
          <Link href="/reading/vocab/lists">
            <BookOpen className="h-4 w-4" aria-hidden />
            Browse Lists
          </Link>
        </Button>
        <Button asChild size="lg" variant="outline">
          <Link href="/reading/vocab/stats">
            <Brain className="h-4 w-4" aria-hidden />
            View Stats
          </Link>
        </Button>
      </section>

      {/* Add a word */}
      <MotionSection>
        <Card padding="lg">
          <h2 id="vocab-add-word-title" className="mb-3 text-lg font-bold text-navy">
            Add a Word
          </h2>
          <div className="flex gap-3">
            <input
              ref={inputRef}
              type="text"
              aria-labelledby="vocab-add-word-title"
              value={newWord}
              onChange={(e) => setNewWord(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') void handleAddWord(); }}
              placeholder="e.g. haemoglobin"
              className="min-h-11 min-w-0 flex-1 rounded-control border border-border bg-background-light px-4 py-2.5 text-sm text-navy placeholder:text-muted focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20"
            />
            <Button
              disabled={addingWord || !newWord.trim()}
              onClick={() => void handleAddWord()}
            >
              {addingWord ? 'Adding…' : 'Add'}
            </Button>
          </div>
        </Card>
      </MotionSection>

      {/* Words due today preview */}
      {dueItems.length > 0 ? (
        <MotionSection>
          <section aria-label="Words Due Today">
            <LearnerSurfaceSectionHeader
              title="Words Due Today"
              className="mb-4"
              action={(
                <Button asChild variant="ghost" size="sm" className="self-start text-primary sm:self-auto">
                  <Link href="/reading/vocab/review">
                    Review All <ArrowRight className="h-4 w-4 rtl:rotate-180" aria-hidden />
                  </Link>
                </Button>
              )}
            />
            <ul className="grid grid-cols-1 gap-3 sm:grid-cols-2 md:grid-cols-3">
              {dueItems.slice(0, 5).map((item, index) => (
                <li key={item.id}>
                  <MotionItem delayIndex={Math.min(index, 5)} className="h-full">
                    <Card padding="sm" className="h-full">
                      <p className="font-semibold text-navy">{item.word}</p>
                      <p className="mt-0.5 line-clamp-2 text-xs text-muted">
                        {item.definitionEn}
                      </p>
                    </Card>
                  </MotionItem>
                </li>
              ))}
            </ul>
          </section>
        </MotionSection>
      ) : null}
    </>
  );
}
