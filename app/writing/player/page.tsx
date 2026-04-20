'use client';

import { useState, useEffect, useCallback, useMemo, useRef } from 'react';
import { AnimatePresence, MotionConfig, motion, useReducedMotion } from 'motion/react';
import type { Transition } from 'motion/react';
import { ChevronLeft, Maximize, Minimize } from 'lucide-react';
import { useSearchParams, useRouter } from 'next/navigation';
import { cn } from '@/lib/utils';
import { Button } from '@/components/ui/button';
import { Modal } from '@/components/ui/modal';
import { Timer } from '@/components/ui/timer';
import { WritingCaseNotesPanel } from '@/components/domain/writing-case-notes-panel';
import { WritingEditor } from '@/components/domain/writing-editor';
import { RulebookFindingsPanel } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import {
  ApiError,
  fetchWritingTask,
  fetchWritingChecklist,
  heartbeatWritingAttempt,
  resolveWritingAttempt,
  submitWritingDraft,
  submitWritingTask,
} from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { WritingTask } from '@/lib/mock-data';
import { useIsMobile } from '@/hooks/use-mobile';
import { deriveWritingCaseNotesMarkers, inferWritingLetterType, lintWritingLetter } from '@/lib/rulebook';

type SaveStatus = 'idle' | 'saving' | 'saved' | 'failed';
type ChecklistState = { id: string; label: string; checked: boolean };

const DEFAULT_WRITING_DURATION_SECONDS = 45 * 60;

function checklistToRecord(checklist: ChecklistState[]): Record<string, boolean> {
  return Object.fromEntries(checklist.map((item) => [item.id, item.checked]));
}

function resolveDurationSeconds(task: WritingTask | null): number {
  if (!task) return DEFAULT_WRITING_DURATION_SECONDS;
  if (typeof task.durationSeconds === 'number' && task.durationSeconds > 0) return task.durationSeconds;
  if (typeof task.estimatedDurationMinutes === 'number' && task.estimatedDurationMinutes > 0) {
    return task.estimatedDurationMinutes * 60;
  }
  const minutes = Number(task.time.match(/\d+/)?.[0]);
  return Number.isFinite(minutes) && minutes > 0 ? minutes * 60 : DEFAULT_WRITING_DURATION_SECONDS;
}

function resolveRulebookProfession(task: WritingTask): 'medicine' {
  return task.professionId?.toLowerCase() === 'medicine' || task.profession.toLowerCase() === 'medicine'
    ? 'medicine'
    : 'medicine';
}

