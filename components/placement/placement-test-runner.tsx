'use client';

import Link from 'next/link';
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { AlertTriangle, CheckCircle2, Headphones, Loader2, Mic, Play, RotateCcw, Square, Volume2 } from 'lucide-react';
import { ResultReportCard } from '@/components/placement/result-report-card';
import { fetchAuthorizedObjectUrl } from '@/lib/api/binary';
import {
  PLACEMENT_ACTIVE_SESSION_KEY,
  createPlacementSession,
  fetchPlacementFullResult,
  fetchPlacementHistory,
  fetchPlacementReceptiveResult,
  fetchPlacementSessionState,
  fetchPlacementSpeakingTasks,
  fetchPlacementStatus,
  fetchPlacementWritingTasks,
  reportPlacementUnitTechnical,
  resolvePlacementAudioUrl,
  savePlacementWritingDraft,
  startPlacementModule,
  startPlacementUnit,
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
  type PlacementTechnicalReason,
  type PlacementWritingTask,
} from '@/lib/api/placement';
import { readErrorMessage } from '@/lib/read-error-message';

/**
 * The free General-English placement journey, in five parts:
 * Language Systems → Reading → Listening → Speaking → Writing, with an
 * overview up front, a transition screen between every part, and a
 * persistent "Part X of 5" header. Every screen reports engine truth —
 * unmeasured modules show as unmeasured; pending-review submissions show as
 * pending, never as scores. Unit clocks are started server-side only once a
 * unit is actually usable, so buffering is never charged to the candidate.
 */

// ── Parts ────────────────────────────────────────────────────────────

type PartKey = PlacementModule | 'SPK' | 'WRT';

interface PartInfo {
  key: PartKey;
  name: string;
  measures: string;
  count: string;
  time: string;
}

const PARTS: PartInfo[] = [
  { key: 'LS', name: 'Language Systems', measures: 'Grammar and vocabulary', count: 'Approx. 12–18 items', time: '8–12 min' },
  { key: 'RD', name: 'Reading', measures: 'Notices, messages and longer texts', count: 'Approx. 12–18 items', time: '10–15 min' },
  { key: 'LSN', name: 'Listening', measures: 'Conversations, announcements and talks', count: 'Approx. 12–18 items', time: '10–15 min' },
  { key: 'SPK', name: 'Speaking', measures: 'Recorded spoken responses', count: '8 recorded responses', time: '10–15 min' },
  { key: 'WRT', name: 'Writing', measures: 'Short and extended written tasks', count: '3 scored tasks', time: '20–30 min' },
];

function partIndex(key: PartKey): number {
  return PARTS.findIndex((part) => part.key === key);
}

type Screen =
  | { kind: 'loading' }
  | { kind: 'disabled' }
  | { kind: 'overview' }
  | { kind: 'part-intro' }
  | { kind: 'objective'; module: PlacementModule }
  | { kind: 'module-complete'; module: 'LS' | 'RD' }
  | { kind: 'audio-check' }
  | { kind: 'foundation-complete' }
  | { kind: 'mic-check' }
  | { kind: 'speaking' }
  | { kind: 'speaking-complete' }
  | { kind: 'writing' }
  | { kind: 'assessment-complete' }
  | { kind: 'results' };

/** Header position for a screen: the part on screen and how many are done. */
function headerFor(screen: Screen): { part: number; completed: number } | null {
  switch (screen.kind) {
    case 'part-intro':
      return { part: 0, completed: 0 };
    case 'objective':
      return { part: partIndex(screen.module), completed: partIndex(screen.module) };
    case 'module-complete':
      return { part: partIndex(screen.module), completed: partIndex(screen.module) + 1 };
    case 'audio-check':
      return { part: 2, completed: 2 };
    case 'foundation-complete':
      return { part: 2, completed: 3 };
    case 'mic-check':
    case 'speaking':
      return { part: 3, completed: 3 };
    case 'speaking-complete':
      return { part: 3, completed: 4 };
    case 'writing':
      return { part: 4, completed: 4 };
    case 'assessment-complete':
      return { part: 4, completed: 5 };
    default:
      return null;
  }
}

/** Where a reloaded, still-open session should pick up. Listening and
 *  Speaking resume through their check screens: audio autoplay needs a fresh
 *  tap, and the microphone needs permission again. */
function resumeScreen(state: PlacementSessionState): Screen {
  if (state.has_result) return { kind: 'results' };
  if (state.ls_status !== 'complete') return { kind: 'objective', module: 'LS' };
  if (state.rd_status !== 'complete') return { kind: 'objective', module: 'RD' };
  if (state.lsn_status !== 'complete') return { kind: 'audio-check' };
  if (state.spk_status !== 'complete') return { kind: 'mic-check' };
  if (state.wrt_status !== 'complete') return { kind: 'writing' };
  return { kind: 'assessment-complete' };
}

function readStoredSession(): string | null {
  try {
    return window.localStorage.getItem(PLACEMENT_ACTIVE_SESSION_KEY);
  } catch {
    return null;
  }
}

function writeStoredSession(sessionId: string | null) {
  try {
    if (sessionId) window.localStorage.setItem(PLACEMENT_ACTIVE_SESSION_KEY, sessionId);
    else window.localStorage.removeItem(PLACEMENT_ACTIVE_SESSION_KEY);
  } catch {
    // Storage blocked (private mode): resume simply won't be offered.
  }
}

const PRIMARY_BUTTON =
  'inline-flex min-h-11 w-full items-center justify-center gap-2 rounded-xl bg-primary px-5 py-2.5 text-sm font-semibold text-white transition hover:opacity-90 disabled:opacity-60 sm:w-auto';
const SECONDARY_BUTTON =
  'inline-flex min-h-11 w-full items-center justify-center gap-2 rounded-xl border border-border bg-surface px-5 py-2.5 text-sm font-semibold text-navy transition hover:border-primary/60 disabled:opacity-60 sm:w-auto';

