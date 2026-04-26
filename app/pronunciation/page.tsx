'use client';

import { useEffect, useState } from 'react';
import { Mic, ChevronRight, Clock } from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import {
  fetchPronunciationDrills,
  fetchMyPronunciationProgress,
  fetchPronunciationDueDrills,
  fetchPronunciationEntitlement,
  type PronunciationDrillSummary,
  type PronunciationProgressItem,
  type PronunciationEntitlement,
} from '@/lib/api';
import { analytics } from '@/lib/analytics';

const DIFFICULTY_COLORS: Record<string, string> = {
  easy: 'bg-success/10 text-success border border-success/20',
  medium: 'bg-warning/10 text-warning border border-warning/20',
  hard: 'bg-danger/10 text-danger border border-danger/20',
};

const FOCUS_LABEL: Record<string, string> = {
  phoneme: 'Phoneme',
  cluster: 'Cluster',
  stress: 'Word stress',
  intonation: 'Intonation',
  prosody: 'Prosody',
  discrimination: 'Listening',
};

function parseExampleWords(value: string) {
  try {
    const parsed = JSON.parse(value) as unknown;
    return Array.isArray(parsed)
      ? parsed.filter((word): word is string => typeof word === 'string')
      : [];
  } catch {
    return [];
  }
}

