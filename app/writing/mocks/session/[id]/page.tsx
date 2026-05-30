'use client';

import { Suspense, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useParams, useRouter, useSearchParams } from 'next/navigation';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { Award, Lock } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { WritingEditorV2 } from '@/components/domain/writing/WritingEditorV2';
import { WritingTimerV2 } from '@/components/domain/writing/WritingTimerV2';
import { WordCounter } from '@/components/domain/writing/WordCounter';
import { SubmitBar } from '@/components/domain/writing/SubmitBar';
import { CaseNotePdfViewer } from '@/components/domain/writing/CaseNotePdfViewer';
import { PracticeScratchpad } from '@/components/domain/writing/PracticeScratchpad';
import {
  beginWritingMockWriting,
  getWritingMockSession,
  getWritingScenario,
  putWritingDraftV2,
  submitWritingMock,
} from '@/lib/writing/api';
import { recordWritingAttemptEvent } from '@/lib/writing/exam-api';
import type { WritingAttemptEventType } from '@/lib/writing/types';
import type {
  WritingMockSessionDto,
  WritingScenarioDto,
} from '@/lib/writing/types';

function WritingMockSessionInner() {
  const t = useTranslations();
  const params = useParams<{ id: string }>();
  const searchParams = useSearchParams();
  const router = useRouter();
  const sessionId = String(params?.id ?? '');

  // Strict mock is the default; `?practice=1` selects the relaxed variant
  // (spec §20.2) — adds the planning scratchpad and relaxes paste/spellcheck.
  const isPractice = searchParams?.get('practice') === '1';
  const strict = !isPractice;

  const [session, setSession] = useState<WritingMockSessionDto | null>(null);
  const [scenario, setScenario] = useState<WritingScenarioDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [phase, setPhase] = useState<'reading' | 'writing' | 'completed'>('reading');
  const [readingSeconds, setReadingSeconds] = useState(0);
  const [writingSeconds, setWritingSeconds] = useState(0);
  const [content, setContent] = useState('');
  const [wordCount, setWordCount] = useState(0);
  const [submitting, setSubmitting] = useState(false);
  // Post-submit lock: instead of navigating away we freeze the letter and show
  // a "Submitted — awaiting tutor review" read-only state with a results link.
  const [locked, setLocked] = useState(false);
  const [frozenLetter, setFrozenLetter] = useState('');
  // Practice-only planning scratchpad (never submitted).
  const [scratch, setScratch] = useState('');
  const startedAtRef = useRef<number>(Date.now());
  const lastAutosaveContent = useRef('');

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
    void getWritingMockSession(sessionId)
      .then(async (s) => {
        if (cancelled) return;
        setSession(s);
        setReadingSeconds(s.readingSecondsRemaining);
        setWritingSeconds(s.writingSecondsRemaining);
        if (s.status === 'reading') setPhase('reading');
        else if (s.status === 'writing') {
          setPhase('writing');
          startedAtRef.current = s.readingPhaseEndedAt ? new Date(s.readingPhaseEndedAt).getTime() : Date.now();
        }
        else setPhase('completed');
        if (s.scenarioId) {
          const sc = await getWritingScenario(s.scenarioId).catch(() => null);
          if (sc) setScenario(sc);
        }
        if (s.status === 'submitted' && s.id) {
          router.replace(`/writing/mocks/session/${encodeURIComponent(s.id)}/results`);
        }
      })
      .catch((err) => {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : t('writing.mocks.session.error.load'));
      });
    return () => {
      cancelled = true;
    };
  }, [sessionId, router, t]);

  useEffect(() => {
    if (phase === 'completed') return;
    const tick = window.setInterval(() => {
      if (phase === 'reading') setReadingSeconds((s) => Math.max(0, s - 1));
      else setWritingSeconds((s) => Math.max(0, s - 1));
    }, 1000);
    return () => window.clearInterval(tick);
  }, [phase]);

  // Confirm on browser close/back while in writing phase (strict mode).
  useEffect(() => {
    if (phase !== 'writing') return;
    const handler = (e: BeforeUnloadEvent) => {
      e.preventDefault();
      e.returnValue = '';
    };
    window.addEventListener('beforeunload', handler);
    return () => window.removeEventListener('beforeunload', handler);
  }, [phase]);

  // Autosave every 5s during writing.
  useEffect(() => {
    if (phase !== 'writing' || !scenario?.id) return;
    const timer = window.setInterval(() => {
      if (content === lastAutosaveContent.current) return;
      lastAutosaveContent.current = content;
      const elapsed = Math.round((Date.now() - startedAtRef.current) / 1000);
      void putWritingDraftV2(scenario.id, 'mock', { content, wordCount, timeSpentSeconds: elapsed }).catch(() => {
        /* best-effort */
      });
      emitEvent('auto_saved', { wordCount });
    }, 5000);
    return () => window.clearInterval(timer);
  }, [phase, scenario?.id, content, wordCount, emitEvent]);

  // attempt_started + reading_started once on mount (computer mode).
  useEffect(() => {
    emitEvent('attempt_started', { variant: strict ? 'mock' : 'practice' });
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

  const canSubmit = phase === 'writing' && wordCount >= 100 && !submitting;
  const helperText = useMemo(() => {
    if (phase !== 'writing') return t('writing.mocks.session.helper.notStarted');
    if (wordCount < 100) return t('writing.mocks.session.helper.tooShort');
    return t('writing.mocks.session.helper.ready');
  }, [phase, wordCount, t]);

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
        await submitWritingMock(sessionId, { letterContent: letter, wordCount, timeSpentSeconds: elapsed });
        // Do NOT navigate: freeze the letter and lock the UI (spec §10/§15).
        setFrozenLetter(letter);
        setLocked(true);
        setPhase('completed');
        emitEvent('attempt_locked');
      } catch (err) {
        setError(err instanceof Error ? err.message : t('writing.mocks.session.error.submit'));
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
  // Preserves the strict auto-submit-on-timer guarantee while showing the
  // locked read-only state instead of an immediate redirect.
  useEffect(() => {
    if (phase === 'completed' && !locked && !submitting) {
      void finalizeSubmit(true);
    }
  }, [phase, locked, submitting, finalizeSubmit]);

  const handlePhaseChange = useCallback(async (next: 'reading' | 'writing' | 'completed') => {
    if (next === 'writing' && phase !== 'writing') {
      setPhase('writing');
      try {
        const updated = await beginWritingMockWriting(sessionId);
        setSession(updated);
        setReadingSeconds(updated.readingSecondsRemaining);
        setWritingSeconds(updated.writingSecondsRemaining);
        startedAtRef.current = updated.readingPhaseEndedAt ? new Date(updated.readingPhaseEndedAt).getTime() : Date.now();
      } catch (err) {
        setError(err instanceof Error ? err.message : t('writing.mocks.session.error.startWriting'));
      }
    } else if (next === 'completed') {
      // Writing timer expired — the completed-phase effect auto-submits + locks.
      setPhase('completed');
    }
  }, [phase, sessionId, t]);

  return (
    <LearnerDashboardShell pageTitle={t('writing.mocks.session.pageTitle')} distractionFree>
      <div className="space-y-4 pb-32" aria-busy={!session}>
        <header
          className="sticky top-0 z-20 flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-border bg-surface/95 p-4 shadow-sm backdrop-blur"
          aria-label={t('writing.mocks.session.controlsLabel')}
        >
          <div className="flex items-center gap-3">
            <Award className="h-5 w-5 text-amber-600" aria-hidden="true" />
            <div>
              <p className="text-xs font-bold uppercase tracking-wider text-muted">{t('writing.mocks.session.eyebrow')}</p>
              {/* Scenario title is OET-authored English content. */}
              <h1 className="text-base font-bold text-navy" dir="ltr">{scenario?.title ?? t('writing.mocks.session.scenarioLoading')}</h1>
              <div className="mt-1 flex flex-wrap gap-1">
                <Badge variant={strict ? 'warning' : 'info'} size="sm">
                  {strict ? 'Strict mock' : 'Practice'}
                </Badge>
                {scenario ? (
                  <>
                    <Badge variant="muted" size="sm">{scenario.letterType}</Badge>
                    <Badge variant="info" size="sm" className="capitalize">{scenario.profession}</Badge>
                  </>
                ) : null}
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
            aria-label={t('writing.mocks.session.caseNotesLabel')}
            className="min-h-[60vh] overflow-hidden rounded-2xl border border-border bg-surface"
            // Case notes are OET-authored English content (spec §32) — force LTR.
            dir="ltr"
          >
            <CaseNotePdfViewer
              caseNotesMarkdown={scenario?.caseNotesMarkdown ?? t('writing.mocks.session.caseNotesLoading')}
              title={scenario?.title ?? t('writing.mocks.session.caseNotesLabel')}
              readingWindowLocked={phase === 'reading'}
            />
          </section>

          <section aria-label={t('writing.mocks.session.editorLabel')} className="flex flex-col gap-3 rounded-2xl border border-border bg-surface p-4">
            {/* Practice-only planning area; never shown in strict mock. */}
            {isPractice && !locked ? (
              <PracticeScratchpad value={scratch} onChange={setScratch} />
            ) : null}

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
                {/* Frozen, read-only letter. */}
                <div
                  className="flex-1 overflow-y-auto whitespace-pre-wrap rounded-xl border border-border bg-background-light p-4 text-sm leading-7 text-navy"
                  dir="ltr"
                  aria-label="Submitted letter (read-only)"
                >
                  {frozenLetter.trim() ? frozenLetter : 'No letter was written.'}
                </div>
                <Button asChild variant="primary" size="md" className="mt-3 self-start">
                  <Link href={`/writing/mocks/session/${encodeURIComponent(sessionId)}/results`}>
                    Go to results
                  </Link>
                </Button>
              </div>
            ) : (
              <WritingEditorV2
                mode="mock"
                initialContent=""
                disabled={phase !== 'writing'}
                blockPaste={strict}
                onPasteBlocked={handlePasteBlocked}
                onChange={(text, words) => {
                  setContent(text);
                  setWordCount(words);
                }}
                placeholder={phase === 'reading' ? t('writing.mocks.session.editorPlaceholder.reading') : t('writing.mocks.session.editorPlaceholder.writing')}
                inputId="mock-editor"
              />
            )}
          </section>
        </div>

        {!locked ? (
          <SubmitBar
            canSubmit={canSubmit}
            submitLabel={t('writing.mocks.session.submit')}
            onSubmit={submit}
            loading={submitting}
            helperText={helperText}
          />
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}

export default function WritingMockSessionPage() {
  // useSearchParams requires a Suspense boundary in the App Router.
  return (
    <Suspense fallback={null}>
      <WritingMockSessionInner />
    </Suspense>
  );
}