// ── Runner ───────────────────────────────────────────────────────────

export function PlacementTestRunner() {
  const [screen, setScreen] = useState<Screen>({ kind: 'loading' });
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [sessionState, setSessionState] = useState<PlacementSessionState | null>(null);
  const [history, setHistory] = useState<PlacementHistoryItem[]>([]);
  const [receptiveReport, setReceptiveReport] = useState<PlacementResultReport | null>(null);
  const [fullReport, setFullReport] = useState<PlacementResultReport | null>(null);
  const [answeredInPart, setAnsweredInPart] = useState(0);
  const [busy, setBusy] = useState(false);
  const topRef = useRef<HTMLDivElement | null>(null);

  const refreshHistory = useCallback(async () => {
    try {
      setHistory(await fetchPlacementHistory());
    } catch {
      // History is a nice-to-have on this screen — the journey continues.
    }
  }, []);

  const go = useCallback((next: Screen) => {
    if (next.kind === 'objective') setAnsweredInPart(0);
    setErrorMessage(null);
    setScreen(next);
    // New part on screen: bring its top into view (mobile keeps scroll).
    topRef.current?.scrollIntoView?.({ block: 'start' });
  }, []);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const status = await fetchPlacementStatus();
        if (cancelled) return;
        if (!status.enabled) {
          setScreen({ kind: 'disabled' });
          return;
        }
        await refreshHistory();

        // Resume an in-flight session if one exists and still loads.
        const stored = readStoredSession();
        if (stored) {
          try {
            const state = await fetchPlacementSessionState(stored);
            if (cancelled) return;
            setSessionId(stored);
            setSessionState(state);
            const next = resumeScreen(state);
            if (next.kind === 'results') {
              setFullReport(await fetchPlacementFullResult(stored));
            } else if (state.lsn_status === 'complete') {
              setReceptiveReport(await fetchPlacementReceptiveResult(stored).catch(() => null));
            }
            if (!cancelled) setScreen(next);
            return;
          } catch {
            writeStoredSession(null);
          }
        }
        if (!cancelled) setScreen({ kind: 'overview' });
      } catch (error) {
        if (!cancelled) {
          setErrorMessage(readErrorMessage(error, 'The placement test is not available right now.'));
          setScreen({ kind: 'disabled' });
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [refreshHistory]);

  const startSession = useCallback(async () => {
    setErrorMessage(null);
    setBusy(true);
    try {
      const created = await createPlacementSession();
      writeStoredSession(created.sessionId);
      setSessionId(created.sessionId);
      setReceptiveReport(null);
      setFullReport(null);
      setSessionState(await fetchPlacementSessionState(created.sessionId));
      go({ kind: 'part-intro' });
    } catch (error) {
      setErrorMessage(readErrorMessage(error, 'Could not start the test — please try again.'));
    } finally {
      setBusy(false);
    }
  }, [go]);

  const handleModuleComplete = useCallback(
    async (module: PlacementModule) => {
      if (!sessionId) return;
      fetchPlacementSessionState(sessionId).then(setSessionState).catch(() => undefined);
      if (module === 'LS' || module === 'RD') {
        go({ kind: 'module-complete', module });
        return;
      }
      try {
        setReceptiveReport(await fetchPlacementReceptiveResult(sessionId));
      } catch {
        setReceptiveReport(null);
      }
      go({ kind: 'foundation-complete' });
    },
    [go, sessionId],
  );

  const viewResults = useCallback(async () => {
    if (!sessionId) return;
    setBusy(true);
    setErrorMessage(null);
    try {
      setFullReport(await fetchPlacementFullResult(sessionId));
      go({ kind: 'results' });
      await refreshHistory();
    } catch (error) {
      setErrorMessage(readErrorMessage(error, 'Your profile is still being prepared — please try again in a moment.'));
    } finally {
      setBusy(false);
    }
  }, [go, refreshHistory, sessionId]);

  const startNewAttempt = useCallback(() => {
    writeStoredSession(null);
    setSessionId(null);
    setSessionState(null);
    setReceptiveReport(null);
    setFullReport(null);
    go({ kind: 'overview' });
  }, [go]);

  if (screen.kind === 'loading') {
    return (
      <div className="flex min-h-[50vh] items-center justify-center gap-3 text-sm text-muted" role="status">
        <Loader2 className="h-5 w-5 animate-spin" aria-hidden /> Loading the placement test…
      </div>
    );
  }

  if (screen.kind === 'disabled') {
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

  const header = headerFor(screen);
  const partFraction = screen.kind === 'objective' ? Math.min(answeredInPart / 15, 0.9) : 0;

  return (
    <div ref={topRef} className="mx-auto w-full max-w-3xl scroll-mt-4 space-y-6">
      {sessionState ? (
        <p className="sr-only">
          Placement session {sessionState.session_id} under ruleset {sessionState.ruleset_version}
        </p>
      ) : null}

      {header ? <ProgressHeader part={header.part} completed={header.completed} fraction={partFraction} /> : null}

      {screen.kind === 'overview' ? (
        <OverviewCard errorMessage={errorMessage} history={history} busy={busy} onStart={startSession} />
      ) : null}

      {screen.kind === 'part-intro' ? (
        <TransitionCard
          heading="Part 1 of 5 — Language Systems"
          text="Grammar and vocabulary in context. Approx. 8–12 minutes."
          ctaLabel="Start Part 1"
          onContinue={() => go({ kind: 'objective', module: 'LS' })}
        />
      ) : null}

      {screen.kind === 'objective' && sessionId ? (
        <ObjectiveStage
          key={screen.module}
          sessionId={sessionId}
          module={screen.module}
          onProgress={setAnsweredInPart}
          onComplete={() => handleModuleComplete(screen.module)}
        />
      ) : null}

      {screen.kind === 'module-complete' && screen.module === 'LS' ? (
        <TransitionCard
          completed
          heading="Language Systems complete"
          text="Next: Reading — approx. 10–15 minutes."
          ctaLabel="Continue to Reading"
          onContinue={() => go({ kind: 'objective', module: 'RD' })}
        />
      ) : null}

      {screen.kind === 'module-complete' && screen.module === 'RD' ? (
        <TransitionCard
          completed
          heading="Reading complete"
          text="Next: Listening. Please use headphones if available."
          ctaLabel="Check Audio"
          onContinue={() => go({ kind: 'audio-check' })}
        />
      ) : null}

      {screen.kind === 'audio-check' ? (
        <AudioCheckCard
          onStart={() => {
            // This tap is the user gesture that unlocks audio playback for the
            // whole Listening part (see primePlacementAudio).
            primePlacementAudio();
            go({ kind: 'objective', module: 'LSN' });
          }}
        />
      ) : null}

      {screen.kind === 'foundation-complete' ? (
        <TransitionCard
          completed
          heading="Foundation section complete"
          text="Next: Speaking. Check microphone before continuing."
          ctaLabel="Check Microphone"
          onContinue={() => go({ kind: 'mic-check' })}
        >
          {receptiveReport ? (
            <ResultReportCard title="Your foundation profile" report={receptiveReport} embedded />
          ) : null}
        </TransitionCard>
      ) : null}

      {screen.kind === 'mic-check' ? <MicCheckCard onStart={() => go({ kind: 'speaking' })} /> : null}

      {screen.kind === 'speaking' && sessionId ? (
        <SpeakingStage sessionId={sessionId} onComplete={() => go({ kind: 'speaking-complete' })} />
      ) : null}

      {screen.kind === 'speaking-complete' ? (
        <TransitionCard
          completed
          heading="Speaking complete"
          text="Next: Writing — 3 tasks. Desktop/laptop recommended for longer upper-level responses."
          ctaLabel="Continue to Writing"
          onContinue={() => go({ kind: 'writing' })}
        />
      ) : null}

      {screen.kind === 'writing' && sessionId ? (
        <WritingStage sessionId={sessionId} onComplete={() => go({ kind: 'assessment-complete' })} />
      ) : null}

      {screen.kind === 'assessment-complete' ? (
        <TransitionCard
          completed
          heading="Assessment complete"
          text="We are preparing your indicative English profile."
          ctaLabel="View Results"
          busy={busy}
          error={errorMessage}
          onContinue={viewResults}
        />
      ) : null}

      {screen.kind === 'results' && fullReport ? (
        <div className="space-y-4">
          <ResultReportCard title="Your placement profile" report={fullReport} />
          <div className="flex flex-col gap-3 sm:flex-row sm:justify-center">
            <Link href="/placement-test/history" className={SECONDARY_BUTTON}>
              View result history
            </Link>
            <button type="button" onClick={startNewAttempt} className={SECONDARY_BUTTON}>
              <RotateCcw className="h-4 w-4" aria-hidden /> Start a new attempt
            </button>
          </div>
        </div>
      ) : null}
    </div>
  );
}

// ── Chrome ───────────────────────────────────────────────────────────

function ProgressHeader({ part, completed, fraction }: { part: number; completed: number; fraction: number }) {
  const info = PARTS[part];
  const percent = Math.round(((Math.min(completed, PARTS.length) + fraction) / PARTS.length) * 100);
  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
        <p className="text-sm font-semibold text-navy">
          Part {part + 1} of {PARTS.length} — {info.name}
        </p>
        <p className="text-xs text-muted">
          {completed} of {PARTS.length} parts complete
        </p>
      </div>
      <div
        className="h-1.5 w-full overflow-hidden rounded-full bg-border"
        role="progressbar"
        aria-label="Placement test progress"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={percent}
      >
        <div className="h-full rounded-full bg-primary transition-[width] duration-500" style={{ width: `${percent}%` }} />
      </div>
      <ol className="hidden flex-wrap gap-x-4 gap-y-1 text-xs text-muted sm:flex">
        {PARTS.map((item, index) => (
          <li key={item.key} className={`flex items-center gap-1 ${index === part ? 'font-semibold text-navy' : ''}`}>
            {index < completed ? (
              <CheckCircle2 className="h-3.5 w-3.5 text-success" aria-hidden />
            ) : (
              <span className="inline-block h-3.5 w-3.5 rounded-full border border-border" aria-hidden />
            )}
            {item.name}
            {index < completed ? <span className="sr-only"> (complete)</span> : null}
          </li>
        ))}
      </ol>
    </div>
  );
}

