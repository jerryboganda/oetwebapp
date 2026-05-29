'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import Link from 'next/link';
import {
  AlertCircle,
  CheckCircle2,
  Headphones,
  Loader2,
  Pause,
  Play,
  RefreshCw,
  Sparkles,
  Trophy,
} from 'lucide-react';
import { toast } from 'sonner';
import { LearnerDashboardShell } from '@/components/layout';
import { useAuth } from '@/contexts/auth-context';
import {
  getDictationStats,
  startDictationSession,
  submitDictationAnswer,
  type DictationDrillDto,
  type DictationResult,
  type DictationStats,
} from '@/lib/listening-pathway-api';

// ─────────────────────────────────────────────────────────────────────────────
// Dictation Drill page — Phase 4 of OET_LISTENING_MODULE_PATHWAY.md §14.
//
// Flow:
//   1. Render header KPIs (Mastered / Struggling / Accuracy).
//   2. Click "Start a dictation set" → fetch 8 drills, render the first.
//   3. For each drill: audio player, text input, Submit button. After 60s a
//      "Need a hint? Keep listening" line appears (no answer leak).
//   4. After Submit: reveal canonical transcript, show a diff vs the typed
//      answer, then Next → advance to drill i+1.
//   5. After the final drill: complete screen with "X correct / Y", a "Start
//      another set" CTA, and a link back to the listening hub.
// ─────────────────────────────────────────────────────────────────────────────

const HINT_AFTER_SECONDS = 60;

interface AttemptRecord {
  drillId: string;
  result: DictationResult;
}

