'use client';

import { useEffect, useState, useCallback, useRef } from 'react';
import { useRouter } from 'next/navigation';
import { AppShell } from '@/components/layout';
import { AsyncStateWrapper } from '@/components/state';
import { WritingCaseNotesPanel } from '@/components/domain/writing-case-notes-panel';
import { WritingEditor } from '@/components/domain/writing-editor';
import { Button } from '@/components/ui/button';
import { Timer } from '@/components/ui/timer';
import { Modal } from '@/components/ui/modal';
import { InlineAlert } from '@/components/ui/alert';
import { useAnalytics } from '@/hooks/use-analytics';
import { fetchWritingTask, fetchWritingChecklist, submitWritingDraft, submitWritingTask } from '@/lib/api';
import type { WritingTask, ChecklistItem } from '@/lib/mock-data';
import { Send, LogOut, Save, CheckCircle2 } from 'lucide-react';

const DIAGNOSTIC_WRITING_TASK_ID = 'wt-001';
const AUTO_SAVE_DELAY = 3000; // 3s debounce

type SaveStatus = 'idle' | 'saving' | 'saved' | 'offline-saved' | 'failed';

export default function DiagnosticWritingPage() {
  const router = useRouter();
  const { track } = useAnalytics();

  // Data
  const [task, setTask] = useState<WritingTask | null>(null);
  const [checklist, setChecklist] = useState<ChecklistItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();

  // Editor state
  const [content, setContent] = useState('');
  const [saveStatus, setSaveStatus] = useState<SaveStatus>('idle');
  const [caseNotesTab, setCaseNotesTab] = useState<'notes' | 'scratchpad' | 'checklist'>('notes');
  const [scratchpad, setScratchpad] = useState('');

  // Modals
  const [showLeaveModal, setShowLeaveModal] = useState(false);
  const [showSubmitModal, setShowSubmitModal] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  // Timer
  const [timerDone, setTimerDone] = useState(false);

  // Auto-save ref
  const autoSaveTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const contentRef = useRef(content);
  contentRef.current = content;

  const load = useCallback(async () => {
    try {
      setLoading(true);
      setError(undefined);
      const [t, cl] = await Promise.all([
        fetchWritingTask(DIAGNOSTIC_WRITING_TASK_ID),
        fetchWritingChecklist(),
      ]);
      setTask(t);
      setChecklist(cl);
      // Restore local draft
      const draft = localStorage.getItem(`diag-writing-draft-${DIAGNOSTIC_WRITING_TASK_ID}`);
      if (draft) setContent(draft);
      track('task_started', { subTest: 'Writing', mode: 'diagnostic', taskId: DIAGNOSTIC_WRITING_TASK_ID });
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load writing task');
    } finally {
      setLoading(false);
    }
  }, [track]);

  useEffect(() => { load(); }, [load]);

  // Auto-save with debounce
  useEffect(() => {
    if (!content || loading) return;
    if (autoSaveTimer.current) clearTimeout(autoSaveTimer.current);
    autoSaveTimer.current = setTimeout(async () => {
      try {
        setSaveStatus('saving');
        // Save locally first
        localStorage.setItem(`diag-writing-draft-${DIAGNOSTIC_WRITING_TASK_ID}`, content);
        // Then attempt server save
        await submitWritingDraft(DIAGNOSTIC_WRITING_TASK_ID, content);
        setSaveStatus('saved');
      } catch {
        // If server fails, we still have the local save
        setSaveStatus('offline-saved');
      }
    }, AUTO_SAVE_DELAY);
    return () => {
      if (autoSaveTimer.current) clearTimeout(autoSaveTimer.current);
    };
  }, [content, loading]);

  // Warn on leave with unsaved changes
  useEffect(() => {
    const handler = (e: BeforeUnloadEvent) => {
      if (content && saveStatus !== 'saved') {
        e.preventDefault();
      }
    };
    window.addEventListener('beforeunload', handler);
    return () => window.removeEventListener('beforeunload', handler);
  }, [content, saveStatus]);

  const handleSubmit = async () => {
    if (!content.trim()) return;
    try {
      setSubmitting(true);
      await submitWritingTask(DIAGNOSTIC_WRITING_TASK_ID, content);
      localStorage.removeItem(`diag-writing-draft-${DIAGNOSTIC_WRITING_TASK_ID}`);
      track('task_submitted', { subTest: 'Writing', mode: 'diagnostic', taskId: DIAGNOSTIC_WRITING_TASK_ID });
      router.push('/diagnostic/hub');
    } catch {
      setSaveStatus('failed');
    } finally {
      setSubmitting(false);
      setShowSubmitModal(false);
    }
  };

  const handleLeave = () => {
    if (content && saveStatus !== 'saved') {
      setShowLeaveModal(true);
    } else {
      router.push('/diagnostic/hub');
    }
  };

  const wordCount = content.trim().split(/\s+/).filter(Boolean).length;

  const status: 'loading' | 'error' | 'success' =
    loading ? 'loading' : error ? 'error' : 'success';

  return (
    <AppShell
      pageTitle="Diagnostic — Writing"
      distractionFree
      navActions={
        <div className="flex items-center gap-3">
          <Timer
            mode="countdown"
            initialSeconds={45 * 60}
            size="sm"
            onComplete={() => setTimerDone(true)}
          />
          <Button variant="ghost" size="sm" onClick={handleLeave} className="gap-1.5">
            <LogOut className="w-3.5 h-3.5" /> Exit
          </Button>
        </div>
      }
    >
      <AsyncStateWrapper status={status} onRetry={load} errorMessage={error}>
        {task && (
          <div className="flex flex-1 h-[calc(100vh-4rem)] overflow-hidden">
            {/* Case Notes Panel — left side on desktop */}
            <div className="hidden lg:flex w-[380px] shrink-0 border-r border-gray-200">
              <WritingCaseNotesPanel
                caseNotes={task.caseNotes}
                scratchpad={scratchpad}
                onScratchpadChange={setScratchpad}
                checklist={checklist.map((c) => ({
                  id: String(c.id),
                  label: c.text,
                  checked: c.completed,
                }))}
                onChecklistChange={(id, checked) => {
                  setChecklist((prev) =>
                    prev.map((c) =>
                      String(c.id) === id ? { ...c, completed: checked } : c,
                    ),
                  );
                }}
                activeTab={caseNotesTab}
                onTabChange={setCaseNotesTab}
                className="w-full"
              />
            </div>

            {/* Main Editor */}
            <div className="flex-1 flex flex-col min-w-0">
              {/* Time warning */}
              {timerDone && (
                <InlineAlert variant="warning" className="mx-4 mt-2">
                  Time is up! You can still submit your response.
                </InlineAlert>
              )}

              {/* Editor */}
              <WritingEditor
                value={content}
                onChange={setContent}
                saveStatus={saveStatus}
                wordCount={wordCount}
                placeholder="Begin writing your response here..."
                className="flex-1"
              />

              {/* Bottom Bar */}
              <div className="px-4 py-3 border-t border-gray-200 bg-surface flex items-center justify-between gap-4 shrink-0">
                <div className="flex items-center gap-3 text-xs text-muted">
                  {saveStatus === 'saved' && (
                    <span className="flex items-center gap-1 text-emerald-600">
                      <CheckCircle2 className="w-3.5 h-3.5" /> Saved
                    </span>
                  )}
                  {saveStatus === 'saving' && (
                    <span className="flex items-center gap-1 text-amber-600">
                      <Save className="w-3.5 h-3.5" /> Saving…
                    </span>
                  )}
                  {saveStatus === 'offline-saved' && (
                    <span className="flex items-center gap-1 text-blue-600">
                      <Save className="w-3.5 h-3.5" /> Saved locally
                    </span>
                  )}
                  {saveStatus === 'failed' && (
                    <span className="flex items-center gap-1 text-red-600">Save failed — will retry</span>
                  )}
                </div>

                {/* Mobile: toggle case notes */}
                <div className="lg:hidden">
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() =>
                      setCaseNotesTab(caseNotesTab === 'notes' ? 'notes' : 'notes')
                    }
                  >
                    Case Notes
                  </Button>
                </div>

                <Button
                  size="md"
                  onClick={() => setShowSubmitModal(true)}
                  disabled={!content.trim()}
                  className="gap-1.5"
                >
                  <Send className="w-3.5 h-3.5" /> Submit
                </Button>
              </div>
            </div>
          </div>
        )}
      </AsyncStateWrapper>

      {/* Submit Confirmation Modal */}
      <Modal
        open={showSubmitModal}
        onClose={() => setShowSubmitModal(false)}
        title="Submit Your Response?"
      >
        <div className="space-y-4">
          <p className="text-sm text-muted">
            Once submitted, your response will be sent for AI evaluation. You won&apos;t be able to
            edit it after submission.
          </p>
          <p className="text-xs text-navy font-semibold">
            Word count: {wordCount}
          </p>
          <div className="flex gap-3 justify-end">
            <Button variant="outline" onClick={() => setShowSubmitModal(false)}>
              Continue Writing
            </Button>
            <Button onClick={handleSubmit} loading={submitting} className="gap-1.5">
              <Send className="w-3.5 h-3.5" /> Submit
            </Button>
          </div>
        </div>
      </Modal>

      {/* Leave Confirmation Modal */}
      <Modal
        open={showLeaveModal}
        onClose={() => setShowLeaveModal(false)}
        title="Leave Writing Task?"
      >
        <div className="space-y-4">
          <p className="text-sm text-muted">
            You have unsaved changes. Your draft has been saved locally and you can resume later.
          </p>
          <div className="flex gap-3 justify-end">
            <Button variant="outline" onClick={() => setShowLeaveModal(false)}>
              Stay
            </Button>
            <Button
              variant="destructive"
              onClick={() => router.push('/diagnostic/hub')}
            >
              Leave
            </Button>
          </div>
        </div>
      </Modal>
    </AppShell>
  );
}
