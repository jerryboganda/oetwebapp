'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { useParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import {
  PaperBookletSimulation,
  type PaperBookletContent,
  type WritingPhase,
} from '@/components/domain/writing/PaperBookletSimulation';
import { WritingReadingWindowOverlay } from '@/components/domain/writing/WritingReadingWindowOverlay';
import type { Highlight } from '@/components/domain/writing/WritingStimulusViewer';
import {
  beginWritingMockWriting,
  createWritingSubmission,
  getWritingHighlights,
  getWritingMockSession,
  getWritingScenario,
  putWritingDraftV2,
  putWritingHighlights,
  submitWritingMock,
} from '@/lib/writing/api';
import { parseHighlights, serializeHighlights } from '@/lib/writing/highlights';
import { getWritingTask } from '@/lib/writing/exam-api';
import { useDeadlineCountdown } from '@/lib/writing/useCountdown';
import {
  WRITING_READING_WINDOW_SECONDS,
  WRITING_WINDOW_SECONDS,
} from '@/lib/writing/workflow';
import {
  WRITING_PROFESSION_LABELS,
  type WritingMockSessionDto,
  type WritingScenarioDto,
  type WritingTaskDto,
} from '@/lib/writing/types';

const MIN_WORDS = 100;

/**
 * Build booklet content from the richest source available. The enriched
 * authored task gives structured case-note sections, recipient, fixed
 * instructions, and a word guide; the plain scenario is the markdown-only
 * fallback (case notes only) when no task is available for the id.
 */
function buildContentFromTask(task: WritingTaskDto): PaperBookletContent {
  return {
    title: task.title,
    professionLabel: WRITING_PROFESSION_LABELS[task.profession] ?? task.profession,
    writerRole: task.writerRole,
    todayDate: task.todayDate,
    taskPromptMarkdown: task.taskPromptMarkdown,
    fixedInstructions: task.fixedInstructions ?? [],
    wordGuideMin: task.wordGuideMin || 180,
    wordGuideMax: task.wordGuideMax || 200,
  };
}

function buildContentFromScenario(scenario: WritingScenarioDto): PaperBookletContent {
  return {
    title: scenario.title,
    professionLabel: WRITING_PROFESSION_LABELS[scenario.profession] ?? scenario.profession,
    writerRole: null,
    todayDate: null,
    taskPromptMarkdown: scenario.taskPromptMarkdown ?? null,
    fixedInstructions: scenario.fixedInstructions ?? [],
    wordGuideMin: scenario.wordGuideMin ?? 180,
    wordGuideMax: scenario.wordGuideMax ?? 200,
  };
}

/**
 * The reading-window overlay's body renders `<WritingStimulus scenario>`, which
 * only reads `stimulusPdfDownloadPath` (→ real PDF) or the case-note text
 * fields (→ printed fallback). When the route resolves only to an authored task
 * we synthesise a minimal scenario-shaped object from it rather than firing a
 * second `getWritingScenario` request: the task already carries everything the
 * stimulus needs, and a task id is NOT guaranteed to also resolve as a scenario
 * id (the task lookup is a distinct admin endpoint). This keeps a single load
 * path that works whether the id resolves to a task or a scenario.
 */
function scenarioFromTask(task: WritingTaskDto): WritingScenarioDto {
  return {
    id: task.id,
    title: task.title,
    letterType: task.letterType,
    profession: task.profession,
    subDiscipline: null,
    topics: [],
    difficulty: task.difficulty,
    caseNotesStructured: [],
    isDiagnostic: false,
    status: task.status,
    createdAt: task.createdAt,
    updatedAt: task.updatedAt,
    internalCode: task.internalCode,
    taskPromptMarkdown: task.taskPromptMarkdown,
    writerRole: task.writerRole,
    todayDate: task.todayDate,
    fixedInstructions: task.fixedInstructions ?? [],
    wordGuideMin: task.wordGuideMin,
    wordGuideMax: task.wordGuideMax,
    readingTimeSeconds: task.readingTimeSeconds,
    writingTimeSeconds: task.writingTimeSeconds,
    simulationModes: task.simulationModes,
    markingMode: task.markingMode,
    stimulusPdfMediaAssetId: task.stimulusPdfMediaAssetId ?? null,
    stimulusPdfDownloadPath: task.stimulusPdfDownloadPath ?? null,
  };
}

