'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { X, Sparkles, Lightbulb, AlertTriangle, CheckCircle2, BookOpen } from 'lucide-react';
import { cn } from '@/lib/utils';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import {
  connectWritingCoachStream,
  coachPollingFallback,
  type Disposable,
} from '@/lib/writing/realtime';
import type {
  WritingCoachHintDto,
  WritingCoachHintCategory,
  WritingLetterType,
  WritingProfession,
} from '@/lib/writing/types';

export interface CoachPanelProps {
  sessionId: string;
  mode: 'on' | 'off';
  /**
   * Called when the learner dismisses a single hint (× button). The
   * parent decides whether to also notify the backend (recommended:
   * yes, so cost analytics + dismissal-rate suppression work).
   */
  onDismissHint?: (hintId: string) => void;
  /**
   * Toggle handler for the global on/off switch in the panel header.
   * If omitted, the toggle is rendered read-only.
   */
  onToggleMode?: (next: 'on' | 'off') => void;
  /**
   * Context required to build the HTTP polling fallback payload.
   * Snapshot of the editor's current state; the panel reads it via
   * a getter so it stays current without re-rendering the panel.
   */
  getFallbackContext?: () => {
    scenarioId: string;
    letterContent: string;
    wordCount: number;
    letterType: WritingLetterType;
    profession: WritingProfession;
  } | null;
  className?: string;
}

const CATEGORY_META: Record<WritingCoachHintCategory, { label: string; tone: string; icon: typeof Sparkles }> = {
  style: {
    label: 'Style',
    tone: 'bg-red-50 text-red-700 border-red-200/60 dark:bg-red-950 dark:text-red-300 dark:border-red-800/60',
    icon: AlertTriangle,
  },
  structure: {
    label: 'Structure',
    tone: 'bg-amber-50 text-amber-800 border-amber-200/60 dark:bg-amber-950 dark:text-amber-300 dark:border-amber-800/60',
    icon: BookOpen,
  },
  length: {
    label: 'Length',
    tone: 'bg-amber-50 text-amber-800 border-amber-200/60 dark:bg-amber-950 dark:text-amber-300 dark:border-amber-800/60',
    icon: Lightbulb,
  },
  encouragement: {
    label: 'Good job',
    tone: 'bg-emerald-50 text-emerald-700 border-emerald-200/60 dark:bg-emerald-950 dark:text-emerald-300 dark:border-emerald-800/60',
    icon: CheckCircle2,
  },
};

/**
 * Right-rail AI Coach panel.
 *
 * Connection strategy (per spec §11.6):
 *   1. Try WebSocket at /api/backend/ws/writing/coach/{sessionId}.
 *   2. After 5 reconnect attempts, fall back to 30s HTTP polling
 *      against POST /v1/writing/coach/hints.
 *
 * Hints are categorized (style=red, structure=yellow, length=yellow,
 * encouragement=green). aria-live="polite" on the list keeps screen
 * readers in sync without being aggressive. Each hint has a ✕
 * dismiss button; a global toggle in the header turns Coach OFF.
 */
