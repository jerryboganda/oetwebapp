'use client';

/**
 * Phase 3 - tutor sidebar with interlocutor script + cue dispatch
 * (plan C.3 / D).
 *
 * Fetched from `/v1/admin/speaking/role-play-cards/{cardId}/interlocutor-script`
 * since interlocutor data is admin/tutor-only. If the request returns 403,
 * we render an authorisation hint instead of leaking any tutor copy.
 *
 * Cue dispatch broadcasts a hub method `BroadcastCue` over the
 * `SpeakingLiveRoomHub` (`/v1/conversations/hub` - TODO: confirm path
 * with P3 hub agent). When the hub client isn't reachable we fall
 * back to local optimistic state and log a warning so the tutor still
 * gets visual feedback.
 *
 * TODO(P3-hub): once `SpeakingLiveRoomHub` is wired, swap the lazy
 * import below for a typed shared bridge in `lib/conversation-speaking-bridge.ts`.
 */
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  AlertTriangle,
  ChevronDown,
  Clock,
  EyeOff,
  Loader2,
  MessageSquareQuote,
  Volume2,
  VolumeX,
} from 'lucide-react';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import {
  adminGetInterlocutorScript,
  RESISTANCE_LEVEL_OPTIONS,
  RolePlayCardApiError,
  type InterlocutorScriptDetail,
  type ResistanceLevelCode,
} from '@/lib/api/speaking-role-play-cards';

const DEFAULT_ROLE_PLAY_SECONDS = 5 * 60;

export interface TutorCuePanelProps {
  /** Role-play card identifier - pulled from the parent session. */
  cardId: string;
  /** Optional fixed timer length. Defaults to 5 minutes (300 s). */
  rolePlayDurationSeconds?: number;
  /** Fired when the 5-minute timer hits 00:00. */
  onTimerComplete?: () => void;
  /** Optional class for the outer panel. */
  className?: string;
}

interface CueBroadcaster {
  invoke: (cardId: string, cueLabel: string, cueIndex: number) => Promise<void>;
}

async function loadCueBroadcaster(): Promise<CueBroadcaster | null> {
  try {
    const { HubConnectionBuilder, LogLevel } = await import('@microsoft/signalr');
    const { ensureFreshAccessToken } = await import('@/lib/auth-client');
    const connection = new HubConnectionBuilder()
      .withUrl('/api/backend/v1/conversations/hub', {
        accessTokenFactory: async () => (await ensureFreshAccessToken()) ?? '',
      })
      .configureLogging(LogLevel.None)
      .build();
    await connection.start();
    return {
      invoke: async (cardId, label, index) => {
        try {
          await connection.invoke('BroadcastCue', cardId, label, index);
        } catch (err) {
          // Don't kill the panel for transient hub issues - the tutor
          // still sees the local "delivered" state.

          console.warn('[TutorCuePanel] BroadcastCue failed:', err);
        }
      },
    };
  } catch {
    // SignalR or hub method not available - degrade gracefully.
    return null;
  }
}