/**
 * PAPER-mode visual exam simulation (spec §9).
 *
 * The route param `[id]` is resolved leniently to match how the learner can
 * arrive here:
 *   1. As a MOCK SESSION id (preferred — same strict lifecycle as the
 *      computer-mode mock: reading→writing transition recorded server-side via
 *      `beginWritingMockWriting`, submit via `submitWritingMock`). This is the
 *      path used when paper mode is launched from the mocks list.
 *   2. As a SCENARIO/TASK id (direct launch). We then create a paper-mode
 *      submission with `createWritingSubmission` on submit and drive the timer
 *      locally (client-only deadline; no server session to anchor against).
 *
 * Either way the visual presentation is the booklet; only the data plumbing
 * differs, mirroring the existing computer-mode mock session page. Timing is
 * deadline-anchored in BOTH cases: the mock path anchors to the server's
 * reading-phase end; the direct path anchors to a deadline fixed when the
 * reading window opens. `WritingReadingWindowOverlay` shows the real PDF during
 * reading (exam simulation — `allowSkip={false}`, no early start).
 */
export default function WritingPaperSessionPage() {
  const t = useTranslations();
  const params = useParams<{ id: string }>();
  const routeId = String(params?.id ?? '');

  // Resolution: which kind of id did we get?
  const [resolution, setResolution] = useState<'pending' | 'mock' | 'scenario'>('pending');
  const [session, setSession] = useState<WritingMockSessionDto | null>(null);
  const [scenarioId, setScenarioId] = useState<string | null>(null);
  const [content, setContent] = useState<PaperBookletContent | null>(null);
  // Scenario-shaped object feeding the reading-window overlay's stimulus body.
  // Resolved from the real scenario when available, else synthesised from the
  // authored task (see scenarioFromTask).
  const [scenario, setScenario] = useState<WritingScenarioDto | null>(null);

  const [phase, setPhase] = useState<WritingPhase>('reading');
  // Deadline-anchored countdowns (wall-clock epoch ms). Survive tab-backgrounding
  // and refresh — `useDeadlineCountdown` re-derives whole seconds on every tick
  // and on tab refocus. Null until the session/window is resolved.
  const [readingDeadlineMs, setReadingDeadlineMs] = useState<number | null>(null);
  const [writingDeadlineMs, setWritingDeadlineMs] = useState<number | null>(null);

  const [text, setText] = useState('');
  const [wordCount, setWordCount] = useState(0);
  const [submitting, setSubmitting] = useState(false);
  const [submitted, setSubmitted] = useState(false);
  const [submissionId, setSubmissionId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  // Case Notes highlights, lifted so they persist across the reading window and
  // the booklet writing view, and so the page can save/restore them per scenario.
  const [pdfHighlights, setPdfHighlights] = useState<Record<number, Highlight[]>>({});
  const highlightsRef = useRef<Record<number, Highlight[]>>({});
  highlightsRef.current = pdfHighlights;
  const lastSavedHighlightsRef = useRef<string>(serializeHighlights({}));

  const startedAtRef = useRef<number>(Date.now());
  // Guard so the reading→writing transition runs at most once: the reading
  // countdown's null-guarded onZero AND the overlay's onAutoClose both fire on
  // the same tick at reading 0:00. Only the first proceeds (mirrors the mock
  // page's begin-writing idempotency).
  const beganWritingRef = useRef(false);
  // Mirror latest content into refs for the timer-expiry auto-submit, which
  // must not re-bind on every keystroke.
  const textRef = useRef('');
  const wordCountRef = useRef(0);
  textRef.current = text;
  wordCountRef.current = wordCount;

  // ── Load + resolve the route id ───────────────────────────────────────────
  useEffect(() => {
    if (!routeId) return;
    let cancelled = false;

    // Resolve the booklet content AND the overlay's scenario object from the
    // richest source: prefer the enriched authored task, fall back to the plain
    // scenario.
    const loadTaskOrScenarioContent = async (id: string) => {
      const task = await getWritingTask(id).catch(() => null);
      if (cancelled) return;
      if (task) {
        setContent(buildContentFromTask(task));
        setScenario(scenarioFromTask(task));
        return;
      }
      const scenarioDto = await getWritingScenario(id).catch(() => null);
      if (cancelled) return;
      if (scenarioDto) {
        setContent(buildContentFromScenario(scenarioDto));
        setScenario(scenarioDto);
      }
    };

    const run = async () => {
      // 1) Try mock session first.
      const mock = await getWritingMockSession(routeId).catch(() => null);
      if (cancelled) return;

      if (mock) {
        setResolution('mock');
        setSession(mock);
        setScenarioId(mock.scenarioId);
        setSubmissionId(mock.submissionId);
        if (mock.status === 'writing') {
          setPhase('writing');
          // Already begun on the server — the transition must not begin again.
          beganWritingRef.current = true;
          setWritingDeadlineMs(
            mock.readingPhaseEndedAt
              ? new Date(mock.readingPhaseEndedAt).getTime() + WRITING_WINDOW_SECONDS * 1000
              : Date.now() + mock.writingSecondsRemaining * 1000,
          );
          startedAtRef.current = mock.readingPhaseEndedAt
            ? new Date(mock.readingPhaseEndedAt).getTime()
            : Date.now();
        } else if (mock.status === 'submitted' || mock.status === 'abandoned') {
          setPhase('completed');
          setSubmitted(mock.status === 'submitted');
        } else {
          setPhase('reading');
          setReadingDeadlineMs(Date.now() + mock.readingSecondsRemaining * 1000);
        }
        if (mock.scenarioId) await loadTaskOrScenarioContent(mock.scenarioId);
        return;
      }

      // 2) Treat the id as a scenario/task id (direct launch). No server session
      //    to anchor against — open a client-only reading window with a deadline
      //    fixed now (mirrors the practice page, but allowSkip is false because
      //    paper mode is an exam simulation).
      setResolution('scenario');
      setScenarioId(routeId);
      setPhase('reading');
      setReadingDeadlineMs(Date.now() + WRITING_READING_WINDOW_SECONDS * 1000);
      await loadTaskOrScenarioContent(routeId);
    };

    void run().catch((err) => {
      if (!cancelled) {
        setError(err instanceof Error ? err.message : t('writing.paper.error.load'));
      }
    });

    return () => {
      cancelled = true;
    };
  }, [routeId, t]);

  // ── beforeunload guard while actively writing (strict) ────────────────────
  useEffect(() => {
    if (phase !== 'writing' || submitted) return;
    const handler = (e: BeforeUnloadEvent) => {
      e.preventDefault();
      e.returnValue = '';
    };
    window.addEventListener('beforeunload', handler);
    return () => window.removeEventListener('beforeunload', handler);
  }, [phase, submitted]);

  const resultsHref = `/writing/paper/session/${encodeURIComponent(routeId)}/results`;

  // ── Case Notes highlights: load once per scenario, autosave (debounced) ────
  useEffect(() => {
    if (!scenarioId) return;
    let cancelled = false;
    void getWritingHighlights(scenarioId)
      .then((hl) => {
        if (cancelled || !hl?.highlightsJson) return;
        const parsed = parseHighlights(hl.highlightsJson);
        setPdfHighlights(parsed);
        lastSavedHighlightsRef.current = serializeHighlights(parsed);
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, [scenarioId]);

  useEffect(() => {
    if (!scenarioId || submitted) return;
    const json = serializeHighlights(pdfHighlights);
    if (json === lastSavedHighlightsRef.current) return;
    const timer = window.setTimeout(() => {
      lastSavedHighlightsRef.current = json;
      void putWritingHighlights(scenarioId, json).catch(() => {});
    }, 800);
    return () => window.clearTimeout(timer);
  }, [pdfHighlights, scenarioId, submitted]);

  // ── Autosave — page owns the network write (elapsed-time bookkeeping) ──────
  const handleAutosave = useCallback(
    (autoText: string, autoWords: number) => {
      if (!scenarioId) return;
      const elapsed = Math.round((Date.now() - startedAtRef.current) / 1000);
      void putWritingDraftV2(scenarioId, 'mock', {
        content: autoText,
        wordCount: autoWords,
        timeSpentSeconds: elapsed,
      }).catch(() => {
        /* best-effort */
      });
    },
    [scenarioId],
  );

  const handleContentChange = useCallback((next: string, words: number) => {
    setText(next);
    setWordCount(words);
  }, []);

  // ── Phase transition: reading → writing ───────────────────────────────────
  // Idempotent: the reading countdown's onZero AND the overlay's onAutoClose
  // both fire on the same tick at reading 0:00. Only the first proceeds. For the
  // mock path this records the transition server-side; for the direct path it
  // simply anchors the writing deadline locally.
  const beginWriting = useCallback(() => {
    if (beganWritingRef.current) return;
    beganWritingRef.current = true;
    // Provisional deadline BEFORE flipping phase so the writing countdown never
    // momentarily reads 0:00 during the begin-writing round-trip; corrected from
    // the server response below for the mock path.
    setWritingDeadlineMs(Date.now() + WRITING_WINDOW_SECONDS * 1000);
    startedAtRef.current = Date.now();
    setPhase('writing');
    if (resolution === 'mock' && session?.id) {
      void beginWritingMockWriting(session.id)
        .then((updated) => {
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
        })
        .catch((err) => {
          setError(err instanceof Error ? err.message : t('writing.paper.error.startWriting'));
        });
    }
  }, [resolution, session?.id, t]);

  // ── Submit ────────────────────────────────────────────────────────────────
  const doSubmit = useCallback(
    async (submitText: string, submitWords: number, opts: { auto?: boolean } = {}) => {
      if (submitted || submitting) return;
      // Manual submit requires the minimum; auto-submit on expiry sends whatever
      // the candidate produced (even if short) so nothing is lost.
      if (!opts.auto && submitWords < MIN_WORDS) return;
      setSubmitting(true);
      setError(null);
      const elapsed = Math.round((Date.now() - startedAtRef.current) / 1000);
      try {
        if (resolution === 'mock' && session?.id) {
          const result = await submitWritingMock(session.id, {
            letterContent: submitText,
            wordCount: submitWords,
            timeSpentSeconds: elapsed,
          });
          setSubmissionId(result.id ?? null);
        } else if (scenarioId) {
          const result = await createWritingSubmission({
            scenarioId,
            mode: 'mock',
            letterContent: submitText,
            wordCount: submitWords,
            timeSpentSeconds: elapsed,
            inputSource: 'editor',
            caseNoteHighlightsJson: serializeHighlights(highlightsRef.current),
          });
          setSubmissionId(result.id ?? null);
        }
        // Freeze in place — do NOT navigate away (spec: locked booklet + link).
        setSubmitted(true);
        setPhase('completed');
      } catch (err) {
        setError(err instanceof Error ? err.message : t('writing.paper.error.submit'));
        setSubmitting(false);
      }
    },
    [submitted, submitting, resolution, session?.id, scenarioId, t],
  );

  const handleSubmit = useCallback(() => {
    void doSubmit(textRef.current, wordCountRef.current);
  }, [doSubmit]);

  // Auto-submit + lock when the writing window expires.
  const expiredRef = useRef(false);
  const handleWritingExpired = useCallback(() => {
    if (submitted || expiredRef.current) return;
    expiredRef.current = true;
    void doSubmit(textRef.current, wordCountRef.current, { auto: true });
  }, [submitted, doSubmit]);

  // Deadline-anchored seconds. Both hooks run unconditionally (rules of hooks);
  // each gated to its active phase so only the live one counts. The null-guarded
  // onZero fires only for a REAL elapsed deadline — never on the 0 a null
  // deadline yields before load or during the begin-writing round-trip — so
  // transitions cannot fire spuriously. WritingTimerV2 is display-only.
  const readingSeconds = useDeadlineCountdown(
    phase === 'reading' && !submitted ? readingDeadlineMs : null,
    { onZero: beginWriting },
  );
  const writingSeconds = useDeadlineCountdown(
    phase === 'writing' && !submitted ? writingDeadlineMs : null,
    { onZero: handleWritingExpired },
  );

  return (
    <LearnerDashboardShell pageTitle={t('writing.paper.pageTitle')} distractionFree>
      <PaperBookletSimulation
        attemptId={routeId}
        scenarioId={scenarioId}
        submissionId={submissionId}
        content={content}
        stimulus={{ downloadPath: scenario?.stimulusPdfDownloadPath ?? null }}
        phase={phase}
        readingSecondsRemaining={readingSeconds}
        writingSecondsRemaining={writingSeconds}
        loading={resolution === 'pending'}
        error={error}
        submitted={submitted}
        submitting={submitting}
        minWords={MIN_WORDS}
        resultsHref={resultsHref}
        onContentChange={handleContentChange}
        onSubmit={handleSubmit}
        onAutosave={handleAutosave}
        highlights={pdfHighlights}
        onHighlightsChange={setPdfHighlights}
      />

      {/* Forced full-screen reading window. Renders only during the reading
          phase; auto-closes into the writing view at 0:00. allowSkip is false —
          paper mode is always an exam simulation, so there is no early start. */}
      <WritingReadingWindowOverlay
        open={phase === 'reading' && !submitted && !!scenario}
        scenario={scenario}
        secondsRemaining={readingSeconds}
        totalSeconds={scenario?.readingTimeSeconds ?? WRITING_READING_WINDOW_SECONDS}
        allowSkip={false}
        onAutoClose={beginWriting}
        title={scenario?.title ?? content?.title ?? undefined}
        highlights={pdfHighlights}
        onHighlightsChange={setPdfHighlights}
      />
    </LearnerDashboardShell>
  );
}
