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
  const [showCaseNotesModal, setShowCaseNotesModal] = useState(false);
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
    }
  };

  const handleLeave = () => {
    if (content && saveStatus !== 'saved') {
      setShowLeaveModal(true);
    } else {
      router.push('/diagnostic/hub');
    }
  };

  const status: 'loading' | 'error' | 'success' =
    loading ? 'loading' : error ? 'error' : 'success';

  return (
    <AppShell
      pageTitle="Diagnostic — Writing"
      distractionFree
      navActions={
        <div className="flex flex-wrap items-center gap-2 sm:gap-3">
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
          <div className="flex flex-1 min-h-0 h-[calc(100dvh-4rem)] flex-col overflow-hidden md:flex-row bg-background/50 relative">
            <div className="absolute inset-0 bg-[radial-gradient(ellipse_at_top_right,_var(--tw-gradient-stops))] from-primary/10 via-transparent to-transparent pointer-events-none -z-10 blur-3xl opacity-50" />
            <div className="absolute inset-0 bg-[radial-gradient(ellipse_at_bottom_left,_var(--tw-gradient-stops))] from-success/5 via-transparent to-transparent pointer-events-none -z-10 blur-3xl opacity-50" />
            {/* Case Notes Panel — left side on desktop */}
            <div className="hidden md:flex md:w-[320px] lg:w-[420px] shrink-0 border-r border-border/40 shadow-[8px_0_30px_-15px_rgba(0,0,0,0.05)] z-10 bg-white/40 backdrop-blur-2xl transition-all">
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
                className="w-full bg-transparent border-none shadow-none"
              />
            </div>

            {/* Main Editor */}
            <div className="flex-1 flex flex-col min-w-0 relative">
              {/* Time warning */}
              {timerDone && (
                <div className="px-4 pt-3 pb-1 z-20 relative">
                  <InlineAlert variant="warning" className="shadow-lg shadow-warning/10 ring-1 ring-warning/20 border-0 bg-white/80 backdrop-blur-xl">
                    <span className="font-bold">Time is up!</span> You can still submit your response.
                  </InlineAlert>
                </div>
              )}

              {/* Editor */}
              <div className="flex-1 relative z-10 m-2 sm:m-4 md:m-6 rounded-3xl overflow-hidden shadow-[0_8px_30px_rgb(0,0,0,0.04)] ring-1 ring-primary/10 bg-white/70 backdrop-blur-3xl transition-all duration-300 hover:shadow-[0_8px_40px_rgb(0,0,0,0.08)]">
                <WritingEditor
                  value={content}
                  onChange={setContent}
                  saveStatus={saveStatus}
                  placeholder="Begin writing your response here..."
                  className="flex-1 rounded-3xl overflow-hidden"
                />
              </div>

              {/* Bottom Floating Bar */}
              <div className="px-5 py-4 mb-4 mx-4 sm:mx-6 md:mx-auto md:max-w-2xl bg-white/80 backdrop-blur-2xl border border-primary/10 shadow-[0_16px_40px_-12px_rgba(0,0,0,0.15)] ring-1 ring-black/5 rounded-3xl flex items-center justify-between gap-4 shrink-0 relative z-20 hover:scale-[1.01] transition-transform duration-300">
                <div className="flex items-center gap-3 text-xs font-bold text-navy/60 uppercase tracking-wider">
                  {saveStatus === 'saved' && (
                    <span className="flex items-center gap-1.5 text-success bg-success/5 px-2 py-1 rounded-md">
                      <CheckCircle2 className="w-3.5 h-3.5" /> Saved
                    </span>
                  )}
                  {saveStatus === 'saving' && (
                    <span className="flex items-center gap-1.5 text-warning bg-warning/5 px-2 py-1 rounded-md animate-pulse">
                      <Save className="w-3.5 h-3.5" /> Saving…
                    </span>
                  )}
                  {saveStatus === 'offline-saved' && (
                    <span className="flex items-center gap-1.5 text-info bg-info/5 px-2 py-1 rounded-md">
                      <Save className="w-3.5 h-3.5" /> Saved locally
                    </span>
                  )}
                  {saveStatus === 'failed' && (
                    <span className="flex items-center gap-1.5 text-danger bg-danger/5 px-2 py-1 rounded-md">Save failed</span>
                  )}
                  {saveStatus === 'idle' && (
                    <span className="flex items-center gap-1.5 text-navy/40">Draft</span>
                  )}
                </div>

                {/* Mobile: toggle case notes */}
                <div className="md:hidden">
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => setShowCaseNotesModal(true)}
                    className="rounded-full shadow-sm"
                  >
                    Case Notes
                  </Button>
                </div>

                <Button
                  size="lg"
                  onClick={handleSubmit}
                  loading={submitting}
                  disabled={!content.trim() || submitting}
                  className="gap-2 rounded-full px-8 font-black shadow-primary/20 shadow-lg hover:shadow-primary/30 transition-all group overflow-hidden relative"
                >
                  <span className="absolute inset-0 w-full h-full bg-gradient-to-r from-white/0 via-white/20 to-white/0 -translate-x-[150%] group-hover:translate-x-[150%] transition-transform duration-700 ease-in-out" />
                  <Send className="w-4 h-4 group-hover:-translate-y-1 group-hover:translate-x-1 group-hover:scale-110 transition-transform duration-300 drop-shadow-sm" /> 
                  Submit Task
                </Button>
              </div>
            </div>
          </div>
        )}
      </AsyncStateWrapper>

      {/* Submit Confirmation Modal removed: per business requirement,
          submission is one-tap and the platform performs no word-count checks. */}

      <Modal
        open={showCaseNotesModal}
        onClose={() => setShowCaseNotesModal(false)}
        title="Case Notes"
        size="lg"
      >
        <div className="h-[70dvh]">
          <WritingCaseNotesPanel
            caseNotes={task?.caseNotes ?? ''}
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
          />
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
