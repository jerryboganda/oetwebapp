'use client';

import { useState, useEffect, useCallback, useRef } from 'react';
import { AnimatePresence, motion } from 'motion/react';
import { ChevronLeft, Maximize, Minimize } from 'lucide-react';
import Link from 'next/link';
import { useSearchParams, useRouter } from 'next/navigation';
import { Button } from '@/components/ui/button';
import { Modal } from '@/components/ui/modal';
import { Timer } from '@/components/ui/timer';
import { WritingCaseNotesPanel } from '@/components/domain/writing-case-notes-panel';
import { WritingEditor } from '@/components/domain/writing-editor';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchWritingTask, fetchWritingChecklist, submitWritingDraft, submitWritingTask } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { WritingTask, ChecklistItem } from '@/lib/mock-data';

type SaveStatus = 'idle' | 'saving' | 'saved' | 'failed';

export default function WritingPlayer() {
  const searchParams = useSearchParams();
  const router = useRouter();
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
  const saveTimerRef = useRef<NodeJS.Timeout | undefined>(undefined);
  const hasUnsavedChanges = useRef(false);

  useEffect(() => {
    Promise.all([fetchWritingTask(taskId), fetchWritingChecklist()])
      .then(([t, cl]) => {
        setTask(t);
        setChecklist(cl.map(c => ({ id: String(c.id), label: c.text, checked: c.completed })));
      })
      .finally(() => setLoading(false));
  }, [taskId]);

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
      <div className="h-screen flex flex-col">
        <Skeleton className="h-14 w-full" />
        <div className="flex-1 flex">
          <Skeleton className="w-1/2 h-full" />
          <Skeleton className="w-1/2 h-full" />
        </div>
      </div>
    );
  }

  return (
    <div className="h-screen flex flex-col bg-background-light overflow-hidden">
      {/* Header */}
      <AnimatePresence>
        {!isDistractionFree && (
          <motion.header initial={{ height: 0, opacity: 0 }} animate={{ height: 'auto', opacity: 1 }} exit={{ height: 0, opacity: 0 }}
            className="bg-white border-b border-gray-200 shrink-0">
            <div className="flex items-center justify-between px-4 py-3">
              <div className="flex items-center gap-4">
                <button onClick={() => hasUnsavedChanges.current ? setShowLeaveModal(true) : router.push('/writing')} className="text-gray-500 hover:text-navy transition-colors">
                  <ChevronLeft className="w-5 h-5" />
                </button>
                <div>
                  <h1 className="font-bold text-lg leading-tight">{task.title}</h1>
                  <div className="text-xs text-muted font-medium flex items-center gap-2">
                    <span className="bg-blue-100 text-blue-700 px-1.5 py-0.5 rounded">{task.profession}</span>
                    <span>{task.scenarioType}</span>
                  </div>
                </div>
              </div>
              <Timer mode="countdown" initialSeconds={45 * 60} running={timerRunning} size="md" showWarning />
              <div className="flex items-center gap-3">
                <button onClick={() => setIsDistractionFree(true)} className="p-2 text-gray-500 hover:text-navy hover:bg-gray-100 rounded-lg transition-colors" title="Distraction Free" aria-label="Enter distraction-free mode">
                  <Maximize className="w-5 h-5" />
                </button>
                <Button onClick={() => setShowSubmitModal(true)}>Submit</Button>
              </div>
            </div>
          </motion.header>
        )}
      </AnimatePresence>

      {/* Distraction Free Floating Toolbar */}
      <AnimatePresence>
        {isDistractionFree && (
          <motion.div initial={{ y: -50, opacity: 0 }} animate={{ y: 0, opacity: 1 }} exit={{ y: -50, opacity: 0 }}
            className="absolute top-4 left-1/2 -translate-x-1/2 z-50 bg-white shadow-lg border border-gray-200 rounded-full px-4 py-2 flex items-center gap-6">
            <Timer mode="countdown" initialSeconds={45 * 60} running={timerRunning} size="sm" showWarning />
            <button onClick={() => setIsDistractionFree(false)} className="flex items-center gap-1.5 text-sm font-bold text-primary hover:text-primary-dark transition-colors" aria-label="Exit distraction-free mode">
              <Minimize className="w-4 h-4" /> Exit
            </button>
          </motion.div>
        )}
      </AnimatePresence>

      {/* Main Workspace */}
      <main className="flex-1 flex overflow-hidden">
        <div className={`transition-all duration-300 ${isDistractionFree ? 'w-1/3' : 'w-1/2'}`}>
          <WritingCaseNotesPanel
            caseNotes={task.caseNotes}
            scratchpad={scratchpad}
            onScratchpadChange={setScratchpad}
            checklist={checklist}
            onChecklistChange={handleChecklistChange}
            activeTab={activeTab}
            onTabChange={setActiveTab}
          />
        </div>
        <div className={`transition-all duration-300 ${isDistractionFree ? 'w-2/3' : 'w-1/2'}`}>
          <WritingEditor
            value={content}
            onChange={handleContentChange}
            saveStatus={saveStatus === 'idle' ? 'idle' : saveStatus}
            fontSize={fontSize}
            onFontSizeChange={setFontSize}
            placeholder="Begin writing your response..."
          />
        </div>
      </main>

      {/* Submit Confirmation Modal */}
      <Modal open={showSubmitModal} onClose={() => setShowSubmitModal(false)} title="Submit Your Response?">
        <p className="text-sm text-muted mb-4">Once submitted, your response will be evaluated by our AI engine. You can request an expert human review later.</p>
        <p className="text-sm font-semibold text-navy mb-6">Word count: {content.trim().split(/\s+/).filter(Boolean).length}</p>
        <div className="flex gap-3 justify-end">
          <Button variant="secondary" onClick={() => setShowSubmitModal(false)}>Cancel</Button>
          <Button loading={submitting} onClick={handleSubmit}>Confirm Submit</Button>
        </div>
      </Modal>

      {/* Leave Confirmation Modal */}
      <Modal open={showLeaveModal} onClose={() => setShowLeaveModal(false)} title="Leave Writing Task?">
        <p className="text-sm text-muted mb-6">You have unsaved changes. Are you sure you want to leave? Your progress will be saved as a draft.</p>
        <div className="flex gap-3 justify-end">
          <Button variant="secondary" onClick={() => setShowLeaveModal(false)}>Stay</Button>
          <Button variant="destructive" onClick={() => router.push('/writing')}>Leave</Button>
        </div>
      </Modal>
    </div>
  );
}
