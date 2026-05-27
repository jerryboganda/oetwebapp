'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { ClipboardCheck } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { InlineAlert } from '@/components/ui/alert';
import { WritingEditorV2 } from '@/components/domain/writing/WritingEditorV2';
import { WritingTimerV2 } from '@/components/domain/writing/WritingTimerV2';
import { WordCounter } from '@/components/domain/writing/WordCounter';
import { SubmitBar } from '@/components/domain/writing/SubmitBar';
import { WritingCaseNotesPanel } from '@/components/domain/writing-case-notes-panel';
import {
  beginWritingDiagnosticWriting,
  getWritingDiagnosticSession,
  getWritingScenario,
  putWritingDraftV2,
  submitWritingDiagnostic,
} from '@/lib/writing/api';
import { connectWritingSubmissionStream } from '@/lib/writing/realtime';
import type { WritingDiagnosticSessionDto } from '@/lib/writing/api';
import type { WritingGradeDto, WritingScenarioDto } from '@/lib/writing/types';

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
  const startedAtRef = useRef<number>(Date.now());
  const lastAutosaveContent = useRef<string>('');

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
    const t = window.setInterval(() => {
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
    }, 5000);
    return () => window.clearInterval(t);
  }, [phase, scenario?.id, content, wordCount]);

  const helperText = useMemo(() => {
    if (phase !== 'writing') return t('writing.diagnostic.session.helper.notStarted');
    if (wordCount < 50) return t('writing.diagnostic.session.helper.tooShort');
    return t('writing.diagnostic.session.helper.ready');
  }, [phase, wordCount, t]);

  const canSubmit = phase === 'writing' && wordCount >= 50 && !submitting;

  const submit = useCallback(async () => {
    if (!canSubmit) return;
    setSubmitting(true);
    setError(null);
    try {
      const elapsed = Math.round((Date.now() - startedAtRef.current) / 1000);
      await submitWritingDiagnostic(sessionId, {
        letterContent: content,
        wordCount,
        timeSpentSeconds: elapsed,
      });
      router.push(`/writing/diagnostic/session/${encodeURIComponent(sessionId)}/results`);
    } catch (err) {
      setError(err instanceof Error ? err.message : t('writing.diagnostic.session.error.submit'));
      setSubmitting(false);
    }
  }, [canSubmit, content, sessionId, wordCount, router, t]);

  // SignalR — grade-ready push (in case backend grades quickly while still on this page).
  useEffect(() => {
    if (!session?.submissionId) return;
    const d = connectWritingSubmissionStream(session.submissionId, {
      onGradeReady: (_: WritingGradeDto) => {
        router.push(`/writing/diagnostic/session/${encodeURIComponent(sessionId)}/results`);
      },
    });
    return () => d.close();
  }, [session?.submissionId, sessionId, router]);

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
            <WritingCaseNotesPanel
              caseNotes={scenario?.caseNotesMarkdown ?? t('writing.diagnostic.session.caseNotesLoading')}
              readingWindowLocked={phase === 'reading'}
              taskId={sessionId}
            />
          </section>

          <section
            aria-label={t('writing.diagnostic.session.editorLabel')}
            className="rounded-2xl border border-border bg-surface p-4"
            // Learner writes the letter in English — keep the editor LTR.
            dir="ltr"
          >
            <WritingEditorV2
              mode="diagnostic"
              initialContent=""
              disabled={phase !== 'writing'}
              spellCheck={true}
              onChange={(text, words) => {
                setContent(text);
                setWordCount(words);
              }}
              placeholder={
                phase === 'reading'
                  ? t('writing.diagnostic.session.placeholderReading')
                  : t('writing.diagnostic.session.placeholderWriting')
              }
              inputId="diagnostic-editor"
            />
          </section>
        </div>

        <SubmitBar
          canSubmit={canSubmit}
          submitLabel={t('writing.diagnostic.session.submit')}
          onSubmit={() => void submit()}
          loading={submitting}
          helperText={helperText}
        />
      </div>
    </LearnerDashboardShell>
  );
}