function TransitionCard({
  heading,
  text,
  ctaLabel,
  onContinue,
  completed = false,
  busy = false,
  error = null,
  children,
}: {
  heading: string;
  text: string;
  ctaLabel: string;
  onContinue: () => void;
  completed?: boolean;
  busy?: boolean;
  error?: string | null;
  children?: ReactNode;
}) {
  return (
    <section className="space-y-5 rounded-2xl border border-border bg-surface p-5 sm:p-6">
      <div className="space-y-2">
        {completed ? <CheckCircle2 className="h-8 w-8 text-success" aria-hidden /> : null}
        <h2 className="text-xl font-semibold text-navy">{heading}</h2>
        <p className="text-sm leading-relaxed text-muted">{text}</p>
      </div>
      {children}
      {error ? (
        <p role="alert" className="text-sm text-danger">
          {error}
        </p>
      ) : null}
      <button type="button" onClick={onContinue} disabled={busy} className={PRIMARY_BUTTON}>
        {busy ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : null}
        {ctaLabel}
      </button>
    </section>
  );
}

// ── Overview ─────────────────────────────────────────────────────────

function OverviewCard({
  errorMessage,
  history,
  busy,
  onStart,
}: {
  errorMessage: string | null;
  history: PlacementHistoryItem[];
  busy: boolean;
  onStart: () => void;
}) {
  return (
    <section className="space-y-5 rounded-2xl border border-border bg-surface p-5 sm:p-6">
      <div className="space-y-2">
        <h2 className="text-xl font-semibold text-navy">Free General English Placement Test</h2>
        <p className="text-sm leading-relaxed text-muted">
          An indicative CEFR placement estimate from Pre-A1 to C2 across Reading, Listening, Speaking and Writing,
          with a grammar and vocabulary diagnostic. Suitable for OET, IELTS, PTE, TOEFL and General English learners.
        </p>
      </div>

      <ol className="divide-y divide-border overflow-hidden rounded-xl border border-border">
        {PARTS.map((part, index) => (
          <li key={part.key} className="flex flex-col gap-1 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
            <div>
              <p className="text-sm font-semibold text-navy">
                Part {index + 1} of {PARTS.length} — {part.name}
              </p>
              <p className="text-xs text-muted">{part.measures}</p>
            </div>
            <p className="text-xs text-muted sm:text-right">
              {part.count} · {part.time}
            </p>
          </li>
        ))}
      </ol>

      <p className="text-sm leading-relaxed text-navy">
        Approximate full-profile time: 60–85 minutes. The test is adaptive, so the exact number of objective questions can
        vary. You may take a short break between parts, but not during an active timed question, recording or audio item.
      </p>

      <ul className="grid gap-2 text-sm text-muted sm:grid-cols-2">
        <li className="flex items-start gap-2">
          <Headphones className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden /> Headphones or speakers for Listening
        </li>
        <li className="flex items-start gap-2">
          <Mic className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden /> A working microphone for Speaking
        </li>
        <li className="flex items-start gap-2">
          <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden /> Listening and Reading are scored
          instantly
        </li>
        <li className="flex items-start gap-2">
          <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden /> Writing and Speaking are reviewed by
          Dr Hesham&apos;s team
        </li>
      </ul>

      <p className="text-xs text-muted">
        Your answers save as you go, so you can continue after an interruption. Results are saved to your profile.
      </p>

      {errorMessage ? (
        <p role="alert" className="text-sm text-danger">
          {errorMessage}
        </p>
      ) : null}

      <button type="button" onClick={onStart} disabled={busy} className={PRIMARY_BUTTON}>
        {busy ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : <Play className="h-4 w-4" aria-hidden />}
        Start Free Placement Test
      </button>

      {history.length > 0 ? (
        <p className="text-xs text-muted">
          You have{' '}
          <Link href="/placement-test/history" className="text-primary underline">
            {history.length} previous result{history.length === 1 ? '' : 's'}
          </Link>{' '}
          saved on your profile ({history[0].status === 'completed' ? 'latest complete' : 'latest partial'}). Starting a
          new test creates a fresh attempt.
        </p>
      ) : null}
    </section>
  );
}

