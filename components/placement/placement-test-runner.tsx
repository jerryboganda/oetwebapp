'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { AlertTriangle, CheckCircle2, Loader2, Mic, Play, Square } from 'lucide-react';
import {
  createPlacementSession,
  fetchPlacementFullResult,
  fetchPlacementHistory,
  fetchPlacementReceptiveResult,
  fetchPlacementSessionState,
  fetchPlacementSpeakingTasks,
  fetchPlacementStatus,
  fetchPlacementWritingTasks,
  resolvePlacementAudioUrl,
  savePlacementWritingDraft,
  startPlacementModule,
  submitPlacementResponses,
  submitPlacementSpeaking,
  submitPlacementWriting,
  uploadPlacementRecording,
  type PlacementDeliveryUnit,
  type PlacementHistoryItem,
  type PlacementModule,
  type PlacementResultReport,
  type PlacementSessionState,
  type PlacementSpeakingTask,
  type PlacementWritingTask,
} from '@/lib/api/placement';
import { readErrorMessage } from '@/lib/read-error-message';

/**
 * The free General-English placement journey: receptive modules
 * (Language Systems → Reading → Listening), then Speaking and Writing,
 * then the skill-first result report. Every screen reports engine truth —
 * unmeasured modules show as unmeasured; pending-review submissions show
 * as pending, never as scores.
 */

const SESSION_STORAGE_KEY = 'oet_placement_active_session';
const OBJECTIVE_MODULES: PlacementModule[] = ['LS', 'RD', 'LSN'];

const MODULE_LABELS: Record<PlacementModule, string> = {
  LS: 'Language Systems',
  RD: 'Reading',
  LSN: 'Listening',
};

type Stage =
  | 'loading'
  | 'disabled'
  | 'intro'
  | 'objective'
  | 'receptive-result'
  | 'speaking'
  | 'writing'
  | 'results';

function deadlineSecondsFrom(deadlineAt: string): number {
  const parsed = Date.parse(deadlineAt);
  if (Number.isNaN(parsed)) return 0;
  return Math.max(0, Math.round((parsed - Date.now()) / 1000));
}

function bandLabel(band: string | null): string {
  return band ?? 'Not measured';
}

