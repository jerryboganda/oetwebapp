'use client';

import { useState } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { CheckCircle2, AlertCircle, Volume2, Loader2, Turtle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  submitRecallsListenType,
  fetchRecallsAudio,
  explainRecallsMistake,
  type RecallsListenTypeResponse,
  type RecallsExplainResponse,
} from '@/lib/api';
import { playTransientAudio } from '@/lib/recalls-audio';
import { useRecallsAudioUpgrade } from './audio-upgrade-modal';

interface ListenAndTypeProps {
  termId: string;
  termHint?: string;
  onResult?: (result: RecallsListenTypeResponse) => void;
}

const ERROR_LABELS: Record<RecallsListenTypeResponse['code'], string> = {
  correct: 'Correct',
  case_only: 'Correct (case differs)',
  british_variant: 'Use British spelling',
  missing_letter: 'Missing letter',
  extra_letter: 'Extra letter',
  transposition: 'Letters swapped',
  double_letter: 'Double letter',
  hyphen: 'Hyphen difference',
  homophone: 'Sounds similar — different word',
  unknown: 'Try again',
};

/**
 * Listen-and-type spell check tile. Plays the British TTS audio for a term,
 * accepts the learner's typed answer, and shows the server-classified diff
 * (green / red / strike-through letters) per docs/RECALLS-MODULE-PLAN.md §6.
 */
export function ListenAndType({ termId, termHint, onResult }: ListenAndTypeProps) {
  const [typed, setTyped] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [result, setResult] = useState<RecallsListenTypeResponse | null>(null);
  const [audioLoading, setAudioLoading] = useState(false);
  const [slowLoading, setSlowLoading] = useState(false);
  const [explain, setExplain] = useState<RecallsExplainResponse | null>(null);
  const [explainLoading, setExplainLoading] = useState(false);
  const { guardAudio, modal: upgradeModal } = useRecallsAudioUpgrade();

  async function handleExplain() {
    if (!result || result.isCorrect || explain || explainLoading) return;
    setExplainLoading(true);
    try {
      const e = await explainRecallsMistake(termId, typed);
      setExplain(e);
    } catch {
      // Silent: AI is enhancement, not blocking. Diff is already shown.
    } finally {
      setExplainLoading(false);
    }
  }

  async function handlePlay() {
    if (audioLoading) return;
    setAudioLoading(true);
    try {
      const resp = await guardAudio(() => fetchRecallsAudio(termId, 'normal'), { termId });
      if (!resp) return;
      playTransientAudio(resp.url);
    } finally {
      setAudioLoading(false);
    }
  }

  async function handlePlaySlow() {
    if (slowLoading) return;
    setSlowLoading(true);
    try {
      const resp = await guardAudio(() => fetchRecallsAudio(termId, 'slow'), { termId });
      if (!resp) return;
      playTransientAudio(resp.url);
    } finally {
      setSlowLoading(false);
    }
  }

  async function handleCheck() {
    if (!typed.trim() || submitting) return;
    setSubmitting(true);
    try {
      const r = await submitRecallsListenType(termId, typed);
      setResult(r);
      onResult?.(r);
    } finally {
      setSubmitting(false);
    }
  }

  function reset() {
    setResult(null);
    setTyped('');
    setExplain(null);
  }

  return (
    <div className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
      <div className="flex items-center gap-3">
        <Button
          type="button"
          variant="primary"
          onClick={handlePlay}
          disabled={audioLoading}
          aria-label="Play British pronunciation"
          className="flex h-12 w-12 items-center justify-center rounded-full p-0"
        >
          {audioLoading ? <Loader2 className="h-5 w-5 animate-spin" /> : <Volume2 className="h-5 w-5" />}
        </Button>
        <Button
          type="button"
          variant="secondary"
          onClick={handlePlaySlow}
          disabled={slowLoading}
          aria-label="Play slow"
          className="flex h-10 w-10 items-center justify-center rounded-full p-0"
        >
          {slowLoading ? <Loader2 className="h-4 w-4 animate-spin" /> : <Turtle className="h-4 w-4" />}
        </Button>
        <div className="flex-1">
          <div className="text-xs font-semibold uppercase tracking-wide text-muted">Listen &amp; type</div>
          {termHint && <div className="text-sm text-navy">{termHint}</div>}
        </div>
      </div>

      <div className="mt-4">
        <label htmlFor="recalls-typed" className="sr-only">
          Type the word you hear
        </label>
        <input
          id="recalls-typed"
          type="text"
          value={typed}
          onChange={(e) => setTyped(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') void handleCheck();
          }}
          placeholder="Type what you hear…"
          autoComplete="off"
          autoCorrect="off"
          spellCheck={false}
          className="w-full rounded-xl border border-border bg-background-light px-4 py-3 font-mono text-lg tracking-wide focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20"
          disabled={submitting || result !== null}
        />
      </div>

      <div className="mt-3 flex gap-2">
        {result === null ? (
          <Button onClick={handleCheck} disabled={submitting || !typed.trim()} variant="primary">
            {submitting ? 'Checking…' : 'Check answer'}
          </Button>
        ) : (
          <Button onClick={reset} variant="primary">
            Try again
          </Button>
        )}
      </div>

      <AnimatePresence>
        {result && (
          <motion.div
            initial={{ opacity: 0, y: 6 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0 }}
            className={`mt-4 rounded-xl border p-4 ${
              result.isCorrect
                ? 'border-success/30 bg-success/10 text-success'
                : 'border-warning/30 bg-warning/10 text-navy'
            }`}
          >
            <div className="flex items-center gap-2 text-sm font-semibold">
              {result.isCorrect ? <CheckCircle2 className="h-4 w-4" /> : <AlertCircle className="h-4 w-4" />}
              {ERROR_LABELS[result.code]}
            </div>
            <div className="mt-2 font-mono text-base">
              {result.segments.map((seg, i) => (
                <span
                  key={i}
                  className={
                    seg.kind === 'equal'
                      ? 'text-success'
                      : seg.kind === 'missing'
                        ? 'rounded bg-warning/20 px-0.5 text-warning'
                        : 'text-danger line-through'
                  }
                >
                  {seg.text}
                </span>
              ))}
            </div>
            {!result.isCorrect && (
              <div className="mt-2 text-xs text-muted">
                Canonical (British): <span className="font-mono">{result.canonical}</span>
                {result.americanSpelling && (
                  <>
                    {' '}
                    · American: <span className="font-mono">{result.americanSpelling}</span>
                  </>
                )}
              </div>
            )}
            {!result.isCorrect && !explain && (
              <div className="mt-3">
                <Button
                  variant="primary"
                  onClick={handleExplain}
                  disabled={explainLoading}
                  className="text-xs"
                >
                  {explainLoading ? 'Thinking…' : 'Explain my mistake'}
                </Button>
              </div>
            )}
            {explain && (
              <div className="mt-3 rounded-lg border border-info/30 bg-info/5 p-3 text-xs text-navy">
                <div className="font-semibold">{explain.shortReason}</div>
                {explain.longExplanation && (
                  <p className="mt-1 whitespace-pre-wrap text-muted">{explain.longExplanation}</p>
                )}
              </div>
            )}
          </motion.div>
        )}
      </AnimatePresence>
      {upgradeModal}
    </div>
  );
}
