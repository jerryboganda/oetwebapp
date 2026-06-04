'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { ClipboardCheck, Lock } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { WritingEditorV2 } from '@/components/domain/writing/WritingEditorV2';
import { WritingTimerV2 } from '@/components/domain/writing/WritingTimerV2';
import { WordCounter } from '@/components/domain/writing/WordCounter';
import { SubmitBar } from '@/components/domain/writing/SubmitBar';
import { WritingStimulus } from '@/components/domain/writing/WritingStimulus';
import { WritingReadingWindowOverlay } from '@/components/domain/writing/WritingReadingWindowOverlay';
import {
  beginWritingDiagnosticWriting,
  getWritingDiagnosticSession,
  getWritingScenario,
  putWritingDraftV2,
  submitWritingDiagnostic,
} from '@/lib/writing/api';
import { useDeadlineCountdown } from '@/lib/writing/useCountdown';
import { WRITING_WINDOW_SECONDS } from '@/lib/writing/workflow';
import { connectWritingSubmissionStream } from '@/lib/writing/realtime';
import { recordWritingAttemptEvent } from '@/lib/writing/exam-api';
import type { WritingDiagnosticSessionDto } from '@/lib/writing/api';
import type { WritingAttemptEventType, WritingScenarioDto } from '@/lib/writing/types';