export function PlacementTestRunner() {
  const [stage, setStage] = useState<Stage>('loading');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [sessionState, setSessionState] = useState<PlacementSessionState | null>(null);
  const [history, setHistory] = useState<PlacementHistoryItem[]>([]);
  const [receptiveReport, setReceptiveReport] = useState<PlacementResultReport | null>(null);
  const [fullReport, setFullReport] = useState<PlacementResultReport | null>(null);

  const refreshHistory = useCallback(async () => {
    try {
      setHistory(await fetchPlacementHistory());
    } catch {
      // History is a nice-to-have on this screen — the journey continues.
    }
  }, []);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const status = await fetchPlacementStatus();
        if (cancelled) return;
        if (!status.enabled) {
          setStage('disabled');
          return;
        }
        await refreshHistory();

        // Resume an in-flight session if one exists and still loads.
        const stored = typeof window !== 'undefined' ? window.localStorage.getItem(SESSION_STORAGE_KEY) : null;
        if (stored) {
          try {
            const state = await fetchPlacementSessionState(stored);
            if (cancelled) return;
            setSessionId(stored);
            setSessionState(state);
            if (state.has_result) {
              setFullReport(await fetchPlacementFullResult(stored));
              setStage('results');
            } else if (state.rd_status === 'complete' && state.lsn_status === 'complete') {
              setReceptiveReport(await fetchPlacementReceptiveResult(stored));
              setStage(state.spk_status === 'complete' ? 'writing' : 'speaking');
            } else {
              setStage('objective');
            }
            return;
          } catch {
            window.localStorage.removeItem(SESSION_STORAGE_KEY);
          }
        }
        if (!cancelled) setStage('intro');
      } catch (error) {
        if (!cancelled) {
          setErrorMessage(readErrorMessage(error, 'The placement test is not available right now.'));
          setStage('disabled');
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [refreshHistory]);

  const startSession = useCallback(async () => {
    setErrorMessage(null);
    try {
      const created = await createPlacementSession();
      window.localStorage.setItem(SESSION_STORAGE_KEY, created.sessionId);
      setSessionId(created.sessionId);
      const state = await fetchPlacementSessionState(created.sessionId);
      setSessionState(state);
      setStage('objective');
    } catch (error) {
      setErrorMessage(readErrorMessage(error, 'Could not start the test — please try again.'));
    }
  }, []);

  if (stage === 'loading') {
    return (
      <div className="flex min-h-[50vh] items-center justify-center gap-3 text-sm text-muted" role="status">
        <Loader2 className="h-5 w-5 animate-spin" aria-hidden /> Loading the placement test…
      </div>
    );
  }

  if (stage === 'disabled') {
    return (
      <div className="mx-auto max-w-xl space-y-3 rounded-2xl border border-border bg-surface p-6 text-center">
        <AlertTriangle className="mx-auto h-8 w-8 text-warning" aria-hidden />
        <h2 className="text-lg font-semibold text-navy">Placement test unavailable</h2>
        <p className="text-sm text-muted">
          {errorMessage ?? 'The free placement test has not been switched on for your account yet. Please check back soon.'}
        </p>
      </div>
    );
  }

  if (stage === 'intro') {
    return (
      <IntroCard
        errorMessage={errorMessage}
        history={history}
        onStart={startSession}
      />
    );
  }

  return (
    <div className="space-y-6">
      {sessionState ? (
        <p className="sr-only">
          Placement session {sessionState.session_id} under ruleset {sessionState.ruleset_version}
        </p>
      ) : null}

      {stage === 'objective' && sessionId ? (
        <ObjectiveStage
          sessionId={sessionId}
          onModuleComplete={async (module) => {
            const state = await fetchPlacementSessionState(sessionId);
            setSessionState(state);
            if (module === 'LSN') {
              const report = await fetchPlacementReceptiveResult(sessionId);
              setReceptiveReport(report);
              setStage('receptive-result');
            }
          }}
        />
      ) : null}

      {stage === 'receptive-result' && receptiveReport ? (
        <ResultReportCard
          title="Your foundation profile"
          report={receptiveReport}
          primaryLabel="Continue to Speaking"
          onPrimary={() => setStage('speaking')}
        />
      ) : null}

      {stage === 'speaking' && sessionId ? (
        <SpeakingStage
          sessionId={sessionId}
          onComplete={async () => {
            const state = await fetchPlacementSessionState(sessionId);
            setSessionState(state);
            setStage('writing');
          }}
        />
      ) : null}

      {stage === 'writing' && sessionId ? (
        <WritingStage
          sessionId={sessionId}
          onComplete={async () => {
            const report = await fetchPlacementFullResult(sessionId);
            setFullReport(report);
            setStage('results');
            await refreshHistory();
          }}
        />
      ) : null}

      {stage === 'results' && fullReport ? <ResultReportCard title="Your placement profile" report={fullReport} /> : null}
    </div>
  );
}

// ── Intro ────────────────────────────────────────────────────────────

function IntroCard({
  errorMessage,
  history,
  onStart,
}: {
  errorMessage: string | null;
  history: PlacementHistoryItem[];
  onStart: () => void;
}) {
  return (
    <div className="mx-auto max-w-xl space-y-4 rounded-2xl border border-border bg-surface p-6">
      <h2 className="text-xl font-semibold text-navy">Free General English Placement Test</h2>
      <p className="text-sm text-muted">
        Four short modules — Listening, Reading, Writing and Speaking — place you from Pre-A1 to C2.
        Your answers save as you go, so you can safely continue after an interruption. Results are
        saved to your profile when you finish.
      </p>
      <ul className="space-y-1 text-sm text-muted">
        <li>• Language Systems, Reading and Listening: multiple choice with timers</li>
        <li>• Speaking: short recorded responses (microphone required)</li>
        <li>• Writing: short written tasks with autosave</li>
      </ul>
      {errorMessage ? <p role="alert" className="text-sm text-danger">{errorMessage}</p> : null}
      <button
        type="button"
        onClick={onStart}
        className="inline-flex items-center gap-2 rounded-xl bg-primary px-5 py-2.5 text-sm font-semibold text-white transition hover:opacity-90"
      >
        <Play className="h-4 w-4" aria-hidden /> Start Free Placement Test
      </button>
      {history.length > 0 ? (
        <p className="text-xs text-muted">
          You have {history.length} previous result{history.length === 1 ? '' : 's'} saved on your profile
          ({history[0].status === 'completed' ? 'latest complete' : 'latest partial'}).
          Starting a new test creates a fresh attempt.
        </p>
      ) : null}
    </div>
  );
}
// ── Objective modules (LS / RD / LSN) ────────────────────────────────

