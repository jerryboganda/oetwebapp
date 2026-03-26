'use client';

import { useState, useEffect, useCallback } from 'react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Tabs, TabPanel } from '@/components/ui/tabs';
import { Select, Textarea } from '@/components/ui/form-controls';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Timer } from '@/components/ui/timer';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Info, Save, Send, MessageSquare, RotateCcw } from 'lucide-react';
import { WritingCaseNotesPanel } from '@/components/domain/writing-case-notes-panel';
import { useParams, useRouter } from 'next/navigation';
import { fetchWritingReviewDetail, saveDraftReview, submitExpertWritingReview, requestRework } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { useExpertStore } from '@/lib/stores/expert-store';
import type { WritingReviewDetail, WritingCriterionKey, AnchoredComment } from '@/lib/types/expert';

type AsyncStatus = 'loading' | 'error' | 'success';

const CRITERIA: { key: WritingCriterionKey; label: string }[] = [
  { key: 'purpose', label: 'Purpose' },
  { key: 'content', label: 'Content' },
  { key: 'conciseness', label: 'Conciseness & Clarity' },
  { key: 'genre', label: 'Genre & Style' },
  { key: 'organization', label: 'Organisation' },
  { key: 'language', label: 'Language' },
];

const BAND_OPTIONS = [
  { value: '7', label: '7 (Excellent)' },
  { value: '6', label: '6 (Good)' },
  { value: '5', label: '5 (Satisfactory)' },
  { value: '4', label: '4 (Borderline)' },
  { value: '3', label: '3 (Needs Improvement)' },
  { value: '2', label: '2 (Poor)' },
  { value: '1', label: '1 (Very Poor)' },
  { value: '0', label: '0 (Unscorable)' },
];

