import { useCallback, useEffect, useRef, useState } from 'react';
import { CheckCircle2, Loader2, RotateCcw } from 'lucide-react';
import { reportPlacementUnitTechnical, startPlacementModule, startPlacementUnit, submitPlacementResponses, type PlacementDeliveryUnit, type PlacementModule, type PlacementTechnicalReason } from '@/lib/api/placement';
import { readErrorMessage } from '@/lib/read-error-message';
import { PARTS, partIndex, PRIMARY_BUTTON, SECONDARY_BUTTON } from './placement-shared';
import { UnitAudio } from './placement-unit-audio';

// ── Objective modules (LS / RD / LSN) ────────────────────────────────

export function unitKeyOf(unit: PlacementDeliveryUnit): string {
  return unit.items.map((item) => item.item_id).join('|');
}

/** Seconds until a server-issued deadline; null when there is none or it
 *  does not parse (never treated as zero — that would auto-submit). */
export function useSecondsLeft(deadlineAt: string | null): number | null {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!deadlineAt) return;
    const tick = () => setNow(Date.now());
    const first = window.setTimeout(tick, 0);
    const interval = window.setInterval(tick, 1000);
    return () => {
      window.clearTimeout(first);
      window.clearInterval(interval);
    };
  }, [deadlineAt]);
  if (!deadlineAt) return null;
  // The engine emits nanosecond precision ("…00.123456789+00:00"); only
  // three fractional digits are guaranteed to parse (Safari rejects more).
  const parsed = Date.parse(deadlineAt.replace(/(\.\d{3})\d+/, '$1'));
  if (Number.isNaN(parsed)) return null;
  return Math.max(0, Math.round((parsed - now) / 1000));
}

export function UnitTimer({ secondsLeft, waitingForAudio }: { secondsLeft: number | null; waitingForAudio: boolean }) {
  if (waitingForAudio) return <p className="text-xs text-muted">Timer starts when the audio begins</p>;
  if (secondsLeft === null) return null;
  const tone = secondsLeft <= 10 ? 'font-semibold text-danger' : secondsLeft <= 60 ? 'text-warning' : 'text-muted';
  return (
    <p className={`font-mono text-xs tabular-nums ${tone}`} aria-live="off">
      <span className="sr-only">Time left: </span>
      {Math.floor(secondsLeft / 60)}:{String(secondsLeft % 60).padStart(2, '0')}
    </p>
  );
}