function ObjectiveStage({
  sessionId,
  onModuleComplete,
}: {
  sessionId: string;
  onModuleComplete: (module: PlacementModule) => void;
}) {
  const [moduleIndex, setModuleIndex] = useState(0);
  const [unit, setUnit] = useState<PlacementDeliveryUnit | null>(null);
  const [selected, setSelected] = useState<Record<string, string>>({});
  const [secondsLeft, setSecondsLeft] = useState<number | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const replayCountsRef = useRef<Record<string, number>>({});
  const shownAtRef = useRef<number>(Date.now());

  const moduleName = OBJECTIVE_MODULES[moduleIndex];

  const loadUnit = useCallback(async () => {
    setError(null);
    setSelected({});
    try {
      const nextUnit = await startPlacementModule(sessionId, moduleName);
      if (!nextUnit.module_complete) {
        shownAtRef.current = Date.now();
        setSecondsLeft(deadlineSecondsFrom(nextUnit.deadline_at));
      }
      setUnit(nextUnit);
    } catch (err) {
      setError(readErrorMessage(err, 'Could not load the next questions.'));
    }
  }, [moduleName, sessionId]);

  useEffect(() => {
    setUnit(null);
    loadUnit();
  }, [loadUnit]);

  useEffect(() => {
    if (secondsLeft === null || !unit || unit.module_complete) return;
    const timer = setInterval(() => {
      setSecondsLeft((current) => (current === null ? null : Math.max(0, current - 1)));
    }, 1000);
    return () => clearInterval(timer);
  }, [secondsLeft, unit]);

  const handleSubmit = useCallback(async (isTimeout = false) => {
    if (!unit || submitting) return;
    const elapsed = Date.now() - shownAtRef.current;
    const responseList = unit.items.map((item) => ({
      itemId: item.item_id,
      selectedOptionId: selected[item.item_id] ?? null,
      responseMs: Math.min(elapsed, 20 * 60 * 1000),
    }));
    if (!isTimeout && responseList.some((r) => r.selectedOptionId === null)) {
      setError('Answer every question in this set before continuing.');
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      const result = await submitPlacementResponses(sessionId, responseList, replayCountsRef.current[unit.items[0]?.item_id ?? ''] ?? 0);
      replayCountsRef.current = {};
      if (result.next && !result.next.module_complete) {
        shownAtRef.current = Date.now();
        setSecondsLeft(deadlineSecondsFrom(result.next.deadline_at));
        setSelected({});
        setUnit(result.next);
      } else {
        // Module finished (final confirmation outcome). The runner's parent
        // stage-advance (result → speaking) is driven by onModuleComplete;
        // within the objective stage we advance to the next module unless
        // this was the last one.
        setUnit(result.next && result.next.module_complete ? result.next : unit);
        if (moduleIndex + 1 < OBJECTIVE_MODULES.length) {
          setModuleIndex(moduleIndex + 1);
        }
        onModuleComplete(moduleName);
      }
    } catch (err) {
      setError(readErrorMessage(err, 'Could not save your answers — check your connection and retry.'));
    } finally {
      setSubmitting(false);
    }
  }, [moduleName, moduleIndex, onModuleComplete, selected, sessionId, submitting, unit]);

  useEffect(() => {
    if (secondsLeft === 0 && unit && !unit.module_complete && !submitting) {
      // The server records late submissions as omissions (grace window) —
      // auto-submit keeps the candidate moving.
      handleSubmit(true);
    }
  }, [handleSubmit, secondsLeft, submitting, unit]);

  if (!unit) {
    return <div className="flex items-center gap-2 text-sm text-muted" role="status"><Loader2 className="h-4 w-4 animate-spin" aria-hidden /> Preparing {MODULE_LABELS[moduleName]}…</div>;
  }

  if (unit.module_complete) {
    return <div className="flex items-center gap-2 text-sm text-muted" role="status"><Loader2 className="h-4 w-4 animate-spin" aria-hidden /> Finishing {MODULE_LABELS[moduleName]}…</div>;
  }

  const minutes = Math.floor((secondsLeft ?? 0) / 60);
  const seconds = (secondsLeft ?? 0) % 60;

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm font-semibold text-navy">
          {MODULE_LABELS[moduleName]} — set {unit.items.length} question{unit.items.length === 1 ? '' : 's'}
        </p>
        <p className={`text-sm font-mono ${secondsLeft !== null && secondsLeft < 30 ? 'text-danger' : 'text-muted'}`} aria-live="off">
          {minutes}:{String(seconds).padStart(2, '0')} left
        </p>
      </div>

      {unit.audio_url ? (
        <UnitAudio audioUrl={unit.audio_url} onReplay={(itemId) => {
          replayCountsRef.current[itemId] = (replayCountsRef.current[itemId] ?? 0) + 1;
        }} />
      ) : null}

      {unit.stimulus_text ? (
        <div className="max-h-72 overflow-y-auto rounded-2xl border border-border bg-surface p-4 text-sm leading-relaxed text-navy">
          {unit.stimulus_text}
        </div>
      ) : null}

      <div className="space-y-4">
        {unit.items.map((item, index) => (
          <fieldset key={item.item_id} className="space-y-2 rounded-2xl border border-border bg-surface p-4">
            <legend className="px-1 text-sm font-semibold text-navy">{index + 1}. {item.stem}</legend>
            {item.options.map((option) => (
              <label key={option.option_id} className="flex cursor-pointer items-start gap-2 text-sm text-navy">
                <input
                  type="radio"
                  className="mt-1"
                  name={item.item_id}
                  value={option.option_id}
                  checked={selected[item.item_id] === option.option_id}
                  onChange={() => setSelected((current) => ({ ...current, [item.item_id]: option.option_id }))}
                />
                <span>{option.text}</span>
              </label>
            ))}
          </fieldset>
        ))}
      </div>

      {error ? <p role="alert" className="text-sm text-danger">{error}</p> : null}

      <button
        type="button"
        onClick={() => handleSubmit(false)}
        disabled={submitting}
        className="inline-flex items-center gap-2 rounded-xl bg-primary px-5 py-2.5 text-sm font-semibold text-white transition hover:opacity-90 disabled:opacity-60"
      >
        {submitting ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : <CheckCircle2 className="h-4 w-4" aria-hidden />}
        Submit answers
      </button>
    </div>
  );
}