// ── Audio / microphone checks ────────────────────────────────────────

// One element reused for every Listening unit. iOS Safari unlocks autoplay
// per media element, so priming this element inside the Start Listening tap
// lets every later unit auto-play without another gesture.
let sharedListeningAudio: HTMLAudioElement | null = null;
const SILENT_WAV = 'data:audio/wav;base64,UklGRiQAAABXQVZFZm10IBAAAAABAAEAQB8AAEAfAAABAAgAZGF0YQAAAAA=';

function getListeningAudio(): HTMLAudioElement {
  sharedListeningAudio ??= new Audio();
  return sharedListeningAudio;
}

function primePlacementAudio() {
  if (typeof Audio === 'undefined') return;
  const element = getListeningAudio();
  element.src = SILENT_WAV;
  element.play().catch(() => undefined);
}

/** Speaks a short sound-check sentence (Web Speech), falling back to a tone. */
function playSoundCheck(): boolean {
  if (typeof window === 'undefined') return false;
  if ('speechSynthesis' in window) {
    const utterance = new SpeechSynthesisUtterance(
      'This is your sound check. If you can hear this clearly, you are ready to begin the listening part.',
    );
    utterance.lang = 'en-GB';
    window.speechSynthesis.cancel();
    window.speechSynthesis.speak(utterance);
    return true;
  }
  const Ctx = window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
  if (!Ctx) return false;
  const context = new Ctx();
  const oscillator = context.createOscillator();
  const gain = context.createGain();
  oscillator.frequency.value = 660;
  gain.gain.value = 0.15;
  oscillator.connect(gain).connect(context.destination);
  oscillator.start();
  oscillator.stop(context.currentTime + 1);
  oscillator.onended = () => void context.close();
  return true;
}

function AudioCheckCard({ onStart }: { onStart: () => void }) {
  const [played, setPlayed] = useState<boolean | null>(null);
  return (
    <section className="space-y-5 rounded-2xl border border-border bg-surface p-5 sm:p-6">
      <div className="space-y-2">
        <h2 className="text-xl font-semibold text-navy">Part 3 of 5 — Listening</h2>
        <p className="text-sm leading-relaxed text-muted">
          The audio will start automatically after you begin. Questions are visible while you listen.
        </p>
      </div>
      <div className="space-y-3 rounded-xl border border-border bg-background-light p-4">
        <p className="flex items-center gap-2 text-sm font-semibold text-navy">
          <Headphones className="h-4 w-4 text-primary" aria-hidden /> Audio check
        </p>
        <p className="text-sm text-muted">
          Put on headphones if you have them, then play the sample and adjust your volume until it is comfortable.
        </p>
        <button type="button" onClick={() => setPlayed(playSoundCheck())} className={SECONDARY_BUTTON}>
          <Volume2 className="h-4 w-4" aria-hidden /> Play sample
        </button>
        {played === false ? (
          <p className="text-sm text-warning">This browser could not play the sample. You can still continue.</p>
        ) : null}
        <p className="text-xs text-muted">
          Each recording can be played twice. A replay never lowers your result.
        </p>
      </div>
      <button type="button" onClick={onStart} className={PRIMARY_BUTTON}>
        <Play className="h-4 w-4" aria-hidden /> Start Listening
      </button>
    </section>
  );
}

