'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { motion } from 'motion/react';
import { Brain, Layers, BookOpen, HelpCircle, Headphones, Flame, Sparkles, ArrowRight } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { RevisionPlanCard } from '@/components/domain/recalls/revision-plan-card';
import { WeeklyReportCard } from '@/components/domain/recalls/weekly-report-card';
import { fetchVocabularyStats, fetchReviewSummary } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { VocabularyStats } from '@/lib/types/vocabulary';

type ReviewSummary = { due: number; total: number; dueToday: number; mastered: number; upcoming?: number };

/**
 * Recalls — unified home for vocabulary cards and spaced-repetition review.
 *
 * Phase 0 (this PR): aggregator landing page that surfaces both today's vocab
 * due-set and today's generic review queue, and routes the learner into the
 * existing /vocabulary and /review flows. Subsequent phases migrate those
 * flows under /recalls/* per docs/RECALLS-MODULE-PLAN.md.
 */
export default function RecallsHomePage() {
  const [vocab, setVocab] = useState<VocabularyStats | null>(null);
  const [review, setReview] = useState<ReviewSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('recalls_home_viewed');
    Promise.allSettled([fetchVocabularyStats(), fetchReviewSummary()]).then(([v, r]) => {
      if (v.status === 'fulfilled') setVocab(v.value as VocabularyStats);
      if (r.status === 'fulfilled') setReview(r.value as ReviewSummary);
      if (v.status === 'rejected' && r.status === 'rejected') setError('Could not load your recall queue.');
      setLoading(false);
    });
  }, []);

  const dueToday = (vocab?.dueToday ?? 0) + (review?.dueToday ?? 0);
  const mastered = (vocab?.mastered ?? 0) + (review?.mastered ?? 0);
  const total = (vocab?.totalInList ?? 0) + (review?.total ?? 0);
  const streak = vocab?.streakDays ?? 0;

  const heroHighlights = [
    { icon: Brain, label: 'Due today', value: `${dueToday}` },
    { icon: Layers, label: 'Mastered', value: `${mastered}` },
    { icon: BookOpen, label: 'Total', value: `${total}` },
    { icon: Flame, label: 'Streak', value: `${streak}d` },
  ];

  const tabs = [
    {
      href: '/recalls/words',
      eyebrow: 'Words',
      title: 'Vocabulary banks & flashcards',
      description: 'Curated medical terms, daily set, browse, type-to-spell, and quiz formats.',
      icon: <BookOpen className="h-6 w-6" />,
      tile: 'bg-info/10 text-info',
      badge: vocab?.dueToday ? `${vocab.dueToday} due` : null,
    },
    {
      href: '/recalls/cards',
      eyebrow: 'Cards',
      title: 'Spaced-repetition review',
      description: 'Cards seeded from your listening drills, mocks, conversation issues, and writing feedback.',
      icon: <Brain className="h-6 w-6" />,
      tile: 'bg-primary/10 text-primary',
      badge: review?.dueToday ? `${review.dueToday} due` : null,
    },
    {
      href: '/recalls/cards?mode=listen_and_type',
      eyebrow: 'Quiz',
      title: 'Listen, recognise, type',
      description: 'Six quiz modes including listen-and-type and high-risk British spelling.',
      icon: <HelpCircle className="h-6 w-6" />,
      tile: 'bg-emerald-50 text-emerald-700',
      badge: null,
    },
    {
      href: '/recalls/library',
      eyebrow: 'Library',
      title: 'Mastery & weak areas',
      description: 'See what is improving, what is mastered, and which clinical topics need work.',
      icon: <Headphones className="h-6 w-6" />,
      tile: 'bg-purple-50 text-purple-700',
      badge: null,
    },
  ];

  return (
    <LearnerDashboardShell>
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Recalls"
          title="Everything you are trying to remember, in one place"
          description="Vocabulary cards and spaced-repetition review unified. Click. Listen. Type. Star. Revise. Master."
          icon={Sparkles}
          highlights={heroHighlights}
        />

        {error && <InlineAlert variant="warning">{error}</InlineAlert>}

        <LearnerSurfaceSectionHeader
          eyebrow="Today"
          title="Pick a mode to start your recall session"
          description="Each tab routes into the same SM-2 engine — your progress is shared across vocabulary and review."
          className="mb-4"
        />

        {loading ? (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            {Array.from({ length: 4 }).map((_, i) => (
              <Skeleton key={i} className="h-32 rounded-2xl" />
            ))}
          </div>
        ) : (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            {tabs.map((t) => (
              <Link key={t.href} href={t.href}>
                <motion.div
                  whileHover={{ scale: 1.01 }}
                  className="group flex h-full cursor-pointer items-start gap-4 rounded-2xl border border-border bg-surface p-5 shadow-sm transition-all hover:border-border-hover hover:shadow-md"
                >
                  <div className={`flex h-12 w-12 shrink-0 items-center justify-center rounded-xl ${t.tile}`}>
                    {t.icon}
                  </div>
                  <div className="flex-1">
                    <div className="text-xs font-semibold uppercase tracking-wide text-muted">{t.eyebrow}</div>
                    <div className="mt-1 flex items-center gap-2">
                      <span className="font-semibold text-navy">{t.title}</span>
                      {t.badge && (
                        <span className="rounded-full bg-warning/10 px-2 py-0.5 text-xs font-medium text-warning">
                          {t.badge}
                        </span>
                      )}
                    </div>
                    <p className="mt-1 text-sm text-muted">{t.description}</p>
                  </div>
                  <ArrowRight className="mt-1 h-5 w-5 shrink-0 text-muted transition-transform group-hover:translate-x-0.5" />
                </motion.div>
              </Link>
            ))}
          </div>
        )}

        <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
          <RevisionPlanCard />
          <WeeklyReportCard />
        </div>

      </div>
    </LearnerDashboardShell>
  );
}
