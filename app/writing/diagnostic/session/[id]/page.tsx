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
import { CaseNotePdfViewer } from '@/components/domain/writing/CaseNotePdfViewer';
import {
  beginWritingDiagnosticWriting,
  getWritingDiagnosticSession,
  getWritingScenario,
  putWritingDraftV2,
  submitWritingDiagnostic,
} from '@/lib/writing/api';
import { connectWritingSubmissionStream } from '@/lib/writing/realtime';
import { recordWritingAttemptEvent } from '@/lib/writing/exam-api';
import type { WritingDiagnosticSessionDto } from '@/lib/writing/api';
import type { WritingAttemptEventType, WritingGradeDto, WritingScenarioDto } from '@/lib/writing/types';

export default function WritingDiagnosticSessionPage() {
  const t = useTranslations();
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const sessionId = String(params?.id ?? '');

  const [session, setSession] = useState<WritingDiagnosticSessionDto | null>(null);
  const [scenario, setScenario] = useState<WritingScenarioDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [content, setContent] = useState('');
  const [wordCount, setWordCount] = useState(0);
  const [submitting, setSubmitting] = useState(false);
  const [phase, setPhase] = useState<'reading' | 'writing' | 'completed'>('reading');
  const [readingSeconds, setReadingSeconds] = useState(0);
  const [writingSeconds, setWritingSeconds] = useState(0);
  // Post-submit lock: freeze the letter + show a read-only "awaiting review"
  // state instead of navigating away (spec §10/§15).
  const [locked, setLocked] = useState(false);
  const [frozenLetter, setFrozenLetter] = useState('');
  const startedAtRef = useRef<number>(Date.now());
  const lastAutosaveContent = useRef<string>('');
  // Throttle `response_typed` to at most one event per 10s window (spec §17.7).
  const lastTypedEventAt = useRef(0);

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

  const refresh = useCallback(async () => {
    if (!sessionId) return;
    try {
      const next = await getWritingDiagnosticSession(sessionId);
      setSession(next);
      setReadingSeconds(next.readingSecondsRemaining);
      setWritingSeconds(next.writingSecondsRemaining);
      setPhase(next.phase === 'submitted' ? 'completed' : next.phase);
      if (next.phase === 'submitted' && next.submissionId) {
        router.replace(`/writing/diagnostic/session/${encodeURIComponent(sessionId)}/results`);
      }
      if (next.scenarioId) {
        const sc = await getWritingScenario(next.scenarioId);
        setScenario(sc);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : t('writing.diagnostic.session.error.load'));
    }
  }, [sessionId, router, t]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  // Local tick for both windows.
  useEffect(() => {
    if (phase === 'completed') return;
    const tick = window.setInterval(() => {
      if (phase === 'reading') setReadingSeconds((s) => Math.max(0, s - 1));
      else if (phase === 'writing') setWritingSeconds((s) => Math.max(0, s - 1));
    }, 1000);
    return () => window.clearInterval(tick);
  }, [phase]);

  // Phase transition: reading → writing must call backend so it can record the
  // phase boundary and (re)issue token bookkeeping.
  const handlePhaseChange = useCallback(
    async (next: 'reading' | 'writing' | 'completed') => {
      if (next === 'writing' && phase !== 'writing') {
        setPhase('writing');
        try {
          const updated = await beginWritingDiagnosticWriting(sessionId);
          setReadingSeconds(updated.readingSecondsRemaining);
          setWritingSeconds(updated.writingSecondsRemaining);
        } catch (err) {
          setError(err instanceof Error ? err.message : t('writing.diagnostic.session.error.startWriting'));
        }
      } else if (next === 'completed') {
        setPhase('completed');
      }
    },
    [phase, sessionId, t],
  );

  // Autosave every 5s during writing phase.
  useEffect(() => {
    if (phase !== 'writing' || !scenario?.id) return;
    const interval = window.setInterval(() => {
      const elapsed = Math.round((Date.now() - startedAtRef.current) / 1000);
      if (content === lastAutosaveContent.current) return;
      lastAutosaveContent.current = content;
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

  // SignalR — grade-ready push (in case backend grades quickly while still on
  // this page). Suppressed once we've shown the local locked state so the
  // learner stays on the "awaiting review" screen (they navigate via the link).
  useEffect(() => {
    if (!session?.submissionId || locked) return;
    const d = connectWritingSubmissionStream(session.submissionId, {
      onGradeReady: (_: WritingGradeDto) => {
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
              <h1 className="text-base font-bold text-navy">{scenario?.title ?? t('writing.diagnostic.session.scenarioLoading')}</h1>
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
              onPhaseChange={(next) => void handlePhaseChange(next)}
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
            <CaseNotePdfViewer
              caseNotesMarkdown={scenario?.caseNotesMarkdown ?? t('writing.diagnostic.session.caseNotesLoading')}
              caseNoteSections={scenario?.caseNoteSections}
              recipient={scenario?.recipient ?? null}
              taskPrompt={scenario?.taskPromptMarkdown ?? null}
              title={scenario?.title ?? t('writing.diagnostic.session.caseNotesLabel')}
              readingWindowLocked={phase === 'reading'}
            />
          </section>

          <section
            aria-label={t('writing.diagnostic.session.editorLabel')}
            className="rounded-2xl border border-border bg-surface p-4"
            // Learner writes the letter in English — keep the editor LTR.
            dir="ltr"
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
      </div>
    </LearnerDashboardShell>
  );
}