function UnitAudio({ audioUrl, onReplay }: { audioUrl: string; onReplay: (itemId: string) => void }) {
  const resolved = useMemo(() => resolvePlacementAudioUrl(audioUrl), [audioUrl]);
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const [playCount, setPlayCount] = useState(0);

  if (!resolved) {
    return <p className="text-sm text-danger">Audio for this set is unavailable — the server will treat it as a technical issue.</p>;
  }

  return (
    <div className="flex items-center gap-3 rounded-2xl border border-border bg-surface p-4">
      {/* eslint-disable-next-line jsx-a11y/media-has-caption -- stimulus audio has no captions by design */}
      <audio
        ref={audioRef}
        src={resolved}
        controls
        preload="none"
        className="w-full"
        onPlay={() => {
          setPlayCount((count) => count + 1);
          onReplay('');
        }}
      />
      <span className="whitespace-nowrap text-xs text-muted">{playCount} play{playCount === 1 ? '' : 's'}</span>
    </div>
  );
}

// ── Speaking ─────────────────────────────────────────────────────────

function SpeakingStage({ sessionId, onComplete }: { sessionId: string; onComplete: () => void }) {
  const [tasks, setTasks] = useState<PlacementSpeakingTask[] | null>(null);
  const [index, setIndex] = useState(0);
  const [micState, setMicState] = useState<'idle' | 'granted' | 'denied'>('idle');
  const [recorder, setRecorder] = useState<MediaRecorder | null>(null);
  const [recordedUrl, setRecordedUrl] = useState<string | null>(null);
  const [isUploading, setIsUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const chunksRef = useRef<Blob[]>([]);

  useEffect(() => {
    let cancelled = false;
    fetchPlacementSpeakingTasks(sessionId)
      .then((loaded) => {
        if (!cancelled) setTasks(loaded);
      })
      .catch((err) => {
        if (!cancelled) setError(readErrorMessage(err, 'Could not load the speaking tasks.'));
      });
    return () => {
      cancelled = true;
    };
  }, [sessionId]);

  const requestMic = async () => {
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      stream.getTracks().forEach((track) => track.stop());
      setMicState('granted');
    } catch {
      setMicState('denied');
    }
  };

  const startRecording = async () => {
    setError(null);
    setNotice(null);
    setRecordedUrl(null);
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      const mimeType = MediaRecorder.isTypeSupported('audio/webm;codecs=opus')
        ? 'audio/webm;codecs=opus'
        : MediaRecorder.isTypeSupported('audio/mp4')
          ? 'audio/mp4'
          : '';
      const rec = new MediaRecorder(stream, mimeType ? { mimeType } : undefined);
      chunksRef.current = [];
      rec.ondataavailable = (event) => {
        if (event.data.size > 0) chunksRef.current.push(event.data);
      };
      rec.onstop = () => {
        stream.getTracks().forEach((track) => track.stop());
        const blob = new Blob(chunksRef.current, { type: rec.mimeType || 'audio/webm' });
        setRecordedUrl(URL.createObjectURL(blob));
      };
      rec.start(1000);
      setRecorder(rec);
    } catch {
      setMicState('denied');
      setError('Microphone access is required to record your speaking response.');
    }
  };

  const stopRecording = () => {
    recorder?.stop();
    setRecorder(null);
  };

  const submitRecording = async () => {
    if (!recordedUrl) return;
    const task = tasks?.[index];
    if (!task) return;
    setIsUploading(true);
    setError(null);
    try {
      const blob = await (await fetch(recordedUrl)).blob();
      const extension = blob.type.includes('mp4') ? 'm4a' : 'webm';
      const uploaded = await uploadPlacementRecording(blob, `speaking-${task.taskId}.${extension}`);
      const outcome = await submitPlacementSpeaking(sessionId, task.taskId, uploaded.storagePath);
      setNotice(
        outcome.status === 'rated'
          ? 'Response submitted and rated.'
          : 'Response submitted. It is saved securely and queued for review — your result updates once reviewed.',
      );
      setRecordedUrl(null);
      if (index + 1 < (tasks?.length ?? 0)) {
        setIndex((current) => current + 1);
      } else {
        onComplete();
      }
    } catch (err) {
      setError(readErrorMessage(err, 'Could not submit the recording — please retry.'));
    } finally {
      setIsUploading(false);
    }
  };

  if (!tasks) {
    return <div className="flex items-center gap-2 text-sm text-muted" role="status"><Loader2 className="h-4 w-4 animate-spin" aria-hidden /> {error ?? 'Preparing speaking tasks…'}</div>;
  }

  const task = tasks[index];

  return (
    <div className="mx-auto max-w-xl space-y-4 rounded-2xl border border-border bg-surface p-6">
      <p className="text-sm font-semibold text-navy">Speaking — task {index + 1} of {tasks.length}</p>
      <p className="text-sm text-navy">{task?.prompt}</p>

      {micState !== 'granted' ? (
        <button type="button" onClick={requestMic} className="inline-flex items-center gap-2 rounded-xl bg-primary px-4 py-2 text-sm font-semibold text-white hover:opacity-90">
          <Mic className="h-4 w-4" aria-hidden /> Check microphone
        </button>
      ) : null}
      {micState === 'denied' ? <p className="text-sm text-danger">Microphone access was blocked — enable it in your browser settings to continue.</p> : null}

      {micState === 'granted' ? (
        <div className="flex flex-wrap items-center gap-3">
          {!recorder ? (
            <button type="button" onClick={startRecording} className="inline-flex items-center gap-2 rounded-xl bg-primary px-4 py-2 text-sm font-semibold text-white hover:opacity-90">
              <Mic className="h-4 w-4" aria-hidden /> {recordedUrl ? 'Record again' : 'Start recording'}
            </button>
          ) : (
            <button type="button" onClick={stopRecording} className="inline-flex items-center gap-2 rounded-xl bg-danger px-4 py-2 text-sm font-semibold text-white hover:opacity-90">
              <Square className="h-4 w-4" aria-hidden /> Stop
            </button>
          )}
          {recordedUrl ? (
            <>
              {/* eslint-disable-next-line jsx-a11y/media-has-caption -- candidate playback of own recording */}
              <audio src={recordedUrl} controls className="max-w-xs" />
              <button type="button" onClick={submitRecording} disabled={isUploading} className="inline-flex items-center gap-2 rounded-xl bg-primary px-4 py-2 text-sm font-semibold text-white hover:opacity-90 disabled:opacity-60">
                {isUploading ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : null} Submit response
              </button>
            </>
          ) : null}
        </div>
      ) : null}

      {notice ? <p className="text-sm text-success">{notice}</p> : null}
      {error ? <p role="alert" className="text-sm text-danger">{error}</p> : null}
    </div>
  );
}

