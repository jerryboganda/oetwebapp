'use client';

import { useState, useEffect, useCallback } from 'react';
import { Button } from '@/components/ui/button';
import { Tabs, TabPanel } from '@/components/ui/tabs';
import { Select, Textarea } from '@/components/ui/form-controls';
import { Badge } from '@/components/ui/badge';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Timer } from '@/components/ui/timer';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Save, Send, Flag, PlayCircle, MessageSquare, RotateCcw } from 'lucide-react';
import { useParams, useRouter } from 'next/navigation';
import { AudioPlayerWaveform } from '@/components/domain/audio-player-waveform';
import { SpeakingRoleCard } from '@/components/domain/speaking-role-card';
import { fetchSpeakingReviewDetail, saveDraftReview, submitExpertSpeakingReview, requestRework } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { useExpertStore } from '@/lib/stores/expert-store';
import type { SpeakingReviewDetail, SpeakingCriterionKey, TimestampComment, ExpertTranscriptLine } from '@/lib/types/expert';

type AsyncStatus = 'loading' | 'error' | 'success';

const LINGUISTIC_CRITERIA: { key: SpeakingCriterionKey; label: string }[] = [
  { key: 'intelligibility', label: 'Intelligibility' },
  { key: 'fluency', label: 'Fluency' },
  { key: 'appropriateness', label: 'Appropriateness' },
  { key: 'grammar', label: 'Grammar' },
];

const CLINICAL_CRITERIA: { key: SpeakingCriterionKey; label: string }[] = [
  { key: 'clinicalCommunication', label: 'Clinical Communication Skills' },
];

const ALL_CRITERIA = [...LINGUISTIC_CRITERIA, ...CLINICAL_CRITERIA];

const BAND_OPTIONS = [
  { value: '6', label: '6 (Excellent)' },
  { value: '5', label: '5 (Good)' },
  { value: '4', label: '4 (Satisfactory)' },
  { value: '3', label: '3 (Borderline)' },
  { value: '2', label: '2 (Poor)' },
  { value: '1', label: '1 (Very Poor)' },
  { value: '0', label: '0 (Unscorable)' },
];