export default function WritingReviewWorkspace() {
  const params = useParams();
  const reviewRequestId = params?.reviewRequestId as string | undefined;
  const router = useRouter();

  // Page state
  const [reviewDetail, setReviewDetail] = useState<WritingReviewDetail | null>(null);
  const [pageStatus, setPageStatus] = useState<AsyncStatus>('loading');
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState('response');

  // Rubric state
  const { draftScores, setDraftScores, draftCriterionComments, setDraftCriterionComments, draftFinalComment, setDraftFinalComment, clearDraft } = useExpertStore();
  const [scores, setScores] = useState<Record<string, string>>({});
  const [criterionComments, setCriterionComments] = useState<Record<string, string>>({});
  const [finalComment, setFinalComment] = useState('');
  const [validationErrors, setValidationErrors] = useState<Set<string>>(new Set());

  // Anchored comments
  const [anchoredComments, setAnchoredComments] = useState<AnchoredComment[]>([]);
  const [selectedText, setSelectedText] = useState<{ text: string; start: number; end: number } | null>(null);

  // Toast/save state
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const [isSaving, setIsSaving] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isReworking, setIsReworking] = useState(false);
  const [showReworkPrompt, setShowReworkPrompt] = useState(false);
  const [reworkReason, setReworkReason] = useState('');
  const [scratchpad, setScratchpad] = useState('');
  const [caseNotesTab, setCaseNotesTab] = useState<'notes' | 'scratchpad' | 'checklist'>('notes');

  // SLA countdown
  const [slaSeconds, setSlaSeconds] = useState(0);

  // Load review detail
  useEffect(() => {
    if (!reviewRequestId) return;
    let cancelled = false;
    (async () => {
      try {
        setPageStatus('loading');
        const detail = await fetchWritingReviewDetail(reviewRequestId);
        if (cancelled) return;
        setReviewDetail(detail);
        setPageStatus('success');
        // Calculate SLA remaining seconds
        const remaining = Math.max(0, Math.floor((new Date(detail.slaDue).getTime() - Date.now()) / 1000));
        setSlaSeconds(remaining);
        analytics.track('review_started', { reviewRequestId, type: 'writing' });
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

  // Auto-save to store on change
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

  const handleSaveDraft = useCallback(async () => {
    if (!reviewRequestId) return;
    setIsSaving(true);
    try {
      const numScores: Record<string, number> = {};
      Object.entries(scores).forEach(([k, v]) => { if (v) numScores[k] = parseInt(v); });
      await saveDraftReview({ reviewRequestId, scores: numScores, criterionComments, finalComment, comments: anchoredComments, savedAt: new Date().toISOString() });
      setToast({ variant: 'success', message: 'Draft saved successfully.' });
      analytics.track('review_draft_saved', { reviewRequestId });
    } catch {
      setToast({ variant: 'error', message: 'Failed to save draft. Your work is preserved locally.' });
    } finally {
      setIsSaving(false);
    }
  }, [reviewRequestId, scores, criterionComments, finalComment, anchoredComments]);

  const handleSubmit = useCallback(async () => {
    if (!reviewRequestId) return;
    // Validate all scores
    const missing = new Set<string>();
    CRITERIA.forEach(c => { if (!scores[c.key]) missing.add(c.key); });
    if (missing.size > 0) { setValidationErrors(missing); setToast({ variant: 'error', message: `Please complete all ${missing.size} rubric score(s) before submitting.` }); return; }
    if (!finalComment.trim()) { setToast({ variant: 'error', message: 'Please provide a final overall comment.' }); return; }

    setIsSubmitting(true);
    try {
      const numScores: Record<string, number> = {};
      Object.entries(scores).forEach(([k, v]) => { numScores[k] = parseInt(v); });
      await submitExpertWritingReview(reviewRequestId, { scores: numScores, criterionComments, finalComment });
      analytics.track('review_submitted', { reviewRequestId, type: 'writing' });
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

  // Text selection for anchored comments
  const handleTextSelection = () => {
    const sel = window.getSelection();
    if (!sel || sel.isCollapsed || !sel.rangeCount) { setSelectedText(null); return; }
    const range = sel.getRangeAt(0);
    const container = document.getElementById('learner-response-content');
    if (!container || !container.contains(range.startContainer)) { setSelectedText(null); return; }
    setSelectedText({ text: sel.toString(), start: range.startOffset, end: range.endOffset });
  };

  const addAnchoredComment = (commentText: string) => {
    if (!selectedText || !commentText.trim()) return;
    const comment: AnchoredComment = { id: `ac-${Date.now()}`, text: commentText, startOffset: selectedText.start, endOffset: selectedText.end, createdAt: new Date().toISOString() };
    setAnchoredComments(prev => [...prev, comment]);
    setSelectedText(null);
    window.getSelection()?.removeAllRanges();
  };

  const tabOptions = [
    { id: 'response', label: 'Learner Response' },
    { id: 'casenotes', label: 'Case Notes' },
    { id: 'ai', label: 'AI Draft Feedback' },
  ];

  return (
    <div className="h-screen flex flex-col md:flex-row overflow-hidden bg-background-light">
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <AsyncStateWrapper status={pageStatus} onRetry={() => reviewRequestId && window.location.reload()} errorMessage={errorMsg ?? undefined}>
        {/* Left Pane: Submission Context */}
        <div className="flex-1 w-full md:w-1/2 flex flex-col border-r border-gray-200 overflow-hidden">
          <Tabs tabs={tabOptions} activeTab={activeTab} onChange={setActiveTab} className="bg-surface" />

          <div className="flex-1 overflow-y-auto p-4 bg-white">
            <TabPanel id="response" activeTab={activeTab} className="h-full">
              <div id="learner-response-content" className="whitespace-pre-wrap font-serif text-[15px] leading-relaxed text-navy" onMouseUp={handleTextSelection} role="article" aria-label="Learner response text">
                {reviewDetail?.learnerResponse}
              </div>

              {/* Anchored comment prompt */}
              {selectedText && (
                <div className="mt-3 p-3 bg-blue-50 border border-blue-200 rounded-lg">
                  <p className="text-xs text-blue-700 mb-2 flex items-center gap-1"><MessageSquare className="w-3 h-3" /> Add comment for selected text:</p>
                  <p className="text-xs text-muted italic mb-2 truncate">&ldquo;{selectedText.text.slice(0, 80)}{selectedText.text.length > 80 ? '…' : ''}&rdquo;</p>
                  <div className="flex gap-2">
                    <input
                      type="text"
                      placeholder="Your comment…"
                      className="flex-1 text-sm border border-gray-200 rounded px-2 py-1 focus:outline-none focus:ring-2 focus:ring-primary/30"
                      onKeyDown={(e) => { if (e.key === 'Enter') { addAnchoredComment((e.target as HTMLInputElement).value); (e.target as HTMLInputElement).value = ''; } }}
                      aria-label="Anchored comment text"
                    />
                    <Button size="sm" variant="outline" onClick={() => setSelectedText(null)}>Cancel</Button>
                  </div>
                </div>
              )}

              {/* Show existing anchored comments */}
              {anchoredComments.length > 0 && (
                <div className="mt-4 space-y-2">
                  <h4 className="text-xs font-semibold text-muted uppercase tracking-wide">Anchored Comments ({anchoredComments.length})</h4>
                  {anchoredComments.map(c => (
                    <div key={c.id} className="p-2 bg-yellow-50 border border-yellow-200 rounded text-sm flex justify-between items-start">
                      <p className="text-navy">{c.text}</p>
                      <button onClick={() => setAnchoredComments(prev => prev.filter(x => x.id !== c.id))} className="text-muted hover:text-error text-xs ml-2 shrink-0" aria-label="Remove comment">&times;</button>
                    </div>
                  ))}
                </div>
              )}
            </TabPanel>

            <TabPanel id="casenotes" activeTab={activeTab} className="h-full">
              <WritingCaseNotesPanel
                caseNotes={reviewDetail?.caseNotes ?? ''}
                scratchpad={scratchpad}
                onScratchpadChange={setScratchpad}
                activeTab={caseNotesTab}
                onTabChange={setCaseNotesTab}
              />
            </TabPanel>

            <TabPanel id="ai" activeTab={activeTab} className="h-full">
              <InlineAlert variant="info" title="Advisory Only" className="mb-4">
                This AI-generated draft feedback is for reference only. Do not treat as authoritative.
              </InlineAlert>
              <div className="whitespace-pre-wrap text-sm text-navy bg-blue-50 p-4 rounded-md border border-blue-100">
                {reviewDetail?.aiDraftFeedback}
              </div>
            </TabPanel>
          </div>
        </div>

        {/* Right Pane: Rubric & Entry */}
        <div className="w-full md:w-[450px] lg:w-[500px] flex flex-col bg-surface overflow-hidden">
          <div className="p-4 border-b border-gray-200 flex justify-between items-center bg-white" role="banner">
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
            {CRITERIA.map(({ key, label }) => (
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
