'use client';

import Link from 'next/link';
import { useCallback, useEffect, useRef, useState } from 'react';
import { AlertTriangle, Loader2, RotateCcw } from 'lucide-react';
import { ResultReportCard } from '@/components/placement/result-report-card';
import { PLACEMENT_ACTIVE_SESSION_KEY, createPlacementSession, fetchPlacementFullResult, fetchPlacementHistory, fetchPlacementReceptiveResult, fetchPlacementSessionState, fetchPlacementStatus, type PlacementHistoryItem, type PlacementModule, type PlacementResultReport, type PlacementSessionState } from '@/lib/api/placement';
import { readErrorMessage } from '@/lib/read-error-message';
import { partIndex, SECONDARY_BUTTON } from './placement-shared';
import { ProgressHeader, TransitionCard, OverviewCard, primePlacementAudio, AudioCheckCard, MicCheckCard } from './placement-preflight';
import { ObjectiveStage } from './placement-objective-stage';
import { SpeakingStage } from './placement-speaking-stage';
import { WritingStage } from './placement-writing-stage';

/**
 * The free General-English placement journey, in five parts:
 * Language Systems → Reading → Listening → Speaking → Writing, with an
 * overview up front, a transition screen between every part, and a
 * persistent "Part X of 5" header. Every screen reports engine truth —
 * unmeasured modules show as unmeasured; pending-review submissions show as
 * pending, never as scores. Unit clocks are started server-side only once a
 * unit is actually usable, so buffering is never charged to the candidate.
 */

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
  // Admin-approved extra time, as reported by the server. Display only — the
  // candidate has no control over it.
  const [extraTimePercent, setExtraTimePercent] = useState<number | null>(null);
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
        setExtraTimePercent(status.extraTimePercent ?? null);
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
        <AlertTriangle className="mx-auto h-8 w-8 text-warning-strong" aria-hidden />
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
        <OverviewCard
          errorMessage={errorMessage}
          history={history}
          busy={busy}
          extraTimePercent={extraTimePercent}
          onStart={startSession}
        />
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