function formatMmSs(secondsLeft: number): string {
  const safe = Math.max(0, secondsLeft);
  const m = Math.floor(safe / 60);
  const s = safe % 60;
  return `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
}

function describeResistance(level: ResistanceLevelCode | string | undefined): {
  label: string;
  className: string;
  description: string;
} {
  const lookup = RESISTANCE_LEVEL_OPTIONS.find((o) => o.value === level);
  switch (level) {
    case 'high':
      return {
        label: 'High resistance',
        className: 'bg-rose-100 text-rose-800 border-rose-200',
        description: lookup?.description ?? '',
      };
    case 'medium':
      return {
        label: 'Medium resistance',
        className: 'bg-amber-100 text-amber-800 border-amber-200',
        description: lookup?.description ?? '',
      };
    default:
      return {
        label: 'Low resistance',
        className: 'bg-emerald-100 text-emerald-800 border-emerald-200',
        description: lookup?.description ?? '',
      };
  }
}

export function TutorCuePanel({
  cardId,
  rolePlayDurationSeconds = DEFAULT_ROLE_PLAY_SECONDS,
  onTimerComplete,
  className,
}: TutorCuePanelProps) {
  const [script, setScript] = useState<InterlocutorScriptDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [forbidden, setForbidden] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [reading, setReading] = useState(false);
  const [hiddenOpen, setHiddenOpen] = useState(false);
  const [deliveredCues, setDeliveredCues] = useState<Set<number>>(new Set());
  const [secondsLeft, setSecondsLeft] = useState<number>(rolePlayDurationSeconds);
  const broadcasterRef = useRef<CueBroadcaster | null>(null);
  const timerCompletedRef = useRef(false);
  const onTimerCompleteRef = useRef(onTimerComplete);

  useEffect(() => {
    onTimerCompleteRef.current = onTimerComplete;
  }, [onTimerComplete]);

  // -- Fetch script -----------------------------------------------------------
  useEffect(() => {
    let cancelled = false;
    window.queueMicrotask(() => {
      if (cancelled) return;
      setLoading(true);
      setError(null);
      setForbidden(false);
    });
    adminGetInterlocutorScript(cardId)
      .then((data) => {
        if (cancelled) return;
        setScript(data);
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        if (err instanceof RolePlayCardApiError && err.status === 403) {
          setForbidden(true);
        } else {
          const msg = err instanceof Error ? err.message : 'Could not load tutor script.';
          setError(msg);
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [cardId]);

  // -- Lazy-load cue broadcaster ---------------------------------------------
  useEffect(() => {
    let cancelled = false;
    loadCueBroadcaster().then((b) => {
      if (!cancelled) broadcasterRef.current = b;
    });
    return () => {
      cancelled = true;
    };
  }, []);

  // -- 5-minute timer ---------------------------------------------------------
  useEffect(() => {
    window.queueMicrotask(() => setSecondsLeft(Math.max(0, Math.floor(rolePlayDurationSeconds))));
    timerCompletedRef.current = false;
    const start = Date.now();
    const id = window.setInterval(() => {
      const elapsed = Math.floor((Date.now() - start) / 1000);
      const remaining = Math.max(0, rolePlayDurationSeconds - elapsed);
      setSecondsLeft(remaining);
      if (remaining <= 0 && !timerCompletedRef.current) {
        timerCompletedRef.current = true;
        window.clearInterval(id);
        onTimerCompleteRef.current?.();
      }
    }, 500);
    return () => {
      window.clearInterval(id);
    };
  }, [rolePlayDurationSeconds]);

  const handleDispatchCue = useCallback(
    async (index: number, text: string) => {
      setDeliveredCues((prev) => {
        const next = new Set(prev);
        next.add(index);
        return next;
      });
      try {
        await broadcasterRef.current?.invoke(cardId, text, index);
      } catch {
        /* swallowed in loader */
      }
    },
    [cardId],
  );

  const cues = useMemo(() => {
    if (!script) return [] as Array<{ index: number; text: string }>;
    return [script.prompt1, script.prompt2, script.prompt3]
      .map((text, index) => ({ index, text: (text ?? '').trim() }))
      .filter((c) => c.text.length > 0);
  }, [script]);

  const resistance = describeResistance(script?.resistanceLevel);
  const isTimerWarning = secondsLeft > 0 && secondsLeft <= 60;

  return (
    <div
      className={cn(
        'flex h-full flex-col gap-4 text-sm text-foreground',
        className,
      )}
      data-testid="tutor-cue-panel"
    >
      {/* Timer header */}
      <header className="flex items-center justify-between border-b border-border pb-3">
        <div className="flex items-center gap-2">
          <Clock className={cn('h-4 w-4', isTimerWarning ? 'text-rose-600' : 'text-muted-foreground')} aria-hidden />
          <span className="font-medium text-foreground">Role-play time</span>
        </div>
        <span
          className={cn(
            'rounded-md px-2 py-0.5 font-mono text-base tabular-nums',
            isTimerWarning ? 'bg-rose-50 text-rose-700' : 'bg-muted text-foreground',
          )}
          aria-live="polite"
        >
          {formatMmSs(secondsLeft)}
        </span>
      </header>

      {loading ? (
        <div className="flex items-center gap-2 text-muted-foreground">
          <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> Loading interlocutor script...
        </div>
      ) : forbidden ? (
        <div
          role="alert"
          className="flex items-start gap-2 rounded-md border border-amber-300 bg-amber-50 p-3 text-amber-900"
        >
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
          <p className="text-xs">
            You don&apos;t have permission to view the interlocutor script for this card.
            Ask an admin to grant tutor access if you should be running role-plays.
          </p>
        </div>
      ) : error ? (
        <div
          role="alert"
          className="flex items-start gap-2 rounded-md border border-rose-300 bg-rose-50 p-3 text-rose-700"
        >
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
          <p className="text-xs">{error}</p>
        </div>
      ) : script ? (
        <>
          <ResistancePill resistance={resistance} />

          <section>
            <h4 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
              Opening response
            </h4>
            <p className="mt-1 whitespace-pre-wrap rounded-md bg-muted p-3 leading-relaxed text-foreground">
              {script.openingResponse}
            </p>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={() => setReading((v) => !v)}
              className="mt-2 text-muted-foreground"
              aria-pressed={reading}
            >
              {reading ? (
                <>
                  <VolumeX className="mr-2 h-4 w-4" aria-hidden /> Stop reading aloud
                </>
              ) : (
                <>
                  <Volume2 className="mr-2 h-4 w-4" aria-hidden /> Mark as reading aloud
                </>
              )}
            </Button>
          </section>

          <section>
            <h4 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
              Cue prompts
            </h4>
            <p className="mt-1 text-xs text-muted-foreground">
              Tap a prompt to deliver it. Delivered cues are highlighted; click again to re-broadcast.
            </p>
            <ul className="mt-2 space-y-2">
              {cues.length === 0 ? (
                <li className="text-xs italic text-muted-foreground">No cue prompts recorded for this card.</li>
              ) : null}
              {cues.map((cue) => {
                const delivered = deliveredCues.has(cue.index);
                return (
                  <li key={cue.index}>
                    <button
                      type="button"
                      onClick={() => void handleDispatchCue(cue.index, cue.text)}
                      className={cn(
                        'group flex w-full items-start gap-2 rounded-lg border px-3 py-2 text-left transition-colors',
                        delivered
                          ? 'border-emerald-300 bg-emerald-50 text-emerald-900'
                          : 'border-border bg-surface hover:border-border-hover hover:bg-muted',
                      )}
                      data-testid={`tutor-cue-${cue.index}`}
                    >
                      <MessageSquareQuote
                        className={cn(
                          'mt-0.5 h-4 w-4 shrink-0',
                          delivered ? 'text-emerald-600' : 'text-muted-foreground',
                        )}
                        aria-hidden
                      />
                      <span className="text-sm leading-snug">{cue.text}</span>
                    </button>
                  </li>
                );
              })}
            </ul>
          </section>

          <section>
            <button
              type="button"
              onClick={() => setHiddenOpen((v) => !v)}
              aria-expanded={hiddenOpen}
              className="flex w-full items-center justify-between rounded-md border border-border bg-muted px-3 py-2 text-left text-sm font-medium text-foreground hover:bg-muted/80"
            >
              <span className="inline-flex items-center gap-2">
                <EyeOff className="h-4 w-4 text-muted-foreground" aria-hidden />
                Hidden information (reveal only when probed)
              </span>
              <ChevronDown
                className={cn('h-4 w-4 transition-transform', hiddenOpen && 'rotate-180')}
                aria-hidden
              />
            </button>
            {hiddenOpen ? (
              <p className="mt-2 whitespace-pre-wrap rounded-md border border-border bg-surface p-3 leading-relaxed text-foreground">
                {script.hiddenInformation || (
                  <span className="italic text-muted-foreground">No hidden information for this card.</span>
                )}
              </p>
            ) : null}
          </section>

          <section>
            <h4 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
              Closing cue
            </h4>
            <p className="mt-1 whitespace-pre-wrap rounded-md bg-muted p-3 leading-relaxed text-foreground">
              {script.closingCue || (
                <span className="italic text-muted-foreground">No closing cue recorded.</span>
              )}
            </p>
          </section>

          {script.layLanguageTriggers && script.layLanguageTriggers.length > 0 ? (
            <section>
              <h4 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                Lay-language triggers
              </h4>
              <ul className="mt-1 flex flex-wrap gap-1.5">
                {script.layLanguageTriggers.map((t) => (
                  <li
                    key={t}
                    className="rounded-full border border-border bg-surface px-2 py-0.5 text-xs text-muted-foreground"
                  >
                    {t}
                  </li>
                ))}
              </ul>
            </section>
          ) : null}
        </>
      ) : (
        <p className="text-xs italic text-muted-foreground">
          No interlocutor script published for this card yet.
        </p>
      )}
    </div>
  );
}

function ResistancePill({
  resistance,
}: {
  resistance: { label: string; className: string; description: string };
}) {
  return (
    <div
      className={cn(
        'inline-flex items-start gap-2 rounded-full border px-3 py-1 text-xs font-medium',
        resistance.className,
      )}
      title={resistance.description}
    >
      <span className="h-2 w-2 rounded-full bg-current" aria-hidden />
      {resistance.label}
    </div>
  );
}

export default TutorCuePanel;