export function CoachPanel({
  sessionId,
  mode,
  onDismissHint,
  onToggleMode,
  getFallbackContext,
  className,
}: CoachPanelProps) {
  const [hints, setHints] = useState<WritingCoachHintDto[]>([]);
  const [connectionStatus, setConnectionStatus] = useState<
    'idle' | 'connecting' | 'open' | 'reconnecting' | 'closed' | 'fallback' | 'off'
  >('idle');
  const dismissedRef = useRef<Set<string>>(new Set());

  const handleHint = useCallback((hint: WritingCoachHintDto) => {
    if (dismissedRef.current.has(hint.id)) return;
    setHints((prev) => {
      if (prev.some((existing) => existing.id === hint.id)) return prev;
      // cap to 20 most recent to avoid runaway memory
      const next = [hint, ...prev].slice(0, 20);
      return next;
    });
  }, []);

  // WebSocket lifecycle
  useEffect(() => {
    if (mode === 'off') {
      setConnectionStatus('off');
      return;
    }
    let primary: Disposable | null = null;
    let fallback: Disposable | null = null;
    const ensureFallback = () => {
      if (!fallback && getFallbackContext) {
        fallback = coachPollingFallback(
          () => {
            const ctx = getFallbackContext();
            if (!ctx) return null;
            return { sessionId, ...ctx };
          },
          handleHint,
          30_000,
        );
      }
    };

    primary = connectWritingCoachStream(sessionId, {
      onHint: handleHint,
      onStatusChange: (status) => {
        if (status === 'fallback') {
          setConnectionStatus('fallback');
          ensureFallback();
        } else {
          setConnectionStatus(status);
        }
      },
    }, {}, getFallbackContext
      ? () => {
          const ctx = getFallbackContext();
          if (!ctx) return null;
          return { sessionId, ...ctx };
        }
      : undefined);

    return () => {
      primary?.close();
      fallback?.close();
    };
  }, [mode, sessionId, handleHint, getFallbackContext]);

  const handleDismiss = useCallback(
    (hintId: string) => {
      dismissedRef.current.add(hintId);
      setHints((prev) => prev.filter((h) => h.id !== hintId));
      onDismissHint?.(hintId);
    },
    [onDismissHint],
  );

  const statusLabel = useMemo(() => {
    switch (connectionStatus) {
      case 'open':
        return 'Live';
      case 'connecting':
        return 'Connecting…';
      case 'reconnecting':
        return 'Reconnecting…';
      case 'fallback':
        return 'Polling';
      case 'closed':
        return 'Offline';
      case 'off':
        return 'Off';
      default:
        return '';
    }
  }, [connectionStatus]);

  return (
    <aside
      className={cn(
        'flex flex-col h-full bg-surface border border-border rounded-2xl shadow-sm',
        className,
      )}
      aria-label="AI writing coach panel"
    >
      <header className="flex items-center justify-between gap-2 border-b border-border px-4 py-3">
        <div className="flex items-center gap-2 min-w-0">
          <Sparkles className="w-4 h-4 text-primary shrink-0" aria-hidden="true" />
          <h3 className="text-sm font-bold text-navy dark:text-white truncate">AI Coach</h3>
          {statusLabel ? (
            <span className="text-[10px] uppercase tracking-wider font-bold text-muted ml-1">
              {statusLabel}
            </span>
          ) : null}
        </div>
        {onToggleMode ? (
          <Button
            type="button"
            variant={mode === 'on' ? 'primary' : 'outline'}
            size="sm"
            onClick={() => onToggleMode(mode === 'on' ? 'off' : 'on')}
            aria-pressed={mode === 'on'}
            aria-label={mode === 'on' ? 'Turn coach off' : 'Turn coach on'}
          >
            {mode === 'on' ? 'On' : 'Off'}
          </Button>
        ) : (
          <span
            className={cn(
              'text-[10px] uppercase tracking-wider font-bold rounded-full px-2 py-0.5',
              mode === 'on'
                ? 'bg-emerald-50 text-emerald-700'
                : 'bg-slate-100 text-slate-600',
            )}
          >
            {mode}
          </span>
        )}
      </header>

      <div
        className="flex-1 overflow-y-auto p-3 space-y-3"
        aria-live="polite"
        aria-relevant="additions text"
        aria-atomic="false"
      >
        {mode === 'off' ? (
          <p className="text-sm text-muted py-8 text-center">
            Coach is off. Turn it on for real-time hints while you write.
          </p>
        ) : hints.length === 0 ? (
          <p className="text-sm text-muted py-8 text-center">
            Keep writing — hints will appear here as you go.
          </p>
        ) : (
          hints.map((hint) => {
            const meta = CATEGORY_META[hint.category] ?? CATEGORY_META.style;
            const Icon = meta.icon;
            return (
              <Card
                key={hint.id}
                padding="sm"
                className={cn('border', meta.tone)}
              >
                <div className="flex items-start gap-2">
                  <Icon className="w-4 h-4 mt-0.5 shrink-0" aria-hidden="true" />
                  <div className="flex-1 min-w-0">
                    <div className="text-[10px] uppercase tracking-wider font-bold opacity-80">
                      {meta.label}
                    </div>
                    <p className="text-sm leading-snug mt-0.5">{hint.text}</p>
                    {hint.ruleId ? (
                      <a
                        href={`/writing/canon/${encodeURIComponent(hint.ruleId)}`}
                        className="text-xs font-bold underline mt-1 inline-block"
                      >
                        Learn the rule ({hint.ruleId})
                      </a>
                    ) : null}
                  </div>
                  <button
                    type="button"
                    onClick={() => handleDismiss(hint.id)}
                    className="shrink-0 -mt-1 -mr-1 rounded p-1 hover:bg-black/5 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                    aria-label="Dismiss hint"
                  >
                    <X className="w-3.5 h-3.5" aria-hidden="true" />
                  </button>
                </div>
              </Card>
            );
          })
        )}
      </div>
    </aside>
  );
}