export function ObjectiveStage({
  sessionId,
  module,
  onProgress,
  onComplete,
}: {
  sessionId: string;
  module: PlacementModule;
  onProgress: (answered: number) => void;
  onComplete: () => void;
}) {
  const [unit, setUnit] = useState<PlacementDeliveryUnit | null>(null);
  const [deadlineAt, setDeadlineAt] = useState<string | null>(null);
  // Mandatory-first-play: an audio unit's answers/submit stay locked until
  // the real 'playing' event fires (never preload/loadedmetadata) — see
  // UnitAudio's onPlaybackStart below. Units with no audio have nothing to
  // wait for.
  const [playbackStarted, setPlaybackStarted] = useState(false);
  const [selected, setSelected] = useState<Record<string, string>>({});
  const [answered, setAnswered] = useState(0);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [audioAttempt, setAudioAttempt] = useState(0);
  const [loadAttempt, setLoadAttempt] = useState(0);
  const playsRef = useRef(0);
  const shownAtRef = useRef(Date.now());
  const currentKeyRef = useRef<string | null>(null);
  const settledKeyRef = useRef<string | null>(null);
  const completedRef = useRef(false);
  // The parent passes fresh closures every render (and re-renders on every
  // progress update). Holding them in a ref keeps `present` stable, so the
  // module-load effect below never re-fires mid-unit.
  const callbacksRef = useRef({ onProgress, onComplete });
  useEffect(() => {
    callbacksRef.current = { onProgress, onComplete };
  });

  const finish = useCallback(() => {
    if (completedRef.current) return;
    completedRef.current = true;
    callbacksRef.current.onComplete();
  }, []);

  const present = useCallback(
    (next: PlacementDeliveryUnit | null) => {
      if (!next || next.module_complete) {
        finish();
        return;
      }
      playsRef.current = 0;
      shownAtRef.current = Date.now();
      currentKeyRef.current = unitKeyOf(next);
      setSelected({});
      setDeadlineAt(null);
      setPlaybackStarted(false);
      setAudioAttempt(0);
      setUnit(next);
    },
    [finish],
  );

  useEffect(() => {
    let cancelled = false;
    startPlacementModule(sessionId, module)
      .then((next) => {
        if (!cancelled) present(next);
      })
      .catch((err) => {
        if (!cancelled) setError(readErrorMessage(err, 'Could not load the next questions.'));
      });
    return () => {
      cancelled = true;
    };
  }, [loadAttempt, module, present, sessionId]);

  /** Start the server clock for the unit on screen (idempotent engine-side).
   *  Called either immediately (no-audio units) or from the real 'playing'
   *  event (audio units) — both cases mean the unit is now genuinely usable,
   *  so this is also where the mandatory-first-play gate unlocks. */
  const startClock = useCallback(
    async (target: PlacementDeliveryUnit) => {
      const key = unitKeyOf(target);
      shownAtRef.current = Date.now();
      setPlaybackStarted(true);
      const started = await startPlacementUnit(sessionId, module);
      if (currentKeyRef.current === key) setDeadlineAt((started ?? target).deadline_at);
    },
    [module, sessionId],
  );

  // Units without audio are usable the moment they render.
  useEffect(() => {
    if (!unit || unit.audio_url) return;
    void startClock(unit);
  }, [startClock, unit]);

  const secondsLeft = useSecondsLeft(deadlineAt);
  const audioGate = !unit?.audio_url || playbackStarted;

  const handleSubmit = useCallback(
    async (isTimeout: boolean) => {
      if (!unit || submitting) return;
      // A manual submit can't reach the button while it's disabled, but
      // guard the handler itself too — the timeout path can never hit this
      // since deadlineAt (and secondsLeft) never leave null until the same
      // startClock() call that sets playbackStarted=true.
      if (!isTimeout && !audioGate) return;
      const key = unitKeyOf(unit);
      if (settledKeyRef.current === key) return;
      const elapsed = Math.max(0, Date.now() - shownAtRef.current);
      const responses = unit.items.map((item) => ({
        itemId: item.item_id,
        selectedOptionId: selected[item.item_id] ?? null,
        responseMs: Math.min(elapsed, 20 * 60 * 1000),
      }));
      if (!isTimeout && responses.some((response) => response.selectedOptionId === null)) {
        setError(unit.items.length === 1 ? 'Choose an answer to continue.' : 'Answer every question in this set before continuing.');
        return;
      }
      settledKeyRef.current = key;
      setSubmitting(true);
      setError(null);
      setNotice(null);
      try {
        const result = await submitPlacementResponses(sessionId, responses, Math.max(0, playsRef.current - 1));
        const total = answered + unit.items.length;
        setAnswered(total);
        callbacksRef.current.onProgress(total);
        present(result.next);
      } catch (err) {
        settledKeyRef.current = null;
        setError(readErrorMessage(err, 'Could not save your answers — check your connection and try again.'));
      } finally {
        setSubmitting(false);
      }
    },
    [answered, audioGate, present, selected, sessionId, submitting, unit],
  );

  useEffect(() => {
    if (secondsLeft === 0 && unit && deadlineAt && !submitting) {
      // The server records late submissions as omissions (grace window) —
      // auto-submit keeps the candidate moving.
      void handleSubmit(true);
    }
  }, [deadlineAt, handleSubmit, secondsLeft, submitting, unit]);

  // A unit whose media failed is excluded from scoring; the engine serves a
  // replacement. Never scored as wrong, never charged against the timer.
  const handleTechnical = useCallback(
    async (reason: PlacementTechnicalReason) => {
      if (!unit) return;
      const key = unitKeyOf(unit);
      if (settledKeyRef.current === key) return;
      settledKeyRef.current = key;
      setDeadlineAt(null);
      setError(null);
      setNotice('Technical audio problem — loading a replacement item. This item will not be scored.');
      try {
        const result = await reportPlacementUnitTechnical(sessionId, module, reason);
        present(result.next);
      } catch {
        settledKeyRef.current = null;
        setNotice(null);
        setError('The audio could not be loaded. Check your connection, then reload the audio.');
      }
    },
    [module, present, sessionId, unit],
  );

  if (!unit) {
    return error ? (
      <div className="space-y-3 rounded-2xl border border-border bg-surface p-5 sm:p-6">
        <p role="alert" className="text-sm text-danger">
          {error}
        </p>
        <button
          type="button"
          onClick={() => {
            setError(null);
            setLoadAttempt((attempt) => attempt + 1);
          }}
          className={SECONDARY_BUTTON}
        >
          <RotateCcw className="h-4 w-4" aria-hidden /> Try again
        </button>
      </div>
    ) : (
      <div className="flex items-center gap-2 text-sm text-muted" role="status">
        <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> Preparing {PARTS[partIndex(module)].name}…
      </div>
    );
  }

  const firstNumber = answered + 1;
  const isSingle = unit.items.length === 1;
  const unitKey = unitKeyOf(unit);

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between gap-3">
        <p className="text-sm text-muted">
          {isSingle ? `Question ${firstNumber}` : `Questions ${firstNumber}–${answered + unit.items.length}`}
        </p>
        <UnitTimer secondsLeft={secondsLeft} waitingForAudio={Boolean(unit.audio_url) && !deadlineAt} />
      </div>

      {notice ? (
        <p role="status" className="rounded-xl border border-warning/40 bg-warning/10 px-4 py-3 text-sm text-navy">
          {notice}
        </p>
      ) : null}

      {unit.audio_url ? (
        <UnitAudio
          key={`${unitKey}#${audioAttempt}`}
          audioUrl={unit.audio_url}
          maxPlays={unit.max_plays ?? 2}
          onPlaybackStart={() => void startClock(unit)}
          onPlay={(plays) => {
            playsRef.current = plays;
          }}
          onFailure={handleTechnical}
        />
      ) : null}

      {unit.stimulus_text ? (
        <section className="rounded-2xl border border-border bg-primary/5 p-5 sm:p-6" aria-label="Reading text">
          <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted">Read the text</p>
          <div className="whitespace-pre-line break-words text-sm leading-relaxed text-navy">{unit.stimulus_text}</div>
        </section>
      ) : null}

      {unit.items.map((item, index) => (
        <QuestionCard
          key={item.item_id}
          number={firstNumber + index}
          item={item}
          value={selected[item.item_id] ?? null}
          disabled={submitting || !audioGate}
          onChange={(optionId) => setSelected((current) => ({ ...current, [item.item_id]: optionId }))}
        />
      ))}

      {error ? (
        <div className="space-y-2">
          <p role="alert" className="text-sm text-danger">
            {error}
          </p>
          {unit.audio_url && !deadlineAt ? (
            <button
              type="button"
              onClick={() => {
                setError(null);
                setAudioAttempt((attempt) => attempt + 1);
              }}
              className={SECONDARY_BUTTON}
            >
              <RotateCcw className="h-4 w-4" aria-hidden /> Reload audio
            </button>
          ) : null}
        </div>
      ) : null}

      <button
        type="button"
        onClick={() => void handleSubmit(false)}
        disabled={submitting || !audioGate}
        className={PRIMARY_BUTTON}
      >
        {submitting ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : <CheckCircle2 className="h-4 w-4" aria-hidden />}
        {isSingle ? 'Next' : 'Submit answers'}
      </button>
      {!audioGate ? (
        <p className="text-xs text-muted">Play the audio to start before you can answer.</p>
      ) : null}
    </div>
  );
}