export default function PronunciationPage() {
  const [drills, setDrills] = useState<PronunciationDrillSummary[]>([]);
  const [due, setDue] = useState<PronunciationDrillSummary[]>([]);
  const [progressMap, setProgressMap] = useState<Record<string, PronunciationProgressItem>>({});
  const [entitlement, setEntitlement] = useState<PronunciationEntitlement | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [difficulty, setDifficulty] = useState('');
  const [focus, setFocus] = useState('');

  const heroHighlights = [
    { icon: Mic, label: 'Focus', value: focus ? FOCUS_LABEL[focus] ?? focus : 'All sounds' },
    { icon: ChevronRight, label: 'Level', value: difficulty || 'All levels' },
    { icon: Clock, label: 'Due today', value: `${due.length} drill${due.length === 1 ? '' : 's'}` },
  ];

  useEffect(() => { analytics.track('pronunciation_page_viewed'); }, []);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      setLoading(true);
      setError(null);
      const [drillsR, progressR, dueR, entR] = await Promise.allSettled([
        fetchPronunciationDrills({
          difficulty: difficulty || undefined,
          focus: focus || undefined,
        }),
        fetchMyPronunciationProgress(),
        fetchPronunciationDueDrills(6),
        fetchPronunciationEntitlement(),
      ]);
      if (cancelled) return;
      if (drillsR.status === 'fulfilled') setDrills(drillsR.value as PronunciationDrillSummary[]);
      if (progressR.status === 'fulfilled') {
        const items = progressR.value as PronunciationProgressItem[];
        setProgressMap(Object.fromEntries(items.map((p) => [p.phonemeCode, p])));
      }
      if (dueR.status === 'fulfilled') setDue(dueR.value as PronunciationDrillSummary[]);
      if (entR.status === 'fulfilled') setEntitlement(entR.value as PronunciationEntitlement);
      if (drillsR.status === 'rejected') setError('Could not load pronunciation drills.');
      setLoading(false);
    })();
    return () => { cancelled = true; };
  }, [difficulty, focus]);

  return (
    <LearnerDashboardShell>
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Pronunciation Practice"
          title="Sharpen the sounds that lift your Speaking band"
          description="Record yourself, get phoneme-level AI feedback, and track your projected Speaking score with every drill."
          icon={Mic}
          highlights={heroHighlights}
        />

        {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

        {entitlement && entitlement.tier === 'free' && (
          <InlineAlert variant={entitlement.allowed ? 'info' : 'warning'}>
            {entitlement.reason}
          </InlineAlert>
        )}

        {due.length > 0 && (
          <section aria-labelledby="due-today-heading">
            <LearnerSurfaceSectionHeader
              eyebrow="Today"
              title="Due for practice"
              description="Spaced-repetition queue based on your weakest phonemes."
              className="mb-3"
            />
            <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
              {due.map((d, i) => (
                <MotionItem key={d.id} delayIndex={i}>
                  <Link href={`/pronunciation/${d.id}`}>
                    <div className="group cursor-pointer rounded-2xl border border-primary/30 bg-primary/5 p-4 shadow-sm transition hover:border-primary/60">
                      <div className="flex items-center gap-2">
                        <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-primary text-white">
                          <Mic className="h-4 w-4" aria-hidden />
                        </div>
                        <div className="flex-1 min-w-0">
                          <div className="text-sm font-semibold text-navy dark:text-white truncate">
                            {d.label}
                          </div>
                          <div className="text-xs text-muted">
                            /{d.targetPhoneme}/ · {FOCUS_LABEL[d.focus] ?? d.focus} · {d.difficulty}
                          </div>
                        </div>
                        <ChevronRight className="h-4 w-4 text-muted" aria-hidden />
                      </div>
                    </div>
                  </Link>
                </MotionItem>
              ))}
            </div>
          </section>
        )}

        <Card className="p-5 shadow-sm">
          <LearnerSurfaceSectionHeader
            eyebrow="Library filters"
            title="Adjust drill filters"
            description="Narrow by difficulty or the type of sound you want to work on."
            className="mb-4"
          />
          <div className="flex flex-wrap gap-3">
            <label className="flex flex-col text-xs uppercase tracking-[0.15em] text-muted">
              Difficulty
              <select
                value={difficulty}
                onChange={(e) => setDifficulty(e.target.value)}
                className="mt-1 rounded-2xl border border-border bg-surface px-4 py-3 text-sm text-navy shadow-sm focus:outline-none focus:ring-2 focus:ring-primary focus:ring-offset-2 dark:text-white"
                aria-label="Filter drills by difficulty"
              >
                <option value="">All levels</option>
                <option value="easy">Easy</option>
                <option value="medium">Medium</option>
                <option value="hard">Hard</option>
              </select>
            </label>
            <label className="flex flex-col text-xs uppercase tracking-[0.15em] text-muted">
              Focus
              <select
                value={focus}
                onChange={(e) => setFocus(e.target.value)}
                className="mt-1 rounded-2xl border border-border bg-surface px-4 py-3 text-sm text-navy shadow-sm focus:outline-none focus:ring-2 focus:ring-primary focus:ring-offset-2 dark:text-white"
                aria-label="Filter drills by focus area"
              >
                <option value="">All focus areas</option>
                <option value="phoneme">Phonemes</option>
                <option value="cluster">Consonant clusters</option>
                <option value="stress">Word stress</option>
                <option value="intonation">Intonation</option>
                <option value="prosody">Prosody / rhythm</option>
              </select>
            </label>
          </div>
        </Card>

        {loading ? (
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            {Array.from({ length: 6 }).map((_, i) => (
              <Skeleton key={i} className="h-28 rounded-[24px]" />
            ))}
          </div>
        ) : drills.length === 0 ? (
          <Card className="border-dashed border-border p-8 text-center shadow-sm">
            <p className="text-sm text-muted">No pronunciation drills match these filters.</p>
          </Card>
        ) : (
          <div>
            <LearnerSurfaceSectionHeader
              eyebrow="Drills"
              title="Pronunciation drill cards"
              description="Each drill shows difficulty, focus, rule, and your latest average."
              className="mb-4"
            />
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
              {drills.map((drill, i) => {
                const prog = progressMap[drill.targetPhoneme];
                const exampleWords = parseExampleWords(drill.exampleWordsJson).slice(0, 4);
                return (
                  <MotionItem key={drill.id} delayIndex={i}>
                    <Link
                      href={`/pronunciation/${drill.id}`}
                      aria-label={`Open pronunciation drill ${drill.label}`}
                    >
                      <div className="group cursor-pointer rounded-[24px] border border-border bg-surface p-5 shadow-sm transition-[border-color,box-shadow,transform] duration-200 hover:border-border-hover hover:shadow-clinical active:scale-[0.99]">
                        <div className="flex items-start gap-3">
                          <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl bg-rose-50 text-rose-600">
                            <Mic className="h-5 w-5" aria-hidden />
                          </div>
                          <div className="flex-1 min-w-0">
                            <div className="flex items-center gap-2 mb-1 flex-wrap">
                              <span className="font-semibold text-navy transition-colors group-hover:text-rose-600 dark:text-white">
                                {drill.label}
                              </span>
                              <span className="rounded bg-background-light px-1.5 font-mono text-sm text-muted">
                                /{drill.targetPhoneme}/
                              </span>
                              {drill.primaryRuleId && (
                                <span className="rounded bg-primary/10 px-1.5 font-mono text-xs text-primary">
                                  {drill.primaryRuleId}
                                </span>
                              )}
                            </div>
                            <div className="flex items-center gap-3 mt-2 flex-wrap">
                              <span className={`rounded-full px-2 py-0.5 text-xs font-medium capitalize ${DIFFICULTY_COLORS[drill.difficulty] ?? 'bg-background-light text-muted border border-border'}`}>
                                {drill.difficulty}
                              </span>
                              <span className="text-xs text-muted">{FOCUS_LABEL[drill.focus] ?? drill.focus}</span>
                              {prog && (
                                <span className="text-xs text-muted">
                                  {Math.round(prog.averageScore)}% avg · {prog.attemptCount} attempt{prog.attemptCount !== 1 ? 's' : ''}
                                </span>
                              )}
                            </div>
                            {exampleWords.length > 0 && (
                              <div className="mt-3 flex flex-wrap gap-2">
                                {exampleWords.map((word) => (
                                  <span key={word} className="rounded-full bg-background-light px-2.5 py-1 text-xs font-medium text-muted">
                                    {word}
                                  </span>
                                ))}
                              </div>
                            )}
                          </div>
                          <ChevronRight className="mt-1 h-4 w-4 shrink-0 text-muted transition-colors group-hover:text-rose-400" aria-hidden />
                        </div>
                      </div>
                    </Link>
                  </MotionItem>
                );
              })}
            </div>
          </div>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
