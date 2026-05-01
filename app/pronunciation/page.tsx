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
  easy: 'bg-emerald-50 text-emerald-700 border border-emerald-200',
  medium: 'bg-amber-50 text-amber-700 border border-amber-200',
  hard: 'bg-rose-50 text-rose-700 border border-rose-200',
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
                    <div className="group cursor-pointer rounded-[24px] border border-border bg-surface p-5 text-navy shadow-sm transition-all duration-200 hover:border-primary/40 hover:shadow-clinical">
                      <div className="flex items-center gap-3">
                        <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-[14px] bg-primary/10 text-primary">
                          <Mic className="h-5 w-5" aria-hidden />
                        </div>
                        <div className="flex-1 min-w-0">
                          <div className="truncate text-[15px] font-black text-navy">
                            {d.label}
                          </div>
                          <div className="mt-0.5 text-[13px] font-medium text-muted">
                            <span className="font-mono text-primary mr-1">/{d.targetPhoneme}/</span>
                            <span>&middot; {FOCUS_LABEL[d.focus] ?? d.focus} &middot; <span className="capitalize">{d.difficulty}</span></span>
                          </div>
                        </div>
                        <ChevronRight className="h-5 w-5 text-muted transition-colors group-hover:text-primary" aria-hidden />
                      </div>
                    </div>
                  </Link>
                </MotionItem>
              ))}
            </div>
          </section>
        )}

        <Card className="rounded-[24px] p-6 shadow-sm border border-border">
          <LearnerSurfaceSectionHeader
            eyebrow="Library filters"
            title="Adjust drill filters"
            description="Narrow by difficulty or the type of sound you want to work on."
            className="mb-5"
          />
          <div className="flex flex-col gap-6 sm:flex-row sm:items-start sm:gap-8">
            <div className="flex flex-col gap-2">
              <span className="text-[11px] font-bold uppercase tracking-[0.15em] text-muted">Difficulty</span>
              <div className="flex flex-wrap gap-2">
                {[
                  { id: '', label: 'All levels' },
                  { id: 'easy', label: 'Easy' },
                  { id: 'medium', label: 'Medium' },
                  { id: 'hard', label: 'Hard' },
                ].map((opt) => (
                  <button
                    key={opt.id}
                    onClick={() => setDifficulty(opt.id)}
                    className={`rounded-full px-4 py-2 text-sm font-semibold transition-all duration-200 ${
                      difficulty === opt.id
                        ? 'bg-primary text-white shadow-md'
                        : 'bg-lavender text-navy hover:bg-primary/15'
                    }`}
                  >
                    {opt.label}
                  </button>
                ))}
              </div>
            </div>
            
            <div className="flex flex-col gap-2">
              <span className="text-[11px] font-bold uppercase tracking-[0.15em] text-muted">Focus Area</span>
              <div className="flex flex-wrap gap-2">
                {[
                  { id: '', label: 'All areas' },
                  { id: 'phoneme', label: 'Phonemes' },
                  { id: 'cluster', label: 'Clusters' },
                  { id: 'stress', label: 'Word stress' },
                  { id: 'intonation', label: 'Intonation' },
                  { id: 'prosody', label: 'Prosody/Rhythm' },
                ].map((opt) => (
                  <button
                    key={opt.id}
                    onClick={() => setFocus(opt.id)}
                    className={`rounded-full px-4 py-2 text-sm font-semibold transition-all duration-200 ${
                      focus === opt.id
                        ? 'bg-primary text-white shadow-md'
                        : 'bg-lavender text-navy hover:bg-primary/15'
                    }`}
                  >
                    {opt.label}
                  </button>
                ))}
              </div>
            </div>
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
                    <div className="group cursor-pointer rounded-[24px] border border-border bg-surface p-6 text-navy shadow-sm transition-all duration-200 hover:border-primary/40 hover:shadow-clinical hover:-translate-y-0.5 active:scale-[0.99]">
                        <div className="flex items-start gap-4">
                          <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-[16px] bg-primary/10 text-primary transition-colors group-hover:bg-primary/20">
                            <Mic className="h-[22px] w-[22px]" aria-hidden />
                          </div>
                          <div className="flex-1 min-w-0">
                            <div className="flex items-center gap-2 mb-1.5 flex-wrap">
                              <span className="text-base font-black tracking-tight text-navy transition-colors group-hover:text-primary">
                                {drill.label}
                              </span>
                              <span className="rounded-[8px] bg-lavender px-2 py-0.5 font-mono text-[13px] font-bold text-primary">
                                /{drill.targetPhoneme}/
                              </span>
                              {drill.primaryRuleId && (
                                <span className="rounded-[8px] bg-lavender/60 px-2 py-0.5 font-mono text-[11px] font-medium text-muted">
                                  {drill.primaryRuleId}
                                </span>
                              )}
                            </div>
                            <div className="flex items-center gap-3 mt-1 flex-wrap">
                              <span className={`rounded-full px-2.5 py-0.5 text-[11px] font-bold uppercase tracking-wider ${DIFFICULTY_COLORS[drill.difficulty] ?? 'bg-lavender text-navy border border-border'}`}>
                                {drill.difficulty}
                              </span>
                              <span className="text-[13px] font-medium text-muted">{FOCUS_LABEL[drill.focus] ?? drill.focus}</span>
                              {prog && (
                                <div className="flex items-center gap-1.5 ml-1">
                                  <div className="h-1 w-1 rounded-full bg-border"></div>
                                  <span className="text-[13px] font-medium text-muted">
                                    <span className={prog.averageScore >= 70 ? 'text-success font-bold' : ''}>{Math.round(prog.averageScore)}%</span> avg · {prog.attemptCount} attempt{prog.attemptCount !== 1 ? 's' : ''}
                                  </span>
                                </div>
                              )}
                            </div>
                            {exampleWords.length > 0 && (
                              <div className="mt-4 flex flex-wrap gap-2">
                                {exampleWords.map((word) => (
                                  <span key={word} className="rounded-xl border border-border bg-lavender/40 px-3 py-1 text-[13px] font-semibold text-navy transition-colors group-hover:border-primary/30 group-hover:bg-primary/10 group-hover:text-primary">
                                    {word}
                                  </span>
                                ))}
                              </div>
                            )}
                          </div>
                          <ChevronRight className="mt-1 h-5 w-5 shrink-0 text-muted transition-all group-hover:translate-x-0.5 group-hover:text-primary" aria-hidden />
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