export function QuestionCard({
  number,
  item,
  value,
  disabled,
  onChange,
}: {
  number: number;
  item: PlacementDeliveryUnit['items'][number];
  value: string | null;
  disabled: boolean;
  onChange: (optionId: string) => void;
}) {
  // A plain div + role="radiogroup" keeps the prompt inside the card — a
  // <legend> is laid across the fieldset border and escaped it visually.
  const promptId = `placement-q-${item.item_id}`;
  return (
    <div role="radiogroup" aria-labelledby={promptId} className="rounded-2xl border border-border bg-surface p-5 shadow-sm sm:p-6">
      <p id={promptId} className="break-words text-base font-semibold leading-relaxed text-navy">
        <span className="mr-2 text-muted">{number}.</span>
        {item.stem}
      </p>
      <div className="mt-4 space-y-2.5">
        {item.options.map((option) => (
          <label
            key={option.option_id}
            className="flex min-h-11 w-full cursor-pointer items-center gap-3 rounded-xl border border-border bg-background-light px-4 py-3 text-sm text-navy transition hover:border-primary/60 has-[:checked]:border-primary has-[:checked]:bg-primary/5 has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-primary/40"
          >
            <input
              type="radio"
              className="h-4 w-4 shrink-0 accent-primary"
              name={item.item_id}
              value={option.option_id}
              checked={value === option.option_id}
              disabled={disabled}
              onChange={() => onChange(option.option_id)}
            />
            <span className="break-words">{option.text}</span>
          </label>
        ))}
      </div>
    </div>
  );
}