export default function WritingDiagnosticSessionPage() {
  const t = useTranslations();
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const sessionId = String(params?.id ?? '');

  const [session, setSession] = useState<WritingDiagnosticSessionDto | null>(null);
  const [scenario, setScenario] = useState<WritingScenarioDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [phase, setPhase] = useState<'reading' | 'writing' | 'completed'>('reading');
  // Deadline-anchored countdowns (wall-clock epoch ms). Survive tab-backgrounding
  // and refresh — the hook re-derives whole seconds from these on every tick and
  // on tab refocus. Null until the session loads.
  const [readingDeadlineMs, setReadingDeadlineMs] = useState<number | null>(null);
  const [writingDeadlineMs, setWritingDeadlineMs] = useState<number | null>(null);
  const [content, setContent] = useState('');
  const [wordCount, setWordCount] = useState(0);
  const [submitting, setSubmitting] = useState(false);
  // Post-submit lock: freeze the letter + show a read-only "awaiting review"
  // state instead of navigating away (spec §10/§15).
  const [locked, setLocked] = useState(false);
  const [frozenLetter, setFrozenLetter] = useState('');
  const startedAtRef = useRef<number>(Date.now());
  const lastAutosaveContent = useRef<string>('');
  // Throttle `response_typed` to at most one event per 10s window (spec §17.7).
  const lastTypedEventAt = useRef(0);
  // Guard so the reading→writing transition calls `beginWritingDiagnosticWriting`
  // at most once: the reading countdown's onZero AND the overlay's onAutoClose
  // both fire on the same tick at reading 0:00.
  const beganWritingRef = useRef(false);

  // ----- Attempt-event emission (computer mode; fire-and-forget) -----
  const contentRef = useRef(content);
  contentRef.current = content;

  const emitEvent = useCallback(
    (eventType: WritingAttemptEventType, payload?: Record<string, unknown>) => {
      recordWritingAttemptEvent({
        eventType,
        timestamp: new Date().toISOString(),
        mode: 'computer',
        sessionId: sessionId || null,
        scenarioId: scenario?.id ?? null,
        payload,
      }).catch(() => {});
    },
    [sessionId, scenario?.id],
  );

  useEffect(() => {
    if (!sessionId) return;
    let cancelled = false;
    void getWritingDiagnosticSession(sessionId)
      .then(async (s) => {
        if (cancelled) return;
        setSession(s);
        if (s.phase === 'reading') {
          setPhase('reading');
          setReadingDeadlineMs(Date.now() + s.readingSecondsRemaining * 1000);
        } else if (s.phase === 'writing') {
          setPhase('writing');
          // A session resumed in the writing phase has already begun on the
          // server — don't let the transition fire begin-writing again.
          beganWritingRef.current = true;
          setWritingDeadlineMs(
            s.readingPhaseEndedAt
              ? new Date(s.readingPhaseEndedAt).getTime() + WRITING_WINDOW_SECONDS * 1000
              : Date.now() + s.writingSecondsRemaining * 1000,
          );
          startedAtRef.current = s.readingPhaseEndedAt
            ? new Date(s.readingPhaseEndedAt).getTime()
            : Date.now();
        } else {
          setPhase('completed');
          if (s.submissionId) {
            router.replace(`/writing/diagnostic/session/${encodeURIComponent(sessionId)}/results`);
          }
        }
        if (s.scenarioId) {
          const sc = await getWritingScenario(s.scenarioId).catch(() => null);
          if (sc) setScenario(sc);
        }
      })
      .catch((err) => {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : t('writing.diagnostic.session.error.load'));
      });
    return () => {
      cancelled = true;
    };
  }, [sessionId, router, t]);

  // Confirm on browser close/back while in writing phase.
  useEffect(() => {
    if (phase !== 'writing') return;
    const handler = (e: BeforeUnloadEvent) => {
      e.preventDefault();
      e.returnValue = '';
    };
    window.addEventListener('beforeunload', handler);
    return () => window.removeEventListener('beforeunload', handler);
  }, [phase]);

  // Autosave every 5s during writing phase.
  useEffect(() => {
    if (phase !== 'writing' || !scenario?.id) return;
    const interval = window.setInterval(() => {
      if (content === lastAutosaveContent.current) return;
      lastAutosaveContent.current = content;
      const elapsed = Math.round((Date.now() - startedAtRef.current) / 1000);
      void putWritingDraftV2(scenario.id, 'diagnostic', {
        content,
        wordCount,
        timeSpentSeconds: elapsed,
      }).catch(() => {
        /* autosave is best-effort; do not surface */
      });
      emitEvent('auto_saved', { wordCount });
    }, 5000);
    return () => window.clearInterval(interval);
  }, [phase, scenario?.id, content, wordCount, emitEvent]);

  // attempt_started + reading_started once on mount (computer mode).
  useEffect(() => {
    emitEvent('attempt_started', { variant: 'diagnostic' });
    emitEvent('reading_started');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // focus_lost on window blur (integrity signal).
  useEffect(() => {
    const onBlur = () => emitEvent('focus_lost');
    window.addEventListener('blur', onBlur);
    return () => window.removeEventListener('blur', onBlur);
  }, [emitEvent]);

  // reading_ended + writing_started on the reading→writing transition.
  const prevPhaseRef = useRef(phase);
  useEffect(() => {
    if (prevPhaseRef.current === 'reading' && phase === 'writing') {
      emitEvent('reading_ended');
      emitEvent('writing_started');
    }
    prevPhaseRef.current = phase;
  }, [phase, emitEvent]);

  const handlePasteBlocked = useCallback(
    (rejectedLength: number) => {
      emitEvent('paste', { blocked: true, rejectedLength });
    },
    [emitEvent],
  );

  // Editor change handler. Emits a throttled `response_typed` (first keystroke
  // per 10s window) while writing, in addition to tracking content/word count.
  const handleEditorChange = useCallback(
    (text: string, words: number) => {
      setContent(text);
      setWordCount(words);
      if (phase !== 'writing') return;
      const now = Date.now();
      if (now - lastTypedEventAt.current >= 10_000) {
        lastTypedEventAt.current = now;
        emitEvent('response_typed', { wordCount: words });
      }
    },
    [phase, emitEvent],
  );

  const helperText = useMemo(() => {
    if (phase !== 'writing') return t('writing.diagnostic.session.helper.notStarted');
    if (wordCount < 50) return t('writing.diagnostic.session.helper.tooShort');
    return t('writing.diagnostic.session.helper.ready');
  }, [phase, wordCount, t]);

  const canSubmit = phase === 'writing' && wordCount >= 50 && !submitting;

  // Shared submit path. `auto` = true when fired by the writing-timer expiry.
  const finalizeSubmit = useCallback(
    async (auto: boolean) => {
      if (submitting || locked) return;
      setSubmitting(true);
      setError(null);
      emitEvent(auto ? 'timer_expired' : 'submit_clicked', { wordCount, auto });
      try {
        const elapsed = Math.round((Date.now() - startedAtRef.current) / 1000);
        const letter = contentRef.current;
        await submitWritingDiagnostic(sessionId, {
          letterContent: letter,
          wordCount,
          timeSpentSeconds: elapsed,
        });
        // Do NOT navigate: freeze the letter and lock the UI.
        setFrozenLetter(letter);
        setLocked(true);
        setPhase('completed');
        emitEvent('attempt_locked');
      } catch (err) {
        setError(err instanceof Error ? err.message : t('writing.diagnostic.session.error.submit'));
      } finally {
        setSubmitting(false);
      }
    },
    [submitting, locked, sessionId, wordCount, emitEvent, t],
  );

  const submit = useCallback(() => {
    if (!canSubmit) return;
    void finalizeSubmit(false);
  }, [canSubmit, finalizeSubmit]);

  // Auto-submit + lock when the writing timer expires (phase → completed).
  // Keeps the strict auto-submit-on-timer guarantee while showing the locked
  // read-only state instead of an immediate redirect.
  useEffect(() => {
    if (phase === 'completed' && !locked && !submitting) {
      void finalizeSubmit(true);
    }
  }, [phase, locked, submitting, finalizeSubmit]);

  const handlePhaseChange = useCallback(async (next: 'reading' | 'writing' | 'completed') => {
    if (next === 'writing') {
      // Idempotent: the timer's deadline onZero AND the overlay's onAutoClose
      // both fire on the same tick at reading 0:00. Only the first proceeds.
      if (beganWritingRef.current) return;
      beganWritingRef.current = true;
      // Provisional deadline BEFORE flipping phase so the writing countdown never
      // momentarily reads 0:00 during the begin-writing round-trip; corrected
      // from the server response below.
      setWritingDeadlineMs(Date.now() + WRITING_WINDOW_SECONDS * 1000);
      setPhase('writing');
      try {
        const updated = await beginWritingDiagnosticWriting(sessionId);
        setSession(updated);
        // Anchor the writing countdown to the server's reading-phase end so it
        // stays accurate across refresh/backgrounding.
        setWritingDeadlineMs(
          updated.readingPhaseEndedAt
            ? new Date(updated.readingPhaseEndedAt).getTime() + WRITING_WINDOW_SECONDS * 1000
            : Date.now() + updated.writingSecondsRemaining * 1000,
        );
        startedAtRef.current = updated.readingPhaseEndedAt
          ? new Date(updated.readingPhaseEndedAt).getTime()
          : Date.now();
      } catch (err) {
        setError(err instanceof Error ? err.message : t('writing.diagnostic.session.error.startWriting'));
      }
    } else if (next === 'completed') {
      // Writing timer expired — the completed-phase effect auto-submits + locks.
      setPhase('completed');
    }
  }, [sessionId, t]);

  // Deadline-anchored seconds. Both hooks run unconditionally (rules of hooks);
  // each gated to its active phase so only the live one counts. The null-guarded
  // onZero fires only for a REAL elapsed deadline — never on the 0 a null deadline
  // yields before load or during the begin-writing round-trip — so transitions
  // cannot fire spuriously. WritingTimerV2 is display/beep only.
  const readingSeconds = useDeadlineCountdown(
    phase === 'reading' ? readingDeadlineMs : null,
    { onZero: () => void handlePhaseChange('writing') },
  );
  const writingSeconds = useDeadlineCountdown(
    phase === 'writing' ? writingDeadlineMs : null,
    { onZero: () => void handlePhaseChange('completed') },
  );

  // SignalR — grade-ready push (in case backend grades quickly while still on
  // this page). Suppressed once we've shown the local locked state so the
  // learner stays on the "awaiting review" screen (they navigate via the link).
  useEffect(() => {
    if (!session?.submissionId || locked) return;
    const d = connectWritingSubmissionStream(session.submissionId, {
      onGradeReady: () => {
        router.push(`/writing/diagnostic/session/${encodeURIComponent(sessionId)}/results`);
      },
    });
    return () => d.close();
  }, [session?.submissionId, sessionId, router, locked]);

  return (
    <LearnerDashboardShell pageTitle={t('writing.diagnostic.session.pageTitle')} distractionFree>
      <div className="space-y-4 pb-32" aria-busy={!session}>
        <header
          className="sticky top-0 z-20 flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-border bg-surface/95 p-4 shadow-sm backdrop-blur"
          aria-label={t('writing.diagnostic.session.controlsLabel')}
        >
          <div className="flex items-center gap-3">
            <ClipboardCheck className="h-5 w-5 text-amber-600" aria-hidden="true" />
            <div>
              <p className="text-xs font-bold uppercase tracking-wider text-muted">{t('writing.diagnostic.session.eyebrow')}</p>
              {/*
                Scenario title is OET-authored English content — spec §32 says
                case-note & scenario text stays English even in Arabic UI. The
                fallback placeholder is the only translated string here.
              */}
              <h1 className="text-base font-bold text-navy" dir="ltr">{scenario?.title ?? t('writing.diagnostic.session.scenarioLoading')}</h1>
              <div className="mt-1 flex flex-wrap items-center gap-1">
                {/* Diagnostic is always strict (spec §20). */}
                <Badge variant="warning" size="sm">Strict diagnostic</Badge>
                {scenario ? (
                  <>
                    <Badge variant="muted" size="sm">{scenario.letterType}</Badge>
                    <Badge variant="info" size="sm" className="capitalize">{scenario.profession}</Badge>
                  </>
                ) : null}
                <span className="text-[11px] font-medium text-muted">
                  No spellcheck · no hints · no AI · no model answer
                </span>
              </div>
            </div>
          </div>
          <div className="flex items-center gap-4">
            <WordCounter count={wordCount} target={{ min: 180, max: 220 }} ariaLabelPrefix="Letter length" />
            <WritingTimerV2
              phase={phase}
              readingSecondsRemaining={readingSeconds}
              writingSecondsRemaining={writingSeconds}
              strict
            />
          </div>
        </header>

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <div className="grid gap-4 lg:grid-cols-2">
          <section
            aria-label={t('writing.diagnostic.session.caseNotesLabel')}
            className="min-h-[60vh] overflow-hidden rounded-2xl border border-border bg-surface"
            // Case notes content is always English (OET spec §32) — force LTR so
            // the editorial content renders correctly even when the surrounding
            // chrome is RTL.
            dir="ltr"
          >
            <WritingStimulus
              scenario={scenario}
              locked={phase === 'reading'}
              title={scenario?.title ?? undefined}
            />
          </section>

          <section
            aria-label={t('writing.diagnostic.session.editorLabel')}
            className="flex flex-col gap-3 rounded-2xl border border-border bg-surface p-4"
          >
            {locked ? (
              <div className="flex min-h-[60vh] flex-col">
                <div
                  role="status"
                  aria-live="polite"
                  className="mb-3 flex items-start gap-2 rounded-xl border border-success/40 bg-success/10 px-4 py-3"
                >
                  <Lock className="mt-0.5 h-4 w-4 shrink-0 text-success" aria-hidden="true" />
                  <div>
                    <p className="text-sm font-bold text-navy">Submitted — awaiting tutor review</p>
                    <p className="mt-0.5 text-xs text-muted">
                      Your letter is locked and can no longer be edited.
                    </p>
                  </div>
                </div>
                <div
                  className="flex-1 overflow-y-auto whitespace-pre-wrap rounded-xl border border-border bg-background-light p-4 text-sm leading-7 text-navy"
                  dir="ltr"
                  aria-label="Submitted letter (read-only)"
                >
                  {frozenLetter.trim() ? frozenLetter : 'No letter was written.'}
                </div>
                <Button asChild variant="primary" size="md" className="mt-3 self-start">
                  <Link href={`/writing/diagnostic/session/${encodeURIComponent(sessionId)}/results`}>
                    Go to results
                  </Link>
                </Button>
              </div>
            ) : (
              <WritingEditorV2
                mode="diagnostic"
                initialContent=""
                disabled={phase !== 'writing'}
                blockPaste
                onPasteBlocked={handlePasteBlocked}
                onChange={handleEditorChange}
                placeholder={
                  phase === 'reading'
                    ? t('writing.diagnostic.session.placeholderReading')
                    : t('writing.diagnostic.session.placeholderWriting')
                }
                inputId="diagnostic-editor"
              />
            )}
          </section>
        </div>

        {!locked ? (
          <SubmitBar
            canSubmit={canSubmit}
            submitLabel={t('writing.diagnostic.session.submit')}
            onSubmit={submit}
            loading={submitting}
            helperText={helperText}
          />
        ) : null}

        {/* Forced full-screen reading window. Renders only during the reading
            phase; auto-closes into the writing view at 0:00. allowSkip is always
            false for diagnostic — the session is strictly server-gated and early
            begin-writing is rejected. */}
        <WritingReadingWindowOverlay
          open={phase === 'reading' && !!session}
          scenario={scenario}
          secondsRemaining={readingSeconds}
          totalSeconds={scenario?.readingTimeSeconds ?? 300}
          allowSkip={false}
          onAutoClose={() => void handlePhaseChange('writing')}
          title={scenario?.title ?? undefined}
        />
      </div>
    </LearnerDashboardShell>
  );
}