// ── Writing ──────────────────────────────────────────────────────────

function WritingStage({ sessionId, onComplete }: { sessionId: string; onComplete: () => void }) {
  const [tasks, setTasks] = useState<PlacementWritingTask[] | null>(null);
  const [index, setIndex] = useState(0);
  const [text, setText] = useState('');
  const [savedAt, setSavedAt] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const taskId = tasks?.[index]?.taskId;

  useEffect(() => {
    let cancelled = false;
    fetchPlacementWritingTasks(sessionId)
      .then((loaded) => {
        if (!cancelled) setTasks(loaded);
      })
      .catch((err) => {
        if (!cancelled) setError(readErrorMessage(err, 'Could not load the writing tasks.'));
      });
    return () => {
      cancelled = true;
    };
  }, [sessionId]);

  // Debounced server autosave — a network blip never costs the draft.
  useEffect(() => {
    if (!taskId || !text) return;
    const handle = setTimeout(() => {
      savePlacementWritingDraft(sessionId, taskId, text)
        .then(() => setSavedAt(new Date().toLocaleTimeString()))
        .catch(() => setSavedAt(null));
    }, 1200);
    return () => clearTimeout(handle);
  }, [sessionId, taskId, text]);

  const submit = async () => {
    if (!taskId || isSubmitting) return;
    setIsSubmitting(true);
    setError(null);
    try {
      const outcome = await submitPlacementWriting(sessionId, taskId, text);
      setNotice(
        outcome.status === 'rated'
          ? 'Response submitted.'
          : 'Response submitted. It is saved securely and queued for review — your result updates once reviewed.',
      );
      setText('');
      setSavedAt(null);
      if (index + 1 < (tasks?.length ?? 0)) {
        setIndex((current) => current + 1);
      } else {
        onComplete();
      }
    } catch (err) {
      setError(readErrorMessage(err, 'Could not submit the response — please retry.'));
    } finally {
      setIsSubmitting(false);
    }
  };

  if (!tasks) {
    return <div className="flex items-center gap-2 text-sm text-muted" role="status"><Loader2 className="h-4 w-4 animate-spin" aria-hidden /> {error ?? 'Preparing writing tasks…'}</div>;
  }

  const task = tasks[index];
  const wordCount = text.trim().split(/\s+/).filter(Boolean).length;

  return (
    <div className="mx-auto max-w-2xl space-y-4 rounded-2xl border border-border bg-surface p-6">
      <p className="text-sm font-semibold text-navy">Writing — task {index + 1} of {tasks.length}</p>
      <p className="text-sm text-navy">{task?.prompt}</p>
      <textarea
        value={text}
        onChange={(event) => setText(event.target.value)}
        rows={12}
        className="w-full rounded-xl border border-border bg-background-light p-3 text-sm text-navy focus:outline-none focus:ring-2 focus:ring-primary/40"
        placeholder="Write your response here…"
        aria-label={`Writing task ${index + 1} response`}
      />
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-xs text-muted">
          {wordCount} words{savedAt ? ` · draft saved ${savedAt}` : ''}
        </p>
        <button
          type="button"
          onClick={submit}
          disabled={isSubmitting || wordCount === 0}
          className="inline-flex items-center gap-2 rounded-xl bg-primary px-5 py-2.5 text-sm font-semibold text-white hover:opacity-90 disabled:opacity-60"
        >
          {isSubmitting ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : <CheckCircle2 className="h-4 w-4" aria-hidden />}
          Submit response
        </button>
      </div>
      {notice ? <p className="text-sm text-success">{notice}</p> : null}
      {error ? <p role="alert" className="text-sm text-danger">{error}</p> : null}
    </div>
  );
}

