'use client';

import { useState, useEffect, useCallback, useRef } from 'react';
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
import { Skeleton } from '@/components/ui/skeleton';
import { fetchWritingTask, fetchWritingChecklist, submitWritingDraft, submitWritingTask } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { WritingTask } from '@/lib/mock-data';
import { useIsMobile } from '@/hooks/use-mobile';

type SaveStatus = 'idle' | 'saving' | 'saved' | 'failed';

export default function WritingPlayer() {
  const searchParams = useSearchParams();
  const router = useRouter();
  const isMobile = useIsMobile();
  const prefersReducedMotion = useReducedMotion();
  const taskId = searchParams?.get('taskId') ?? 'wt-001';

  const [task, setTask] = useState<WritingTask | null>(null);
  const [checklist, setChecklist] = useState<{ id: string; label: string; checked: boolean }[]>([]);
  const [loading, setLoading] = useState(true);
  const [content, setContent] = useState('');
  const [scratchpad, setScratchpad] = useState('');
  const [saveStatus, setSaveStatus] = useState<SaveStatus>('idle');
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
  const panelTransition: Transition = prefersReducedMotion
    ? { duration: 0.01 }
    : { type: 'spring', stiffness: 420, damping: 38 };

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
  const triggerAutoSave = useCallback(() => {
    hasUnsavedChanges.current = true;
    setSaveStatus('saving');
    if (saveTimerRef.current) clearTimeout(saveTimerRef.current);
    saveTimerRef.current = setTimeout(async () => {
      try {
        await submitWritingDraft(taskId, content);
        setSaveStatus('saved');
        hasUnsavedChanges.current = false;
      } catch {
        setSaveStatus('failed');
      }
    }, 3000);
  }, [taskId, content]);

  const handleContentChange = useCallback((val: string) => {
    setContent(val);
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
    setSubmitting(true);
    try {
      const result = await submitWritingTask(taskId, content);
      analytics.track('task_submitted', { taskId, subtest: 'writing', wordCount: content.split(/\s+/).filter(Boolean).length });
      setTimerRunning(false);
      router.push(`/writing/result?id=${result.id}`);
    } catch {
      setSubmitting(false);
    }
  };

  const handleChecklistChange = useCallback((id: string, checked: boolean) => {
    setChecklist(prev => prev.map(c => c.id === id ? { ...c, checked } : c));
    triggerAutoSave();
  }, [triggerAutoSave]);

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
                    </div>
                  </div>
                </div>

                <div className="flex items-center gap-2 self-end sm:gap-3 sm:self-auto sm:justify-end">
                  <Timer mode="countdown" initialSeconds={45 * 60} running={timerRunning} size={isMobile ? 'sm' : 'md'} showWarning />
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
              <Timer mode="countdown" initialSeconds={45 * 60} running={timerRunning} size="sm" showWarning />
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
                      onScratchpadChange={setScratchpad}
                      checklist={checklist}
                      onChecklistChange={handleChecklistChange}
                      activeTab={activeTab}
                      onTabChange={setActiveTab}
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
                    <WritingEditor
                      value={content}
                      onChange={handleContentChange}
                      saveStatus={saveStatus === 'idle' ? 'idle' : saveStatus}
                      fontSize={fontSize}
                      onFontSizeChange={setFontSize}
                      showFontSizeControls={false}
                      placeholder="Begin writing your response..."
                      className="h-full"
                    />
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
              />
            </motion.div>
            <motion.div
              layout
              className="h-full min-h-0 min-w-0 w-full"
              transition={panelTransition}
            >
              <WritingEditor
                value={content}
                onChange={handleContentChange}
                saveStatus={saveStatus === 'idle' ? 'idle' : saveStatus}
                fontSize={fontSize}
                onFontSizeChange={setFontSize}
                showFontSizeControls={!isMobile}
                placeholder="Begin writing your response..."
                className="h-full"
              />
            </motion.div>
          </div>
        </main>

        {/* Submit Confirmation Modal */}
        <Modal open={showSubmitModal} onClose={() => setShowSubmitModal(false)} title="Submit Your Response?">
          <p className="mb-4 text-sm text-muted">Once submitted, your response will be evaluated by our AI engine. You can request an expert human review later.</p>
          <p className="mb-6 text-sm font-semibold text-navy">Word count: {content.trim().split(/\s+/).filter(Boolean).length}</p>
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
