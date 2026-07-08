'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { PenTool } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Modal } from '@/components/ui/modal';
import { WritingEditorV2 } from '@/components/domain/writing/WritingEditorV2';
import { WritingTimerV2 } from '@/components/domain/writing/WritingTimerV2';
import { WordCounter } from '@/components/domain/writing/WordCounter';
import { SubmitBar } from '@/components/domain/writing/SubmitBar';
import { WritingStimulus } from '@/components/domain/writing/WritingStimulus';
import type { Highlight } from '@/components/domain/writing/WritingStimulusViewer';
import { WritingReadingWindowOverlay } from '@/components/domain/writing/WritingReadingWindowOverlay';
import {
  InsufficientCreditsModal,
  isInsufficientCreditsError,
  readInsufficientCreditsMessage,
} from '@/components/domain/InsufficientCreditsModal';
import {
  checkWritingScenarioEligibility,
  createWritingSubmission,
  getWritingDraftV2,
  getWritingHighlights,
  getWritingScenario,
  putWritingDraftV2,
  putWritingHighlights,
} from '@/lib/writing/api';
import { parseHighlights, serializeHighlights } from '@/lib/writing/highlights';
import { useDeadlineCountdown } from '@/lib/writing/useCountdown';
import { WRITING_READING_WINDOW_SECONDS, WRITING_WINDOW_SECONDS } from '@/lib/writing/workflow';
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
  const mode: ScenarioMode = 'practice';
  const [content, setContent] = useState('');
  const [initialContent, setInitialContent] = useState('');
  const [wordCount, setWordCount] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [noCreditsOpen, setNoCreditsOpen] = useState(false);
  const [insufficientCreditsMessage, setInsufficientCreditsMessage] = useState<string | null>(null);
  const startedAtRef = useRef<number>(Date.now());
  const lastAutosaveContent = useRef<string>('');
  // Latest content for auto-submit (timer expiry reads the current letter).
  const contentRef = useRef('');
  contentRef.current = content;

  // ── Strict 45-minute exam clock ─────────────────────────────────────────────
  // 5 min forced reading (pad LOCKED) → 40 min writing → hard auto-submit. The
  // deadlines are persisted per-scenario in sessionStorage so a refresh resumes
  // the same clock instead of granting extra time.
  const [phase, setPhase] = useState<'reading' | 'writing' | 'completed'>('reading');
  const [readingDeadlineMs, setReadingDeadlineMs] = useState<number | null>(null);
  const [writingDeadlineMs, setWritingDeadlineMs] = useState<number | null>(null);
  // Yellow highlights, lifted here so they persist from the reading window into
  // the writing view (both render the same Case Notes PDF).
  const [pdfHighlights, setPdfHighlights] = useState<Record<number, Highlight[]>>({});
  // Latest highlights for auto-submit (timer expiry snapshots the current marks).
  const highlightsRef = useRef<Record<number, Highlight[]>>({});
  highlightsRef.current = pdfHighlights;
  // Last value persisted to the server — avoids redundant autosaves. Seeded to an
  // empty map so a scenario with no saved marks doesn't trigger a no-op save.
  const lastSavedHighlightsRef = useRef<string>(serializeHighlights({}));

  const clockKey = `writing-practice-clock:${scenarioId}`;

  const windowSeconds = scenario?.readingTimeSeconds ?? WRITING_READING_WINDOW_SECONDS;

  // Resolve/initialise the exam clock once the scenario has loaded.
  useEffect(() => {
    if (typeof window === 'undefined' || !scenarioId || !scenario) return;

    const readingSecs = scenario.readingTimeSeconds ?? WRITING_READING_WINDOW_SECONDS;
    let readingDeadline: number;
    let writingDeadline: number;

    const stored = sessionStorage.getItem(clockKey);
    let parsed: { reading: number; writing: number } | null = null;
    if (stored) {
      try {
        parsed = JSON.parse(stored) as { reading: number; writing: number };
      } catch {
        parsed = null;
      }
    }

    if (parsed && Number.isFinite(parsed.reading) && Number.isFinite(parsed.writing)) {
      readingDeadline = parsed.reading;
      writingDeadline = parsed.writing;
    } else {
      readingDeadline = Date.now() + readingSecs * 1000;
      writingDeadline = readingDeadline + WRITING_WINDOW_SECONDS * 1000;
      sessionStorage.setItem(
        clockKey,
        JSON.stringify({ reading: readingDeadline, writing: writingDeadline }),
      );
    }

    setReadingDeadlineMs(readingDeadline);
    setWritingDeadlineMs(writingDeadline);

    if (Date.now() < readingDeadline) {
      setPhase('reading');
    } else {
      setPhase('writing');
      startedAtRef.current = readingDeadline;
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [scenarioId, scenario?.id]);

  // ── Scenario + draft load ───────────────────────────────────────────────────
  // Eligibility is checked FIRST, before any task content is fetched — a
  // learner without enough AI grading credits never sees the case notes or
  // starts the reading/writing clock.
  useEffect(() => {
    if (!scenarioId) return;
    let cancelled = false;
    void checkWritingScenarioEligibility(scenarioId)
      .then(() =>
        Promise.all([
          getWritingScenario(scenarioId),
          getWritingDraftV2(scenarioId, mode).catch(() => null),
          // Saved Case Notes highlights persist per (user, scenario) across attempts.
          getWritingHighlights(scenarioId).catch(() => null),
        ]),
      )
      .then(([sc, draft, hl]) => {
        if (cancelled) return;
        setScenario(sc);
        if (draft?.content) {
          setInitialContent(draft.content);
          setContent(draft.content);
          setWordCount(draft.wordCount);
        }
        if (hl?.highlightsJson) {
          const parsed = parseHighlights(hl.highlightsJson);
          setPdfHighlights(parsed);
          // Record what's already on the server so the autosave effect doesn't
          // immediately echo the just-loaded marks back.
          lastSavedHighlightsRef.current = serializeHighlights(parsed);
        }
      })
      .catch((err) => {
        if (cancelled) return;
        if (isInsufficientCreditsError(err)) {
          setInsufficientCreditsMessage(readInsufficientCreditsMessage(err));
          return;
        }
        setError(err instanceof Error ? err.message : t('writing.practice.session.error.load'));
      });
    return () => {
      cancelled = true;
    };
  }, [scenarioId, mode, t]);

  // ── Autosave (writing phase only) ────────────────────────────────────────────
  useEffect(() => {
    if (phase !== 'writing' || !scenarioId) return;
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
  }, [phase, scenarioId, content, wordCount, mode]);

  // ── Highlight autosave (reading + writing) ───────────────────────────────────
  // Persists Case Notes marks per (user, scenario) the moment they change, so
  // they survive refresh and pre-load on every future attempt. Debounced; skips
  // when nothing changed and once the exam is over.
  useEffect(() => {
    if (!scenarioId || phase === 'completed') return;
    const json = serializeHighlights(pdfHighlights);
    if (json === lastSavedHighlightsRef.current) return;
    const timer = window.setTimeout(() => {
      lastSavedHighlightsRef.current = json;
      void putWritingHighlights(scenarioId, json).catch(() => {
        /* best-effort */
      });
    }, 800);
    return () => window.clearTimeout(timer);
  }, [pdfHighlights, scenarioId, phase]);

  const canSubmit = phase === 'writing' && !submitting;

  const helperText = t('writing.practice.session.helper.ready');

  // Shared submit path. `auto` = true when fired by the writing-timer expiry.
  const finalizeSubmit = useCallback(
    async (auto: boolean) => {
      if (submitting || !scenario) return;
      setSubmitting(true);
      setError(null);
      try {
        const elapsed = Math.round((Date.now() - startedAtRef.current) / 1000);
        const submission = await createWritingSubmission({
          scenarioId: scenario.id,
          mode,
          letterContent: contentRef.current,
          wordCount,
          timeSpentSeconds: elapsed,
          inputSource: 'editor',
          caseNoteHighlightsJson: serializeHighlights(highlightsRef.current),
        });
        // Clear the clock so a future retake of this scenario starts fresh.
        if (typeof window !== 'undefined') sessionStorage.removeItem(clockKey);
        router.push(`/writing/submissions/${encodeURIComponent(submission.id)}/grading`);
      } catch (err) {
        // Balance = 0 (spec §9): the AI grading credit pool is exhausted. Surface
        // a dedicated modal with a direct path to the AI Credits storefront rather
        // than a generic inline error. The draft autosaves, so nothing is lost.
        const code = (err as { code?: string }).code;
        const status = (err as { status?: number }).status;
        if (code === 'ai_credits_insufficient' || status === 402) {
          setNoCreditsOpen(true);
        } else if (!auto) {
          setError(err instanceof Error ? err.message : t('writing.practice.session.error.submit'));
        }
        setSubmitting(false);
      }
    },
    [submitting, scenario, wordCount, mode, router, clockKey, t],
  );

  const onSubmit = useCallback(() => {
    if (!canSubmit) return;
    void finalizeSubmit(false);
  }, [canSubmit, finalizeSubmit]);

  // ── Phase transitions ───────────────────────────────────────────────────────
  const beganWritingRef = useRef(false);
  const handleReadingEnd = useCallback(() => {
    if (beganWritingRef.current) return;
    beganWritingRef.current = true;
    startedAtRef.current = Date.now();
    setPhase('writing');
  }, []);

  // Deadline-anchored countdowns; each gated to its active phase.
  const readingSeconds = useDeadlineCountdown(
    phase === 'reading' ? readingDeadlineMs : null,
    { onZero: handleReadingEnd },
  );
  const writingSeconds = useDeadlineCountdown(
    phase === 'writing' ? writingDeadlineMs : null,
    { onZero: () => setPhase('completed') },
  );

  // Hard auto-submit when the 40-minute writing window expires.
  useEffect(() => {
    if (phase === 'completed' && !submitting) {
      void finalizeSubmit(true);
    }
  }, [phase, submitting, finalizeSubmit]);

  const readingActive = phase === 'reading';

  if (insufficientCreditsMessage) {
    return (
      <LearnerDashboardShell pageTitle={t('writing.practice.session.pageTitle')} distractionFree>
        <InsufficientCreditsModal
          open
          message={insufficientCreditsMessage}
          onClose={() => router.push('/writing')}
        />
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell pageTitle={t('writing.practice.session.pageTitle')} distractionFree>
      {/* Forced 5-minute reading window — non-skippable. Auto-closes into the
          writing view at 0:00. */}
      <WritingReadingWindowOverlay
        open={readingActive && !!scenario}
        scenario={scenario}
        secondsRemaining={readingSeconds}
        totalSeconds={windowSeconds}
        allowSkip={false}
        onAutoClose={handleReadingEnd}
        title={scenario?.title ?? undefined}
        highlights={pdfHighlights}
        onHighlightsChange={setPdfHighlights}
      />

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
              <div className="mt-1 flex flex-wrap items-center gap-1">
                <Badge variant="info" size="sm">Practice mode</Badge>
                {scenario ? (
                  <>
                    <Badge variant="muted" size="sm">{scenario.letterType}</Badge>
                    <Badge variant="info" size="sm" className="capitalize">{scenario.profession}</Badge>
                  </>
                ) : null}
                <span className="text-[11px] font-medium text-muted">
                  5 min reading · 40 min writing · auto-submit
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
          {/* Case Notes PDF (with yellow highlighter) or text fallback. */}
          <section
            aria-label={t('writing.practice.session.caseNotesLabel')}
            className="min-h-[60vh] overflow-hidden rounded-2xl border border-border bg-surface"
            dir="ltr"
          >
            <WritingStimulus
              scenario={scenario}
              locked={readingActive}
              title={scenario?.title ?? undefined}
              highlights={pdfHighlights}
              onHighlightsChange={setPdfHighlights}
            />
          </section>

          <section
            aria-label={t('writing.practice.session.editorLabel')}
            className="flex flex-col gap-3 rounded-2xl border border-border bg-surface p-4"
          >
            <WritingEditorV2
              mode={mode}
              initialContent={initialContent}
              disabled={phase !== 'writing'}
              blockPaste
              onChange={(text, words) => {
                setContent(text);
                setWordCount(words);
              }}
              placeholder={t('writing.practice.session.editorPlaceholder')}
              inputId="practice-editor"
            />
          </section>
        </div>

        <SubmitBar
          canSubmit={canSubmit}
          submitLabel={t('writing.practice.session.submit')}
          onSubmit={onSubmit}
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
            cards instantly. Purchase a package to continue. Your draft has been saved.
          </p>
          <div className="flex flex-wrap justify-end gap-2">
            <Button variant="outline" onClick={() => setNoCreditsOpen(false)}>
              Not now
            </Button>
            <Button onClick={() => router.push('/ai-packages')}>
              Buy AI Credits
            </Button>
          </div>
        </div>
      </Modal>
    </LearnerDashboardShell>
  );
}
