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
import {
  beginWritingMockWriting,
  createWritingSubmission,
  getWritingMockSession,
  getWritingScenario,
  putWritingDraftV2,
  submitWritingMock,
} from '@/lib/writing/api';
import { getWritingTask } from '@/lib/writing/exam-api';
import { WRITING_READING_WINDOW_SECONDS, WRITING_WINDOW_SECONDS } from '@/lib/writing/workflow';
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
    caseNoteSections: task.caseNoteSections ?? [],
    caseNotesMarkdown: task.caseNotesMarkdown ?? '',
    writerRole: task.writerRole,
    todayDate: task.todayDate,
    taskPromptMarkdown: task.taskPromptMarkdown,
    recipient: task.recipient,
    fixedInstructions: task.fixedInstructions ?? [],
    wordGuideMin: task.wordGuideMin || 180,
    wordGuideMax: task.wordGuideMax || 200,
  };
}

function buildContentFromScenario(scenario: WritingScenarioDto): PaperBookletContent {
  return {
    title: scenario.title,
    professionLabel: WRITING_PROFESSION_LABELS[scenario.profession] ?? scenario.profession,
    caseNoteSections: [],
    caseNotesMarkdown: scenario.caseNotesMarkdown ?? '',
    writerRole: null,
    todayDate: null,
    taskPromptMarkdown: null,
    recipient: null,
    fixedInstructions: [],
    wordGuideMin: 180,
    wordGuideMax: 200,
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
 *      locally.
 *
 * Either way the visual presentation is the booklet; only the data plumbing
 * differs, mirroring the existing computer-mode mock session page.
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

  const [phase, setPhase] = useState<WritingPhase>('reading');
  const [readingSeconds, setReadingSeconds] = useState(WRITING_READING_WINDOW_SECONDS);
  const [writingSeconds, setWritingSeconds] = useState(WRITING_WINDOW_SECONDS);

  const [text, setText] = useState('');
  const [wordCount, setWordCount] = useState(0);
  const [submitting, setSubmitting] = useState(false);
  const [submitted, setSubmitted] = useState(false);
  const [submissionId, setSubmissionId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const startedAtRef = useRef<number>(Date.now());
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

    const loadTaskOrScenarioContent = async (id: string) => {
      // Prefer the enriched authored task; fall back to plain scenario.
      const task = await getWritingTask(id).catch(() => null);
      if (cancelled) return;
      if (task) {
        setContent(buildContentFromTask(task));
        return;
      }
      const scenario = await getWritingScenario(id).catch(() => null);
      if (cancelled) return;
      if (scenario) setContent(buildContentFromScenario(scenario));
    };

    const run = async () => {
      // 1) Try mock session first.
      const mock = await getWritingMockSession(routeId).catch(() => null);
      if (cancelled) return;

      if (mock) {
        setResolution('mock');
        setSession(mock);
        setScenarioId(mock.scenarioId);
        setReadingSeconds(mock.readingSecondsRemaining);
        setWritingSeconds(mock.writingSecondsRemaining);
        setSubmissionId(mock.submissionId);
        if (mock.status === 'writing') {
          setPhase('writing');
          startedAtRef.current = mock.readingPhaseEndedAt
            ? new Date(mock.readingPhaseEndedAt).getTime()
            : Date.now();
        } else if (mock.status === 'submitted' || mock.status === 'abandoned') {
          setPhase('completed');
          setSubmitted(mock.status === 'submitted');
        } else {
          setPhase('reading');
        }
        if (mock.scenarioId) await loadTaskOrScenarioContent(mock.scenarioId);
        return;
      }

      // 2) Treat the id as a scenario/task id (direct launch).
      setResolution('scenario');
      setScenarioId(routeId);
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

  // ── Local countdown for the active window (mirrors mock session) ──────────
  useEffect(() => {
    if (phase === 'completed' || submitted) return;
    const tick = window.setInterval(() => {
      if (phase === 'reading') setReadingSeconds((s) => Math.max(0, s - 1));
      else setWritingSeconds((s) => Math.max(0, s - 1));
    }, 1000);
    return () => window.clearInterval(tick);
  }, [phase, submitted]);

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

  // ── Phase transition: reading → writing (record server-side for mocks) ────
  const handlePhaseChange = useCallback(
    (next: WritingPhase) => {
      if (next === 'writing' && phase !== 'writing') {
        setPhase('writing');
        startedAtRef.current = Date.now();
        if (resolution === 'mock' && session?.id) {
          void beginWritingMockWriting(session.id)
            .then((updated) => {
              setSession(updated);
              setReadingSeconds(updated.readingSecondsRemaining);
              setWritingSeconds(updated.writingSecondsRemaining);
              startedAtRef.current = updated.readingPhaseEndedAt
                ? new Date(updated.readingPhaseEndedAt).getTime()
                : Date.now();
            })
            .catch((err) => {
              setError(err instanceof Error ? err.message : t('writing.paper.error.startWriting'));
            });
        }
      } else if (next === 'completed') {
        setPhase('completed');
      }
    },
    [phase, resolution, session?.id, t],
  );

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
  useEffect(() => {
    if (phase === 'writing' && writingSeconds <= 0 && !submitted && !expiredRef.current) {
      expiredRef.current = true;
      void doSubmit(textRef.current, wordCountRef.current, { auto: true });
    }
  }, [phase, writingSeconds, submitted, doSubmit]);

  return (
    <LearnerDashboardShell pageTitle={t('writing.paper.pageTitle')} distractionFree>
      <PaperBookletSimulation
        attemptId={routeId}
        scenarioId={scenarioId}
        submissionId={submissionId}
        content={content}
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
        onPhaseChange={handlePhaseChange}
        onSubmit={handleSubmit}
        onAutosave={handleAutosave}
      />
    </LearnerDashboardShell>
  );
}