function MicCheckCard({ onStart }: { onStart: () => void }) {
  const [state, setState] = useState<'idle' | 'checking' | 'ok' | 'silent' | 'denied'>('idle');
  const [level, setLevel] = useState(0);

  const check = async () => {
    setState('checking');
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      const context = new AudioContext();
      const analyser = context.createAnalyser();
      analyser.fftSize = 512;
      context.createMediaStreamSource(stream).connect(analyser);
      const samples = new Uint8Array(analyser.fftSize);
      const startedAt = performance.now();
      let peak = 0;
      const tick = () => {
        analyser.getByteTimeDomainData(samples);
        let max = 0;
        for (const value of samples) max = Math.max(max, Math.abs(value - 128));
        const current = max / 128;
        peak = Math.max(peak, current);
        setLevel(current);
        if (performance.now() - startedAt < 4000) {
          requestAnimationFrame(tick);
          return;
        }
        stream.getTracks().forEach((track) => track.stop());
        void context.close();
        setLevel(0);
        setState(peak > 0.05 ? 'ok' : 'silent');
      };
      requestAnimationFrame(tick);
    } catch {
      setState('denied');
    }
  };

  return (
    <section className="space-y-5 rounded-2xl border border-border bg-surface p-5 sm:p-6">
      <div className="space-y-2">
        <h2 className="text-xl font-semibold text-navy">Part 4 of 5 — Speaking</h2>
        <p className="text-sm leading-relaxed text-muted">
          8 responses. You will see planning/response time for each task.
        </p>
      </div>
      <div className="space-y-3 rounded-xl border border-border bg-background-light p-4">
        <p className="flex items-center gap-2 text-sm font-semibold text-navy">
          <Mic className="h-4 w-4 text-primary" aria-hidden /> Microphone check
        </p>
        <p className="text-sm text-muted">Tap the button, allow microphone access, then say a sentence out loud.</p>
        <button type="button" onClick={check} disabled={state === 'checking'} className={SECONDARY_BUTTON}>
          {state === 'checking' ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : <Mic className="h-4 w-4" aria-hidden />}
          {state === 'checking' ? 'Listening…' : 'Check microphone'}
        </button>
        {state === 'checking' ? (
          <div className="h-2 w-full overflow-hidden rounded-full bg-border" aria-hidden>
            <div className="h-full rounded-full bg-success transition-[width]" style={{ width: `${Math.min(100, level * 250)}%` }} />
          </div>
        ) : null}
        <p className="text-sm" role="status">
          {state === 'ok' ? <span className="text-success">Your microphone is working.</span> : null}
          {state === 'silent' ? (
            <span className="text-warning">
              We could not hear you. Check your microphone and try again — you can still continue.
            </span>
          ) : null}
          {state === 'denied' ? (
            <span className="text-danger">
              Microphone access was blocked. Allow it in your browser settings to record your responses.
            </span>
          ) : null}
        </p>
      </div>
      <button type="button" onClick={onStart} disabled={state === 'checking'} className={PRIMARY_BUTTON}>
        <Play className="h-4 w-4" aria-hidden /> Start Speaking
      </button>
    </section>
  );
}

// ── Objective modules (LS / RD / LSN) ────────────────────────────────

function unitKeyOf(unit: PlacementDeliveryUnit): string {
  return unit.items.map((item) => item.item_id).join('|');
}

/** Seconds until a server-issued deadline; null when there is none or it
 *  does not parse (never treated as zero — that would auto-submit). */
