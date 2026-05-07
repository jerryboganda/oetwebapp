'use client';

import { useState, useEffect, useCallback, useMemo, useRef } from 'react';
import { AnimatePresence, MotionConfig, motion, useReducedMotion } from 'motion/react';
import type { Transition } from 'motion/react';
import { BookOpen, ChevronLeft, ClipboardCheck, GraduationCap, Maximize, Minimize } from 'lucide-react';
import { useSearchParams, useRouter } from 'next/navigation';
import { cn } from '@/lib/utils';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Modal } from '@/components/ui/modal';
import { Timer } from '@/components/ui/timer';
import { WritingCaseNotesPanel } from '@/components/domain/writing-case-notes-panel';
import { WritingEditor } from '@/components/domain/writing-editor';
import { RulebookFindingsPanel } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchWritingTask, fetchWritingChecklist, submitWritingDraft, submitWritingTask, completeMockSection } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { WritingTask } from '@/lib/mock-data';
import { useIsMobile } from '@/hooks/use-mobile';
import { deriveWritingCaseNotesMarkers, inferWritingLetterType, lintWritingLetter } from '@/lib/rulebook';
import {
  normalizeWritingPracticeMode,
  WRITING_CRITERIA,
  WRITING_READING_WINDOW_SECONDS,
  WRITING_WINDOW_SECONDS,
} from '@/lib/writing/workflow';

type SaveStatus = 'idle' | 'saving' | 'saved' | 'failed';

/**
 * Official OET Writing exam phases. Source: Dr. Ahmed Hesham corrections.
 *
 * Reading window is 5 min; writing window is 40 min. During the reading window
 * the editor MUST be read-only AND the case notes MUST NOT allow highlighting,
 * annotation, or copying. After 5 minutes both unlock for the full 40-minute
 * writing period. Applies to ALL OET professions (Medicine, Nursing, etc.).
 */
