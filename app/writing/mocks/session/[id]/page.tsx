'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { Award } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { WritingEditorV2 } from '@/components/domain/writing/WritingEditorV2';
import { WritingTimerV2 } from '@/components/domain/writing/WritingTimerV2';
import { WordCounter } from '@/components/domain/writing/WordCounter';
import { SubmitBar } from '@/components/domain/writing/SubmitBar';
import { WritingCaseNotesPanel } from '@/components/domain/writing-case-notes-panel';
import {
  beginWritingMockWriting,
  getWritingMockSession,
  getWritingScenario,
  putWritingDraftV2,
  submitWritingMock,
} from '@/lib/writing/api';
import type {
  WritingMockSessionDto,
  WritingScenarioDto,
} from '@/lib/writing/types';

export default function WritingMockSessionPage() {
  const t = useTranslations();
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const sessionId = String(params?.id ?? '');

  const [session, setSession] = useState<WritingMockSessionDto | null>(null);
  const [scenario, setScenario] = useState<WritingScenarioDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [phase, setPhase] = useState<'reading' | 'writing' | 'completed'>('reading');
  const [readingSeconds, setReadingSeconds] = useState(0);
  const [writingSeconds, setWritingSeconds] = useState(0);
  const [content, setContent] = useState('');
  const [wordCount, setWordCount] = useState(0);
  const [submitting, setSubmitting] = useState(false);
  const startedAtRef = useRef<number>(Date.now());
  const lastAutosaveContent = useRef('');

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
    }, 5000);
    return () => window.clearInterval(timer);
  }, [phase, scenario?.id, content, wordCount]);

  const canSubmit = phase === 'writing' && wordCount >= 100 && !submitting;
  const helperText = useMemo(() => {
    if (phase !== 'writing') return t('writing.mocks.session.helper.notStarted');
    if (wordCount < 100) return t('writing.mocks.session.helper.tooShort');
    return t('writing.mocks.session.helper.ready');
  }, [phase, wordCount, t]);

  const submit = useCallback(async () => {
    if (!canSubmit) return;
    setSubmitting(true);
    setError(null);
    try {
      const elapsed = Math.round((Date.now() - startedAtRef.current) / 1000);
      await submitWritingMock(sessionId, { letterContent: content, wordCount, timeSpentSeconds: elapsed });
      router.push(`/writing/mocks/session/${encodeURIComponent(sessionId)}/results`);
    } catch (err) {
      setError(err instanceof Error ? err.message : t('writing.mocks.session.error.submit'));
      setSubmitting(false);
    }
  }, [canSubmit, content, sessionId, wordCount, router, t]);

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
          <section aria-label={t('writing.mocks.session.caseNotesLabel')} className="min-h-[60vh] overflow-hidden rounded-2xl border border-border bg-surface">
            <WritingCaseNotesPanel
              caseNotes={scenario?.caseNotesMarkdown ?? t('writing.mocks.session.caseNotesLoading')}
              readingWindowLocked={phase === 'reading'}
              taskId={sessionId}
            />
          </section>

          <section aria-label={t('writing.mocks.session.editorLabel')} className="rounded-2xl border border-border bg-surface p-4">
            <WritingEditorV2
              mode="mock"
              initialContent=""
              disabled={phase !== 'writing'}
              spellCheck={false}
              onChange={(text, words) => {
                setContent(text);
                setWordCount(words);
              }}
              placeholder={phase === 'reading' ? t('writing.mocks.session.editorPlaceholder.reading') : t('writing.mocks.session.editorPlaceholder.writing')}
              inputId="mock-editor"
            />
          </section>
        </div>

        <SubmitBar
          canSubmit={canSubmit}
          submitLabel={t('writing.mocks.session.submit')}
          onSubmit={() => void submit()}
          loading={submitting}
          helperText={helperText}
        />
      </div>
    </LearnerDashboardShell>
  );
}