export default function SpeakingReviewWorkspace() {
  const params = useParams();
  const reviewRequestId = params?.reviewRequestId as string | undefined;
  const router = useRouter();

  // Page state
  const [reviewDetail, setReviewDetail] = useState<SpeakingReviewDetail | null>(null);
  const [pageStatus, setPageStatus] = useState<AsyncStatus>('loading');
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState('transcript');
  const [currentTime, setCurrentTime] = useState(0);
  const [seekTo, setSeekTo] = useState<number | null>(null);

  // Rubric state
  const { draftScores, setDraftScores, draftCriterionComments, setDraftCriterionComments, draftFinalComment, setDraftFinalComment, clearDraft } = useExpertStore();
  const [scores, setScores] = useState<Record<string, string>>({});
  const [criterionComments, setCriterionComments] = useState<Record<string, string>>({});
  const [finalComment, setFinalComment] = useState('');
  const [validationErrors, setValidationErrors] = useState<Set<string>>(new Set());

  // Timestamp comments
  const [timestampComments, setTimestampComments] = useState<TimestampComment[]>([]);
  const [activeCommentLine, setActiveCommentLine] = useState<string | null>(null);

  // Toast/save state
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const [isSaving, setIsSaving] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isReworking, setIsReworking] = useState(false);
  const [showReworkPrompt, setShowReworkPrompt] = useState(false);
  const [reworkReason, setReworkReason] = useState('');

  // SLA
  const [slaSeconds, setSlaSeconds] = useState(0);

  // Load review detail
  useEffect(() => {
    if (!reviewRequestId) return;
    let cancelled = false;
    (async () => {
      try {
        setPageStatus('loading');
        const detail = await fetchSpeakingReviewDetail(reviewRequestId);
        if (cancelled) return;
        setReviewDetail(detail);
        setPageStatus('success');
        const remaining = Math.max(0, Math.floor((new Date(detail.slaDue).getTime() - Date.now()) / 1000));
        setSlaSeconds(remaining);
        analytics.track('review_started', { reviewRequestId, type: 'speaking' });
      } catch {
        if (!cancelled) { setErrorMsg('Failed to load review details.'); setPageStatus('error'); }
      }
    })();
    return () => { cancelled = true; };
  }, [reviewRequestId]);

  // Restore draft from store
  useEffect(() => {
    if (Object.keys(draftScores).length > 0) {
      const restored: Record<string, string> = {};
      Object.entries(draftScores).forEach(([k, v]) => { restored[k] = String(v); });
      setScores(restored);
    }
    if (Object.keys(draftCriterionComments).length > 0) setCriterionComments(draftCriterionComments);
    if (draftFinalComment) setFinalComment(draftFinalComment);
  }, [draftCriterionComments, draftFinalComment, draftScores]);

  // Auto-save to store
  useEffect(() => {
    const numScores: Record<string, number> = {};
    Object.entries(scores).forEach(([k, v]) => { if (v) numScores[k] = parseInt(v); });
    setDraftScores(numScores);
    setDraftCriterionComments(criterionComments);
    setDraftFinalComment(finalComment);
  }, [scores, criterionComments, finalComment, setDraftScores, setDraftCriterionComments, setDraftFinalComment]);

  const handleScoreChange = (criterion: string, val: string) => {
    setScores(prev => ({ ...prev, [criterion]: val }));
    setValidationErrors(prev => { const next = new Set(prev); next.delete(criterion); return next; });
  };

  const handleTranscriptClick = (time: number) => {
    setSeekTo(time);
    setTimeout(() => setSeekTo(null), 100);
  };

  // Auto-scroll transcript
  useEffect(() => {
    if (!reviewDetail || activeTab !== 'transcript') return;
    const activeLine = reviewDetail.transcriptLines.find(l => currentTime >= l.startTime && currentTime <= l.endTime);
    if (activeLine) {
      const el = document.getElementById(`transcript-line-${activeLine.id}`);
      if (el) el.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }
  }, [currentTime, activeTab, reviewDetail]);

  const addTimestampComment = (lineId: string, line: ExpertTranscriptLine, commentText: string) => {
    if (!commentText.trim()) return;
    const comment: TimestampComment = { id: `tc-${Date.now()}`, text: commentText, timestampStart: line.startTime, timestampEnd: line.endTime, createdAt: new Date().toISOString() };
    setTimestampComments(prev => [...prev, comment]);
    setActiveCommentLine(null);
  };

  const handleSaveDraft = useCallback(async () => {
    if (!reviewRequestId) return;
    setIsSaving(true);
    try {
      const numScores: Record<string, number> = {};
      Object.entries(scores).forEach(([k, v]) => { if (v) numScores[k] = parseInt(v); });
      await saveDraftReview({ reviewRequestId, scores: numScores, criterionComments, finalComment, comments: timestampComments, savedAt: new Date().toISOString() });
      setToast({ variant: 'success', message: 'Draft saved successfully.' });
      analytics.track('review_draft_saved', { reviewRequestId });
    } catch {
      setToast({ variant: 'error', message: 'Failed to save draft. Your work is preserved locally.' });
    } finally {
      setIsSaving(false);
    }
  }, [reviewRequestId, scores, criterionComments, finalComment, timestampComments]);

  const handleSubmit = useCallback(async () => {
    if (!reviewRequestId) return;
    const missing = new Set<string>();
    ALL_CRITERIA.forEach(c => { if (!scores[c.key]) missing.add(c.key); });
    if (missing.size > 0) { setValidationErrors(missing); setToast({ variant: 'error', message: `Please complete all ${missing.size} rubric score(s).` }); return; }
    if (!finalComment.trim()) { setToast({ variant: 'error', message: 'Please provide a final overall comment.' }); return; }

    setIsSubmitting(true);
    try {
      const numScores: Record<string, number> = {};
      Object.entries(scores).forEach(([k, v]) => { numScores[k] = parseInt(v); });
      await submitExpertSpeakingReview(reviewRequestId, { scores: numScores, criterionComments, finalComment });
      analytics.track('review_submitted', { reviewRequestId, type: 'speaking' });
      clearDraft();
      setToast({ variant: 'success', message: 'Review submitted successfully!' });
      setTimeout(() => router.push('/expert/queue'), 1000);
    } catch {
      setToast({ variant: 'error', message: 'Failed to submit review. Please try again.' });
    } finally {
      setIsSubmitting(false);
    }
  }, [reviewRequestId, scores, criterionComments, finalComment, clearDraft, router]);

  const handleRework = useCallback(async () => {
    if (!reviewRequestId || !reworkReason.trim()) {
      setToast({ variant: 'error', message: 'Please provide a reason for the rework request.' });
      return;
    }
    setIsReworking(true);
    try {
      await requestRework(reviewRequestId, reworkReason);
      setToast({ variant: 'success', message: 'Rework request submitted.' });
      setShowReworkPrompt(false);
      setReworkReason('');
      setTimeout(() => router.push('/expert/queue'), 1000);
    } catch {
      setToast({ variant: 'error', message: 'Failed to submit rework request.' });
    } finally {
      setIsReworking(false);
    }
  }, [reviewRequestId, reworkReason, router]);

  // Keyboard shortcuts
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key === 's') { e.preventDefault(); handleSaveDraft(); }
      if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') { e.preventDefault(); handleSubmit(); }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [handleSaveDraft, handleSubmit]);

  const tabOptions = [
    { id: 'transcript', label: 'Transcript & Audio' },
    { id: 'rolecard', label: 'Role Card' },
    { id: 'aiflags', label: 'AI Flags' },
  ];

  const renderCriteriaGroup = (title: string, criteria: { key: SpeakingCriterionKey; label: string }[]) => (
    <>
      <h3 className="font-bold text-navy border-b border-gray-200 pb-2">{title}</h3>
      {criteria.map(({ key, label }) => (
        <div key={key} className={`p-3 bg-white border rounded-md ${validationErrors.has(key) ? 'border-red-400 ring-1 ring-red-200' : 'border-gray-200'}`}>
          <Select
            label={label}
            value={scores[key] ?? ''}
            onChange={(e) => handleScoreChange(key, e.target.value)}
            options={BAND_OPTIONS}
            placeholder="Select band…"
            error={validationErrors.has(key) ? 'Score required' : undefined}
            aria-label={`Score for ${label}`}
          />
          <Textarea
            placeholder={`Comment on ${label.toLowerCase()}…`}
            value={criterionComments[key] ?? ''}
            onChange={(e) => setCriterionComments(prev => ({ ...prev, [key]: e.target.value }))}
            rows={2}
            className="mt-2"
            aria-label={`Comment for ${label}`}
          />
        </div>
      ))}
    </>
  );

  return (
    <div className="h-screen flex flex-col md:flex-row overflow-hidden bg-background-light">
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <AsyncStateWrapper status={pageStatus} onRetry={() => reviewRequestId && window.location.reload()} errorMessage={errorMsg ?? undefined}>
        {/* Left Pane: Media & Context */}
        <div className="flex-1 w-full md:w-1/2 flex flex-col border-r border-gray-200 overflow-hidden">
          {/* Sticky Audio Player */}
          <div className="p-4 bg-surface border-b border-gray-200 shrink-0">
            <h3 className="text-sm font-semibold text-navy mb-2">Candidate Audio Submission</h3>
            {reviewDetail && (
              <AudioPlayerWaveform
                audioUrl={reviewDetail.audioUrl}
                onTimeUpdate={setCurrentTime}
                seekToTime={seekTo}
              />
            )}
          </div>

          <Tabs tabs={tabOptions} activeTab={activeTab} onChange={setActiveTab} className="bg-surface shrink-0" />

          <div className="flex-1 overflow-y-auto p-4 bg-white" role="region" aria-label="Review content">
            <TabPanel id="transcript" activeTab={activeTab} className="h-full space-y-2">
              {!reviewDetail?.transcriptLines.length && (
                <InlineAlert variant="warning">Transcript is still being processed. You may begin reviewing with the audio.</InlineAlert>
              )}
              {reviewDetail?.transcriptLines.map((line) => {
                const isActive = currentTime >= line.startTime && currentTime <= line.endTime;
                const lineComments = timestampComments.filter(c => c.timestampStart === line.startTime);
                return (
                  <div key={line.id} id={`transcript-line-${line.id}`}>
                    <div
                      className={`p-3 rounded-lg border transition-colors cursor-pointer ${isActive ? 'bg-blue-50 border-blue-200 shadow-sm' : 'bg-white border-transparent hover:border-gray-200'}`}
                      onClick={() => handleTranscriptClick(line.startTime)}
                      role="button"
                      aria-label={`Seek to ${line.startTime.toFixed(1)} seconds — ${line.speaker}`}
                    >
                      <div className="flex items-center justify-between mb-1">
                        <div className="flex items-center gap-2">
                          <span className={`text-xs font-bold ${line.speaker === 'candidate' ? 'text-primary' : 'text-muted'}`}>{line.speaker === 'candidate' ? 'Candidate' : 'Interlocutor'}</span>
                          <span className="text-xs text-muted flex items-center gap-1"><PlayCircle className="w-3 h-3" /> {line.startTime.toFixed(1)}s</span>
                        </div>
                        <button
                          className="text-xs text-muted hover:text-primary flex items-center gap-1"
                          onClick={(e) => { e.stopPropagation(); setActiveCommentLine(activeCommentLine === line.id ? null : line.id); }}
                          aria-label={`Add comment at ${line.startTime.toFixed(1)}s`}
                        >
                          <MessageSquare className="w-3 h-3" /> Comment
                        </button>
                      </div>
                      <p className="text-sm text-navy">{line.text}</p>
                    </div>

                    {/* Timestamp comment input */}
                    {activeCommentLine === line.id && (
                      <div className="ml-4 mt-1 p-2 bg-blue-50 border border-blue-200 rounded">
                        <div className="flex gap-2">
                          <input
                            type="text"
                            placeholder="Your comment…"
                            className="flex-1 text-sm border border-gray-200 rounded px-2 py-1 focus:outline-none focus:ring-2 focus:ring-primary/30"
                            autoFocus
                            onKeyDown={(e) => { if (e.key === 'Enter') { addTimestampComment(line.id, line, (e.target as HTMLInputElement).value); (e.target as HTMLInputElement).value = ''; } }}
                            aria-label="Timestamp comment text"
                          />
                          <Button size="sm" variant="outline" onClick={() => setActiveCommentLine(null)}>Cancel</Button>
                        </div>
                      </div>
                    )}

                    {/* Existing comments for this line */}
                    {lineComments.map(c => (
                      <div key={c.id} className="ml-4 mt-1 p-2 bg-yellow-50 border border-yellow-200 rounded text-sm flex justify-between items-start">
                        <p className="text-navy">{c.text}</p>
                        <button onClick={() => setTimestampComments(prev => prev.filter(x => x.id !== c.id))} className="text-muted hover:text-error text-xs ml-2 shrink-0" aria-label="Remove comment">&times;</button>
                      </div>
                    ))}
                  </div>
                );
              })}
            </TabPanel>

            <TabPanel id="rolecard" activeTab={activeTab} className="h-full">
              {reviewDetail && (
                <SpeakingRoleCard
                  role={reviewDetail.roleCard.role}
                  setting={reviewDetail.roleCard.setting}
                  patient={reviewDetail.roleCard.patient}
                  task={reviewDetail.roleCard.task}
                  background={reviewDetail.roleCard.background}
                />
              )}
            </TabPanel>

            <TabPanel id="aiflags" activeTab={activeTab} className="h-full">
              {!reviewDetail?.aiFlags.length ? (
                <InlineAlert variant="info">AI analysis pending. Flags will appear once processing completes.</InlineAlert>
              ) : (
                <div className="space-y-3" role="list" aria-label="AI-detected flags">
                  {reviewDetail.aiFlags.map(flag => (
                    <div key={flag.id} className={`p-3 border rounded-md ${flag.severity === 'warning' ? 'border-amber-200 bg-amber-50' : 'border-blue-200 bg-blue-50'}`} role="listitem">
                      <div className="flex items-center gap-2 font-semibold text-sm mb-1" style={{ color: flag.severity === 'warning' ? '#92400e' : '#1e40af' }}>
                        <Flag className="w-4 h-4" /> {flag.type}
                      </div>
                      <p className="text-xs" style={{ color: flag.severity === 'warning' ? '#78350f' : '#1e3a8a' }}>{flag.message}</p>
                      <Button variant="outline" size="sm" className="mt-2 h-7 text-xs" onClick={() => handleTranscriptClick(flag.timestampStart)} aria-label={`Go to ${flag.timestampStart.toFixed(1)}s`}>
                        Go to {flag.timestampStart.toFixed(1)}s
                      </Button>
                    </div>
                  ))}
                </div>
              )}
            </TabPanel>
          </div>
        </div>

        {/* Right Pane: Rubric & Entry */}
        <div className="w-full md:w-[450px] lg:w-[500px] flex flex-col bg-surface overflow-hidden">
          <div className="p-4 border-b border-gray-200 flex justify-between items-center bg-white shrink-0" role="banner">
            <div>
              <h2 className="font-semibold text-navy">Review Rubric</h2>
              <p className="text-xs text-muted">ID: {reviewRequestId}</p>
            </div>
            <div className="flex items-center gap-2">
              {slaSeconds > 0 ? (
                <Timer mode="countdown" initialSeconds={slaSeconds} running size="sm" showWarning />
              ) : (
                <Badge variant="danger">SLA Overdue</Badge>
              )}
            </div>
          </div>

          <div className="flex-1 overflow-y-auto p-4 space-y-4">
            {renderCriteriaGroup('Linguistic Criteria', LINGUISTIC_CRITERIA)}
            <div className="mt-2">{renderCriteriaGroup('Clinical Communication', CLINICAL_CRITERIA)}</div>

            <div className="p-3 bg-white border border-gray-200 rounded-md">
              <Textarea
                label="Final Overall Comment"
                placeholder="Provide a summary of the learner's performance…"
                value={finalComment}
                onChange={(e) => setFinalComment(e.target.value)}
                rows={5}
                aria-label="Final overall comment"
              />
              <p className="text-xs text-muted mt-1 text-right">{finalComment.length} characters</p>
            </div>

            <p className="text-xs text-muted">Keyboard: Ctrl+S save draft · Ctrl+Enter submit</p>
          </div>

          <div className="p-4 border-t border-gray-200 bg-white flex justify-between items-center shrink-0" role="toolbar" aria-label="Review actions">
            <div className="flex items-center gap-2">
              <Button variant="outline" onClick={handleSaveDraft} disabled={isSaving} className="flex items-center gap-2" aria-label="Save draft">
                <Save className="w-4 h-4" /> {isSaving ? 'Saving…' : 'Save Draft'}
              </Button>
              <Button variant="outline" onClick={() => setShowReworkPrompt(!showReworkPrompt)} className="flex items-center gap-2" aria-label="Request rework">
                <RotateCcw className="w-4 h-4" /> Rework
              </Button>
            </div>
            <Button onClick={handleSubmit} disabled={isSubmitting} className="flex items-center gap-2" aria-label="Submit review">
              <Send className="w-4 h-4" /> {isSubmitting ? 'Submitting…' : 'Submit Review'}
            </Button>
          </div>

          {showReworkPrompt && (
            <div className="px-4 pb-4 bg-amber-50 border-t border-amber-200">
              <p className="text-sm font-semibold text-amber-800 my-2">Request Rework</p>
              <textarea
                className="w-full text-sm border border-amber-300 rounded-md px-3 py-2 focus:outline-none focus:ring-2 focus:ring-amber-400/30 bg-white"
                rows={2}
                placeholder="Reason for rework…"
                value={reworkReason}
                onChange={(e) => setReworkReason(e.target.value)}
                aria-label="Rework reason"
              />
              <div className="flex justify-end gap-2 mt-2">
                <Button size="sm" variant="outline" onClick={() => { setShowReworkPrompt(false); setReworkReason(''); }}>Cancel</Button>
                <Button size="sm" onClick={handleRework} disabled={isReworking}>{isReworking ? 'Sending…' : 'Submit Rework'}</Button>
              </div>
            </div>
          )}
        </div>
      </AsyncStateWrapper>
    </div>
  );
}