export default function WritingPlayer() {
  const searchParams = useSearchParams();
  const router = useRouter();
  const isMobile = useIsMobile();
  const prefersReducedMotion = useReducedMotion();
  const taskId = searchParams?.get('taskId') ?? 'wt-001';
  const practiceMode = normalizeWritingPracticeMode(searchParams?.get('mode'));
  const isExamMode = practiceMode === 'exam';
  // Mocks V2 — BuildLaunchRoute attaches mockAttemptId/mockSectionId when
  // this player is launched as a section of a mock attempt. Writing scores
  // are tutor-graded asynchronously, so we POST nulls and mark the section
  // Completed (the mock report renders this as Provisional).
  const mockAttemptId = searchParams?.get('mockAttemptId') ?? null;
  const mockSectionId = searchParams?.get('mockSectionId') ?? null;

  const [task, setTask] = useState<WritingTask | null>(null);
  const [checklist, setChecklist] = useState<{ id: string; label: string; checked: boolean }[]>([]);
  const [loading, setLoading] = useState(true);
  const [content, setContent] = useState('');
  const [scratchpad, setScratchpad] = useState('');
  const [saveStatus, setSaveStatus] = useState<SaveStatus>('idle');
  const [fontSize, setFontSize] = useState(16);
  const [isDistractionFree, setIsDistractionFree] = useState(false);
  const [activeTab, setActiveTab] = useState<'notes' | 'scratchpad' | 'checklist'>('notes');
  const [showLeaveModal, setShowLeaveModal] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [timerRunning, setTimerRunning] = useState(true);
  const [mobileView, setMobileView] = useState<'notes' | 'editor'>('notes');
  /**
   * OET Writing phase enforcement. The exam is split into a 5-minute reading
   * window (editor + highlighting locked) followed by a 40-minute writing
   * window. Once the reading window ends it cannot be re-entered.
   *
   * Source: Dr. Ahmed Hesham corrections (applies to ALL OET professions).
   */
  const [phase, setPhase] = useState<'reading' | 'writing'>(() => (isExamMode ? 'reading' : 'writing'));
  const isReadingPhase = isExamMode && phase === 'reading';
  const saveTimerRef = useRef<NodeJS.Timeout | undefined>(undefined);
  const hasUnsavedChanges = useRef(false);
  const latestContentRef = useRef('');
  const isSubmittingRef = useRef(false);
  const autosaveSequenceRef = useRef(0);
  const panelTransition: Transition = prefersReducedMotion
    ? { duration: 0.01 }
    : { type: 'spring', stiffness: 420, damping: 38 };

  // Threshold below which the live rulebook checker stays quiet to avoid noisy
  // warnings while the candidate is still brainstorming. Character-based so
  // the platform performs no word-counting logic anywhere.
  const LINT_MIN_CONTENT_CHARS = 80;
  const trimmedContentLength = content.trim().length;
  const lintReady = trimmedContentLength >= LINT_MIN_CONTENT_CHARS;

  const inferredLetterType = useMemo(() => inferWritingLetterType(task ?? {}), [task]);
  const caseNotesMarkers = useMemo(() => deriveWritingCaseNotesMarkers(task?.caseNotes), [task?.caseNotes]);

  // Live, advisory word counter. Pure UI hint — the platform never blocks
  // submission on word count; canonical enforcement (where it exists) lives in
  // the rulebook engine. The OET soft guideline is 180–200 words (body only).
  const wordCount = useMemo(() => content.trim().match(/\S+/g)?.length ?? 0, [content]);
  // Mirrors the rulebook detector (`letter_body_length`): below 80 words the
  // learner is still drafting, so band warnings are suppressed and the chip
  // shows a neutral "drafting" hint rather than an alarming red badge.
  const bandState: 'drafting' | 'in' | 'near' | 'out' = useMemo(() => {
    if (wordCount < 80) return 'drafting';
    if (wordCount < 162 || wordCount > 220) return 'out';
    if (wordCount < 180 || wordCount > 200) return 'near';
    return 'in';
  }, [wordCount]);
  const wordCountVariant: 'success' | 'warning' | 'danger' | 'muted' =
    bandState === 'in'
      ? 'success'
      : bandState === 'near'
      ? 'warning'
      : bandState === 'out'
      ? 'danger'
      : 'muted';
  const wordCountLabel =
    bandState === 'drafting'
      ? `${wordCount} words / target 180–200`
      : `${wordCount} / 180–200 words`;

  // Announce only when the band classification changes, so screen readers are
  // not spammed on every keystroke. Transitions in or out of the 'drafting'
  // state are intentionally silent — the band guidance only matters once the
  // learner has a real draft (≥80 words).
  const [bandAnnouncement, setBandAnnouncement] = useState('');
  const lastBandRef = useRef<'drafting' | 'in' | 'near' | 'out' | null>(null);
  useEffect(() => {
    if (lastBandRef.current === bandState) return;
    const previous = lastBandRef.current;
    lastBandRef.current = bandState;
    if (previous === null) return; // suppress initial mount announcement
    if (bandState === 'drafting' || previous === 'drafting') return;
    const message =
      bandState === 'in'
        ? `Word count ${wordCount}: within the OET pass band of 180 to 200 words.`
        : bandState === 'near'
        ? `Word count ${wordCount}: just outside the OET pass band of 180 to 200 words.`
        : `Word count ${wordCount}: well outside the OET pass band of 180 to 200 words.`;
    setBandAnnouncement(message);
  }, [bandState, wordCount]);

  const wordCountIndicator = (
    <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1 border-b border-border bg-surface/95 px-3 py-2">
      <div className="flex items-center gap-2">
        {prefersReducedMotion ? (
          <Badge variant={wordCountVariant} size="sm">
            {wordCountLabel}
          </Badge>
        ) : (
          <AnimatePresence mode="wait" initial={false}>
            <motion.span
              key={bandState}
              initial={{ opacity: 0, y: -2 }}
              animate={{ opacity: 1, y: 0 }}
              exit={{ opacity: 0, y: 2 }}
              transition={panelTransition}
              className="inline-flex"
            >
              <Badge variant={wordCountVariant} size="sm">
                {wordCountLabel}
              </Badge>
            </motion.span>
          </AnimatePresence>
        )}
        <span className="text-[11px] leading-snug text-muted">
          {bandState === 'drafting'
            ? 'Keep writing… word-count guidance shows once your draft passes 80 words.'
            : 'Soft guideline — OET pass band 180–200 words.'}
        </span>
      </div>
      <div role="status" aria-live="polite" className="sr-only">
        {bandAnnouncement}
      </div>
    </div>
  );

  const lintFindings = useMemo(() => {
    if (!task || !lintReady) return [];
    const minorAgeMatch = task.caseNotes.match(/\b(\d+)\s*(years? old|year-old)\b/i);
    return lintWritingLetter({
      letterText: content,
      letterType: inferredLetterType,
      profession: 'medicine',
      recipientSpecialty: task.title,
      recipientName: null,
      patientAge: minorAgeMatch ? Number(minorAgeMatch[1]) : null,
      patientIsMinor: minorAgeMatch ? Number(minorAgeMatch[1]) < 18 : false,
      caseNotesMarkers,
    });
  }, [task, lintReady, content, inferredLetterType, caseNotesMarkers]);

  const lintInactiveMessage = !lintReady
    ? 'The live rulebook checker starts once you have written a few sentences, so early brainstorming does not create noisy warnings.'
    : undefined;

  useEffect(() => {
    Promise.all([fetchWritingTask(taskId), fetchWritingChecklist()])
      .then(([t, cl]) => {
        setTask(t);
        setChecklist(cl.map(c => ({ id: String(c.id), label: c.text, checked: c.completed })));
      })
      .finally(() => setLoading(false));
  }, [taskId]);

  useEffect(() => {
    return () => {
      if (saveTimerRef.current) {
        clearTimeout(saveTimerRef.current);
      }
    };
  }, []);

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
        await submitWritingDraft(taskId, nextContent, practiceMode);
        if (isSubmittingRef.current || autosaveSequenceRef.current !== saveSequence) {
          return;
        }
        setSaveStatus('saved');
        hasUnsavedChanges.current = false;
      } catch {
        if (isSubmittingRef.current || autosaveSequenceRef.current !== saveSequence) {
          return;
        }
        setSaveStatus('failed');
      } finally {
        if (autosaveSequenceRef.current === saveSequence) {
          saveTimerRef.current = undefined;
        }
      }
    }, 3000);
  }, [practiceMode, taskId]);

  const handleContentChange = useCallback((val: string) => {
    latestContentRef.current = val;
    setContent(val);
    triggerAutoSave(val);
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

    setSubmitting(true);
    setSaveStatus('saving');
    try {
      const result = await submitWritingTask(taskId, submittedContent, practiceMode);
      analytics.track('task_submitted', { taskId, subtest: 'writing', mode: practiceMode });
      hasUnsavedChanges.current = false;
      setSaveStatus('saved');
      setTimerRunning(false);
      if (mockAttemptId && mockSectionId) {
        try {
          await completeMockSection(mockAttemptId, mockSectionId, {
            contentAttemptId: result.id,
            rawScore: null,
            rawScoreMax: null,
            scaledScore: null,
            grade: null,
            evidence: { source: 'writing_player', submissionId: result.id, awaitingTutorReview: true },
          });
        } catch (mockErr) {
          // Do not lose the learner's submission on mock-write failure.
          console.warn('Could not mark mock writing section complete', mockErr);
        }
        const mockUrl = `/mocks/player/${mockAttemptId}`;
        router.replace(mockUrl);
        window.setTimeout(() => {
          if (window.location.pathname !== mockUrl) {
            window.location.assign(mockUrl);
          }
        }, 500);
        return;
      }
      const resultUrl = `/writing/result?id=${encodeURIComponent(result.id)}`;
      router.replace(resultUrl);
      window.setTimeout(() => {
        if (window.location.pathname !== '/writing/result') {
          window.location.assign(resultUrl);
        }
      }, 500);
    } catch {
      isSubmittingRef.current = false;
      setSubmitting(false);
      setSaveStatus('failed');
    }
  };

  const handleChecklistChange = useCallback((id: string, checked: boolean) => {
    setChecklist(prev => prev.map(c => c.id === id ? { ...c, checked } : c));
    triggerAutoSave();
  }, [triggerAutoSave]);

  /**
   * Transition the player from the 5-minute reading phase into the 40-minute
   * writing phase. Called when the reading-window countdown reaches zero.
   * Idempotent so rapid duplicate fires from the Timer cannot reset progress.
   */
  const handleReadingWindowComplete = useCallback(() => {
    if (!isExamMode) return;
    setPhase(prev => {
      if (prev !== 'reading') return prev;
      analytics.track('writing_reading_window_ended', { taskId, mode: practiceMode });
      return 'writing';
    });
    // When the reading window ends we auto-surface the editor on mobile so
    // the learner immediately sees where to start writing.
    setMobileView(prev => (prev === 'notes' ? 'editor' : prev));
  }, [isExamMode, practiceMode, taskId]);

  const handleWritingWindowComplete = useCallback(() => {
    // Writing window expired — auto-submit so the learner does not lose work.
    if (isExamMode && !submitting && !isSubmittingRef.current) {
      void handleSubmit();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isExamMode, submitting]);

  const taskMetadata = useMemo(() => {
    if (!task) return [];
    return [
      { label: 'Profession', value: task.profession },
      { label: 'Letter type', value: task.letterType ?? task.scenarioType },
      { label: 'Writer role', value: task.writerRole },
      { label: 'Recipient', value: task.recipient },
      { label: 'Purpose', value: task.purpose },
      { label: 'Task date', value: task.taskDate },
    ].filter((item): item is { label: string; value: string } => typeof item.value === 'string' && item.value.length > 0);
  }, [task]);

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
              className="shrink-0 border-b border-border bg-surface"
            >
              <div className="flex flex-col gap-3 px-3 py-3 sm:flex-row sm:items-center sm:justify-between sm:px-4">
                <div className="flex min-w-0 items-center gap-3">
                  <button
                    onClick={() => (hasUnsavedChanges.current ? setShowLeaveModal(true) : router.push('/writing'))}
                    className="pressable touch-target rounded-2xl p-2 text-muted hover:bg-background-light hover:text-navy"
                    aria-label="Leave writing task"
                  >
                    <ChevronLeft className="h-5 w-5" />
                  </button>

                  <div className="min-w-0 flex-1">
                    <h1 className="truncate text-base font-bold leading-tight text-navy sm:text-lg">{task.title}</h1>
                    <div className="mt-1 flex flex-wrap items-center gap-2 text-[11px] font-medium text-muted sm:text-xs">
                      <Badge variant="info" size="sm">{task.profession}</Badge>
                      <Badge variant="muted" size="sm">{task.letterType ?? task.scenarioType}</Badge>
                      <Badge variant={isExamMode ? 'warning' : 'success'} size="sm">
                        {isExamMode ? 'Exam mode' : 'Learning mode'}
                      </Badge>
                    </div>
                  </div>
                </div>

                <div className="flex items-center gap-2 self-end sm:gap-3 sm:self-auto sm:justify-end">
                  <Timer
                    key={`writing-timer-${phase}`}
                    initialSeconds={isExamMode ? (isReadingPhase ? WRITING_READING_WINDOW_SECONDS : WRITING_WINDOW_SECONDS) : 0}
                    running={timerRunning}
                    mode={isExamMode ? 'countdown' : 'elapsed'}
                    size={isMobile ? 'sm' : 'md'}
                    showWarning={isExamMode && !isReadingPhase}
                    onComplete={isExamMode ? (isReadingPhase ? handleReadingWindowComplete : handleWritingWindowComplete) : undefined}
                  />
                  <button
                    onClick={() => setIsDistractionFree(true)}
                    className="pressable hidden touch-target rounded-2xl p-2 text-muted hover:bg-background-light hover:text-navy lg:inline-flex"
                    title="Distraction Free"
                    aria-label="Enter distraction-free mode"
                  >
                    <Maximize className="h-5 w-5" />
                  </button>
                  <Button size={isMobile ? 'sm' : 'md'} onClick={handleSubmit} loading={submitting} className="shrink-0 whitespace-nowrap touch-target">
                    Submit
                  </Button>
                </div>
              </div>
            </motion.header>
          )}
        </AnimatePresence>

        {mockAttemptId && !isDistractionFree ? (
          <div
            role="status"
            className="flex items-start gap-3 border-b border-info/30 bg-info/10 px-4 py-2.5 text-sm text-info"
          >
            <ClipboardCheck aria-hidden className="mt-0.5 h-4 w-4 shrink-0" />
            <div className="flex-1">
              <p className="font-semibold">Mock section in progress</p>
              <p className="text-xs">
                Submitting will mark this Writing section complete and return you to the mock dashboard. Tutor scoring continues asynchronously.
              </p>
            </div>
          </div>
        ) : null}

        {/* Reading-window banner: visible for the full 5 minutes, announces that
            writing + highlighting are locked. Disappears once phase flips to 'writing'. */}
        <AnimatePresence initial={false}>
          {isReadingPhase && !isDistractionFree && (
            <motion.div
              key="writing-reading-banner"
              initial={{ y: -8, opacity: 0 }}
              animate={{ y: 0, opacity: 1 }}
              exit={{ y: -8, opacity: 0 }}
              transition={panelTransition}
              role="status"
              aria-live="polite"
              className="flex items-start gap-3 border-b border-warning/30 bg-warning/10 px-4 py-2.5 text-sm text-warning"
            >
              <BookOpen aria-hidden className="mt-0.5 h-4 w-4 shrink-0" />
              <div className="flex-1">
                <p className="font-semibold">Reading window &mdash; 5 minutes</p>
                <p className="text-xs text-warning/90">
                  Read the case notes carefully. Typing, highlighting, copying, and scratchpad edits are locked until the reading window ends.
                </p>
              </div>
            </motion.div>
          )}
        </AnimatePresence>

        <AnimatePresence initial={false}>
          {!isDistractionFree ? (
            <motion.section
              key="writing-workflow-guidance"
              initial={{ opacity: 0, y: -8 }}
              animate={{ opacity: 1, y: 0 }}
              exit={{ opacity: 0, y: -8 }}
              transition={panelTransition}
              className="border-b border-border bg-surface/95 px-4 py-3"
            >
              <div className="grid gap-3 lg:grid-cols-[minmax(0,1.25fr)_minmax(0,1.25fr)]">
                <div className="rounded-2xl border border-border bg-background-light p-3">
                  <div className="mb-2 flex items-center gap-2 text-xs font-black uppercase tracking-widest text-navy">
                    <ClipboardCheck className="h-4 w-4 text-primary" /> Task brief
                  </div>
                  <dl className="grid grid-cols-1 gap-2 text-xs sm:grid-cols-2">
                    {taskMetadata.map((item) => (
                      <div key={item.label}>
                        <dt className="font-bold text-muted">{item.label}</dt>
                        <dd className="mt-0.5 text-navy">{item.value}</dd>
                      </div>
                    ))}
                  </dl>
                </div>

                <div className="rounded-2xl border border-border bg-background-light p-3">
                  <div className="mb-2 flex items-center gap-2 text-xs font-black uppercase tracking-widest text-navy">
                    <GraduationCap className="h-4 w-4 text-primary" /> Rubric workflow
                  </div>
                  <div className="flex flex-wrap gap-1.5">
                    {WRITING_CRITERIA.map((criterion) => (
                      <Badge key={criterion.code} variant="outline" size="sm" title={criterion.guidance}>
                        {criterion.label} /{criterion.maxScore}
                      </Badge>
                    ))}
                  </div>
                  <p className="mt-2 text-xs leading-5 text-muted">
                    AI support is practice-only; final readiness decisions should use criterion-based tutor review. Letter length guidance is 180–200 words (body only).
                  </p>
                </div>
              </div>
            </motion.section>
          ) : null}
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
              className="absolute left-1/2 top-4 z-50 flex -translate-x-1/2 items-center gap-4 rounded-full border border-border bg-surface px-4 py-2 shadow-lg"
            >
              <Timer
                key={`writing-timer-df-${phase}`}
                initialSeconds={isExamMode ? (isReadingPhase ? WRITING_READING_WINDOW_SECONDS : WRITING_WINDOW_SECONDS) : 0}
                running={timerRunning}
                mode={isExamMode ? 'countdown' : 'elapsed'}
                size="sm"
                showWarning={isExamMode && !isReadingPhase}
                onComplete={isExamMode ? (isReadingPhase ? handleReadingWindowComplete : handleWritingWindowComplete) : undefined}
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

        {/* Main Workspace */}
        <main className="flex flex-1 min-h-0 flex-col overflow-hidden">
          <div className="flex h-full flex-col lg:hidden">
            <div className="border-b border-border bg-surface/90 px-3 py-3 shadow-sm backdrop-blur">
              <div className="grid grid-cols-2 rounded-[20px] border border-border bg-background-light p-1">
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
                      onScratchpadChange={setScratchpad}
                      checklist={checklist}
                      onChecklistChange={handleChecklistChange}
                      activeTab={activeTab}
                      onTabChange={setActiveTab}
                      taskId={task.id}
                      readingWindowLocked={isReadingPhase}
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
                      {wordCountIndicator}
                      <WritingEditor
                        value={content}
                        onChange={handleContentChange}
                        saveStatus={saveStatus === 'idle' ? 'idle' : saveStatus}

                        fontSize={fontSize}
                        onFontSizeChange={setFontSize}
                        showFontSizeControls={false}
                        placeholder={isReadingPhase ? 'Writing is locked for the 5-minute reading window\u2026' : 'Begin writing your response...'}
                        disabled={isReadingPhase}
                        spellCheck={!isExamMode}
                        className="min-h-0 flex-1"
                      />
                      {!isExamMode ? (
                        <RulebookFindingsPanel
                          title="Rulebook Review"
                          subtitle={`Live checks grounded in Dr. Hesham's Writing rulebook. Inferred letter type: ${inferredLetterType.replace(/_/g, ' ')}.`}
                          findings={lintFindings}
                          inactiveMessage={lintInactiveMessage}
                          className="shrink-0 rounded-none rounded-b-[24px] border-t border-border"
                          ruleHref={(ruleId) => `/writing/rulebook/${ruleId}`}
                        />
                      ) : null}
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
                onScratchpadChange={setScratchpad}
                checklist={checklist}
                onChecklistChange={handleChecklistChange}
                activeTab={activeTab}
                onTabChange={setActiveTab}
                taskId={task.id}
                readingWindowLocked={isReadingPhase}
              />
            </motion.div>
            <motion.div
              layout
              className="h-full min-h-0 min-w-0 w-full"
              transition={panelTransition}
            >
              <div className="flex h-full min-h-0 flex-col">
                {wordCountIndicator}
                <WritingEditor
                  value={content}
                  onChange={handleContentChange}
                  saveStatus={saveStatus === 'idle' ? 'idle' : saveStatus}

                  fontSize={fontSize}
                  onFontSizeChange={setFontSize}
                  showFontSizeControls={!isMobile}
                  placeholder={isReadingPhase ? 'Writing is locked for the 5-minute reading window\u2026' : 'Begin writing your response...'}
                  disabled={isReadingPhase}
                  spellCheck={!isExamMode}
                  className="min-h-0 flex-1"
                />
                {!isExamMode ? (
                  <RulebookFindingsPanel
                    title="Rulebook Review"
                    subtitle={`Live checks grounded in Dr. Hesham's Writing rulebook. Inferred letter type: ${inferredLetterType.replace(/_/g, ' ')}.`}
                    findings={lintFindings}
                    inactiveMessage={lintInactiveMessage}
                    className="shrink-0 rounded-none rounded-b-[24px] border-t border-border"
                    ruleHref={(ruleId) => `/writing/rulebook/${ruleId}`}
                  />
                ) : null}
              </div>
            </motion.div>
          </div>
        </main>

        {/* Submit Confirmation Modal removed: per business requirement,
            submission is one-tap and the platform performs no word-count checks. */}

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