// ── Results ──────────────────────────────────────────────────────────

function ResultReportCard({
  title,
  report,
  primaryLabel,
  onPrimary,
}: {
  title: string;
  report: PlacementResultReport;
  primaryLabel?: string;
  onPrimary?: () => void;
}) {
  return (
    <div className="mx-auto max-w-2xl space-y-4">
      <div className="rounded-2xl border border-border bg-surface p-6">
        <h2 className="text-xl font-semibold text-navy">{title}</h2>
        <p className="mt-1 text-sm text-muted">
          Confidence: {report.confidence}
          {report.headline.kind === 'indicative_overall' && report.headline.band
            ? ` · indicative overall ${report.headline.band}`
            : report.headline.kind === 'uneven' && report.headline.range
              ? ` · uneven profile (${report.headline.range[0]}–${report.headline.range[1]})`
              : ''}
        </p>
        <div className="mt-4 grid gap-3 sm:grid-cols-2">
          {report.skills.map((skill) => (
            <div key={skill.skill} className="rounded-xl border border-border bg-background-light p-4">
              <p className="text-sm font-semibold text-navy">{SKILL_LABELS[skill.skill] ?? skill.skill}</p>
              <p className="text-lg font-semibold text-primary">{skill.band ?? '—'}</p>
              <p className="text-xs uppercase tracking-wide text-muted">{skill.status}</p>
              {skill.notes.slice(0, 2).map((note) => (
                <p key={note} className="mt-1 text-xs text-muted">{note}</p>
              ))}
            </div>
          ))}
        </div>
        {report.confidence_reasons.length > 0 ? (
          <ul className="mt-4 space-y-1 text-xs text-muted">
            {report.confidence_reasons.map((reason) => <li key={reason}>• {reason}</li>)}
          </ul>
        ) : null}
        <p className="mt-4 text-xs text-muted">{report.retest_advice}</p>
        {report.readiness ? (
          <p className="mt-2 text-xs text-muted">{report.readiness.text} {report.readiness.disclaimer}</p>
        ) : null}
      </div>
      {primaryLabel && onPrimary ? (
        <div className="text-center">
          <button type="button" onClick={onPrimary} className="inline-flex items-center gap-2 rounded-xl bg-primary px-5 py-2.5 text-sm font-semibold text-white hover:opacity-90">
            {primaryLabel}
          </button>
        </div>
      ) : null}
    </div>
  );
}

const SKILL_LABELS: Record<string, string> = {
  LS: 'Language Systems',
  RD: 'Reading',
  LSN: 'Listening',
  SPK: 'Speaking',
  WRT: 'Writing',
};
