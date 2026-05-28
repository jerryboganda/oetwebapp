'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { Sparkles, PenTool } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Modal } from '@/components/ui/modal';
import { WritingEditorV2 } from '@/components/domain/writing/WritingEditorV2';
import { WordCounter } from '@/components/domain/writing/WordCounter';
import { SubmitBar } from '@/components/domain/writing/SubmitBar';
import { CoachPanel } from '@/components/domain/writing/CoachPanel';
import { WritingCaseNotesPanel } from '@/components/domain/writing-case-notes-panel';
import {
  createWritingSubmission,
  getWritingDraftV2,
  getWritingScenario,
  putWritingDraftV2,
} from '@/lib/writing/api';
import type {
  WritingEditorMode,
  WritingScenarioDto,
} from '@/lib/writing/types';

type ScenarioMode = Extract<WritingEditorMode, 'practice' | 'coached'>;

export default function WritingPracticeSessionPage() {
  const t = useTranslations();
  const params = useParams<{ scenarioId: string }>();
  const router = useRouter();
  const scenarioId = String(params?.scenarioId ?? '');

  const [scenario, setScenario] = useState<WritingScenarioDto | null>(null);
  const [mode, setMode] = useState<ScenarioMode>('practice');
  const [content, setContent] = useState('');
  const [initialContent, setInitialContent] = useState('');
  const [wordCount, setWordCount] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [noCreditsOpen, setNoCreditsOpen] = useState(false);
  const startedAtRef = useRef<number>(Date.now());
  const lastAutosaveContent = useRef<string>('');
  const coachSessionId = useMemo(() => `practice-${scenarioId}`, [scenarioId]);

  useEffect(() => {
    if (!scenarioId) return;
    let cancelled = false;
    void Promise.all([
      getWritingScenario(scenarioId),
      getWritingDraftV2(scenarioId, mode).catch(() => null),
    ])
      .then(([sc, draft]) => {
        if (cancelled) return;
        setScenario(sc);
        if (draft?.content) {
          setInitialContent(draft.content);
          setContent(draft.content);
          setWordCount(draft.wordCount);
        }
      })
      .catch((err) => {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : t('writing.practice.session.error.load'));
      });
    return () => {
      cancelled = true;
    };
  }, [scenarioId, mode, t]);

  useEffect(() => {
    if (!scenarioId) return;
    const timer = window.setInterval(() => {
      if (content === lastAutosaveContent.current) return;
      lastAutosaveContent.current = content;
      const elapsed = Math.round((Date.now() - startedAtRef.current) / 1000);
      void putWritingDraftV2(scenarioId, mode, {
        content,
        wordCount,
        timeSpentSeconds: elapsed,
      }).catch(() => {
        /* best-effort */
      });
    }, 5000);
    return () => window.clearInterval(timer);
  }, [scenarioId, content, wordCount, mode]);

  const canSubmit = wordCount >= 50 && !submitting;

  const helperText = wordCount < 50
    ? t('writing.practice.session.helper.tooShort', { remaining: 50 - wordCount })
    : t('writing.practice.session.helper.ready');

  const onSubmit = useCallback(async () => {
    if (!canSubmit || !scenario) return;
    setSubmitting(true);
    setError(null);
    try {
      const elapsed = Math.round((Date.now() - startedAtRef.current) / 1000);
      const submission = await createWritingSubmission({
        scenarioId: scenario.id,
        mode,
        letterContent: content,
        wordCount,
        timeSpentSeconds: elapsed,
        inputSource: 'editor',
      });
      router.push(`/writing/submissions/${encodeURIComponent(submission.id)}/grading`);
    } catch (err) {
      // Balance = 0 (spec §9): the AI grading credit pool is exhausted. Surface
      // a dedicated modal with a direct path to the AI Credits storefront rather
      // than a generic inline error. The draft autosaves, so nothing is lost.
      const code = (err as { code?: string }).code;
      const status = (err as { status?: number }).status;
      if (code === 'ai_credits_insufficient' || status === 402) {
        setNoCreditsOpen(true);
      } else {
        setError(err instanceof Error ? err.message : t('writing.practice.session.error.submit'));
      }
      setSubmitting(false);
    }
  }, [canSubmit, content, scenario, wordCount, mode, router, t]);

  return (
    <LearnerDashboardShell pageTitle={t('writing.practice.session.pageTitle')} distractionFree>
      <div className="space-y-4 pb-32" aria-busy={!scenario}>
        <header
          className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-border bg-surface p-4 shadow-sm"
          aria-label={t('writing.practice.session.controlsLabel')}
        >
          <div className="flex items-center gap-3">
            <PenTool className="h-5 w-5 text-amber-600" aria-hidden="true" />
            <div>
              <p className="text-xs font-bold uppercase tracking-wider text-muted">{t('writing.practice.session.eyebrow')}</p>
              {/* Scenario title is OET-authored English content. */}
              <h1 className="text-base font-bold text-navy" dir="ltr">{scenario?.title ?? t('writing.practice.session.scenarioLoading')}</h1>
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
          <div className="flex flex-wrap items-center gap-3">
            <fieldset className="inline-flex rounded-lg border border-border bg-background p-0.5">
              <legend className="sr-only">{t('writing.practice.session.editorModeLegend')}</legend>
              {(['practice', 'coached'] as const).map((m) => {
                const active = mode === m;
                return (
                  <label
                    key={m}
                    className={`cursor-pointer rounded-md px-3 py-1.5 text-xs font-bold transition-colors focus-within:ring-2 focus-within:ring-primary ${active ? 'bg-primary text-white' : 'text-navy hover:bg-primary/10'}`}
                  >
                    <input
                      type="radio"
                      name="editor-mode"
                      value={m}
                      checked={active}
                      onChange={() => setMode(m)}
                      className="sr-only"
                    />
                    {m === 'practice' ? t('writing.practice.session.mode.practice') : t('writing.practice.session.mode.coached')}
                    {m === 'coached' ? <Sparkles className="ml-1 inline h-3 w-3" aria-hidden="true" /> : null}
                  </label>
                );
              })}
            </fieldset>
            <WordCounter count={wordCount} target={{ min: 180, max: 220 }} ariaLabelPrefix="Letter length" />
          </div>
        </header>

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <div className={`grid gap-4 ${mode === 'coached' ? 'lg:grid-cols-12' : 'lg:grid-cols-2'}`}>
          <section
            aria-label={t('writing.practice.session.caseNotesLabel')}
            className={`min-h-[60vh] overflow-hidden rounded-2xl border border-border bg-surface ${mode === 'coached' ? 'lg:col-span-4' : ''}`}
          >
            <WritingCaseNotesPanel
              caseNotes={scenario?.caseNotesMarkdown ?? t('writing.practice.session.caseNotesLoading')}
              taskId={scenarioId}
            />
          </section>

          <section
            aria-label={t('writing.practice.session.editorLabel')}
            className={`rounded-2xl border border-border bg-surface p-4 ${mode === 'coached' ? 'lg:col-span-5' : ''}`}
          >
            <WritingEditorV2
              mode={mode}
              initialContent={initialContent}
              onChange={(text, words) => {
                setContent(text);
                setWordCount(words);
              }}
              placeholder={t('writing.practice.session.editorPlaceholder')}
              inputId="practice-editor"
            />
          </section>

          {mode === 'coached' && scenario ? (
            <aside className="lg:col-span-3" aria-label={t('writing.practice.session.coachLabel')}>
              <CoachPanel
                sessionId={coachSessionId}
                mode="on"
                getFallbackContext={() => ({
                  scenarioId: scenario.id,
                  letterContent: content,
                  wordCount,
                  letterType: scenario.letterType,
                  profession: scenario.profession,
                })}
              />
            </aside>
          ) : null}
        </div>

        <SubmitBar
          canSubmit={canSubmit}
          submitLabel={t('writing.practice.session.submit')}
          onSubmit={() => void onSubmit()}
          loading={submitting}
          helperText={helperText}
        />
      </div>

      <Modal
        open={noCreditsOpen}
        onClose={() => setNoCreditsOpen(false)}
        title="No AI credits remaining"
      >
        <div className="space-y-4">
          <p className="text-sm leading-6 text-muted">
            You have no AI grading credits remaining. AI Credits grade your Writing letters and Speaking
            cards instantly — purchase a package to continue. Your draft has been saved.
          </p>
          <div className="flex flex-wrap justify-end gap-2">
            <Button variant="outline" onClick={() => setNoCreditsOpen(false)}>
              Not now
            </Button>
            <Button onClick={() => router.push('/billing?tab=ai-credits')}>
              Buy AI Credits
            </Button>
          </div>
        </div>
      </Modal>
    </LearnerDashboardShell>
  );
}
