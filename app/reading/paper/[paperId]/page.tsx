'use client';

import { Suspense, use, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { AlertCircle, Bookmark, Clock, Loader2, Save, Send } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { Modal } from '@/components/ui/modal';
import { cn } from '@/lib/utils';
import {
  getReadingAttempt,
  getReadingStructureLearner,
  saveReadingAnswer,
  startReadingAttempt,
  submitReadingAttempt,
  type ReadingAttemptStarted,
  type ReadingAttemptStatus,
  type ReadingLearnerStructureDto,
  type ReadingPartCode,
  type ReadingQuestionLearnerDto,
} from '@/lib/reading-authoring-api';
import { ContentLockedNotice, isContentLockedError, readContentLockedMessage } from '@/components/domain/ContentLockedNotice';

type SaveState = 'idle' | 'saving' | 'saved' | 'error';

interface ActiveAttempt {
  attemptId: string;
  startedAt: string;
  deadlineAt: string;
  partADeadlineAt: string;
  partBCDeadlineAt: string;
  paperTitle: string;
  partATimerMinutes: number;
  partBCTimerMinutes: number;
  answeredCount: number;
  canResume: boolean;
  status: ReadingAttemptStatus;
  /** Phase 3: which practice mode this attempt is running under. */
  mode: 'Exam' | 'Learning' | 'Drill' | 'MiniTest' | 'ErrorBank';
  /** Subset modes only — in-scope question IDs (filter the structure to these). */
  scopeQuestionIds: string[] | null;
}

export default function ReadingPaperPlayerPage({ params }: { params: Promise<{ paperId: string }> }) {
  return (
    <Suspense fallback={<LearnerDashboardShell pageTitle="Reading"><Skeleton className="h-64" /></LearnerDashboardShell>}>
      <ReadingPaperPlayerContent params={params} />
    </Suspense>
  );
}

function ReadingPaperPlayerContent({ params }: { params: Promise<{ paperId: string }> }) {
  const { paperId } = use(params);
  const search = useSearchParams();
  const resumeAttemptId = search?.get('attemptId') ?? '';
  // Phase 3: optional URL hint for the practice mode (the canonical source
  // is the resumed attempt itself — we read this only to render
  // pre-resume context like "starting Drill…").
  const urlMode = search?.get('mode') ?? '';
  const router = useRouter();

  const [structure, setStructure] = useState<ReadingLearnerStructureDto | null>(null);
  const [attempt, setAttempt] = useState<ActiveAttempt | null>(null);
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [flagged, setFlagged] = useState<Set<string>>(() => new Set());
  const [loading, setLoading] = useState(true);
  const [starting, setStarting] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [contentLockedMessage, setContentLockedMessage] = useState<string | null>(null);
  const [activePart, setActivePart] = useState<ReadingPartCode>('A');
  const [activeQuestionId, setActiveQuestionId] = useState<string | null>(null);
  const [nowMs, setNowMs] = useState(() => Date.now());
  const [showConfirm, setShowConfirm] = useState(false);
  const [saveState, setSaveState] = useState<SaveState>('idle');
  const [timingNotice, setTimingNotice] = useState<string | null>(null);

  const saveTimers = useRef<Record<string, ReturnType<typeof setTimeout>>>({});
  const autoSubmitTriggered = useRef(false);
  const dirtyQuestionIds = useRef<Set<string>>(new Set());
  const timingState = useRef({ partALocked: false, partBCWindowEnded: false, paperExpired: false });

  useEffect(() => {
    const interval = setInterval(() => setNowMs(Date.now()), 1000);
    return () => clearInterval(interval);
  }, []);

  useEffect(() => () => {
    Object.values(saveTimers.current).forEach(clearTimeout);
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const loadedStructure = await getReadingStructureLearner(paperId);
      setStructure(loadedStructure);

      if (resumeAttemptId) {
        const saved = await getReadingAttempt(resumeAttemptId);
        const restoredAnswers = Object.fromEntries(
          saved.answers.map((answer) => [answer.readingQuestionId, answer.userAnswerJson]),
        );
        setAttempt({
          attemptId: saved.id,
          startedAt: saved.startedAt,
          deadlineAt: saved.deadlineAt ?? saved.partBCDeadlineAt,
          partADeadlineAt: saved.partADeadlineAt,
          partBCDeadlineAt: saved.partBCDeadlineAt,
          paperTitle: loadedStructure.paper.title,
          partATimerMinutes: minutesBetween(saved.startedAt, saved.partADeadlineAt),
          partBCTimerMinutes: Math.max(0, minutesBetween(saved.partADeadlineAt, saved.partBCDeadlineAt)),
          answeredCount: saved.answeredCount,
          canResume: saved.canResume,
          status: saved.status,
          mode: saved.mode,
          scopeQuestionIds: saved.scopeQuestionIds,
        });
        setAnswers(restoredAnswers);
        dirtyQuestionIds.current.clear();
      }
    } catch (err) {
      setError(readErrorMessage(err, 'Failed to load Reading paper.'));
    } finally {
      setLoading(false);
    }
  }, [paperId, resumeAttemptId]);

  useEffect(() => {
    void load();
  }, [load]);

  // Phase 3: when a subset attempt is in progress, restrict the rendered
  // questions to the in-scope set so the player only shows what the
  // grader will actually mark.
  const displayedStructure = useMemo<ReadingLearnerStructureDto | null>(() => {
    if (!structure) return null;
    if (!attempt?.scopeQuestionIds || attempt.scopeQuestionIds.length === 0) return structure;
    const scope = new Set(attempt.scopeQuestionIds);
    return {
      ...structure,
      parts: structure.parts
        .map((part) => ({
          ...part,
          questions: part.questions.filter((q) => scope.has(q.id)),
        }))
        .filter((part) => part.questions.length > 0),
    };
  }, [attempt?.scopeQuestionIds, structure]);

  const displayedCurrentPart = useMemo(
    () => displayedStructure?.parts.find((part) => part.partCode === activePart) ?? null,
    [activePart, displayedStructure],
  );
  const firstDisplayedPart = displayedStructure?.parts.find((part) => part.questions.length > 0) ?? null;

  const questionPartById = useMemo(() => {
    const map = new Map<string, ReadingPartCode>();
    structure?.parts.forEach((part) => {
      part.questions.forEach((question) => map.set(question.id, part.partCode));
    });
    return map;
  }, [structure]);

  useEffect(() => {
    const part = displayedCurrentPart?.questions.length ? displayedCurrentPart : firstDisplayedPart;
    if (!part?.questions.length) return;
    if (activePart !== part.partCode) {
      setActivePart(part.partCode);
    }
    if (!activeQuestionId || !part.questions.some((question) => question.id === activeQuestionId)) {
      setActiveQuestionId(part.questions[0].id);
    }
  }, [activePart, activeQuestionId, displayedCurrentPart, firstDisplayedPart]);

  const isPracticeMode = attempt !== null && attempt.mode !== 'Exam';
  const totalQuestions = useMemo(() => {
    if (!structure) return 0;
    if (attempt?.scopeQuestionIds && attempt.scopeQuestionIds.length > 0) {
      const scope = new Set(attempt.scopeQuestionIds);
      return structure.parts.reduce(
        (sum, part) => sum + part.questions.filter((q) => scope.has(q.id)).length,
        0,
      );
    }
    return structure.parts.reduce((sum, part) => sum + part.questions.length, 0);
  }, [attempt?.scopeQuestionIds, structure]);
  const answeredCount = Object.values(answers).filter(isAnsweredJson).length;
  const unansweredCount = Math.max(0, totalQuestions - answeredCount);
  // Practice modes ignore the Part-A hard lock, but their own timer still
  // controls autosave, input locking, and auto-submit.
  const partALocked = !isPracticeMode
    && Boolean(attempt && nowMs > new Date(attempt.partADeadlineAt).getTime());
  const partBCWindowEnded = Boolean(attempt && nowMs > new Date(attempt.partBCDeadlineAt).getTime());
  const paperExpired = Boolean(attempt && nowMs > new Date(attempt.deadlineAt).getTime());
  const attemptInputsLocked = partBCWindowEnded || paperExpired;

  useEffect(() => {
    timingState.current = { partALocked, partBCWindowEnded, paperExpired };
  }, [paperExpired, partALocked, partBCWindowEnded]);

  useEffect(() => {
    if (!attempt || !partALocked) {
      setTimingNotice(null);
      return;
    }

    if (activePart === 'A') setActivePart('B');
    setTimingNotice('Part A is locked. Parts B and C are now active.');
  }, [activePart, attempt, partALocked]);

  const start = async () => {
    setStarting(true);
    setError(null);
    setContentLockedMessage(null);
    try {
      const started = await startReadingAttempt(paperId);
      setAttempt(fromStartedAttempt(started));
      setAnswers({});
      setFlagged(new Set());
      autoSubmitTriggered.current = false;
      dirtyQuestionIds.current.clear();
      setTimingNotice(null);
      setActivePart('A');
    } catch (err) {
      if (isContentLockedError(err)) {
        setContentLockedMessage(readContentLockedMessage(err));
      } else {
        setError(readErrorMessage(err, 'Could not start Reading attempt.'));
      }
    } finally {
      setStarting(false);
    }
  };

  const persistAnswer = useCallback(async (questionId: string, valueJson: string) => {
    if (!attempt) return;
    const questionPart = questionPartById.get(questionId);
    const currentTiming = timingState.current;
    if ((questionPart === 'A' && currentTiming.partALocked) || currentTiming.partBCWindowEnded || currentTiming.paperExpired) {
      dirtyQuestionIds.current.delete(questionId);
      setSaveState('saved');
      return;
    }

    setSaveState('saving');
    try {
      await saveReadingAnswer(attempt.attemptId, questionId, valueJson);
      dirtyQuestionIds.current.delete(questionId);
      setSaveState('saved');
    } catch (err) {
      setSaveState('error');
      setError(readErrorMessage(err, 'Autosave failed.'));
    }
  }, [attempt, questionPartById]);

  const setAnswer = (question: ReadingQuestionLearnerDto, value: unknown) => {
    if (!attempt || isQuestionLocked(activePart, partALocked, attemptInputsLocked)) return;

    const json = JSON.stringify(value);
    setAnswers((prev) => ({ ...prev, [question.id]: json }));
    dirtyQuestionIds.current.add(question.id);
    setSaveState('saving');
    if (saveTimers.current[question.id]) clearTimeout(saveTimers.current[question.id]);
    const activeDeadlineMs = new Date(activePart === 'A' ? attempt.partADeadlineAt : attempt.partBCDeadlineAt).getTime();
    if (activeDeadlineMs - Date.now() <= 5000) {
      void persistAnswer(question.id, json);
      return;
    }

    saveTimers.current[question.id] = setTimeout(() => {
      void persistAnswer(question.id, json);
    }, 400);
  };

  const submit = useCallback(async () => {
    if (!attempt) return;
    setSubmitting(true);
    setError(null);
    try {
      Object.values(saveTimers.current).forEach(clearTimeout);
      const lockedQuestionIds: string[] = [];
      const answersToFlush = Object.entries(answers).filter(([questionId]) => {
        if (!dirtyQuestionIds.current.has(questionId)) return false;
        const questionPart = questionPartById.get(questionId);
        if ((questionPart === 'A' && partALocked) || partBCWindowEnded || paperExpired) {
          lockedQuestionIds.push(questionId);
          return false;
        }
        return true;
      });

      lockedQuestionIds.forEach((questionId) => dirtyQuestionIds.current.delete(questionId));
      await Promise.all(answersToFlush.map(([questionId, valueJson]) =>
        saveReadingAnswer(attempt.attemptId, questionId, valueJson)));
      answersToFlush.forEach(([questionId]) => dirtyQuestionIds.current.delete(questionId));
      await submitReadingAttempt(attempt.attemptId);
      router.push(`/reading/paper/${paperId}/results?attemptId=${attempt.attemptId}`);
    } catch (err) {
      setError(readErrorMessage(err, 'Could not submit Reading attempt.'));
    } finally {
      setSubmitting(false);
    }
  }, [answers, attempt, paperId, paperExpired, partALocked, partBCWindowEnded, questionPartById, router]);

  useEffect(() => {
    if (!attempt || attempt.status !== 'InProgress' || !partBCWindowEnded || autoSubmitTriggered.current) return;
    autoSubmitTriggered.current = true;
    void submit();
  }, [attempt, partBCWindowEnded, submit]);

  if (loading) {
    return <LearnerDashboardShell pageTitle="Reading"><Skeleton className="h-64" /></LearnerDashboardShell>;
  }

  if (contentLockedMessage) {
    return (
      <LearnerDashboardShell pageTitle="Reading" backHref="/reading">
        <ContentLockedNotice message={contentLockedMessage} />
      </LearnerDashboardShell>
    );
  }

  if (!structure) {
    return (
      <LearnerDashboardShell pageTitle="Reading" backHref="/reading">
        <InlineAlert variant="error">{error ?? 'Paper not found.'}</InlineAlert>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell pageTitle={structure.paper.title} backHref="/reading">
      <main className="space-y-5">
        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
        {timingNotice ? <InlineAlert variant="warning">{timingNotice}</InlineAlert> : null}
        <div className="md:hidden">
          <InlineAlert variant="warning">Full Reading exam mode is designed for a tablet or desktop-sized screen.</InlineAlert>
        </div>

        {!attempt ? (
          <section className="rounded-[20px] border border-border bg-surface px-5 py-8 text-center shadow-sm">
            <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-2xl bg-info/10 text-info">
              <Clock className="h-6 w-6" />
            </div>
            <h1 className="mt-4 text-2xl font-semibold tracking-tight text-navy">{structure.paper.title}</h1>
            <p className="mx-auto mt-2 max-w-2xl text-sm leading-6 text-muted">
              {urlMode && urlMode !== 'exam'
                ? `Loading your ${urlMode.replace('-', ' ')} practice attempt…`
                : 'Start a server-authoritative Reading attempt. Part A locks after its window, then Parts B and C share the remaining timer.'}
            </p>
            {!urlMode || urlMode === 'exam' ? (
              <div className="mt-5 flex justify-center">
                <Button variant="primary" onClick={() => void start()} loading={starting}>
                  Start attempt
                </Button>
              </div>
            ) : null}
          </section>
        ) : (
          <>
            {isSubsetPracticeMode(attempt.mode) ? (
              <InlineAlert variant="info">
                <strong>{practiceModeLabel(attempt.mode)} — practice only.</strong>{' '}
                This attempt does not produce an OET 0–500 scaled score and does not consume an exam attempt.
              </InlineAlert>
            ) : null}
            <AttemptToolbar
              attempt={attempt}
              activePart={activePart}
              nowMs={nowMs}
              answeredCount={answeredCount}
              totalQuestions={totalQuestions}
              saveState={saveState}
              paperExpired={partBCWindowEnded || paperExpired}
              partALocked={partALocked}
              submitting={submitting}
              onSubmit={() => setShowConfirm(true)}
            />

            <PartTabs
              structure={displayedStructure ?? structure}
              activePart={activePart}
              answers={answers}
              flagged={flagged}
              partALocked={partALocked}
              onChange={(part) => setActivePart(part)}
            />

            {displayedCurrentPart ? (
              <PartBody
                part={displayedCurrentPart}
                answers={answers}
                flagged={flagged}
                activeQuestionId={activeQuestionId}
                locked={attemptInputsLocked || (displayedCurrentPart.partCode === 'A' && partALocked)}
                onActiveQuestionChange={setActiveQuestionId}
                onToggleFlag={(questionId) => setFlagged((prev) => toggleSetValue(prev, questionId))}
                onAnswerChange={setAnswer}
              />
            ) : null}
          </>
        )}

        <Modal open={showConfirm} onClose={() => setShowConfirm(false)} title="Submit Reading attempt?">
          <div className="space-y-4">
            <p className="text-sm leading-6 text-muted">
              You have answered <strong className="text-navy">{answeredCount}</strong> of{' '}
              <strong className="text-navy">{totalQuestions}</strong> questions. Unanswered questions score zero.
            </p>
            {unansweredCount > 0 ? (
              <InlineAlert variant="warning">
                {unansweredCount} unanswered question{unansweredCount === 1 ? '' : 's'} will score zero if you submit now.
              </InlineAlert>
            ) : null}
          </div>
          <div className="mt-5 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
            <Button variant="ghost" onClick={() => setShowConfirm(false)}>Keep working</Button>
            <Button variant="primary" onClick={() => { setShowConfirm(false); void submit(); }} loading={submitting}>
              Submit now
            </Button>
          </div>
        </Modal>
      </main>
    </LearnerDashboardShell>
  );
}

function AttemptToolbar({
  attempt,
  activePart,
  nowMs,
  answeredCount,
  totalQuestions,
  saveState,
  paperExpired,
  partALocked,
  submitting,
  onSubmit,
}: {
  attempt: ActiveAttempt;
  activePart: ReadingPartCode;
  nowMs: number;
  answeredCount: number;
  totalQuestions: number;
  saveState: SaveState;
  paperExpired: boolean;
  partALocked: boolean;
  submitting: boolean;
  onSubmit: () => void;
}) {
  const activeDeadline = attempt.mode === 'Exam' && activePart === 'A'
    ? attempt.partADeadlineAt
    : attempt.partBCDeadlineAt;
  const secondsLeft = Math.max(0, Math.floor((new Date(activeDeadline).getTime() - nowMs) / 1000));
  const timerLabel = attempt.mode === 'Exam'
    ? (activePart === 'A' ? 'Part A window' : 'B/C shared window')
    : 'Practice timer';

  return (
    <section className="rounded-[20px] border border-border bg-surface p-4 shadow-sm" aria-label="Attempt status">
      <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
        <div className="flex flex-wrap items-center gap-3">
          <div
            className="flex items-center gap-2 rounded-xl bg-background-light px-3 py-2"
            role="timer"
            aria-live="polite"
            aria-atomic="true"
            aria-label={`${timerLabel}, ${formatCountdown(secondsLeft)} remaining`}
          >
            <Clock className="h-4 w-4 text-primary" aria-hidden="true" />
            <span className="text-xs font-bold uppercase tracking-[0.14em] text-muted">{timerLabel}</span>
            <span className="font-mono text-base font-bold text-navy">{formatCountdown(secondsLeft)}</span>
          </div>
          {partALocked ? <Badge variant="warning">Part A locked</Badge> : null}
          {paperExpired ? <Badge variant="danger">Time expired</Badge> : null}
          <span className="text-sm font-semibold text-muted" aria-label={`${answeredCount} of ${totalQuestions} questions answered`}>
            {answeredCount}/{totalQuestions} answered
          </span>
        </div>

        <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
          <SaveStatus state={saveState} />
          <Button variant="primary" onClick={onSubmit} loading={submitting} aria-label="Submit attempt for grading">
            <Send className="h-4 w-4" aria-hidden="true" />
            Submit
          </Button>
        </div>
      </div>
    </section>
  );
}

function SaveStatus({ state }: { state: SaveState }) {
  const label = {
    idle: 'Autosave ready',
    saving: 'Saving...',
    saved: 'Saved',
    error: 'Save failed',
  }[state];
  const Icon = state === 'saving' ? Loader2 : state === 'error' ? AlertCircle : Save;

  return (
    <span
      className={cn(
        'inline-flex items-center gap-2 text-sm font-semibold',
        state === 'error' ? 'text-danger' : 'text-muted',
      )}
      role="status"
      aria-live="polite"
    >
      <Icon className={cn('h-4 w-4', state === 'saving' && 'animate-spin')} aria-hidden="true" />
      {label}
    </span>
  );
}

function PartTabs({
  structure,
  activePart,
  answers,
  flagged,
  partALocked,
  onChange,
}: {
  structure: ReadingLearnerStructureDto;
  activePart: ReadingPartCode;
  answers: Record<string, string>;
  flagged: Set<string>;
  partALocked: boolean;
  onChange: (part: ReadingPartCode) => void;
}) {
  return (
    <div
      className="flex gap-2 overflow-x-auto border-b border-border"
      role="tablist"
      aria-label="Reading parts"
    >
      {structure.parts.map((part) => {
        const answered = part.questions.filter((question) => isAnsweredJson(answers[question.id])).length;
        const flaggedCount = part.questions.filter((question) => flagged.has(question.id)).length;
        const isActive = activePart === part.partCode;
        const isLocked = part.partCode === 'A' && partALocked;
        const partALabel = isLocked ? ' (locked)' : '';
        return (
          <button
            key={part.partCode}
            type="button"
            role="tab"
            id={`reading-part-tab-${part.partCode}`}
            aria-controls={`reading-part-panel-${part.partCode}`}
            aria-selected={isActive}
            aria-label={`Part ${part.partCode}, ${answered} of ${part.questions.length} answered${flaggedCount ? `, ${flaggedCount} flagged` : ''}${partALabel}`}
            tabIndex={isActive ? 0 : -1}
            onClick={() => onChange(part.partCode)}
            disabled={isLocked}
            className={cn(
              'min-h-11 shrink-0 border-b-2 px-4 py-2 text-left text-sm font-bold transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary',
              isActive
                ? 'border-primary text-primary'
                : 'border-transparent text-muted hover:text-navy',
              isLocked && 'cursor-not-allowed opacity-60 hover:text-muted',
            )}
          >
            <span>Part {part.partCode}</span>
            <span className="ml-2 text-xs font-semibold text-muted" aria-hidden="true">{answered}/{part.questions.length}</span>
            {flaggedCount ? <span className="ml-2 text-xs text-warning" aria-hidden="true">{flaggedCount} flagged</span> : null}
            {isLocked ? <span className="ml-2 text-xs text-danger" aria-hidden="true">locked</span> : null}
          </button>
        );
      })}
    </div>
  );
}

function PartBody({
  part,
  answers,
  flagged,
  activeQuestionId,
  locked,
  onActiveQuestionChange,
  onToggleFlag,
  onAnswerChange,
}: {
  part: ReadingLearnerStructureDto['parts'][number];
  answers: Record<string, string>;
  flagged: Set<string>;
  activeQuestionId: string | null;
  locked: boolean;
  onActiveQuestionChange: (questionId: string) => void;
  onToggleFlag: (questionId: string) => void;
  onAnswerChange: (question: ReadingQuestionLearnerDto, value: unknown) => void;
}) {
  const activeQuestion = part.questions.find((question) => question.id === activeQuestionId) ?? part.questions[0];

  return (
    <div
      className="grid grid-cols-1 gap-4 xl:grid-cols-[minmax(0,1.1fr)_minmax(360px,0.9fr)]"
      role="tabpanel"
      id={`reading-part-panel-${part.partCode}`}
      aria-labelledby={`reading-part-tab-${part.partCode}`}
    >
      <section
        className="max-h-[72vh] overflow-y-auto rounded-[20px] border border-border bg-surface p-5 shadow-sm"
        aria-label={`Reading passages for Part ${part.partCode}`}
      >
        <div className="mb-4 flex items-center justify-between gap-3">
          <h2 className="text-sm font-black uppercase tracking-[0.18em] text-muted">Passages</h2>
          <Badge variant="muted">Part {part.partCode}</Badge>
        </div>
        <div className="space-y-6">
          {part.texts.map((text) => (
            <article key={text.id} className="space-y-2">
              <div>
                <h3 className="text-base font-bold text-navy">{text.title}</h3>
                {text.source ? <p className="text-xs font-semibold text-muted">Source: {text.source}</p> : null}
              </div>
              <div
                className="prose prose-sm max-w-none text-navy"
                dangerouslySetInnerHTML={{ __html: text.bodyHtml }}
              />
            </article>
          ))}
        </div>
      </section>

      <section
        className="rounded-[20px] border border-border bg-surface p-5 shadow-sm"
        aria-label={`Questions for Part ${part.partCode}`}
      >
        <div className="mb-4 flex flex-col gap-3">
          <div className="flex items-center justify-between gap-3">
            <h2 className="text-sm font-black uppercase tracking-[0.18em] text-muted">Questions</h2>
            {locked ? <Badge variant="warning">Inputs locked</Badge> : null}
          </div>
          <QuestionNavigator
            questions={part.questions}
            answers={answers}
            flagged={flagged}
            activeQuestionId={activeQuestion?.id ?? null}
            onSelect={onActiveQuestionChange}
          />
        </div>

        {activeQuestion ? (
          <QuestionInput
            question={activeQuestion}
            texts={part.texts}
            valueJson={answers[activeQuestion.id] ?? ''}
            flagged={flagged.has(activeQuestion.id)}
            locked={locked}
            onToggleFlag={() => onToggleFlag(activeQuestion.id)}
            onChange={(value) => onAnswerChange(activeQuestion, value)}
          />
        ) : null}
      </section>
    </div>
  );
}

function QuestionNavigator({
  questions,
  answers,
  flagged,
  activeQuestionId,
  onSelect,
}: {
  questions: ReadingQuestionLearnerDto[];
  answers: Record<string, string>;
  flagged: Set<string>;
  activeQuestionId: string | null;
  onSelect: (questionId: string) => void;
}) {
  const handleKeyDown = useCallback(
    (event: React.KeyboardEvent<HTMLDivElement>) => {
      // Arrow-key navigation between question buttons. Left/Up = previous,
      // Right/Down = next, Home = first, End = last. Wraps at the ends.
      const key = event.key;
      if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End'].includes(key))
        return;
      if (questions.length === 0) return;
      event.preventDefault();
      const currentIndex = Math.max(0, questions.findIndex((q) => q.id === activeQuestionId));
      let nextIndex = currentIndex;
      if (key === 'ArrowLeft' || key === 'ArrowUp') {
        nextIndex = (currentIndex - 1 + questions.length) % questions.length;
      } else if (key === 'ArrowRight' || key === 'ArrowDown') {
        nextIndex = (currentIndex + 1) % questions.length;
      } else if (key === 'Home') {
        nextIndex = 0;
      } else if (key === 'End') {
        nextIndex = questions.length - 1;
      }
      const next = questions[nextIndex];
      if (next) onSelect(next.id);
    },
    [questions, activeQuestionId, onSelect],
  );

  return (
    <div
      className="grid grid-cols-[repeat(auto-fill,minmax(42px,1fr))] gap-2"
      role="group"
      aria-label="Question navigator (use arrow keys to move between questions)"
      onKeyDown={handleKeyDown}
    >
      {questions.map((question) => {
        const answered = isAnsweredJson(answers[question.id]);
        const isActive = question.id === activeQuestionId;
        const isFlagged = flagged.has(question.id);
        return (
          <button
            key={question.id}
            type="button"
            onClick={() => onSelect(question.id)}
            tabIndex={isActive ? 0 : -1}
            aria-current={isActive ? 'true' : undefined}
            aria-label={`Question ${question.displayOrder}${answered ? ', answered' : ', unanswered'}${isFlagged ? ', flagged' : ''}`}
            className={cn(
              'relative min-h-11 rounded-lg border text-sm font-bold transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary',
              isActive ? 'border-primary bg-primary text-white' : 'border-border bg-background-light text-navy hover:border-primary/40',
              answered && !isActive && 'border-success/30 bg-success/10 text-success',
              isFlagged && !isActive && 'border-warning/30 bg-warning/10 text-warning',
            )}
          >
            {question.displayOrder}
            {isFlagged ? <Bookmark className="absolute right-1 top-1 h-3 w-3 fill-current" aria-hidden="true" /> : null}
          </button>
        );
      })}
    </div>
  );
}

function QuestionInput({
  question,
  texts,
  valueJson,
  flagged,
  locked,
  onToggleFlag,
  onChange,
}: {
  question: ReadingQuestionLearnerDto;
  texts: ReadingLearnerStructureDto['parts'][number]['texts'];
  valueJson: string;
  flagged: boolean;
  locked: boolean;
  onToggleFlag: () => void;
  onChange: (value: unknown) => void;
}) {
  const current = useMemo(() => parseAnswer(valueJson), [valueJson]);

  return (
    <div className="space-y-5">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-xs font-black uppercase tracking-[0.16em] text-muted">Question {question.displayOrder}</p>
          <h3 className="mt-2 text-base font-semibold leading-7 text-navy">{question.stem}</h3>
        </div>
        <Button variant="ghost" size="sm" onClick={onToggleFlag} aria-pressed={flagged}>
          <Bookmark className={cn('h-4 w-4', flagged && 'fill-current text-warning')} />
          {flagged ? 'Flagged' : 'Flag'}
        </Button>
      </div>

      {question.questionType === 'MultipleChoice3' || question.questionType === 'MultipleChoice4' ? (
        <McqControl question={question} current={current} locked={locked} onChange={onChange} />
      ) : question.questionType === 'MatchingTextReference' ? (
        <MatchingControl question={question} texts={texts} current={current} locked={locked} onChange={onChange} />
      ) : (
        <TextAnswerControl current={current} locked={locked} onChange={onChange} />
      )}
    </div>
  );
}

function McqControl({
  question,
  current,
  locked,
  onChange,
}: {
  question: ReadingQuestionLearnerDto;
  current: unknown;
  locked: boolean;
  onChange: (value: unknown) => void;
}) {
  const options = toOptionList(question.options);

  return (
    <div className="space-y-2">
      {options.map((option, index) => {
        const letter = option.value || String.fromCharCode(65 + index);
        return (
          <label
            key={`${letter}-${option.label}`}
            className={cn(
              'flex min-h-11 cursor-pointer items-start gap-3 rounded-lg border border-border bg-background-light p-3 text-sm transition-colors',
              current === letter && 'border-primary bg-primary/5',
              locked && 'cursor-not-allowed opacity-70',
            )}
          >
            <input
              type="radio"
              name={question.id}
              className="mt-1"
              disabled={locked}
              checked={current === letter}
              onChange={() => onChange(letter)}
            />
            <span className="font-mono font-bold text-navy">{letter}.</span>
            <span className="leading-6 text-navy">{option.label}</span>
          </label>
        );
      })}
    </div>
  );
}

function MatchingControl({
  question,
  texts,
  current,
  locked,
  onChange,
}: {
  question: ReadingQuestionLearnerDto;
  texts: ReadingLearnerStructureDto['parts'][number]['texts'];
  current: unknown;
  locked: boolean;
  onChange: (value: unknown) => void;
}) {
  const options = toMatchingOptions(question.options, texts);
  const multi = question.points > 1 || Array.isArray(current);
  const selected = Array.isArray(current)
    ? current.map(String)
    : typeof current === 'string' && current ? [current] : [];

  const toggle = (value: string) => {
    if (!multi) {
      onChange(value);
      return;
    }
    const next = selected.includes(value)
      ? selected.filter((item) => item !== value)
      : [...selected, value];
    onChange(next);
  };

  return (
    <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
      {options.map((option) => {
        const isSelected = selected.includes(option.value);
        return (
          <button
            key={option.value}
            type="button"
            disabled={locked}
            onClick={() => toggle(option.value)}
            className={cn(
              'min-h-12 rounded-lg border border-border bg-background-light px-3 py-2 text-left transition-colors disabled:cursor-not-allowed disabled:opacity-70',
              isSelected && 'border-primary bg-primary/5',
            )}
          >
            <span className="block text-sm font-bold text-navy">Text {option.value}</span>
            {option.label ? <span className="block text-xs text-muted">{option.label}</span> : null}
          </button>
        );
      })}
    </div>
  );
}

function TextAnswerControl({
  current,
  locked,
  onChange,
}: {
  current: unknown;
  locked: boolean;
  onChange: (value: unknown) => void;
}) {
  return (
    <input
      className="min-h-11 w-full rounded-lg border border-border bg-background-light px-3 py-2 text-sm text-navy outline-none transition focus:border-primary focus:ring-2 focus:ring-primary/20 disabled:cursor-not-allowed disabled:opacity-70"
      placeholder="Type your answer"
      disabled={locked}
      value={typeof current === 'string' ? current : ''}
      onChange={(event) => onChange(event.target.value)}
    />
  );
}

function fromStartedAttempt(started: ReadingAttemptStarted): ActiveAttempt {
  return {
    attemptId: started.attemptId,
    startedAt: started.startedAt,
    deadlineAt: started.deadlineAt,
    partADeadlineAt: started.partADeadlineAt,
    partBCDeadlineAt: started.partBCDeadlineAt,
    paperTitle: started.paperTitle,
    partATimerMinutes: started.partATimerMinutes,
    partBCTimerMinutes: started.partBCTimerMinutes,
    answeredCount: started.answeredCount,
    canResume: started.canResume,
    status: 'InProgress',
    // The /attempts/{id} POST returns the full canonical attempt; mode is
    // always Exam at this entry point. Practice modes are launched via the
    // dedicated practice endpoints which redirect to this page with
    // ?attemptId=… so the resume path picks up `mode` from the GET.
    mode: 'Exam',
    scopeQuestionIds: null,
  };
}

function isQuestionLocked(activePart: ReadingPartCode, partALocked: boolean, paperExpired: boolean) {
  return paperExpired || (activePart === 'A' && partALocked);
}

function practiceModeLabel(mode: 'Exam' | 'Learning' | 'Drill' | 'MiniTest' | 'ErrorBank'): string {
  switch (mode) {
    case 'Learning': return 'Learning Mode';
    case 'Drill': return 'Skill Drill';
    case 'MiniTest': return 'Mini-Test';
    case 'ErrorBank': return 'Error Bank Retest';
    default: return 'Practice';
  }
}

function isSubsetPracticeMode(mode: 'Exam' | 'Learning' | 'Drill' | 'MiniTest' | 'ErrorBank') {
  return mode === 'Drill' || mode === 'MiniTest' || mode === 'ErrorBank';
}

function toOptionList(options: unknown): Array<{ value: string; label: string }> {
  if (!Array.isArray(options)) return [];
  return options.map((option, index) => {
    if (typeof option === 'string') return { value: String.fromCharCode(65 + index), label: option };
    if (typeof option === 'number') return { value: String.fromCharCode(65 + index), label: String(option) };
    if (option && typeof option === 'object') {
      const record = option as Record<string, unknown>;
      return {
        value: String(record.value ?? record.key ?? record.letter ?? String.fromCharCode(65 + index)),
        label: String(record.label ?? record.text ?? record.title ?? record.value ?? ''),
      };
    }
    return { value: String.fromCharCode(65 + index), label: String(option ?? '') };
  });
}

function toMatchingOptions(
  options: unknown,
  texts: ReadingLearnerStructureDto['parts'][number]['texts'],
): Array<{ value: string; label: string }> {
  const parsed = toOptionList(options).map((option, index) => ({
    value: option.value || String(index + 1),
    label: option.label,
  }));
  if (parsed.length > 0) return parsed;
  return texts.map((text) => ({ value: String(text.displayOrder), label: text.title }));
}

function parseAnswer(valueJson: string): unknown {
  if (!valueJson) return null;
  try {
    return JSON.parse(valueJson);
  } catch {
    return valueJson;
  }
}

function isAnsweredJson(valueJson: string | undefined): boolean {
  const value = parseAnswer(valueJson ?? '');
  if (Array.isArray(value)) return value.length > 0;
  return typeof value === 'string' ? value.trim().length > 0 : value !== null && value !== undefined;
}

function toggleSetValue(source: Set<string>, value: string): Set<string> {
  const next = new Set(source);
  if (next.has(value)) next.delete(value);
  else next.add(value);
  return next;
}

function formatCountdown(totalSec: number): string {
  const minutes = Math.floor(totalSec / 60);
  const seconds = totalSec % 60;
  return `${minutes}:${String(seconds).padStart(2, '0')}`;
}

function minutesBetween(start: string, end: string): number {
  return Math.max(0, Math.round((new Date(end).getTime() - new Date(start).getTime()) / 60000));
}

function readErrorMessage(err: unknown, fallback: string): string {
  const detail = (err as { detail?: { message?: string; error?: string } })?.detail;
  return detail?.message ?? detail?.error ?? (err instanceof Error ? err.message : fallback);
}