export default function DictationDrillPage() {
  const { isAuthenticated, loading: authLoading } = useAuth();

  const [stats, setStats] = useState<DictationStats | null>(null);
  const [statsLoading, setStatsLoading] = useState(true);
  const [drills, setDrills] = useState<DictationDrillDto[]>([]);
  const [currentIndex, setCurrentIndex] = useState(0);
  const [phase, setPhase] = useState<'idle' | 'starting' | 'attempting' | 'reviewing' | 'complete'>('idle');
  const [answer, setAnswer] = useState('');
  const [currentResult, setCurrentResult] = useState<DictationResult | null>(null);
  const [history, setHistory] = useState<AttemptRecord[]>([]);
  const [submitting, setSubmitting] = useState(false);
  const [secondsOnDrill, setSecondsOnDrill] = useState(0);
  const [audioPlaying, setAudioPlaying] = useState(false);

  const audioRef = useRef<HTMLAudioElement | null>(null);
  const inputRef = useRef<HTMLInputElement | null>(null);
  const tickRef = useRef<ReturnType<typeof setInterval> | null>(null);

  // Load stats on mount.
  useEffect(() => {
    if (authLoading) return;
    if (!isAuthenticated) {
      setStatsLoading(false);
      return;
    }
    let cancelled = false;
    (async () => {
      try {
        setStatsLoading(true);
        const s = await getDictationStats();
        if (!cancelled) setStats(s);
      } catch {
        // Stats are non-blocking — show "—" placeholders rather than block UI.
      } finally {
        if (!cancelled) setStatsLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [authLoading, isAuthenticated]);

  // Tick a seconds counter while the learner is composing an answer so we can
  // surface the "need a hint?" affordance after 60s.
  useEffect(() => {
    if (phase !== 'attempting') {
      if (tickRef.current) {
        clearInterval(tickRef.current);
        tickRef.current = null;
      }
      return;
    }
    setSecondsOnDrill(0);
    tickRef.current = setInterval(() => {
      setSecondsOnDrill((prev) => prev + 1);
    }, 1000);
    return () => {
      if (tickRef.current) {
        clearInterval(tickRef.current);
        tickRef.current = null;
      }
    };
  }, [phase, currentIndex]);

  // Focus the input each time the learner moves onto a new attempt.
  useEffect(() => {
    if (phase === 'attempting') {
      const t = window.setTimeout(() => inputRef.current?.focus(), 80);
      return () => window.clearTimeout(t);
    }
  }, [phase, currentIndex]);

  const currentDrill = drills[currentIndex] ?? null;

  const handleStart = useCallback(async () => {
    setPhase('starting');
    try {
      const next = await startDictationSession(8);
      if (next.length === 0) {
        toast.info('No drills available yet. Try again once content is published.');
        setPhase('idle');
        return;
      }
      setDrills(next);
      setCurrentIndex(0);
      setHistory([]);
      setAnswer('');
      setCurrentResult(null);
      setPhase('attempting');
    } catch (error) {
      console.error('Failed to start dictation set', error);
      toast.error('Could not load a dictation set. Please try again.');
      setPhase('idle');
    }
  }, []);

  const playAudio = useCallback(() => {
    const audio = audioRef.current;
    if (!audio) return;
    if (audio.paused) {
      void audio.play().catch(() => {
        toast.error('Could not play the audio clip.');
      });
    } else {
      audio.pause();
    }
  }, []);

  const handleSubmit = useCallback(async () => {
    if (!currentDrill || submitting) return;
    setSubmitting(true);
    try {
      const result = await submitDictationAnswer(currentDrill.id, answer);
      setCurrentResult(result);
      setHistory((prev) => [...prev, { drillId: currentDrill.id, result }]);
      setPhase('reviewing');
    } catch (error) {
      console.error('Failed to submit dictation answer', error);
      toast.error('Could not submit your answer. Please retry.');
    } finally {
      setSubmitting(false);
    }
  }, [answer, currentDrill, submitting]);

  const handleNext = useCallback(() => {
    const nextIndex = currentIndex + 1;
    if (nextIndex >= drills.length) {
      setPhase('complete');
      // Refresh aggregate stats in the background — non-blocking.
      void getDictationStats()
        .then((s) => setStats(s))
        .catch(() => undefined);
      return;
    }
    setCurrentIndex(nextIndex);
    setAnswer('');
    setCurrentResult(null);
    setPhase('attempting');
  }, [currentIndex, drills.length]);

  const handleRestart = useCallback(() => {
    setDrills([]);
    setCurrentIndex(0);
    setHistory([]);
    setAnswer('');
    setCurrentResult(null);
    setPhase('idle');
  }, []);

  const totalAnsweredCorrectly = useMemo(
    () => history.filter((h) => h.result.isCorrect).length,
    [history],
  );

  // ────────── Render ──────────

  return (
    <LearnerDashboardShell pageTitle="Dictation Drills">
      <main className="space-y-8">
        <section className="rounded-2xl border border-violet-200 bg-violet-50 px-8 py-7 dark:border-violet-900/50 dark:bg-violet-950/30">
          <p className="mb-1 text-xs font-semibold uppercase tracking-widest text-violet-500">
            Phase 4 · Listening pathway
          </p>
          <h1 className="text-2xl font-bold text-navy">
            Dictation Drills
          </h1>
          <p className="mt-1 text-sm text-muted">
            Train your ear and your spelling at the same time. We grade healthcare
            vocabulary with typo tolerance, and every correct keystroke counts.
          </p>
        </section>

        <StatsStrip stats={stats} loading={statsLoading} />

        {phase === 'idle' && (
          <IdlePanel onStart={handleStart} hasHistory={(stats?.totalAttempted ?? 0) > 0} />
        )}

        {phase === 'starting' && <StartingPanel />}

        {(phase === 'attempting' || phase === 'reviewing') && currentDrill && (
          <DrillPanel
            drill={currentDrill}
            index={currentIndex}
            total={drills.length}
            phase={phase}
            answer={answer}
            onAnswerChange={setAnswer}
            onSubmit={handleSubmit}
            onNext={handleNext}
            audioRef={audioRef}
            inputRef={inputRef}
            audioPlaying={audioPlaying}
            onAudioPlay={playAudio}
            onAudioStateChange={setAudioPlaying}
            secondsOnDrill={secondsOnDrill}
            submitting={submitting}
            result={currentResult}
          />
        )}

        {phase === 'complete' && (
          <CompletePanel
            correct={totalAnsweredCorrectly}
            total={drills.length}
            onAgain={handleStart}
            onRestart={handleRestart}
          />
        )}
      </main>
    </LearnerDashboardShell>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Sub-components
// ─────────────────────────────────────────────────────────────────────────────

function StatsStrip({ stats, loading }: { stats: DictationStats | null; loading: boolean }) {
  const cards = [
    { label: 'Mastered', value: stats?.mastered ?? '—', icon: Trophy, accent: 'emerald' },
    { label: 'Struggling', value: stats?.struggling ?? '—', icon: AlertCircle, accent: 'amber' },
    {
      label: 'Accuracy',
      value: stats ? `${Math.round(stats.accuracyPercentage)}%` : '—',
      icon: CheckCircle2,
      accent: 'violet',
    },
    {
      label: 'Total attempts',
      value: stats?.totalAttempted ?? '—',
      icon: Sparkles,
      accent: 'blue',
    },
  ] as const;

  const accentMap: Record<string, string> = {
    emerald:
      'bg-emerald-50 border-emerald-200 text-emerald-700 dark:bg-emerald-950/40 dark:border-emerald-800/50 dark:text-emerald-300',
    amber:
      'bg-amber-50 border-amber-200 text-amber-700 dark:bg-amber-950/40 dark:border-amber-800/50 dark:text-amber-300',
    violet:
      'bg-violet-50 border-violet-200 text-violet-700 dark:bg-violet-950/40 dark:border-violet-800/50 dark:text-violet-300',
    blue: 'bg-blue-50 border-blue-200 text-blue-700 dark:bg-blue-950/40 dark:border-blue-800/50 dark:text-blue-300',
  };

  if (loading) {
    return (
      <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
        {[0, 1, 2, 3].map((i) => (
          <div
            key={i}
            className="h-24 motion-safe:animate-pulse rounded-xl border border-border bg-border/40"
          />
        ))}
      </div>
    );
  }

  return (
    <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
      {cards.map(({ label, value, icon: Icon, accent }) => (
        <div
          key={label}
          className={`flex flex-col gap-2 rounded-xl border px-5 py-4 ${accentMap[accent]}`}
        >
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold uppercase tracking-wide opacity-70">{label}</span>
            <Icon className="h-4 w-4 opacity-60" aria-hidden />
          </div>
          <p className="text-2xl font-bold">{value}</p>
        </div>
      ))}
    </div>
  );
}

function IdlePanel({ onStart, hasHistory }: { onStart: () => void; hasHistory: boolean }) {
  return (
    <section className="rounded-2xl border border-border bg-surface px-8 py-10 text-center">
      <Headphones className="mx-auto h-12 w-12 text-violet-500" aria-hidden />
      <h2 className="mt-4 text-xl font-bold text-navy">
        {hasHistory ? 'Ready for another set?' : 'Start your first dictation set'}
      </h2>
      <p className="mx-auto mt-2 max-w-md text-sm text-muted">
        We&apos;ll play 8 short healthcare clips. Type what you hear: single terms,
        short phrases, full sentences. Mix of due reviews and fresh drills.
      </p>
      <button
        type="button"
        onClick={onStart}
        className="mt-6 inline-flex items-center gap-2 rounded-xl bg-primary px-6 py-3 text-sm font-semibold text-white shadow-sm transition-colors hover:bg-primary-dark dark:bg-violet-700 dark:hover:bg-violet-600 active:scale-95"
      >
        <Play className="h-4 w-4" aria-hidden />
        Start a dictation set
      </button>
      <p className="mt-3 text-xs text-muted">
        Takes about 8&ndash;10 minutes.
      </p>
    </section>
  );
}

function StartingPanel() {
  return (
    <section className="flex h-48 items-center justify-center rounded-2xl border border-border bg-surface">
      <div className="flex items-center gap-3 text-sm text-muted">
        <Loader2 className="h-5 w-5 animate-spin" aria-hidden />
        Loading your dictation set…
      </div>
    </section>
  );
}

interface DrillPanelProps {
  drill: DictationDrillDto;
  index: number;
  total: number;
  phase: 'attempting' | 'reviewing';
  answer: string;
  onAnswerChange: (value: string) => void;
  onSubmit: () => void;
  onNext: () => void;
  audioRef: React.MutableRefObject<HTMLAudioElement | null>;
  inputRef: React.MutableRefObject<HTMLInputElement | null>;
  audioPlaying: boolean;
  onAudioPlay: () => void;
  onAudioStateChange: (playing: boolean) => void;
  secondsOnDrill: number;
  submitting: boolean;
  result: DictationResult | null;
}

function DrillPanel({
  drill,
  index,
  total,
  phase,
  answer,
  onAnswerChange,
  onSubmit,
  onNext,
  audioRef,
  inputRef,
  audioPlaying,
  onAudioPlay,
  onAudioStateChange,
  secondsOnDrill,
  submitting,
  result,
}: DrillPanelProps) {
  return (
    <section className="space-y-6 rounded-2xl border border-border bg-surface px-8 py-8">
      <div className="flex items-center justify-between">
        <span className="text-xs font-semibold uppercase tracking-wide text-muted">
          Drill {index + 1} of {total} · {humaniseDrillType(drill.drillType)} · {drill.accent}
        </span>
        <div className="h-1.5 w-32 overflow-hidden rounded-full bg-border">
          <div
            className="h-full bg-primary transition-[width,background-color] duration-300"
            style={{ width: `${((index + (phase === 'reviewing' ? 1 : 0)) / total) * 100}%` }}
          />
        </div>
      </div>

      {/* Audio player */}
      <div className="flex flex-col items-center gap-4 rounded-xl border border-violet-100 bg-violet-50 px-6 py-8 dark:border-violet-900/50 dark:bg-violet-950/30">
        {drill.audioAssetUrl ? (
          <>
            <button
              type="button"
              onClick={onAudioPlay}
              className="flex h-16 w-16 items-center justify-center rounded-full bg-primary text-white shadow-md transition-colors hover:bg-primary-dark dark:bg-violet-700 dark:hover:bg-violet-600 active:scale-95"
              aria-label={audioPlaying ? 'Pause clip' : 'Play clip'}
            >
              {audioPlaying ? <Pause className="h-7 w-7" /> : <Play className="h-7 w-7 ml-0.5" />}
            </button>
            <p className="text-xs text-muted">
              {drill.durationSeconds}s clip · Tap to {audioPlaying ? 'pause' : 'replay'} as
              needed
            </p>
            <audio
              ref={audioRef}
              src={drill.audioAssetUrl}
              onPlay={() => onAudioStateChange(true)}
              onPause={() => onAudioStateChange(false)}
              onEnded={() => onAudioStateChange(false)}
              preload="metadata"
            />
          </>
        ) : (
          <p className="text-sm italic text-muted">
            Audio asset missing for this drill. Please report it.
          </p>
        )}
      </div>

      {/* Answer input */}
      <div className="space-y-2">
        <label htmlFor="dictation-input" className="text-sm font-semibold text-navy">
          Type what you hear
        </label>
        <input
          ref={inputRef}
          id="dictation-input"
          type="text"
          value={answer}
          onChange={(e) => onAnswerChange(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter' && phase === 'attempting' && !submitting) {
              e.preventDefault();
              onSubmit();
            }
          }}
          disabled={phase === 'reviewing'}
          placeholder="Listen, then type your answer…"
          className="w-full rounded-xl border border-border bg-background-light px-4 py-3 text-base text-navy placeholder:text-muted focus:border-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/30 disabled:opacity-70"
        />
        {phase === 'attempting' && secondsOnDrill >= HINT_AFTER_SECONDS && (
          <p className="text-xs text-amber-600 dark:text-amber-400">
            Stuck? Replay the clip a few times. Listening twice through often helps more than thinking harder.
          </p>
        )}
      </div>

      {/* Review block */}
      {phase === 'reviewing' && result && <ReviewBlock result={result} />}

      {/* Action bar */}
      <div className="flex items-center justify-end gap-3">
        {phase === 'attempting' ? (
          <button
            type="button"
            onClick={onSubmit}
            disabled={submitting || answer.trim().length === 0}
            className="inline-flex items-center gap-2 rounded-xl bg-primary px-6 py-2.5 text-sm font-semibold text-white shadow-sm transition-[color,background-color,transform] duration-200 hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {submitting ? <Loader2 className="h-4 w-4 animate-spin" /> : null}
            {submitting ? 'Checking…' : 'Submit'}
          </button>
        ) : (
          <button
            type="button"
            onClick={onNext}
            className="inline-flex items-center gap-2 rounded-xl bg-primary px-6 py-2.5 text-sm font-semibold text-white shadow-sm transition-colors hover:bg-primary-dark dark:bg-violet-700 dark:hover:bg-violet-600 active:scale-95"
          >
            {index + 1 === total ? 'Finish set' : 'Next drill'}
          </button>
        )}
      </div>
    </section>
  );
}

function ReviewBlock({ result }: { result: DictationResult }) {
  if (result.isCorrect) {
    return (
      <div className="rounded-xl border border-emerald-200 bg-emerald-50 px-5 py-4 dark:border-emerald-800/50 dark:bg-emerald-950/30">
        <div className="flex items-start gap-3">
          <CheckCircle2 className="mt-0.5 h-5 w-5 flex-shrink-0 text-emerald-600 dark:text-emerald-400" />
          <div className="space-y-1">
            <p className="text-sm font-semibold text-emerald-900 dark:text-emerald-200">
              Correct!
            </p>
            <p className="text-sm text-emerald-800 dark:text-emerald-200/80">
              {result.correctAnswer}
            </p>
          </div>
        </div>
      </div>
    );
  }

  if (result.offByOneTypo) {
    return (
      <div className="rounded-xl border border-amber-200 bg-amber-50 px-5 py-4 dark:border-amber-800/50 dark:bg-amber-950/30">
        <div className="flex items-start gap-3">
          <AlertCircle className="mt-0.5 h-5 w-5 flex-shrink-0 text-amber-600 dark:text-amber-400" />
          <div className="space-y-1">
            <p className="text-sm font-semibold text-amber-900 dark:text-amber-200">
              Did you mean &ldquo;{result.correctAnswer}&rdquo;?
            </p>
            <p className="text-sm text-amber-800 dark:text-amber-200/80">
              Very close, just a one-letter slip. We&apos;ll resurface this one soon so you can
              nail the spelling.
            </p>
            <SpellingDiffLine canonical={result.correctAnswer} typed={result.learnerAnswer} />
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="rounded-xl border border-rose-200 bg-rose-50 px-5 py-4 dark:border-rose-800/50 dark:bg-rose-950/30">
      <div className="flex items-start gap-3">
        <AlertCircle className="mt-0.5 h-5 w-5 flex-shrink-0 text-rose-600 dark:text-rose-400" />
        <div className="space-y-1">
          <p className="text-sm font-semibold text-rose-900 dark:text-rose-200">
            Correct answer:
          </p>
          <p className="text-sm text-rose-800 dark:text-rose-200/80">
            {result.correctAnswer}
          </p>
          {result.learnerAnswer && (
            <p className="text-xs italic text-rose-700/80 dark:text-rose-300/70">
              You typed: {result.learnerAnswer}
            </p>
          )}
        </div>
      </div>
    </div>
  );
}

/**
 * Minimal letter-by-letter visual diff. Equal characters render in neutral
 * tone; differing characters in red on the typed side. Pure-presentation —
 * the canonical "correct" copy lives above in the parent block.
 */
function SpellingDiffLine({ canonical, typed }: { canonical: string; typed: string }) {
  const cells = useMemo(() => buildDiffCells(canonical, typed), [canonical, typed]);
  return (
    <p className="font-mono text-xs">
      <span className="text-muted">Yours: </span>
      {cells.map((cell, i) => (
        <span
          key={i}
          className={
            cell.kind === 'equal'
              ? 'text-navy'
              : 'text-rose-700 underline decoration-rose-400 dark:text-rose-300'
          }
        >
          {cell.char === ' ' ? ' ' : cell.char}
        </span>
      ))}
    </p>
  );
}

interface DiffCell {
  kind: 'equal' | 'different';
  char: string;
}

function buildDiffCells(canonical: string, typed: string): DiffCell[] {
  const max = Math.max(canonical.length, typed.length);
  const cells: DiffCell[] = [];
  for (let i = 0; i < max; i++) {
    const c = canonical[i];
    const t = typed[i];
    if (t === undefined) break;
    cells.push({
      char: t,
      kind: c !== undefined && c.toLowerCase() === t.toLowerCase() ? 'equal' : 'different',
    });
  }
  return cells;
}

function CompletePanel({
  correct,
  total,
  onAgain,
  onRestart,
}: {
  correct: number;
  total: number;
  onAgain: () => void;
  onRestart: () => void;
}) {
  const score = total > 0 ? Math.round((correct / total) * 100) : 0;
  const tone =
    score >= 80
      ? 'bg-emerald-50 border-emerald-200 dark:bg-emerald-950/30 dark:border-emerald-800/50'
      : score >= 50
      ? 'bg-amber-50 border-amber-200 dark:bg-amber-950/30 dark:border-amber-800/50'
      : 'bg-rose-50 border-rose-200 dark:bg-rose-950/30 dark:border-rose-800/50';

  return (
    <section
      className={`space-y-6 rounded-2xl border px-8 py-10 text-center ${tone}`}
    >
      <Trophy className="mx-auto h-12 w-12 text-violet-500" aria-hidden />
      <div>
        <h2 className="text-2xl font-bold text-navy">
          Set complete!
        </h2>
        <p className="mt-2 text-3xl font-bold text-navy">
          {correct} correct / {total}
        </p>
        <p className="mt-1 text-sm text-muted">
          {score}% accuracy on this set
        </p>
      </div>
      <div className="flex flex-wrap justify-center gap-3">
        <button
          type="button"
          onClick={onAgain}
          className="inline-flex items-center gap-2 rounded-xl bg-primary px-6 py-3 text-sm font-semibold text-white shadow-sm transition-colors hover:bg-primary-dark dark:bg-violet-700 dark:hover:bg-violet-600 active:scale-95"
        >
          <RefreshCw className="h-4 w-4" aria-hidden />
          Continue practicing
        </button>
        <button
          type="button"
          onClick={onRestart}
          className="inline-flex items-center gap-2 rounded-xl border border-border bg-surface px-6 py-3 text-sm font-semibold text-navy transition-colors hover:bg-background-light"
        >
          Back to start
        </button>
        <Link
          href="/listening"
          className="inline-flex items-center gap-2 rounded-xl border border-border bg-surface px-6 py-3 text-sm font-semibold text-navy transition-colors hover:bg-background-light"
        >
          Listening hub
        </Link>
      </div>
    </section>
  );
}

function humaniseDrillType(type: string): string {
  switch (type) {
    case 'single_term':
      return 'Single term';
    case 'short_phrase':
      return 'Short phrase';
    case 'full_sentence':
      return 'Full sentence';
    case 'mini_consultation':
      return 'Mini consultation';
    default:
      return type.replace(/_/g, ' ');
  }
}
