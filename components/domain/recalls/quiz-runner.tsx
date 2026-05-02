'use client';

import { useEffect, useMemo, useState } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { Volume2, CheckCircle2, XCircle, ArrowRight, Loader2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import {
  fetchRecallsQuiz,
  fetchRecallsAudio,
  submitRecallsListenType,
  type RecallQuizModeKey,
  type RecallsQuizSession,
  type RecallsQuizItem,
} from '@/lib/api';

interface QuizRunnerProps {
  mode: RecallQuizModeKey;
  limit?: number;
}

/**
 * Renders the 5 non-listen-and-type quiz modes as a single runner. Each
 * mode is a thin variation of the same answer/feedback flow:
 * - word_recognition: hear audio → pick the correctly-spelled term.
 * - meaning_check: see term → pick correct definition.
 * - clinical_sentence: hear sentence audio + see blanked sentence → type missing word.
 * - high_risk_spelling: same as listen_and_type but filtered server-side.
 * - starred_only: same as listen_and_type but filtered to starred cards.
 *
 * For listen_and_type proper, use `<ListenAndType />` directly — this file
 * does not duplicate that experience for non-quiz pages.
 *
 * See spec §4 (Quiz Modes) and docs/RECALLS-MODULE-PLAN.md §6.
 */
export function QuizRunner({ mode, limit = 10 }: QuizRunnerProps) {
  const [session, setSession] = useState<RecallsQuizSession | null>(null);
  const [index, setIndex] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [score, setScore] = useState({ correct: 0, total: 0 });

  useEffect(() => {
    let cancelled = false;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- data-fetch on mount
    setLoading(true);
    fetchRecallsQuiz(mode, limit)
      .then((s) => {
        if (cancelled) return;
        setSession(s);
        setIndex(0);
        setScore({ correct: 0, total: 0 });
        setError(null);
      })
      .catch(() => {
        if (!cancelled) setError('Could not load this quiz mode.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [mode, limit]);

  const current = session?.items[index] ?? null;

  function handleNext(wasCorrect: boolean) {
    setScore((s) => ({ correct: s.correct + (wasCorrect ? 1 : 0), total: s.total + 1 }));
    setTimeout(() => setIndex((i) => i + 1), 900);
  }

  if (loading) return <Skeleton className="h-44 rounded-xl" />;
  if (error) return <InlineAlert variant="warning">{error}</InlineAlert>;
  if (!session || session.items.length === 0) {
    return (
      <div className="rounded-xl border border-dashed border-border p-6 text-center text-sm text-muted">
        No cards available for this mode yet. Star some words or seed your queue from
        Listening practice first.
      </div>
    );
  }
  if (!current || index >= session.items.length) {
    return (
      <div className="rounded-xl border border-success/30 bg-success/10 p-6 text-center">
        <CheckCircle2 className="mx-auto h-8 w-8 text-success" />
        <p className="mt-2 font-semibold text-navy">Session complete</p>
        <p className="mt-1 text-sm text-muted">
          {score.correct} / {score.total} correct
        </p>
      </div>
    );
  }

  const progressLabel = `Card ${index + 1} of ${session.items.length}`;

  return (
    <div className="space-y-3">
      <div className="text-xs text-muted">{progressLabel}</div>
      <AnimatePresence mode="wait">
        <motion.div
          key={current.cardId}
          initial={{ opacity: 0, y: 8 }}
          animate={{ opacity: 1, y: 0 }}
          exit={{ opacity: 0, y: -8 }}
        >
          {mode === 'word_recognition' && <WordRecognitionCard item={current} onAnswered={handleNext} />}
          {mode === 'meaning_check' && <MeaningCheckCard item={current} onAnswered={handleNext} />}
          {mode === 'clinical_sentence' && <ClinicalSentenceCard item={current} onAnswered={handleNext} />}
          {(mode === 'high_risk_spelling' || mode === 'starred_only') && (
            <SpellingCard item={current} onAnswered={handleNext} />
          )}
        </motion.div>
      </AnimatePresence>
    </div>
  );
}

interface CardProps {
  item: RecallsQuizItem;
  onAnswered: (correct: boolean) => void;
}

function shuffled<T>(arr: T[]): T[] {
  const copy = [...arr];
  for (let i = copy.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [copy[i], copy[j]] = [copy[j]!, copy[i]!];
  }
  return copy;
}

function PlayAudioButton({ termId, src, label }: { termId: string; src: string | null; label: string }) {
  const [resolved, setResolved] = useState<string | null>(src);
  const [loading, setLoading] = useState(false);
  async function play() {
    if (loading) return;
    if (resolved) {
      void new Audio(resolved).play().catch(() => undefined);
      return;
    }
    setLoading(true);
    try {
      const r = await fetchRecallsAudio(termId, 'normal');
      setResolved(r.url);
      void new Audio(r.url).play().catch(() => undefined);
    } finally {
      setLoading(false);
    }
  }
  return (
    <Button
      type="button"
      variant="primary"
      onClick={play}
      disabled={loading}
      aria-label={label}
      className="flex h-12 w-12 items-center justify-center rounded-full p-0"
    >
      {loading ? <Loader2 className="h-5 w-5 animate-spin" /> : <Volume2 className="h-5 w-5" />}
    </Button>
  );
}

/** Quiz Mode 2 — Word recognition (audio → 4 similar-looking options). */
function WordRecognitionCard({ item, onAnswered }: CardProps) {
  const options = useMemo(
    () => shuffled([item.term, ...item.termDistractors.slice(0, 3)]),
    [item.term, item.termDistractors],
  );
  const [picked, setPicked] = useState<string | null>(null);
  const correct = picked === item.term;

  return (
    <div className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
      <div className="flex items-center gap-3">
        <PlayAudioButton termId={item.termId} src={item.audioUrl} label="Play term audio" />
        <div className="text-xs uppercase tracking-wide text-muted">Listen and pick the correct spelling</div>
      </div>
      <div className="mt-4 grid grid-cols-1 gap-2 sm:grid-cols-2">
        {options.map((opt) => {
          const isPicked = picked === opt;
          const isAnswer = opt === item.term;
          const showState = picked !== null;
          return (
            <button
              key={opt}
              type="button"
              disabled={picked !== null}
              onClick={() => {
                setPicked(opt);
                onAnswered(opt === item.term);
              }}
              className={`rounded-xl border p-3 text-left font-mono text-sm transition-colors ${
                showState && isAnswer
                  ? 'border-success bg-success/10 text-success'
                  : showState && isPicked && !isAnswer
                    ? 'border-danger bg-danger/10 text-danger'
                    : 'border-border bg-background-light hover:border-primary'
              }`}
            >
              {opt}
            </button>
          );
        })}
      </div>
      {picked && (
        <div className={`mt-3 text-xs ${correct ? 'text-success' : 'text-danger'}`}>
          {correct ? <CheckCircle2 className="inline h-4 w-4" /> : <XCircle className="inline h-4 w-4" />}{' '}
          {correct ? 'Correct.' : `Answer: ${item.term}`}
        </div>
      )}
    </div>
  );
}

/** Quiz Mode 3 — Meaning check (term → 4 definition options). */
function MeaningCheckCard({ item, onAnswered }: CardProps) {
  const options = useMemo(
    () => shuffled([item.definition, ...item.definitionDistractors.slice(0, 3)]),
    [item.definition, item.definitionDistractors],
  );
  const [picked, setPicked] = useState<string | null>(null);
  const correct = picked === item.definition;

  return (
    <div className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
      <div className="text-xs uppercase tracking-wide text-muted">What does this mean?</div>
      <div className="mt-1 font-mono text-2xl font-semibold text-navy">{item.term}</div>
      {item.ipa && <div className="text-xs text-muted">{item.ipa}</div>}
      <div className="mt-4 grid grid-cols-1 gap-2">
        {options.map((opt) => {
          const isPicked = picked === opt;
          const isAnswer = opt === item.definition;
          const showState = picked !== null;
          return (
            <button
              key={opt}
              type="button"
              disabled={picked !== null}
              onClick={() => {
                setPicked(opt);
                onAnswered(opt === item.definition);
              }}
              className={`rounded-xl border p-3 text-left text-sm transition-colors ${
                showState && isAnswer
                  ? 'border-success bg-success/10'
                  : showState && isPicked && !isAnswer
                    ? 'border-danger bg-danger/10'
                    : 'border-border bg-background-light hover:border-primary'
              }`}
            >
              {opt}
            </button>
          );
        })}
      </div>
      {picked && (
        <div className={`mt-3 text-xs ${correct ? 'text-success' : 'text-danger'}`}>
          {correct ? 'Correct.' : `Answer: ${item.definition}`}
        </div>
      )}
    </div>
  );
}

/** Quiz Mode 4 — Clinical sentence (audio + blanked sentence → type missing word). */
function ClinicalSentenceCard({ item, onAnswered }: CardProps) {
  const [typed, setTyped] = useState('');
  const [submitted, setSubmitted] = useState(false);
  const [correct, setCorrect] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  async function check() {
    if (!typed.trim() || submitting) return;
    setSubmitting(true);
    try {
      const r = await submitRecallsListenType(item.termId, typed);
      setCorrect(r.isCorrect);
      setSubmitted(true);
      onAnswered(r.isCorrect);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
      <div className="flex items-center gap-3">
        <PlayAudioButton termId={item.termId} src={item.audioSentenceUrl ?? item.audioUrl} label="Play sentence" />
        <div className="text-xs uppercase tracking-wide text-muted">Listen and complete</div>
      </div>
      <p className="mt-3 text-sm text-navy">
        {item.blankedSentence ?? `…${item.term}…`}
      </p>
      <input
        type="text"
        value={typed}
        onChange={(e) => setTyped(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'Enter') void check();
        }}
        disabled={submitted}
        autoComplete="off"
        autoCorrect="off"
        spellCheck={false}
        className="mt-3 w-full rounded-xl border border-border bg-background-light px-4 py-3 font-mono text-base focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20"
        placeholder="Type the missing word…"
      />
      <div className="mt-3">
        {!submitted ? (
          <Button onClick={check} variant="primary" disabled={submitting || !typed.trim()}>
            {submitting ? 'Checking…' : (
              <>
                Check <ArrowRight className="ml-1 inline h-4 w-4" />
              </>
            )}
          </Button>
        ) : (
          <div className={`text-xs ${correct ? 'text-success' : 'text-danger'}`}>
            {correct ? 'Correct.' : `Answer: ${item.term}`}
          </div>
        )}
      </div>
    </div>
  );
}

/** Quiz Modes 5 & 6 — High-risk spelling / Starred only (server-filtered listen-and-type). */
function SpellingCard({ item, onAnswered }: CardProps) {
  const [typed, setTyped] = useState('');
  const [submitted, setSubmitted] = useState(false);
  const [correct, setCorrect] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  async function check() {
    if (!typed.trim() || submitting) return;
    setSubmitting(true);
    try {
      const r = await submitRecallsListenType(item.termId, typed);
      setCorrect(r.isCorrect);
      setSubmitted(true);
      onAnswered(r.isCorrect);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
      <div className="flex items-center gap-3">
        <PlayAudioButton termId={item.termId} src={item.audioUrl} label="Play term" />
        <div className="text-xs uppercase tracking-wide text-muted">Listen &amp; type — British spelling</div>
      </div>
      {item.ipa && <div className="mt-2 text-xs text-muted">{item.ipa}</div>}
      <input
        type="text"
        value={typed}
        onChange={(e) => setTyped(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'Enter') void check();
        }}
        disabled={submitted}
        autoComplete="off"
        autoCorrect="off"
        spellCheck={false}
        className="mt-3 w-full rounded-xl border border-border bg-background-light px-4 py-3 font-mono text-lg focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20"
        placeholder="Type what you hear…"
      />
      <div className="mt-3">
        {!submitted ? (
          <Button onClick={check} variant="primary" disabled={submitting || !typed.trim()}>
            {submitting ? 'Checking…' : 'Check'}
          </Button>
        ) : (
          <div className={`text-xs ${correct ? 'text-success' : 'text-danger'}`}>
            {correct ? 'Correct.' : `Answer: ${item.term}${item.americanSpelling ? ` · US: ${item.americanSpelling}` : ''}`}
          </div>
        )}
      </div>
    </div>
  );
}