function useSecondsLeft(deadlineAt: string | null): number | null {
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

function UnitTimer({ secondsLeft, waitingForAudio }: { secondsLeft: number | null; waitingForAudio: boolean }) {
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

function ObjectiveStage({
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

  /** Start the server clock for the unit on screen (idempotent engine-side). */
  const startClock = useCallback(
    async (target: PlacementDeliveryUnit) => {
      const key = unitKeyOf(target);
      shownAtRef.current = Date.now();
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

  const handleSubmit = useCallback(
    async (isTimeout: boolean) => {
      if (!unit || submitting) return;
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
    [answered, present, selected, sessionId, submitting, unit],
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
          disabled={submitting}
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

      <button type="button" onClick={() => void handleSubmit(false)} disabled={submitting} className={PRIMARY_BUTTON}>
        {submitting ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : <CheckCircle2 className="h-4 w-4" aria-hidden />}
        {isSingle ? 'Next' : 'Submit answers'}
      </button>
    </div>
  );
}

function QuestionCard({
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

function UnitAudio({
  audioUrl,
  maxPlays,
  onPlaybackStart,
  onPlay,
  onFailure,
}: {
  audioUrl: string;
  maxPlays: number;
  onPlaybackStart: () => void;
  onPlay: (plays: number) => void;
  onFailure: (reason: PlacementTechnicalReason) => void;
}) {
  const apiPath = useMemo(() => resolvePlacementAudioUrl(audioUrl), [audioUrl]);
  const [objectUrl, setObjectUrl] = useState<string | null>(null);
  const [phase, setPhase] = useState<'loading' | 'blocked' | 'playing' | 'ended'>('loading');
  const [plays, setPlays] = useState(0);
  const [progress, setProgress] = useState(0);
  const playsRef = useRef(0);
  const startedRef = useRef(false);
  const failedRef = useRef(false);
  const readyTimeoutRef = useRef<number | null>(null);
  // Parent callbacks change identity every render; keep the latest in refs so
  // the media listeners below are attached once per clip.
  const callbacksRef = useRef({ onPlaybackStart, onPlay, onFailure });
  useEffect(() => {
    callbacksRef.current = { onPlaybackStart, onPlay, onFailure };
  });

  const fail = useCallback((reason: PlacementTechnicalReason) => {
    if (failedRef.current) return;
    failedRef.current = true;
    if (readyTimeoutRef.current !== null) window.clearTimeout(readyTimeoutRef.current);
    callbacksRef.current.onFailure(reason);
  }, []);

  const tryPlay = useCallback(() => {
    const element = getListeningAudio();
    element.play().catch((err: unknown) => {
      if (err instanceof DOMException && err.name === 'NotAllowedError') setPhase('blocked');
      else if (!(err instanceof DOMException && err.name === 'AbortError')) fail('audio_decode_error');
    });
  }, [fail]);

  // Fetch the clip as an authorised blob: the endpoint is Bearer-gated and
  // lives behind the API base, which a bare <audio src> cannot satisfy.
  useEffect(() => {
    if (!apiPath) {
      const handle = window.setTimeout(() => fail('audio_unavailable'), 0);
      return () => window.clearTimeout(handle);
    }
    let cancelled = false;
    let created: string | null = null;
    readyTimeoutRef.current = window.setTimeout(() => fail('media_timeout'), 30_000);
    fetchAuthorizedObjectUrl(apiPath)
      .then((url) => {
        if (cancelled) {
          URL.revokeObjectURL(url);
          return;
        }
        created = url;
        setObjectUrl(url);
      })
      .catch(() => {
        if (!cancelled) fail('audio_unavailable');
      });
    return () => {
      cancelled = true;
      if (readyTimeoutRef.current !== null) window.clearTimeout(readyTimeoutRef.current);
      if (created) URL.revokeObjectURL(created);
    };
  }, [apiPath, fail]);

  // Drive the shared (pre-unlocked) element for this clip.
  useEffect(() => {
    if (!objectUrl) return;
    const element = getListeningAudio();
    const onLoaded = () => {
      if (readyTimeoutRef.current !== null) window.clearTimeout(readyTimeoutRef.current);
      if (!Number.isFinite(element.duration) || element.duration <= 0) {
        fail('audio_zero_duration');
        return;
      }
      tryPlay();
    };
    const onPlaying = () => {
      setPhase('playing');
      if (!startedRef.current) {
        startedRef.current = true;
        callbacksRef.current.onPlaybackStart();
      }
    };
    const onPlayEvent = () => {
      playsRef.current += 1;
      setPlays(playsRef.current);
      callbacksRef.current.onPlay(playsRef.current);
    };
    const onTime = () => {
      if (element.duration > 0) setProgress(Math.min(1, element.currentTime / element.duration));
    };
    const onEnded = () => {
      setProgress(1);
      setPhase('ended');
    };
    const onError = () => fail('audio_decode_error');

    element.addEventListener('loadedmetadata', onLoaded);
    element.addEventListener('playing', onPlaying);
    element.addEventListener('play', onPlayEvent);
    element.addEventListener('timeupdate', onTime);
    element.addEventListener('ended', onEnded);
    element.addEventListener('error', onError);
    element.preload = 'auto';
    element.src = objectUrl;
    element.load();

    return () => {
      element.removeEventListener('loadedmetadata', onLoaded);
      element.removeEventListener('playing', onPlaying);
      element.removeEventListener('play', onPlayEvent);
      element.removeEventListener('timeupdate', onTime);
      element.removeEventListener('ended', onEnded);
      element.removeEventListener('error', onError);
      element.pause();
    };
  }, [fail, objectUrl, tryPlay]);

  const replaysLeft = Math.max(0, maxPlays - plays);

  return (
    <section className="space-y-3 rounded-2xl border border-border bg-surface p-5 sm:p-6" aria-label="Listening audio">
      <div className="flex items-center gap-2 text-sm font-semibold text-navy">
        <Headphones className="h-4 w-4 text-primary" aria-hidden />
        <span role="status">
          {phase === 'loading' ? 'Loading audio…' : null}
          {phase === 'blocked' ? 'Tap to start the audio' : null}
          {phase === 'playing' ? 'Playing — listen carefully' : null}
          {phase === 'ended' ? 'Audio finished' : null}
        </span>
      </div>

      <div
        className="h-2 w-full overflow-hidden rounded-full bg-border"
        role="progressbar"
        aria-label="Audio progress"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={Math.round(progress * 100)}
      >
        <div className="h-full rounded-full bg-primary transition-[width]" style={{ width: `${Math.round(progress * 100)}%` }} />
      </div>

      {phase === 'loading' ? <Loader2 className="h-5 w-5 animate-spin text-muted" aria-hidden /> : null}

      {phase === 'blocked' ? (
        <button type="button" onClick={tryPlay} className={`${PRIMARY_BUTTON} min-h-12 sm:w-full`}>
          <Play className="h-5 w-5" aria-hidden /> Tap to start audio
        </button>
      ) : null}

      {phase === 'ended' && replaysLeft > 0 ? (
        <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
          <button
            type="button"
            onClick={() => {
              const element = getListeningAudio();
              element.currentTime = 0;
              setProgress(0);
              tryPlay();
            }}
            className={SECONDARY_BUTTON}
          >
            <RotateCcw className="h-4 w-4" aria-hidden /> Play again ({replaysLeft} left)
          </button>
          <p className="text-xs text-muted">A replay never lowers your result.</p>
        </div>
      ) : null}
    </section>
  );
}

// ── Speaking ─────────────────────────────────────────────────────────

type SpeakPhase = 'ready' | 'prep' | 'recording' | 'recorded' | 'uploading' | 'upload-failed';

function pickRecordingMimeType(): string {
  if (typeof MediaRecorder === 'undefined') return '';
  if (MediaRecorder.isTypeSupported('audio/webm;codecs=opus')) return 'audio/webm;codecs=opus';
  if (MediaRecorder.isTypeSupported('audio/mp4')) return 'audio/mp4';
  return '';
}

function SpeakingStage({ sessionId, onComplete }: { sessionId: string; onComplete: () => void }) {
  const [tasks, setTasks] = useState<PlacementSpeakingTask[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [index, setIndex] = useState(0);
  const [phase, setPhase] = useState<SpeakPhase>('ready');
  const [endsAt, setEndsAt] = useState<number | null>(null);
  const [remaining, setRemaining] = useState<number | null>(null);
  const [playbackUrl, setPlaybackUrl] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const recorderRef = useRef<MediaRecorder | null>(null);
  const chunksRef = useRef<Blob[]>([]);
  // The recording stays on this device until the server confirms receipt; a
  // failed upload never discards it.
  const blobRef = useRef<Blob | null>(null);
  const uploadedPathRef = useRef<string | null>(null);
  const playbackUrlRef = useRef<string | null>(null);
  const elapsedActionRef = useRef<'record' | 'stop' | null>(null);
  const responseSecondsRef = useRef(60);

  useEffect(() => {
    let cancelled = false;
    fetchPlacementSpeakingTasks(sessionId)
      .then((loaded) => {
        if (!cancelled) setTasks(loaded);
      })
      .catch((err) => {
        if (!cancelled) setLoadError(readErrorMessage(err, 'Could not load the speaking tasks.'));
      });
    return () => {
      cancelled = true;
    };
  }, [sessionId]);

  useEffect(
    () => () => {
      if (playbackUrlRef.current) URL.revokeObjectURL(playbackUrlRef.current);
      recorderRef.current?.stream.getTracks().forEach((track) => track.stop());
    },
    [],
  );

  const replacePlayback = useCallback((url: string | null) => {
    if (playbackUrlRef.current) URL.revokeObjectURL(playbackUrlRef.current);
    playbackUrlRef.current = url;
    setPlaybackUrl(url);
  }, []);

  const stopRecording = useCallback(() => {
    elapsedActionRef.current = null;
    setEndsAt(null);
    const recorder = recorderRef.current;
    if (recorder && recorder.state !== 'inactive') recorder.stop();
  }, []);

  const beginRecording = useCallback(
    async (seconds: number) => {
      elapsedActionRef.current = null;
      setError(null);
      try {
        const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        const mimeType = pickRecordingMimeType();
        const recorder = new MediaRecorder(stream, mimeType ? { mimeType } : undefined);
        chunksRef.current = [];
        recorder.ondataavailable = (event) => {
          if (event.data.size > 0) chunksRef.current.push(event.data);
        };
        recorder.onstop = () => {
          stream.getTracks().forEach((track) => track.stop());
          recorderRef.current = null;
          const blob = new Blob(chunksRef.current, { type: recorder.mimeType || 'audio/webm' });
          if (blob.size === 0) {
            setPhase('ready');
            setError('Nothing was recorded — check your microphone and record again.');
            return;
          }
          blobRef.current = blob;
          uploadedPathRef.current = null;
          replacePlayback(URL.createObjectURL(blob));
          setPhase('recorded');
        };
        recorder.start(1000);
        recorderRef.current = recorder;
        setPhase('recording');
        setRemaining(seconds);
        elapsedActionRef.current = 'stop';
        setEndsAt(Date.now() + seconds * 1000);
      } catch {
        setPhase('ready');
        setError('Microphone access is required to record your response. Allow it in your browser settings, then try again.');
      }
    },
    [replacePlayback],
  );

  // Planning and response windows: planning hands over to recording, and the
  // response window stops the recorder when it runs out.
  useEffect(() => {
    if (endsAt === null) return;
    const tick = () => {
      const left = Math.max(0, Math.ceil((endsAt - Date.now()) / 1000));
      setRemaining(left);
      if (left > 0) return;
      const action = elapsedActionRef.current;
      elapsedActionRef.current = null;
      if (action === 'record') void beginRecording(responseSecondsRef.current);
      else if (action === 'stop') stopRecording();
    };
    const interval = window.setInterval(tick, 250);
    return () => window.clearInterval(interval);
  }, [beginRecording, endsAt, stopRecording]);

  if (!tasks) {
    return (
      <div className="flex items-center gap-2 text-sm text-muted" role="status">
        {loadError ? <span className="text-danger">{loadError}</span> : <><Loader2 className="h-4 w-4 animate-spin" aria-hidden /> Preparing speaking tasks…</>}
      </div>
    );
  }

  const task = tasks[index];
  if (!task) {
    return (
      <TransitionCard
        heading="No speaking tasks left"
        text="There are no speaking responses left in this attempt."
        ctaLabel="Continue"
        onContinue={onComplete}
      />
    );
  }
  const prepSeconds = task.prepSeconds ?? 0;
  const responseSeconds = task.speakingSeconds ?? 60;

  const startTask = () => {
    setError(null);
    responseSecondsRef.current = responseSeconds;
    if (prepSeconds > 0) {
      setPhase('prep');
      setRemaining(prepSeconds);
      elapsedActionRef.current = 'record';
      setEndsAt(Date.now() + prepSeconds * 1000);
    } else {
      void beginRecording(responseSeconds);
    }
  };

  const recordNow = () => {
    setEndsAt(null);
    void beginRecording(responseSeconds);
  };

  const submit = async () => {
    const blob = blobRef.current;
    if (!blob) return;
    setPhase('uploading');
    setError(null);
    try {
      if (!uploadedPathRef.current) {
        const extension = blob.type.includes('mp4') ? 'm4a' : 'webm';
        const uploaded = await uploadPlacementRecording(blob, `speaking-${task.taskId}.${extension}`);
        uploadedPathRef.current = uploaded.storagePath;
      }
      await submitPlacementSpeaking(sessionId, task.taskId, uploadedPathRef.current);
      // Saved server-side — only now is the local copy released.
      blobRef.current = null;
      uploadedPathRef.current = null;
      replacePlayback(null);
      if (index + 1 < tasks.length) {
        setIndex((current) => current + 1);
        setPhase('ready');
        setRemaining(null);
      } else {
        onComplete();
      }
    } catch {
      setPhase('upload-failed');
    }
  };

  return (
    <div className="space-y-4">
      <p className="text-sm text-muted">
        Response {index + 1} of {tasks.length}
      </p>

      <section className="space-y-2 rounded-2xl border border-border bg-surface p-5 sm:p-6" aria-label="Speaking task">
        <p className="text-xs font-semibold uppercase tracking-wide text-muted">Task</p>
        <p className="whitespace-pre-line break-words text-base leading-relaxed text-navy">{task.prompt}</p>
        <p className="text-xs text-muted">
          {prepSeconds > 0 ? `Planning time: ${prepSeconds} s · ` : ''}Response time: up to {responseSeconds} s
        </p>
      </section>

      <section className="space-y-3 rounded-2xl border border-border bg-surface p-5 sm:p-6" aria-label="Recording">
        {phase === 'ready' ? (
          <button type="button" onClick={startTask} className={PRIMARY_BUTTON}>
            <Play className="h-4 w-4" aria-hidden /> {prepSeconds > 0 ? 'Start planning time' : 'Start recording'}
          </button>
        ) : null}

        {phase === 'prep' ? (
          <div className="space-y-3">
            <p className="text-sm text-navy" role="status">
              Planning time — recording starts automatically in{' '}
              <span className="font-mono tabular-nums">{remaining ?? prepSeconds}</span> s.
            </p>
            <button type="button" onClick={recordNow} className={SECONDARY_BUTTON}>
              <Mic className="h-4 w-4" aria-hidden /> Start recording now
            </button>
          </div>
        ) : null}

        {phase === 'recording' ? (
          <div className="space-y-3">
            <p className="flex items-center gap-2 text-sm font-semibold text-danger" role="status">
              <span className="inline-block h-2.5 w-2.5 animate-pulse rounded-full bg-danger" aria-hidden />
              Recording — <span className="font-mono tabular-nums">{remaining ?? responseSeconds}</span> s left
            </p>
            <button
              type="button"
              onClick={stopRecording}
              className="inline-flex min-h-11 w-full items-center justify-center gap-2 rounded-xl bg-danger px-5 py-2.5 text-sm font-semibold text-white hover:opacity-90 sm:w-auto"
            >
              <Square className="h-4 w-4" aria-hidden /> Stop recording
            </button>
          </div>
        ) : null}

        {phase === 'recorded' || phase === 'uploading' || phase === 'upload-failed' ? (
          <div className="space-y-3">
            {playbackUrl ? (
              // eslint-disable-next-line jsx-a11y/media-has-caption -- candidate playback of own recording
              <audio src={playbackUrl} controls className="w-full" />
            ) : null}
            {phase === 'upload-failed' ? (
              <p role="alert" className="rounded-xl border border-warning/40 bg-warning/10 px-4 py-3 text-sm text-navy">
                We could not upload your recording. Your recording is still saved on this device. Retry upload.
              </p>
            ) : null}
            <div className="flex flex-col gap-3 sm:flex-row">
              <button type="button" onClick={submit} disabled={phase === 'uploading'} className={PRIMARY_BUTTON}>
                {phase === 'uploading' ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : <CheckCircle2 className="h-4 w-4" aria-hidden />}
                {phase === 'upload-failed' ? 'Retry upload' : phase === 'uploading' ? 'Saving your response…' : 'Submit response'}
              </button>
              <button type="button" onClick={recordNow} disabled={phase === 'uploading'} className={SECONDARY_BUTTON}>
                <Mic className="h-4 w-4" aria-hidden /> Record again
              </button>
            </div>
          </div>
        ) : null}

        {error ? (
          <p role="alert" className="text-sm text-danger">
            {error}
          </p>
        ) : null}
      </section>
    </div>
  );
}

// ── Writing ──────────────────────────────────────────────────────────

function WritingStage({ sessionId, onComplete }: { sessionId: string; onComplete: () => void }) {
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
        {loadError ? <span className="text-danger">{loadError}</span> : <><Loader2 className="h-4 w-4 animate-spin" aria-hidden /> Preparing writing tasks…</>}
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

function draftStorageKey(sessionId: string, taskId: string): string {
  return `oet_placement_draft:${sessionId}:${taskId}`;
}

function readLocalDraft(key: string): string {
  try {
    return window.localStorage.getItem(key) ?? '';
  } catch {
    return '';
  }
}

function writeLocalDraft(key: string, text: string | null) {
  try {
    if (text) window.localStorage.setItem(key, text);
    else window.localStorage.removeItem(key);
  } catch {
    // Storage blocked: the server-side autosave still runs.
  }
}

function WritingTaskEditor({
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

  const submit = async () => {
    if (isSubmitting) return;
    setIsSubmitting(true);
    setError(null);
    try {
      await submitPlacementWriting(sessionId, task.taskId, text);
      writeLocalDraft(draftKey, null);
      onSubmitted();
    } catch (err) {
      setError(readErrorMessage(err, 'Could not submit your response — your text is still here. Please try again.'));
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="space-y-4">
      <p className="text-sm text-muted">
        Task {position} of {total}
      </p>

      <section className="space-y-2 rounded-2xl border border-border bg-surface p-5 sm:p-6" aria-label="Writing task">
        <p className="text-xs font-semibold uppercase tracking-wide text-muted">Task</p>
        <p className="whitespace-pre-line break-words text-base leading-relaxed text-navy">{task.prompt}</p>
        <p className="text-xs text-muted">
          {task.minutes ? `Suggested time: about ${task.minutes} minutes` : ''}
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
          <p role="alert" className="text-sm text-danger">
            {error}
          </p>
        ) : null}
        <button type="button" onClick={submit} disabled={isSubmitting || wordCount === 0} className={PRIMARY_BUTTON}>
          {isSubmitting ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : <CheckCircle2 className="h-4 w-4" aria-hidden />}
          Submit response
        </button>
      </section>
    </div>
  );
}
