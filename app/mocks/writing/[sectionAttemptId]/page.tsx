'use client';

import { useCallback, useMemo, useState } from 'react';
import { useParams, useRouter, useSearchParams } from 'next/navigation';
import { ArrowLeft, Lock, Save, Send } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { WritingEditor } from '@/components/domain/writing-editor';
import { WritingCaseNotePanel } from '@/components/domain/mock-player/WritingCaseNotePanel';
import { WritingPhaseTimer, type WritingPhase } from '@/components/domain/mock-player/WritingPhaseTimer';
import { completeMockSection } from '@/lib/api';
import { deriveDeliveryMode, deliveryModeLabel } from '@/lib/mocks/delivery-mode';

const DEFAULT_READING_SECONDS = 5 * 60;
const DEFAULT_EDITING_SECONDS = 40 * 60;

export default function MockWritingSectionPage() {
  const params = useParams<{ sectionAttemptId: string }>();
  const searchParams = useSearchParams();
  const router = useRouter();
  const sectionAttemptId = params?.sectionAttemptId ?? '';
  const mockAttemptId = searchParams?.get('mockAttemptId') ?? '';
  const mockSectionId = searchParams?.get('mockSectionId') ?? sectionAttemptId;
  const caseNoteHtml = searchParams?.get('caseNoteHtml') ?? undefined;
  const readingSeconds = Number(searchParams?.get('readingWindowSeconds') ?? DEFAULT_READING_SECONDS) || DEFAULT_READING_SECONDS;
  const editingSeconds = Number(searchParams?.get('editingWindowSeconds') ?? DEFAULT_EDITING_SECONDS) || DEFAULT_EDITING_SECONDS;
  // Delivery mode (paper | computer | oet_home) is attached by the mock launch.
  // The Writing task content is identical across modes; this drives an
  // informational badge (Writing's own paper/computer simulation lives on the
  // authored task's SimulationModes).
  const deliveryModeParam = searchParams?.get('deliveryMode');
  const deliveryMode = deriveDeliveryMode(searchParams);

  const [phase, setPhase] = useState<WritingPhase>('reading');
  const [phaseStartedAt, setPhaseStartedAt] = useState(Date.now());
  const [content, setContent] = useState('');
  const [saveStatus, setSaveStatus] = useState<'idle' | 'saving' | 'saved' | 'offline-saved' | 'failed'>('idle');
  const [submitting, setSubmitting] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const wordCount = useMemo(() => content.trim().split(/\s+/).filter(Boolean).length, [content]);

  const autosave = useCallback((next: string) => {
    setContent(next);
    setSaveStatus('saving');
    try {
      if (typeof window !== 'undefined') {
        window.localStorage.setItem(`mock-writing:${sectionAttemptId}`, next);
      }
      window.setTimeout(() => setSaveStatus('offline-saved'), 150);
    } catch {
      setSaveStatus('failed');
    }
  }, [sectionAttemptId]);

  const startEditing = useCallback(() => {
    setPhase('editing');
    setPhaseStartedAt(Date.now());
    setMessage('Reading window complete. The writing editor is now unlocked for 40 minutes.');
  }, []);

  const submit = useCallback(async (source: 'manual' | 'timer') => {
    if (submitting || phase === 'submitted') return;
    setSubmitting(true);
    setError(null);
    try {
      if (mockAttemptId && mockSectionId) {
        await completeMockSection(mockAttemptId, mockSectionId, {
          evidence: {
            source: `mock_writing_${source}`,
            sectionAttemptId,
            wordCount,
            submittedAt: new Date().toISOString(),
            contentPreview: content.slice(0, 400),
          },
        });
      }
      setPhase('submitted');
      setMessage(source === 'timer' ? 'Time expired. Your writing response has been submitted.' : 'Writing response submitted.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not submit the writing section.');
    } finally {
      setSubmitting(false);
    }
  }, [content, mockAttemptId, mockSectionId, phase, sectionAttemptId, submitting, wordCount]);

  const handleTimerExpire = useCallback(() => {
    if (phase === 'reading') {
      startEditing();
      return;
    }
    if (phase === 'editing') {
      void submit('timer');
    }
  }, [phase, startEditing, submit]);

  return (
    <LearnerDashboardShell pageTitle="Writing Mock" subtitle="Strict 5 + 40 minute writing workflow." backHref="/mocks">
      <div className="space-y-6">
        <Button variant="ghost" className="gap-2" onClick={() => router.back()}>
          <ArrowLeft className="h-4 w-4" />
          Back
        </Button>

        {message ? <InlineAlert variant="success">{message}</InlineAlert> : null}
        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <WritingPhaseTimer
          phase={phase}
          durationSeconds={phase === 'reading' ? readingSeconds : editingSeconds}
          startedAt={phaseStartedAt}
          onExpire={handleTimerExpire}
        />

        <div className="grid gap-6 xl:grid-cols-[0.85fr_1.15fr]">
          <WritingCaseNotePanel html={caseNoteHtml} compact={phase === 'editing'} />

          <section className="min-h-[620px] overflow-hidden rounded-2xl border border-border bg-surface shadow-sm" aria-label="Writing response editor">
            <div className="flex flex-wrap items-center justify-between gap-3 border-b border-border bg-background-light px-4 py-3">
              <div>
                <p className="text-sm font-black text-navy">Response</p>
                <p className="text-xs text-muted">{wordCount} words / autosave is local until the backend autosave endpoint is enabled.</p>
              </div>
              <div className="flex items-center gap-2">
                {deliveryModeParam ? (
                  <span className="inline-flex items-center rounded-full border border-border bg-background-light px-3 py-1 text-xs font-black uppercase tracking-widest text-muted">
                    {deliveryModeLabel(deliveryMode)}
                  </span>
                ) : null}
                {phase === 'reading' ? (
                  <span className="inline-flex items-center gap-2 rounded-full bg-warning/10 px-3 py-1 text-xs font-black uppercase tracking-widest text-warning">
                    <Lock className="h-3.5 w-3.5" aria-hidden />
                    Editor locked
                  </span>
                ) : null}
              </div>
            </div>

            <WritingEditor
              value={content}
              onChange={autosave}
              saveStatus={saveStatus}
              disabled={phase !== 'editing'}
              spellCheck={phase !== 'editing'}
              showFontSizeControls
              placeholder={phase === 'reading' ? 'The editor unlocks after the 5-minute reading window.' : 'Write your letter here. AI and grammar assistance are disabled.'}
              className="h-[560px]"
            />
          </section>
        </div>

        <div className="sticky bottom-4 z-10 flex flex-wrap gap-3 rounded-2xl border border-border bg-surface/95 p-3 shadow-lg backdrop-blur">
          {phase === 'reading' ? (
            <Button variant="secondary" onClick={startEditing}>
              Start writing phase now
            </Button>
          ) : null}
          <Button variant="primary" disabled={phase !== 'editing' || submitting} loading={submitting} onClick={() => void submit('manual')}>
            <Send className="h-4 w-4" />
            Submit writing section
          </Button>
          <Button variant="outline" onClick={() => setSaveStatus('offline-saved')}>
            <Save className="h-4 w-4" />
            Confirm local save
          </Button>
        </div>
      </div>
    </LearnerDashboardShell>
  );
}