export default function WritingPlayer() {
  const searchParams = useSearchParams();
  const router = useRouter();
  const isMobile = useIsMobile();
  const prefersReducedMotion = useReducedMotion();
  const requestedTaskId = searchParams?.get('taskId') ?? undefined;
  const requestedAttemptId = searchParams?.get('attemptId') ?? undefined;
  const taskId = requestedTaskId ?? 'wt-001';

  const [task, setTask] = useState<WritingTask | null>(null);
  const [attemptId, setAttemptId] = useState<string | null>(requestedAttemptId ?? null);
  const [draftVersion, setDraftVersion] = useState<number | null>(null);
  const [checklist, setChecklist] = useState<ChecklistState[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [content, setContent] = useState('');
  const [scratchpad, setScratchpad] = useState('');
  const [saveStatus, setSaveStatus] = useState<SaveStatus>('idle');
  const [saveConflictMessage, setSaveConflictMessage] = useState<string | null>(null);
  const [timerInitialSeconds, setTimerInitialSeconds] = useState(DEFAULT_WRITING_DURATION_SECONDS);
  const [fontSize, setFontSize] = useState(16);
  const [isDistractionFree, setIsDistractionFree] = useState(false);
  const [activeTab, setActiveTab] = useState<'notes' | 'scratchpad' | 'checklist'>('notes');
  const [showSubmitModal, setShowSubmitModal] = useState(false);
  const [showLeaveModal, setShowLeaveModal] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [timerRunning, setTimerRunning] = useState(true);
  const [mobileView, setMobileView] = useState<'notes' | 'editor'>('notes');
  const saveTimerRef = useRef<NodeJS.Timeout | undefined>(undefined);
  const hasUnsavedChanges = useRef(false);
  const latestContentRef = useRef('');
  const latestScratchpadRef = useRef('');
  const latestChecklistRef = useRef<Record<string, boolean>>({});
  const latestDraftVersionRef = useRef<number | null>(null);
  const attemptIdRef = useRef<string | null>(requestedAttemptId ?? null);
  const taskIdRef = useRef(taskId);
  const timerDurationRef = useRef(DEFAULT_WRITING_DURATION_SECONDS);
  const elapsedSecondsRef = useRef(0);
  const isSubmittingRef = useRef(false);
  const autosaveSequenceRef = useRef(0);
  const panelTransition: Transition = prefersReducedMotion
    ? { duration: 0.01 }
    : { type: 'spring', stiffness: 420, damping: 38 };

  const wordCount = useMemo(() => content.trim().split(/\s+/).filter(Boolean).length, [content]);

  const inferredLetterType = useMemo(() => inferWritingLetterType(task ?? {}), [task]);
  const caseNotesMarkers = useMemo(() => deriveWritingCaseNotesMarkers(task?.caseNotes), [task?.caseNotes]);

  const lintFindings = useMemo(() => {
    if (!task || wordCount < 20) return [];
    const minorAgeMatch = task.caseNotes.match(/\b(\d+)\s*(years? old|year-old)\b/i);
    return lintWritingLetter({
      letterText: content,
      letterType: inferredLetterType,
      profession: resolveRulebookProfession(task),
      recipientSpecialty: task.title,
      recipientName: null,
      patientAge: minorAgeMatch ? Number(minorAgeMatch[1]) : null,
      patientIsMinor: minorAgeMatch ? Number(minorAgeMatch[1]) < 18 : false,
      caseNotesMarkers,
    });
  }, [task, wordCount, content, inferredLetterType, caseNotesMarkers]);

  const lintInactiveMessage = wordCount < 20
    ? 'The live rulebook checker starts once you have written at least 20 words, so early brainstorming does not create noisy warnings.'
    : undefined;

  useEffect(() => {
    let cancelled = false;

    async function loadAttempt() {
      setLoading(true);
      setLoadError(null);
      try {
        const [attempt, checklistItems] = await Promise.all([
          resolveWritingAttempt({ taskId, attemptId: requestedAttemptId, mode: 'timed' }),
          fetchWritingChecklist(),
        ]);
        const fetchedTask = attempt.task ?? await fetchWritingTask(attempt.contentId || taskId);
        const loadedTask = {
          ...fetchedTask,
          id: fetchedTask.id ?? attempt.contentId ?? taskId,
          contentId: fetchedTask.contentId ?? attempt.contentId ?? taskId,
        };
        if (cancelled) return;

        const resolvedChecklist = checklistItems.map((item) => {
          const id = String(item.id);
          return {
            id,
            label: item.text,
            checked: Boolean(attempt.checklist[id] ?? attempt.checklist[item.text] ?? item.completed),
          };
        });
        const checklistRecord = checklistToRecord(resolvedChecklist);
        const durationSeconds = resolveDurationSeconds(loadedTask);
        const elapsedSeconds = Math.max(0, attempt.elapsedSeconds);
        const remainingSeconds = Math.max(durationSeconds - elapsedSeconds, 0);

        setTask(loadedTask);
        setAttemptId(attempt.attemptId);
        setDraftVersion(attempt.draftVersion);
        setContent(attempt.content);
        setScratchpad(attempt.scratchpad);
        setChecklist(resolvedChecklist);
        setTimerInitialSeconds(remainingSeconds);
        setSaveStatus('idle');
        setSaveConflictMessage(null);

        taskIdRef.current = loadedTask.id;
        attemptIdRef.current = attempt.attemptId;
        latestDraftVersionRef.current = attempt.draftVersion;
        latestContentRef.current = attempt.content;
        latestScratchpadRef.current = attempt.scratchpad;
        latestChecklistRef.current = checklistRecord;
        timerDurationRef.current = durationSeconds;
        elapsedSecondsRef.current = elapsedSeconds;
      } catch {
        if (!cancelled) {
          setLoadError('We could not load this Writing draft. Please return to the Writing dashboard and try again.');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    loadAttempt();
    return () => {
      cancelled = true;
    };
  }, [taskId, requestedAttemptId]);

  useEffect(() => {
    return () => {
      if (saveTimerRef.current) {
        clearTimeout(saveTimerRef.current);
      }
    };
  }, []);

  const handleTimerTick = useCallback((remainingSeconds: number) => {
    elapsedSecondsRef.current = Math.max(timerDurationRef.current - remainingSeconds, 0);
  }, []);

  useEffect(() => {
    if (!attemptId) return undefined;
    const interval = window.setInterval(() => {
      heartbeatWritingAttempt(attemptId, elapsedSecondsRef.current).catch(() => {
        // Heartbeats are opportunistic; autosave owns learner-facing failure states.
      });
    }, 30_000);
    return () => window.clearInterval(interval);
  }, [attemptId]);

  // Auto-save with 3s debounce
  const triggerAutoSave = useCallback((nextContent = latestContentRef.current) => {
    hasUnsavedChanges.current = true;

    if (isSubmittingRef.current) {
      return;
    }

    const saveSequence = autosaveSequenceRef.current + 1;
    autosaveSequenceRef.current = saveSequence;

    setSaveStatus('saving');
    if (saveTimerRef.current) clearTimeout(saveTimerRef.current);
    saveTimerRef.current = setTimeout(async () => {
      if (isSubmittingRef.current || autosaveSequenceRef.current !== saveSequence) {
        return;
      }

      try {
        const saved = await submitWritingDraft(taskIdRef.current, {
          attemptId: attemptIdRef.current,
          content: nextContent,
          scratchpad: latestScratchpadRef.current,
          checklist: latestChecklistRef.current,
          draftVersion: latestDraftVersionRef.current,
        });
        if (isSubmittingRef.current || autosaveSequenceRef.current !== saveSequence) {
          return;
        }
        setDraftVersion(saved.draftVersion);
        latestDraftVersionRef.current = saved.draftVersion;
        attemptIdRef.current = saved.attemptId;
        setAttemptId(saved.attemptId);
        setSaveStatus('saved');
        setSaveConflictMessage(null);
        hasUnsavedChanges.current = false;
      } catch (error) {
        if (isSubmittingRef.current || autosaveSequenceRef.current !== saveSequence) {
          return;
        }
        if (error instanceof ApiError && (error.status === 409 || error.code === 'draft_version_conflict')) {
          setSaveConflictMessage('This draft changed in another tab or device. Your latest text is still here; reload the saved draft before continuing so nothing is overwritten.');
        }
        setSaveStatus('failed');
      } finally {
        if (autosaveSequenceRef.current === saveSequence) {
          saveTimerRef.current = undefined;
        }
      }
    }, 3000);
  }, []);

  const handleContentChange = useCallback((val: string) => {
    latestContentRef.current = val;
    setContent(val);
    triggerAutoSave(val);
  }, [triggerAutoSave]);

  const handleScratchpadChange = useCallback((val: string) => {
    latestScratchpadRef.current = val;
    setScratchpad(val);
    triggerAutoSave();
  }, [triggerAutoSave]);

  // Prevent accidental navigation
  useEffect(() => {
    const handler = (e: BeforeUnloadEvent) => {
      if (hasUnsavedChanges.current) { e.preventDefault(); e.returnValue = ''; }
    };
    window.addEventListener('beforeunload', handler);
    return () => window.removeEventListener('beforeunload', handler);
  }, []);

  const handleSubmit = async () => {
    if (submitting) return;

    isSubmittingRef.current = true;
    autosaveSequenceRef.current += 1;
    if (saveTimerRef.current) {
      clearTimeout(saveTimerRef.current);
      saveTimerRef.current = undefined;
    }

    const submittedContent = latestContentRef.current;
    const submittedWordCount = submittedContent.trim().split(/\s+/).filter(Boolean).length;

    setSubmitting(true);
    setSaveStatus('saving');
    try {
      const result = await submitWritingTask(taskIdRef.current, submittedContent, {
        attemptId: attemptIdRef.current,
        scratchpad: latestScratchpadRef.current,
        checklist: latestChecklistRef.current,
        draftVersion: latestDraftVersionRef.current,
      });
      analytics.track('task_submitted', { taskId: taskIdRef.current, attemptId: attemptIdRef.current, subtest: 'writing', wordCount: submittedWordCount });
      hasUnsavedChanges.current = false;
      setSaveStatus('saved');
      setTimerRunning(false);
      const resultUrl = `/writing/result?id=${encodeURIComponent(result.id)}`;
      router.replace(resultUrl);
      window.setTimeout(() => {
        if (window.location.pathname !== '/writing/result') {
          window.location.assign(resultUrl);
        }
      }, 500);
    } catch (error) {
      isSubmittingRef.current = false;
      setSubmitting(false);
      if (error instanceof ApiError && (error.status === 409 || error.code === 'draft_version_conflict')) {
        setSaveConflictMessage('This draft changed in another tab or device. Your latest text is still here; reload the saved draft before submitting.');
      }
      setSaveStatus('failed');
    }
  };

  const handleChecklistChange = useCallback((id: string, checked: boolean) => {
    setChecklist(prev => {
      const next = prev.map(c => c.id === id ? { ...c, checked } : c);
      latestChecklistRef.current = checklistToRecord(next);
      return next;
    });
    triggerAutoSave();
  }, [triggerAutoSave]);

  if (loadError) {
    return (
      <div className="flex min-h-[var(--app-viewport-height,100dvh)] items-center justify-center bg-background-light p-6">
        <div className="max-w-md rounded-[24px] border border-gray-200 bg-white p-6 text-center shadow-sm">
          <h1 className="text-lg font-black text-navy">Writing draft unavailable</h1>
          <p className="mt-3 text-sm leading-6 text-muted">{loadError}</p>
          <Button className="mt-5" onClick={() => router.push('/writing')}>Back to Writing</Button>
        </div>
      </div>
    );
  }

  if (loading || !task) {
    return (
      <div className="flex min-h-[var(--app-viewport-height,100dvh)] flex-col overflow-hidden bg-background-light">
        <Skeleton className="h-16 w-full shrink-0" />
        <div className="flex flex-1 flex-col lg:flex-row">
          <Skeleton className="h-full w-full lg:w-1/2" />
          <Skeleton className="h-full w-full lg:w-1/2" />
        </div>
      </div>
    );
  }

  return (
    <MotionConfig reducedMotion="user">
      <div className="relative flex min-h-[var(--app-viewport-height,100dvh)] flex-col overflow-hidden bg-background-light">
        {/* Header */}
        <AnimatePresence initial={false}>
          {!isDistractionFree && (
            <motion.header
              key="writing-header"
              layout
              initial={{ opacity: 0, y: -8 }}
              animate={{ opacity: 1, y: 0 }}
              exit={{ opacity: 0, y: -8 }}
              transition={panelTransition}
              className="shrink-0 border-b border-gray-200 bg-white"
            >
              <div className="flex flex-col gap-3 px-3 py-3 sm:flex-row sm:items-center sm:justify-between sm:px-4">
                <div className="flex min-w-0 items-center gap-3">
                  <button
                    onClick={() => (hasUnsavedChanges.current ? setShowLeaveModal(true) : router.push('/writing'))}
                    className="pressable touch-target rounded-2xl p-2 text-gray-500 hover:bg-gray-100 hover:text-navy"
                    aria-label="Leave writing task"
                  >
                    <ChevronLeft className="h-5 w-5" />
                  </button>

                  <div className="min-w-0 flex-1">
                    <h1 className="truncate text-base font-bold leading-tight text-navy sm:text-lg">{task.title}</h1>
                    <div className="mt-1 flex flex-wrap items-center gap-2 text-[11px] font-medium text-muted sm:text-xs">
                      <span className="rounded bg-blue-100 px-1.5 py-0.5 text-blue-700">{task.profession}</span>
                      <span>{task.scenarioType}</span>
                      {draftVersion ? <span>Draft v{draftVersion}</span> : null}
                    </div>
                  </div>
                </div>

                <div className="flex items-center gap-2 self-end sm:gap-3 sm:self-auto sm:justify-end">
                  <Timer
                    key={`header-${attemptId ?? task.id}-${timerInitialSeconds}`}
                    mode="countdown"
                    initialSeconds={timerInitialSeconds}
                    running={timerRunning}
                    onTick={handleTimerTick}
                    size={isMobile ? 'sm' : 'md'}
                    showWarning
                  />
                  <button
                    onClick={() => setIsDistractionFree(true)}
                    className="pressable hidden touch-target rounded-2xl p-2 text-gray-500 hover:bg-gray-100 hover:text-navy lg:inline-flex"
                    title="Distraction Free"
                    aria-label="Enter distraction-free mode"
                  >
                    <Maximize className="h-5 w-5" />
                  </button>
                  <Button size={isMobile ? 'sm' : 'md'} onClick={() => setShowSubmitModal(true)} className="shrink-0 whitespace-nowrap touch-target">
                    Submit
                  </Button>
                </div>
              </div>
            </motion.header>
          )}
        </AnimatePresence>

        {/* Distraction Free Floating Toolbar */}
        <AnimatePresence initial={false}>
          {isDistractionFree && (
            <motion.div
              key="writing-distraction-toolbar"
              initial={{ y: -16, opacity: 0 }}
              animate={{ y: 0, opacity: 1 }}
              exit={{ y: -16, opacity: 0 }}
              transition={panelTransition}
              className="absolute left-1/2 top-4 z-50 flex -translate-x-1/2 items-center gap-4 rounded-full border border-gray-200 bg-white px-4 py-2 shadow-lg"
            >
              <Timer
                key={`floating-${attemptId ?? task.id}-${timerInitialSeconds}`}
                mode="countdown"
                initialSeconds={timerInitialSeconds}
                running={timerRunning}
                onTick={handleTimerTick}
                size="sm"
                showWarning
              />
              <button
                onClick={() => setIsDistractionFree(false)}
                className="flex items-center gap-1.5 text-sm font-bold text-primary transition-colors hover:text-primary-dark"
                aria-label="Exit distraction-free mode"
              >
                <Minimize className="h-4 w-4" /> Exit
              </button>
            </motion.div>
          )}
        </AnimatePresence>

        {saveConflictMessage ? (
          <div className="shrink-0 border-b border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">
            <div className="mx-auto flex max-w-5xl flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
              <p className="leading-6">{saveConflictMessage}</p>
              <Button size="sm" variant="outline" onClick={() => window.location.reload()}>
                Reload saved draft
              </Button>
            </div>
          </div>
        ) : null}

        {/* Main Workspace */}
        <main className="flex flex-1 min-h-0 flex-col overflow-hidden">
          <div className="flex h-full flex-col lg:hidden">
            <div className="border-b border-gray-200 bg-white/90 px-3 py-3 shadow-sm backdrop-blur">
              <div className="grid grid-cols-2 rounded-[20px] border border-gray-200 bg-background-light p-1">
                <button
                  type="button"
                  onClick={() => setMobileView('notes')}
                  aria-pressed={mobileView === 'notes'}
                  className={cn(
                    'pressable touch-target rounded-[16px] px-3 py-2.5 text-sm font-semibold',
                    mobileView === 'notes' ? 'bg-surface text-primary shadow-sm' : 'text-muted hover:text-navy',
                  )}
                >
                  Case Notes
                </button>
                <button
                  type="button"
                  onClick={() => setMobileView('editor')}
                  aria-pressed={mobileView === 'editor'}
                  className={cn(
                    'pressable touch-target rounded-[16px] px-3 py-2.5 text-sm font-semibold',
                    mobileView === 'editor' ? 'bg-surface text-primary shadow-sm' : 'text-muted hover:text-navy',
                  )}
                >
                  Editor
                </button>
              </div>
            </div>

            <div className="flex-1 min-h-0 overflow-hidden">
              <AnimatePresence mode="wait" initial={false}>
                {mobileView === 'notes' ? (
                  <motion.div
                    key="mobile-notes"
                    className="h-full"
                    initial={{ opacity: 0, x: prefersReducedMotion ? 0 : -14 }}
                    animate={{ opacity: 1, x: 0 }}
                    exit={{ opacity: 0, x: prefersReducedMotion ? 0 : 14 }}
                    transition={panelTransition}
                  >
                    <WritingCaseNotesPanel
                      caseNotes={task.caseNotes}
                      scratchpad={scratchpad}
                      onScratchpadChange={handleScratchpadChange}
                      checklist={checklist}
                      onChecklistChange={handleChecklistChange}
                      activeTab={activeTab}
                      onTabChange={setActiveTab}
                      taskId={task.id}
                      className="h-full border-r-0"
                    />
                  </motion.div>
                ) : (
                  <motion.div
                    key="mobile-editor"
                    className="h-full"
                    initial={{ opacity: 0, x: prefersReducedMotion ? 0 : 14 }}
                    animate={{ opacity: 1, x: 0 }}
                    exit={{ opacity: 0, x: prefersReducedMotion ? 0 : -14 }}
                    transition={panelTransition}
                  >
                    <div className="flex h-full min-h-0 flex-col">
                      <WritingEditor
                        value={content}
                        onChange={handleContentChange}
                        saveStatus={saveStatus === 'idle' ? 'idle' : saveStatus}
                        fontSize={fontSize}
                        onFontSizeChange={setFontSize}
                        showFontSizeControls={false}
                        placeholder="Begin writing your response..."
                        className="min-h-0 flex-1"
                      />
                      <RulebookFindingsPanel
                        title="Rulebook Review"
                        subtitle={`Live checks grounded in Dr. Hesham's Writing rulebook. Inferred letter type: ${inferredLetterType.replace(/_/g, ' ')}.`}
                        findings={lintFindings}
                        inactiveMessage={lintInactiveMessage}
                        className="shrink-0 rounded-none rounded-b-[24px] border-t border-gray-200"
                        ruleHref={(ruleId) => `/writing/rulebook/${ruleId}`}
                      />
                    </div>
                  </motion.div>
                )}
              </AnimatePresence>
            </div>
          </div>

          <div className={cn('hidden flex-1 min-h-0 lg:grid', isDistractionFree ? 'lg:grid-cols-[minmax(0,1fr)_minmax(0,2fr)]' : 'lg:grid-cols-2')}>
            <motion.div
              layout
              className="h-full min-h-0 min-w-0 w-full"
              transition={panelTransition}
            >
              <WritingCaseNotesPanel
                caseNotes={task.caseNotes}
                scratchpad={scratchpad}
                onScratchpadChange={handleScratchpadChange}
                checklist={checklist}
                onChecklistChange={handleChecklistChange}
                activeTab={activeTab}
                onTabChange={setActiveTab}
                taskId={task.id}
              />
            </motion.div>
            <motion.div
              layout
              className="h-full min-h-0 min-w-0 w-full"
              transition={panelTransition}
            >
              <div className="flex h-full min-h-0 flex-col">
                <WritingEditor
                  value={content}
                  onChange={handleContentChange}
                  saveStatus={saveStatus === 'idle' ? 'idle' : saveStatus}
                  fontSize={fontSize}
                  onFontSizeChange={setFontSize}
                  showFontSizeControls={!isMobile}
                  placeholder="Begin writing your response..."
                  className="min-h-0 flex-1"
                />
                <RulebookFindingsPanel
                  title="Rulebook Review"
                  subtitle={`Live checks grounded in Dr. Hesham's Writing rulebook. Inferred letter type: ${inferredLetterType.replace(/_/g, ' ')}.`}
                  findings={lintFindings}
                  inactiveMessage={lintInactiveMessage}
                  className="shrink-0 rounded-none rounded-b-[24px] border-t border-gray-200"
                  ruleHref={(ruleId) => `/writing/rulebook/${ruleId}`}
                />
              </div>
            </motion.div>
          </div>
        </main>

        {/* Submit Confirmation Modal */}
        <Modal open={showSubmitModal} onClose={() => setShowSubmitModal(false)} title="Submit Your Response?">
          <p className="mb-4 text-sm text-muted">Once submitted, your response will be evaluated by our AI engine. You can request an expert human review later.</p>
          <p className="mb-6 text-sm font-semibold text-navy">Word count: {wordCount}</p>
          <div className="flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
            <Button variant="secondary" onClick={() => setShowSubmitModal(false)}>Cancel</Button>
            <Button loading={submitting} onClick={handleSubmit}>Confirm Submit</Button>
          </div>
        </Modal>

        {/* Leave Confirmation Modal */}
        <Modal open={showLeaveModal} onClose={() => setShowLeaveModal(false)} title="Leave Writing Task?">
          <p className="mb-6 text-sm text-muted">You have unsaved changes. Are you sure you want to leave? Your progress will be saved as a draft.</p>
          <div className="flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
            <Button variant="secondary" onClick={() => setShowLeaveModal(false)}>Stay</Button>
            <Button variant="destructive" onClick={() => router.push('/writing')}>Leave</Button>
          </div>
        </Modal>
      </div>
    </MotionConfig>
  );
}
