import { useCallback, useEffect, useState } from 'react';
import { CheckCircle2, Loader2 } from 'lucide-react';
import { fetchPlacementWritingTasks, savePlacementWritingDraft, submitPlacementWriting, type PlacementWritingTask } from '@/lib/api/placement';
import { readErrorMessage } from '@/lib/read-error-message';
import { PRIMARY_BUTTON } from './placement-shared';
import { TransitionCard } from './placement-preflight';
import { useSecondsLeft, UnitTimer } from './placement-objective-stage';

// ── Writing ──────────────────────────────────────────────────────────

export function WritingStage({ sessionId, onComplete }: { sessionId: string; onComplete: () => void }) {
  const [tasks, setTasks] = useState<PlacementWritingTask[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [index, setIndex] = useState(0);

  useEffect(() => {
    let cancelled = false;
    fetchPlacementWritingTasks(sessionId)
      .then((loaded) => {
        if (!cancelled) setTasks(loaded);
      })
      .catch((err) => {
        if (!cancelled) setLoadError(readErrorMessage(err, 'Could not load the writing tasks.'));
      });
    return () => {
      cancelled = true;
    };
  }, [sessionId]);

  if (!tasks) {
    return (
      <div className="flex items-center gap-2 text-sm text-muted" role="status">
        {loadError ? <span className="text-danger-strong">{loadError}</span> : <><Loader2 className="h-4 w-4 animate-spin" aria-hidden /> Preparing writing tasks…</>}
      </div>
    );
  }

  const task = tasks[index];
  if (!task) {
    return (
      <TransitionCard
        heading="No writing tasks left"
        text="There are no writing tasks left in this attempt."
        ctaLabel="Continue"
        onContinue={onComplete}
      />
    );
  }

  return (
    <WritingTaskEditor
      key={task.taskId}
      sessionId={sessionId}
      task={task}
      position={index + 1}
      total={tasks.length}
      onSubmitted={() => {
        if (index + 1 < tasks.length) setIndex((current) => current + 1);
        else onComplete();
      }}
    />
  );
}

export function draftStorageKey(sessionId: string, taskId: string): string {
  return `oet_placement_draft:${sessionId}:${taskId}`;
}

export function writingDeadlineStorageKey(sessionId: string, taskId: string): string {
  return `oet_placement_writing_deadline:${sessionId}:${taskId}`;
}

/** The countdown starts the first time this task is opened and survives a
 *  reload (same resilience the local draft already has) — re-opening the
 *  same task later does not grant a fresh window. `seconds` already carries
 *  the extra-time multiplier applied server-side. */
export function readOrCreateWritingDeadline(sessionId: string, taskId: string, seconds: number): string | null {
  const key = writingDeadlineStorageKey(sessionId, taskId);
  try {
    const existing = window.localStorage.getItem(key);
    if (existing) return existing;
    const deadline = new Date(Date.now() + seconds * 1000).toISOString();
    window.localStorage.setItem(key, deadline);
    return deadline;
  } catch {
    // Storage blocked: no persisted deadline, but the in-memory countdown
    // below still runs for this page view.
    return new Date(Date.now() + seconds * 1000).toISOString();
  }
}

export function clearWritingDeadline(sessionId: string, taskId: string) {
  try {
    window.localStorage.removeItem(writingDeadlineStorageKey(sessionId, taskId));
  } catch {
    // Nothing to clean up if storage was never reachable.
  }
}

export function readLocalDraft(key: string): string {
  try {
    return window.localStorage.getItem(key) ?? '';
  } catch {
    return '';
  }
}

export function writeLocalDraft(key: string, text: string | null) {
  try {
    if (text) window.localStorage.setItem(key, text);
    else window.localStorage.removeItem(key);
  } catch {
    // Storage blocked: the server-side autosave still runs.
  }
}

export function WritingTaskEditor({
  sessionId,
  task,
  position,
  total,
  onSubmitted,
}: {
  sessionId: string;
  task: PlacementWritingTask;
  position: number;
  total: number;
  onSubmitted: () => void;
}) {
  const draftKey = draftStorageKey(sessionId, task.taskId);
  // A local copy survives a reload or a dropped connection; the debounced
  // server autosave is the durable one.
  const [text, setText] = useState(() => (typeof window === 'undefined' ? '' : readLocalDraft(draftKey)));
  const [saveState, setSaveState] = useState<'idle' | 'saving' | 'saved' | 'offline'>('idle');
  const [savedAt, setSavedAt] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Real countdown + timeout enforcement, same pattern as the timed
  // objective modules — extra time is already baked into timeLimitSeconds.
  const [deadlineAt, setDeadlineAt] = useState<string | null>(null);
  useEffect(() => {
    if (typeof window === 'undefined') return;
    const seconds = task.timeLimitSeconds ?? (task.minutes ? task.minutes * 60 : null);
    if (!seconds) return;
    setDeadlineAt(readOrCreateWritingDeadline(sessionId, task.taskId, seconds));
  }, [sessionId, task.minutes, task.taskId, task.timeLimitSeconds]);
  const secondsLeft = useSecondsLeft(deadlineAt);

  useEffect(() => {
    if (!text) return;
    writeLocalDraft(draftKey, text);
    const handle = window.setTimeout(() => {
      setSaveState('saving');
      savePlacementWritingDraft(sessionId, task.taskId, text)
        .then(() => {
          setSaveState('saved');
          setSavedAt(new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }));
        })
        .catch(() => setSaveState('offline'));
    }, 1200);
    return () => window.clearTimeout(handle);
  }, [draftKey, sessionId, task.taskId, text]);

  const wordCount = text.trim().split(/\s+/).filter(Boolean).length;

  const submit = useCallback(async () => {
    if (isSubmitting) return;
    setIsSubmitting(true);
    setError(null);
    try {
      await submitPlacementWriting(sessionId, task.taskId, text);
      writeLocalDraft(draftKey, null);
      clearWritingDeadline(sessionId, task.taskId);
      onSubmitted();
    } catch (err) {
      setError(readErrorMessage(err, 'Could not submit your response — your text is still here. Please try again.'));
    } finally {
      setIsSubmitting(false);
    }
  }, [draftKey, isSubmitting, onSubmitted, sessionId, task.taskId, text]);

  // Auto-submit on timeout, same pattern as the timed objective modules —
  // whatever text exists is submitted rather than leaving the candidate
  // stuck on an expired task.
  useEffect(() => {
    if (secondsLeft === 0 && deadlineAt && !isSubmitting) {
      void submit();
    }
  }, [deadlineAt, isSubmitting, secondsLeft, submit]);

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between gap-3">
        <p className="text-sm text-muted">
          Task {position} of {total}
        </p>
        <UnitTimer secondsLeft={secondsLeft} waitingForAudio={false} />
      </div>

      <section className="space-y-2 rounded-2xl border border-border bg-surface p-5 sm:p-6" aria-label="Writing task">
        <p className="text-xs font-semibold uppercase tracking-wide text-muted">Task</p>
        <p className="whitespace-pre-line break-words text-base leading-relaxed text-navy">{task.prompt}</p>
        <p className="text-xs text-muted">
          {task.minutes ? `Time limit: about ${task.minutes} minutes` : ''}
          {task.minutes && task.minWords ? ' · ' : ''}
          {task.minWords ? `Aim for at least ${task.minWords} words` : ''}
        </p>
      </section>

      <section className="space-y-3 rounded-2xl border border-border bg-surface p-5 sm:p-6" aria-label="Your response">
        <textarea
          value={text}
          onChange={(event) => setText(event.target.value)}
          rows={12}
          spellCheck={false}
          className="w-full rounded-xl border border-border bg-background-light p-4 text-sm leading-relaxed text-navy focus:outline-none focus:ring-2 focus:ring-primary/40"
          placeholder="Write your response here…"
          aria-label={`Writing task ${position} response`}
        />
        <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-muted">
          <span>
            {wordCount} word{wordCount === 1 ? '' : 's'}
            {task.minWords && wordCount < task.minWords ? ` · ${task.minWords - wordCount} more suggested` : ''}
          </span>
          <span role="status">
            {saveState === 'saving' ? 'Saving draft…' : null}
            {saveState === 'saved' && savedAt ? `Draft saved ${savedAt}` : null}
            {saveState === 'offline' ? 'Not saved to the server yet — kept on this device, retrying as you type' : null}
          </span>
        </div>
        {error ? (
          <p role="alert" className="text-sm text-danger-strong">
            {error}
          </p>
        ) : null}
        <button type="button" onClick={() => void submit()} disabled={isSubmitting || wordCount === 0} className={PRIMARY_BUTTON}>
          {isSubmitting ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : <CheckCircle2 className="h-4 w-4" aria-hidden />}
          Submit response
        </button>
      </section>
    </div>
  );
}
