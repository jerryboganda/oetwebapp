'use client';

import { useState, useEffect, useCallback, useMemo, useRef } from 'react';
import { Button } from '@/components/ui/button';
import { Tabs, TabPanel } from '@/components/ui/tabs';
import { Select, Textarea } from '@/components/ui/form-controls';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Timer } from '@/components/ui/timer';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { FileAudio, FileText, MessageSquare, Mic, RotateCcw, Save, Send, Square, UploadCloud } from 'lucide-react';
import { WritingCaseNotesPanel } from '@/components/domain/writing-case-notes-panel';
import { useParams, useRouter } from 'next/navigation';
import { addWritingReviewVoiceNote, fetchAuthorizedObjectUrl, fetchExpertLearnerReviewContext, fetchExpertReviewHistory, fetchWritingReviewDetail, isApiError, requestRework, saveDraftReview, submitExpertWritingReview, uploadMedia } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { useExpertStore } from '@/lib/stores/expert-store';
import { paletteFor, WRITING_CRITERIA_ORDER } from '@/lib/writing-criterion-colors';
import type { AnchoredComment, ExpertChecklistItem, ExpertLearnerReviewContext, ExpertReviewHistory, ExpertSavedDraft, WritingCriterionKey, WritingReviewDetail } from '@/lib/types/expert';

type AsyncStatus = 'loading' | 'error' | 'partial' | 'success';

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

// Purpose is scored 0-3 by the OET writing rubric (see backend ExpertService.MaxScoreForCriterion).
const PURPOSE_BAND_OPTIONS = [
  { value: '3', label: '3 (Excellent)' },
  { value: '2', label: '2 (Good)' },
  { value: '1', label: '1 (Borderline)' },
  { value: '0', label: '0 (Unscorable)' },
];
const bandOptionsFor = (criterionKey: string) =>
  criterionKey === 'purpose' ? PURPOSE_BAND_OPTIONS : BAND_OPTIONS;

type DraftCandidate = {
  reviewId: string;
  scores: Record<string, number>;
  criterionComments: Record<string, string>;
  finalComment: string;
  anchoredComments: AnchoredComment[];
  timestampComments: unknown[];
  scratchpad: string;
  checklistItems: ExpertChecklistItem[];
  version?: number;
  updatedAt: string;
};

const DEFAULT_CHECKLIST: ExpertChecklistItem[] = [
  { id: 'purpose', label: 'Purpose is explicit in the opening and remains clear throughout.', checked: false },
  { id: 'content', label: 'Only clinically relevant facts remain in scope for the receiving reader.', checked: false },
  { id: 'organization', label: 'Sequence supports rapid reading and safe follow-up action.', checked: false },
  { id: 'language', label: 'Tone, register, and language control stay appropriate for OET writing.', checked: false },
];

function toDraftSnapshot(reviewId: string, draft: ExpertSavedDraft | null | undefined) {
  if (!draft) {
    return null;
  }

  return {
    reviewId,
    scores: draft.scores,
    criterionComments: draft.criterionComments,
    finalComment: draft.finalComment,
    anchoredComments: draft.anchoredComments,
    timestampComments: draft.timestampComments,
    scratchpad: draft.scratchpad,
    checklistItems: draft.checklistItems,
    version: draft.version,
    updatedAt: draft.savedAt,
  };
}

function pickLatestDraft(localDraft: DraftCandidate | null, serverDraft: DraftCandidate | null): DraftCandidate | null {
  if (!localDraft) return serverDraft;
  if (!serverDraft) return localDraft;
  return new Date(localDraft.updatedAt).getTime() >= new Date(serverDraft.updatedAt).getTime() ? localDraft : serverDraft;
}

function getSelectionOffsets(container: HTMLElement, range: Range) {
  const treeWalker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT);
  let currentNode: Node | null = treeWalker.nextNode();
  let offset = 0;
  let startOffset = -1;
  let endOffset = -1;

  while (currentNode) {
    const textLength = currentNode.textContent?.length ?? 0;
    if (currentNode === range.startContainer) {
      startOffset = offset + range.startOffset;
    }
    if (currentNode === range.endContainer) {
      endOffset = offset + range.endOffset;
      break;
    }
    offset += textLength;
    currentNode = treeWalker.nextNode();
  }

  return { startOffset, endOffset };
}

export default function WritingReviewWorkspace() {
  const params = useParams();
  const rawReviewRequestId = params?.reviewRequestId;
  const reviewRequestId = Array.isArray(rawReviewRequestId) ? rawReviewRequestId[0] ?? '' : rawReviewRequestId ?? '';
  const router = useRouter();
  const { getReviewDraft, upsertReviewDraft, clearReviewDraft } = useExpertStore();

  const [reviewDetail, setReviewDetail] = useState<WritingReviewDetail | null>(null);
  const [pageStatus, setPageStatus] = useState<AsyncStatus>('loading');
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState('response');
  const [scores, setScores] = useState<Record<string, string>>({});
  const [criterionComments, setCriterionComments] = useState<Record<string, string>>({});
  const [finalComment, setFinalComment] = useState('');
  const [validationErrors, setValidationErrors] = useState<Set<string>>(new Set());
  const [anchoredComments, setAnchoredComments] = useState<AnchoredComment[]>([]);
  const [selectedText, setSelectedText] = useState<{ text: string; start: number; end: number } | null>(null);
  const [pendingCriterion, setPendingCriterion] = useState<WritingCriterionKey | undefined>(undefined);
  const [draftVersion, setDraftVersion] = useState<number | undefined>(undefined);
  const [isDirty, _setIsDirty] = useState(false);
  const setIsDirty = useCallback((next: boolean | ((prev: boolean) => boolean), tag?: string) => {
    if (typeof window !== 'undefined') {
      const w = window as unknown as { __dirtyLog?: string[] };
      w.__dirtyLog ??= [];
      const stack = new Error().stack?.split('\n').slice(2, 5).join(' || ') ?? '';
      w.__dirtyLog.push(`${Date.now()} ${tag ?? '?'} ${String(next)} ${stack}`);
    }
    _setIsDirty(next);
  }, []);
  const [lastSavedAt, setLastSavedAt] = useState<string | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const [isSaving, setIsSaving] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isReworking, setIsReworking] = useState(false);
  const [showReworkPrompt, setShowReworkPrompt] = useState(false);
  const [reworkReason, setReworkReason] = useState('');
  const [scratchpad, setScratchpad] = useState('');
  const [checklist, setChecklist] = useState<ExpertChecklistItem[]>(DEFAULT_CHECKLIST);
  const [caseNotesTab, setCaseNotesTab] = useState<'notes' | 'scratchpad' | 'checklist'>('notes');
  const [slaSeconds, setSlaSeconds] = useState(0);
  const [reloadToken, setReloadToken] = useState(0);
  const [isInitialized, setIsInitialized] = useState(false);
  const [reviewHistory, setReviewHistory] = useState<ExpertReviewHistory | null>(null);
  const [learnerContext, setLearnerContext] = useState<ExpertLearnerReviewContext | null>(null);
  const [voiceFile, setVoiceFile] = useState<File | null>(null);
  const [voiceTranscript, setVoiceTranscript] = useState('');
  const [voiceWrittenNotes, setVoiceWrittenNotes] = useState('');
  const [isVoiceSaving, setIsVoiceSaving] = useState(false);
  const [isRecording, setIsRecording] = useState(false);
  const [paperAssetUrls, setPaperAssetUrls] = useState<Record<string, string>>({});
  const [voiceNoteUrls, setVoiceNoteUrls] = useState<Record<string, string>>({});
  const recorderRef = useRef<MediaRecorder | null>(null);
  const recordingChunksRef = useRef<Blob[]>([]);

  const partialMessage = reviewDetail?.artifactStatus?.aiDraftFeedback?.message ?? 'Some review data is still being prepared.';

  const normalizedScoreNumbers = useMemo(
    () => Object.fromEntries(Object.entries(scores).filter(([, value]) => value).map(([key, value]) => [key, Number(value)])),
    [scores],
  );

  const applyDraft = useCallback((draft: DraftCandidate | null) => {
    if (!draft) return;
    setScores(Object.fromEntries(Object.entries(draft.scores).map(([key, value]) => [key, String(value)])));
    setCriterionComments(draft.criterionComments);
    setFinalComment(draft.finalComment);
    setAnchoredComments(draft.anchoredComments);
    setScratchpad(draft.scratchpad ?? '');
    setChecklist(draft.checklistItems?.length > 0 ? draft.checklistItems : DEFAULT_CHECKLIST);
    setDraftVersion(draft.version);
    setLastSavedAt(draft.updatedAt);
  }, []);

  useEffect(() => {
    if (!reviewRequestId) return;
    let cancelled = false;
    (async () => {
      try {
        setPageStatus('loading');
        setErrorMsg(null);
        setIsInitialized(false);
        setScores({});
        setCriterionComments({});
        setFinalComment('');
        setAnchoredComments([]);
        setScratchpad('');
        setChecklist(DEFAULT_CHECKLIST);
        setDraftVersion(undefined);
        setLastSavedAt(null);
        setIsDirty(false, 'load-init');
        const detail = await fetchWritingReviewDetail(reviewRequestId);
        if (cancelled) return;
        const [history, context] = await Promise.all([
          fetchExpertReviewHistory(reviewRequestId),
          fetchExpertLearnerReviewContext(detail.learnerId),
        ]);
        if (cancelled) return;
        setReviewDetail(detail);
        setReviewHistory(history);
        setLearnerContext(context);
        setSlaSeconds(Math.max(0, Math.floor((new Date(detail.slaDue).getTime() - Date.now()) / 1000)));

        const localDraftSnapshot = getReviewDraft(reviewRequestId);
        const latestDraft = pickLatestDraft(localDraftSnapshot, toDraftSnapshot(reviewRequestId, detail.existingDraft));
        applyDraft(latestDraft);
        setIsInitialized(true);
        setPageStatus(detail.artifactStatus?.aiDraftFeedback?.state && detail.artifactStatus.aiDraftFeedback.state !== 'completed' ? 'partial' : 'success');
        analytics.track('review_started', { reviewRequestId, type: 'writing' });
      } catch (error) {
        if (!cancelled) {
          setErrorMsg(isApiError(error) ? error.userMessage : 'Failed to load review details.');
          setPageStatus('error');
        }
      }
    })();
    return () => { cancelled = true; };
  }, [applyDraft, getReviewDraft, reloadToken, reviewRequestId, setIsDirty]);

  useEffect(() => {
    if (!reviewRequestId || !isInitialized) return;
    upsertReviewDraft(reviewRequestId, {
      scores: Object.fromEntries(Object.entries(scores).filter(([, value]) => value).map(([key, value]) => [key, Number(value)])),
      criterionComments,
      finalComment,
      anchoredComments,
      timestampComments: [],
      scratchpad,
      checklistItems: checklist,
      version: draftVersion,
    });
  }, [anchoredComments, checklist, criterionComments, draftVersion, finalComment, isInitialized, reviewRequestId, scratchpad, scores, upsertReviewDraft]);

  useEffect(() => {
    const handleBeforeUnload = (event: BeforeUnloadEvent) => {
      if (!isDirty) return;
      event.preventDefault();
      event.returnValue = '';
    };
    window.addEventListener('beforeunload', handleBeforeUnload);
    return () => window.removeEventListener('beforeunload', handleBeforeUnload);
  }, [isDirty]);

  useEffect(() => () => {
    recorderRef.current?.stream.getTracks().forEach((track) => track.stop());
  }, []);

  useEffect(() => {
    const assets = reviewDetail?.paperAssets ?? [];
    if (!assets.length) {
      return;
    }

    let cancelled = false;
    const createdUrls: string[] = [];
    Promise.all(assets.map(async (asset) => {
      const objectUrl = await fetchAuthorizedObjectUrl(asset.url);
      createdUrls.push(objectUrl);
      return [asset.id, objectUrl] as const;
    }))
      .then((entries) => {
        if (!cancelled) setPaperAssetUrls(Object.fromEntries(entries));
      })
      .catch(() => {
        if (!cancelled) setPaperAssetUrls({});
      });

    return () => {
      cancelled = true;
      createdUrls.forEach((url) => URL.revokeObjectURL(url));
    };
  }, [reviewDetail?.paperAssets]);

  useEffect(() => {
    const notes = reviewDetail?.voiceNotes ?? [];
    if (!notes.length) {
      return;
    }

    let cancelled = false;
    const createdUrls: string[] = [];
    Promise.all(notes.map(async (note) => {
      const objectUrl = await fetchAuthorizedObjectUrl(note.url);
      createdUrls.push(objectUrl);
      return [note.id, objectUrl] as const;
    }))
      .then((entries) => {
        if (!cancelled) setVoiceNoteUrls(Object.fromEntries(entries));
      })
      .catch(() => {
        if (!cancelled) setVoiceNoteUrls({});
      });

    return () => {
      cancelled = true;
      createdUrls.forEach((url) => URL.revokeObjectURL(url));
    };
  }, [reviewDetail?.voiceNotes]);

  const persistDraft = useCallback(async (options?: { quiet?: boolean }) => {
    if (!reviewRequestId) return null;
    const payload = {
      reviewRequestId,
      scores: Object.fromEntries(Object.entries(scores).filter(([, value]) => value).map(([key, value]) => [key, Number(value)])),
      criterionComments,
      finalComment,
      comments: anchoredComments,
      scratchpad,
      checklistItems: checklist,
      savedAt: new Date().toISOString(),
      version: draftVersion,
    };

    const savedDraft = await saveDraftReview(payload);
    setDraftVersion(savedDraft.version);
    setLastSavedAt(savedDraft.savedAt);
    setIsDirty(false, 'persistDraft');
    upsertReviewDraft(reviewRequestId, {
      scores: savedDraft.scores,
      criterionComments: savedDraft.criterionComments,
      finalComment: savedDraft.finalComment,
      anchoredComments: savedDraft.comments as AnchoredComment[],
      timestampComments: [],
      scratchpad: savedDraft.scratchpad ?? '',
      checklistItems: savedDraft.checklistItems ?? DEFAULT_CHECKLIST,
      version: savedDraft.version,
      updatedAt: savedDraft.savedAt,
    });
    if (!options?.quiet) {
      setToast({ variant: 'success', message: 'Draft saved successfully.' });
    }
    analytics.track('review_draft_saved', { reviewRequestId });
    return savedDraft;
  }, [anchoredComments, checklist, criterionComments, draftVersion, finalComment, reviewRequestId, scratchpad, scores, setIsDirty, upsertReviewDraft]);

  const handleSaveDraft = useCallback(async () => {
    setIsSaving(true);
    try {
      await persistDraft();
    } catch (error) {
      setToast({ variant: 'error', message: isApiError(error) ? error.userMessage : 'Failed to save draft. Your local work is still preserved.' });
    } finally {
      setIsSaving(false);
    }
  }, [persistDraft]);

  const handleSubmit = useCallback(async () => {
    if (!reviewRequestId) return;
    const missing = new Set<string>();
    CRITERIA.forEach((criterion) => {
      if (scores[criterion.key] == null) {
        missing.add(criterion.key);
      }
    });
    if (missing.size > 0) {
      setValidationErrors(missing);
      setToast({ variant: 'error', message: `Please complete all ${missing.size} rubric score(s) before submitting.` });
      return;
    }
    if (!finalComment.trim()) {
      setToast({ variant: 'error', message: 'Please provide a final overall comment.' });
      return;
    }
    if ((reviewDetail?.voiceNotes?.length ?? 0) === 0) {
      setActiveTab('voice');
      setToast({ variant: 'error', message: 'Attach a voice note before submitting this writing review.' });
      return;
    }

    setIsSubmitting(true);
    try {
      const savedDraft = await persistDraft({ quiet: true });
      await submitExpertWritingReview(reviewRequestId, {
        scores: normalizedScoreNumbers,
        criterionComments,
        finalComment,
        version: savedDraft?.version ?? draftVersion,
      });
      analytics.track('review_submitted', { reviewRequestId, type: 'writing' });
      clearReviewDraft(reviewRequestId);
      setIsDirty(false, 'submit');
      setToast({ variant: 'success', message: 'Review submitted successfully.' });
      try {
        window.sessionStorage.setItem('expertReviewQueueFlash', 'review-submitted');
      } catch {
        // The local toast above still confirms completion if storage is unavailable.
      }
      window.setTimeout(() => router.push('/expert/queue'), 800);
    } catch (error) {
      setToast({ variant: 'error', message: isApiError(error) ? error.userMessage : 'Failed to submit review. Please try again.' });
    } finally {
      setIsSubmitting(false);
    }
  }, [clearReviewDraft, criterionComments, draftVersion, finalComment, normalizedScoreNumbers, persistDraft, reviewDetail?.voiceNotes?.length, reviewRequestId, router, scores, setIsDirty]);

  const handleRework = useCallback(async () => {
    if (!reviewRequestId) return;
    if (!reworkReason.trim()) {
      setToast({ variant: 'error', message: 'Please provide a reason for the rework request.' });
      return;
    }

    setIsReworking(true);
    try {
      if (isDirty) {
        await persistDraft({ quiet: true });
      }
      await requestRework(reviewRequestId, reworkReason);
      clearReviewDraft(reviewRequestId);
      setToast({ variant: 'success', message: 'Rework request submitted.' });
      setShowReworkPrompt(false);
      setReworkReason('');
      setIsDirty(false, 'rework');
      try {
        window.sessionStorage.setItem('expertReviewQueueFlash', 'rework-submitted');
      } catch {
        // The local toast above still confirms completion if storage is unavailable.
      }
      window.setTimeout(() => router.push('/expert/queue'), 800);
    } catch (error) {
      setToast({ variant: 'error', message: isApiError(error) ? error.userMessage : 'Failed to submit rework request.' });
    } finally {
      setIsReworking(false);
    }
  }, [clearReviewDraft, isDirty, persistDraft, reviewRequestId, reworkReason, router, setIsDirty]);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      const normalizedKey = event.key.toLowerCase();
      if ((event.ctrlKey || event.metaKey) && normalizedKey === 's') {
        event.preventDefault();
        void handleSaveDraft();
      }
      if ((event.ctrlKey || event.metaKey) && normalizedKey === 'enter') {
        event.preventDefault();
        void handleSubmit();
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [handleSaveDraft, handleSubmit]);

  const handleScoreChange = (criterion: string, value: string) => {
    setScores((current) => ({ ...current, [criterion]: value }));
    setValidationErrors((current) => {
      const next = new Set(current);
      next.delete(criterion);
      return next;
    });
    setIsDirty(true, 'handleScoreChange');
  };

  const handleTextSelection = () => {
    const selection = window.getSelection();
    if (!selection || selection.isCollapsed || !selection.rangeCount) {
      setSelectedText(null);
      return;
    }

    const range = selection.getRangeAt(0);
    const container = document.getElementById('learner-response-content');
    if (!container || !container.contains(range.startContainer) || !container.contains(range.endContainer)) {
      setSelectedText(null);
      return;
    }

    const { startOffset, endOffset } = getSelectionOffsets(container, range);
    if (startOffset < 0 || endOffset <= startOffset) {
      setSelectedText(null);
      return;
    }

    setSelectedText({ text: selection.toString(), start: startOffset, end: endOffset });
  };

  const addAnchoredComment = (commentText: string, criterion?: WritingCriterionKey) => {
    if (!selectedText || !commentText.trim()) return;
    const comment: AnchoredComment = {
      id: `ac-${Date.now()}`,
      text: commentText.trim(),
      startOffset: selectedText.start,
      endOffset: selectedText.end,
      createdAt: new Date().toISOString(),
      ...(criterion ? { criterion } : {}),
    };
    setAnchoredComments((current) => [...current, comment]);
    analytics.track('writing_expert_annotation_added', {
      reviewRequestId,
      criterion: criterion ?? 'general',
      length: comment.text.length,
    });
    setSelectedText(null);
    setPendingCriterion(undefined);
    setIsDirty(true, 'addAnchored');
    window.getSelection()?.removeAllRanges();
  };

  const handleStartRecording = useCallback(async () => {
    if (!navigator.mediaDevices?.getUserMedia || typeof MediaRecorder === 'undefined') {
      setToast({ variant: 'error', message: 'Audio recording is not available in this browser.' });
      return;
    }

    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      const recorder = new MediaRecorder(stream);
      recordingChunksRef.current = [];
      recorder.ondataavailable = (event) => {
        if (event.data.size > 0) recordingChunksRef.current.push(event.data);
      };
      recorder.onstop = () => {
        const blob = new Blob(recordingChunksRef.current, { type: recorder.mimeType || 'audio/webm' });
        const file = new File([blob], `dr-ahmed-writing-review-${Date.now()}.webm`, { type: blob.type || 'audio/webm' });
        setVoiceFile(file);
        stream.getTracks().forEach((track) => track.stop());
        recorderRef.current = null;
        setIsRecording(false);
      };
      recorderRef.current = recorder;
      recorder.start();
      setIsRecording(true);
    } catch {
      setToast({ variant: 'error', message: 'Microphone permission was not granted.' });
    }
  }, []);

  const handleStopRecording = useCallback(() => {
    recorderRef.current?.stop();
  }, []);

  const handleSaveVoiceNote = useCallback(async () => {
    if (!reviewRequestId || !voiceFile) {
      setToast({ variant: 'error', message: 'Attach or record an audio note before saving.' });
      return;
    }

    setIsVoiceSaving(true);
    try {
      const uploaded = await uploadMedia(voiceFile);
      const response = await addWritingReviewVoiceNote(reviewRequestId, {
        mediaAssetId: uploaded.id,
        transcriptText: voiceTranscript.trim(),
        writtenNotes: voiceWrittenNotes.trim(),
        rubricScores: normalizedScoreNumbers,
      });
      setReviewDetail((current) => current ? {
        ...current,
        voiceNotes: [...(current.voiceNotes ?? []), response.item],
      } : current);
      setVoiceFile(null);
      setVoiceTranscript('');
      setVoiceWrittenNotes('');
      setToast({ variant: 'success', message: 'Voice note attached to this review.' });
      analytics.track('writing_voice_note_added', { reviewRequestId });
    } catch (error) {
      setToast({ variant: 'error', message: isApiError(error) ? error.userMessage : 'Could not save the voice note.' });
    } finally {
      setIsVoiceSaving(false);
    }
  }, [normalizedScoreNumbers, reviewRequestId, voiceFile, voiceTranscript, voiceWrittenNotes]);

  const tabOptions = [
    { id: 'response', label: 'Learner Response' },
    { id: 'paper', label: 'Paper Assets' },
    { id: 'voice', label: 'Voice Notes' },
    { id: 'casenotes', label: 'Case Notes' },
    { id: 'ai', label: 'AI Draft Feedback' },
  ];

  const workspaceMeta = useMemo(() => {
    if (!reviewDetail) return null;
    return {
      isReadOnly: reviewDetail.permissions?.readOnly ?? false,
      canSaveDraft: reviewDetail.permissions?.canSaveDraft ?? true,
      canSubmit: reviewDetail.permissions?.canSubmit ?? true,
      canRequestRework: reviewDetail.permissions?.canRequestRework ?? true,
    };
  }, [reviewDetail]);

  return (
    <div className="min-h-[var(--app-viewport-height,100dvh)] flex flex-col lg:flex-row bg-background-light">
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => setReloadToken((current) => current + 1)}
        errorMessage={errorMsg ?? undefined}
        partialMessage={partialMessage}
      >
        <div className="flex-1 min-w-0 flex flex-col border-b border-gray-200 overflow-hidden lg:border-b-0 lg:border-r lg:w-1/2">
          <Tabs tabs={tabOptions} activeTab={activeTab} onChange={setActiveTab} className="bg-surface" />

          <div className="flex-1 overflow-y-auto p-4 bg-white">
            <TabPanel id="response" activeTab={activeTab} className="h-full">
              <div id="learner-response-content" className="whitespace-pre-wrap font-serif text-[15px] leading-relaxed text-navy" onMouseUp={handleTextSelection} role="article" aria-label="Learner response text">
                {reviewDetail?.learnerResponse}
              </div>

              {selectedText && !workspaceMeta?.isReadOnly && (
                <div className="mt-3 p-3 bg-blue-50 border border-blue-200 rounded-lg">
                  <p className="text-xs text-blue-700 mb-2 flex items-center gap-1"><MessageSquare className="w-3 h-3" /> Add comment for selected text:</p>
                  <p className="text-xs text-muted italic mb-2 truncate">&ldquo;{selectedText.text.slice(0, 80)}{selectedText.text.length > 80 ? '...' : ''}&rdquo;</p>
                  <div className="mb-2 flex flex-wrap gap-1.5" role="radiogroup" aria-label="Tag this comment with an OET criterion">
                    {WRITING_CRITERIA_ORDER.map((key) => {
                      const palette = paletteFor(key);
                      const active = pendingCriterion === key;
                      return (
                        <button
                          key={key}
                          type="button"
                          role="radio"
                          aria-checked={active}
                          onClick={() => setPendingCriterion(active ? undefined : key)}
                          className={`rounded-full border px-2.5 py-0.5 text-xs font-medium transition-colors ${palette.bgClass} ${palette.borderClass} ${palette.textClass} ${active ? 'ring-2 ring-primary/40 shadow-sm' : 'opacity-80 hover:opacity-100'}`}
                        >
                          {palette.label}
                        </button>
                      );
                    })}
                  </div>
                  <div className="flex gap-2">
                    <input
                      type="text"
                      placeholder="Your comment..."
                      className="flex-1 text-sm border border-gray-200 rounded px-2 py-1 focus:outline-none focus:ring-2 focus:ring-primary/30"
                      onKeyDown={(event) => {
                        if (event.key === 'Enter') {
                          addAnchoredComment((event.target as HTMLInputElement).value, pendingCriterion);
                          (event.target as HTMLInputElement).value = '';
                        }
                      }}
                      aria-label="Anchored comment text"
                    />
                    <Button size="sm" onClick={(event) => { const input = (event.currentTarget.previousElementSibling as HTMLInputElement | null); addAnchoredComment(input?.value ?? '', pendingCriterion); if (input) input.value = ''; }}>Add</Button>
                    <Button size="sm" variant="outline" onClick={() => { setSelectedText(null); setPendingCriterion(undefined); }}>Cancel</Button>
                  </div>
                </div>
              )}

              {anchoredComments.length > 0 && (
                <div className="mt-4 space-y-2">
                  <h4 className="text-xs font-semibold text-muted uppercase tracking-wide">Anchored Comments ({anchoredComments.length})</h4>
                  {anchoredComments.map((comment) => {
                    const palette = paletteFor(comment.criterion);
                    return (
                      <div
                        key={comment.id}
                        className={`p-2 rounded text-sm flex justify-between items-start gap-3 border ${palette.bgClass} ${palette.borderClass}`}
                      >
                        <div className="min-w-0 flex-1">
                          <div className="mb-1 flex items-center gap-2">
                            <span className={`inline-flex items-center rounded-full border px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide ${palette.bgClass} ${palette.borderClass} ${palette.textClass}`}>
                              {palette.label}
                            </span>
                          </div>
                          <p className="text-navy">{comment.text}</p>
                        </div>
                        {!workspaceMeta?.isReadOnly && (
                          <button onClick={() => { setAnchoredComments((current) => current.filter((item) => item.id !== comment.id)); setIsDirty(true, 'removeAnchored'); }} className="text-muted hover:text-error text-xs shrink-0" aria-label="Remove comment">&times;</button>
                        )}
                      </div>
                    );
                  })}
                </div>
              )}
            </TabPanel>

            <TabPanel id="paper" activeTab={activeTab} className="h-full">
              <div className="space-y-4">
                <InlineAlert variant="info" title="Paper Submission">
                  Uploaded pages and extracted text are evidence for paper-mode Writing reviews. Open the source file when handwriting legibility affects scoring.
                </InlineAlert>
                {(reviewDetail?.paperAssets ?? []).length === 0 ? (
                  <div className="rounded-xl border border-dashed border-gray-200 bg-slate-50 p-6 text-sm text-muted">No paper assets are attached to this review.</div>
                ) : (
                  <div className="grid gap-3">
                    {(reviewDetail?.paperAssets ?? []).map((asset) => (
                      <div key={asset.id} className="rounded-xl border border-gray-200 bg-white p-4">
                        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                          <div className="min-w-0">
                            <div className="flex items-center gap-2 text-sm font-semibold text-navy">
                              <FileText className="h-4 w-4 text-primary" />
                              <span className="truncate">Page {asset.pageNumber}: {asset.fileName}</span>
                            </div>
                            <p className="mt-1 text-xs text-muted">{asset.format.toUpperCase()} • {Math.round(asset.sizeBytes / 1024)} KB • {asset.extractedCharCount} OCR characters</p>
                          </div>
                          <div className="flex items-center gap-2">
                            <Badge variant={asset.extractionState === 'completed' ? 'success' : 'warning'}>{asset.extractionState}</Badge>
                            <Button size="sm" variant="outline" onClick={() => paperAssetUrls[asset.id] && window.open(paperAssetUrls[asset.id], '_blank', 'noopener,noreferrer')} disabled={!paperAssetUrls[asset.id]}>Open</Button>
                          </div>
                        </div>
                        {asset.extractionMessage ? <p className="mt-3 rounded-lg bg-slate-50 p-3 text-xs text-muted">{asset.extractionMessage}</p> : null}
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </TabPanel>

            <TabPanel id="voice" activeTab={activeTab} className="h-full">
              <div className="space-y-4">
                <InlineAlert variant="info" title="Voice-note result">
                  Save at least one audio note before submitting so the learner receives Dr. Ahmed&apos;s spoken feedback alongside the rubric.
                </InlineAlert>

                <div className="rounded-xl border border-gray-200 bg-white p-4">
                  <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                    <div>
                      <p className="text-sm font-semibold text-navy">Record or upload audio</p>
                      <p className="text-xs text-muted">MP3, M4A, WAV, OGG, or WebM audio is accepted.</p>
                    </div>
                    <div className="flex flex-wrap gap-2">
                      <label className="inline-flex cursor-pointer items-center justify-center gap-2 rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm font-semibold text-navy hover:border-primary/40">
                        <UploadCloud className="h-4 w-4" /> Upload
                        <input
                          type="file"
                          accept="audio/mpeg,audio/mp4,audio/wav,audio/ogg,audio/webm"
                          className="sr-only"
                          disabled={workspaceMeta?.isReadOnly || isVoiceSaving}
                          onChange={(event) => setVoiceFile(event.currentTarget.files?.[0] ?? null)}
                        />
                      </label>
                      {isRecording ? (
                        <Button type="button" variant="destructive" onClick={handleStopRecording} disabled={isVoiceSaving}>
                          <Square className="h-4 w-4" /> Stop
                        </Button>
                      ) : (
                        <Button type="button" variant="outline" onClick={() => void handleStartRecording()} disabled={workspaceMeta?.isReadOnly || isVoiceSaving}>
                          <Mic className="h-4 w-4" /> Record
                        </Button>
                      )}
                    </div>
                  </div>
                  {voiceFile ? (
                    <div className="mt-3 rounded-lg border border-primary/20 bg-primary/5 p-3 text-sm text-navy">
                      <div className="flex items-center gap-2 font-semibold"><FileAudio className="h-4 w-4 text-primary" /> {voiceFile.name}</div>
                    </div>
                  ) : null}
                  <div className="mt-4 grid gap-3 md:grid-cols-2">
                    <Textarea
                      label="Written voice-note summary"
                      value={voiceWrittenNotes}
                      onChange={(event) => setVoiceWrittenNotes(event.target.value)}
                      rows={4}
                      placeholder="Key spoken feedback points..."
                      disabled={workspaceMeta?.isReadOnly || isVoiceSaving}
                    />
                    <Textarea
                      label="Transcript"
                      value={voiceTranscript}
                      onChange={(event) => setVoiceTranscript(event.target.value)}
                      rows={4}
                      placeholder="Optional transcript for accessibility..."
                      disabled={workspaceMeta?.isReadOnly || isVoiceSaving}
                    />
                  </div>
                  <div className="mt-4 flex justify-end">
                    <Button onClick={() => void handleSaveVoiceNote()} disabled={workspaceMeta?.isReadOnly || isVoiceSaving || !voiceFile} loading={isVoiceSaving}>
                      <FileAudio className="h-4 w-4" /> Save Voice Note
                    </Button>
                  </div>
                </div>

                {(reviewDetail?.voiceNotes ?? []).length === 0 ? (
                  <div className="rounded-xl border border-dashed border-gray-200 bg-slate-50 p-6 text-sm text-muted">No voice notes have been saved yet.</div>
                ) : (
                  <div className="space-y-3">
                    {(reviewDetail?.voiceNotes ?? []).map((note) => (
                      <div key={note.id} className="rounded-xl border border-gray-200 bg-white p-4">
                        <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
                          <div>
                            <p className="text-sm font-semibold text-navy">{note.fileName}</p>
                            <p className="text-xs text-muted">Saved {new Date(note.createdAt).toLocaleString()}</p>
                          </div>
                          <Badge variant="success">{note.status}</Badge>
                        </div>
                        <audio controls src={voiceNoteUrls[note.id]} className="mt-3 w-full" preload="metadata" />
                        {note.writtenNotes ? <p className="mt-3 text-sm text-navy">{note.writtenNotes}</p> : null}
                        {note.transcriptText ? <p className="mt-3 whitespace-pre-wrap rounded-lg bg-slate-50 p-3 text-xs text-muted">{note.transcriptText}</p> : null}
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </TabPanel>

            <TabPanel id="casenotes" activeTab={activeTab} className="h-full">
              <WritingCaseNotesPanel
                caseNotes={reviewDetail?.caseNotes ?? ''}
                scratchpad={scratchpad}
                onScratchpadChange={(value) => { setScratchpad(value); setIsDirty(true, 'scratchpad'); }}
                checklist={checklist}
                onChecklistChange={(id, checked) => {
                  setChecklist((current) => current.map((item) => item.id === id ? { ...item, checked } : item));
                  setIsDirty(true, 'checklist');
                }}
                activeTab={caseNotesTab}
                onTabChange={setCaseNotesTab}
              />
            </TabPanel>

            <TabPanel id="ai" activeTab={activeTab} className="h-full">
              <InlineAlert variant="info" title="Advisory Only" className="mb-4">
                This AI-generated draft feedback is for reference only. Do not treat it as the final expert judgment.
              </InlineAlert>
              <div className="whitespace-pre-wrap text-sm text-navy bg-blue-50 p-4 rounded-md border border-blue-100">
                {reviewDetail?.aiDraftFeedback}
              </div>
            </TabPanel>
          </div>
        </div>

        <div className="w-full lg:w-[520px] flex flex-col bg-surface">
          <div className="p-4 border-b border-gray-200 flex justify-between items-center bg-white" role="banner">
            <div>
              <h2 className="font-semibold text-navy">Review Rubric</h2>
              <p className="text-xs text-muted">ID: {reviewRequestId}</p>
              {lastSavedAt && <p className="text-xs text-muted mt-1">Last saved: {new Date(lastSavedAt).toLocaleTimeString()}</p>}
            </div>
            <div className="flex items-center gap-2">
              {workspaceMeta?.isReadOnly && <Badge variant="info">Read Only</Badge>}
              {isDirty && !workspaceMeta?.isReadOnly && <Badge variant="warning">Unsaved</Badge>}
              {slaSeconds > 0 ? <Timer mode="countdown" initialSeconds={slaSeconds} running size="sm" showWarning /> : <Badge variant="danger">SLA Overdue</Badge>}
            </div>
          </div>

          <div className="flex-1 overflow-y-auto p-4 space-y-4">
            {learnerContext && (
              <div className="rounded-xl border border-gray-200 bg-white p-3">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <p className="text-sm font-semibold text-navy">{learnerContext.name}</p>
                    <p className="text-xs text-muted capitalize">{learnerContext.profession.replace(/_/g, ' ')} • Goal {learnerContext.goalScore}</p>
                  </div>
                  <Badge variant="info">{learnerContext.reviewsInScope} in scope</Badge>
                </div>
                <div className="mt-3 flex flex-wrap gap-3 text-xs text-muted">
                  <span>{learnerContext.examDate ? `Exam ${new Date(learnerContext.examDate).toLocaleDateString()}` : 'Exam date not set'}</span>
                  {learnerContext.subTestScores.length > 0 ? <span>{learnerContext.subTestScores.map((item) => `${item.subTest}:${item.latestScore ?? '-'}`).join(' • ')}</span> : null}
                </div>
                {learnerContext.priorReviews[0] ? (
                  <div className="mt-3 rounded-lg bg-slate-50 p-3">
                    <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Latest prior tutor feedback</p>
                    <p className="mt-1 text-sm text-navy">{learnerContext.priorReviews[0].overallComment}</p>
                  </div>
                ) : null}
              </div>
            )}

            {reviewDetail?.aiSuggestedScores && Object.keys(reviewDetail.aiSuggestedScores).length > 0 && (
              <div className="rounded-xl border border-blue-200 bg-blue-50 p-3">
                <p className="text-sm font-semibold text-blue-900">AI Reference Scores</p>
                <p className="mt-1 text-xs text-blue-700">Visible by default as advisory guidance only. These are not the final tutor scores.</p>
                <div className="mt-3 grid grid-cols-2 gap-2">
                  {CRITERIA.map(({ key, label }) => (
                    <div key={`ai-${key}`} className="rounded-lg bg-white px-3 py-2 text-sm text-navy">
                      <span className="font-medium">{label}</span>
                      <span className="ml-2 text-blue-700">{reviewDetail.aiSuggestedScores?.[key] ?? '-'}</span>
                    </div>
                  ))}
                </div>
              </div>
            )}

            {reviewHistory && (
              <div className="rounded-xl border border-gray-200 bg-white p-3">
                <div className="flex items-center justify-between gap-3">
                  <p className="text-sm font-semibold text-navy">Review History</p>
                  <span className="text-xs text-muted">{reviewHistory.draftVersionCount} draft version(s)</span>
                </div>
                <div className="mt-3 space-y-2">
                  {reviewHistory.entries.slice(-4).reverse().map((entry) => (
                    <div key={`${entry.timestamp}-${entry.action}`} className="rounded-lg bg-slate-50 px-3 py-2 text-xs text-slate-600">
                      <p className="font-medium text-slate-900">{entry.action.replace(/_/g, ' ')}</p>
                      <p>{entry.actorName ?? 'System'} • {new Date(entry.timestamp).toLocaleString()}</p>
                      {entry.details ? <p className="mt-1">{entry.details}</p> : null}
                    </div>
                  ))}
                </div>
              </div>
            )}

            {CRITERIA.map(({ key, label }) => (
              <div key={key} className={`p-3 bg-white border rounded-md ${validationErrors.has(key) ? 'border-red-400 ring-1 ring-red-200' : 'border-gray-200'}`}>
                <Select
                  label={label}
                  value={scores[key] ?? ''}
                  onChange={(event) => handleScoreChange(key, event.target.value)}
                  options={bandOptionsFor(key)}
                  placeholder="Select band..."
                  error={validationErrors.has(key) ? 'Score required' : undefined}
                  aria-label={`Score for ${label}`}
                  disabled={workspaceMeta?.isReadOnly}
                />
                <Textarea
                  placeholder={`Comment on ${label.toLowerCase()}...`}
                  value={criterionComments[key] ?? ''}
                  onChange={(event) => { setCriterionComments((current) => ({ ...current, [key]: event.target.value })); setIsDirty(true, 'criterion-comment'); }}
                  rows={2}
                  className="mt-2"
                  aria-label={`Comment for ${label}`}
                  disabled={workspaceMeta?.isReadOnly}
                />
              </div>
            ))}

            <div className="p-3 bg-white border border-gray-200 rounded-md">
              <Textarea
                label="Final Overall Comment"
                placeholder="Provide a summary of the learner's performance..."
                value={finalComment}
                onChange={(event) => { setFinalComment(event.target.value); setIsDirty(true, 'final-comment'); }}
                rows={5}
                aria-label="Final overall comment"
                disabled={workspaceMeta?.isReadOnly}
              />
              <p className="text-xs text-muted mt-1 text-right">{finalComment.length} characters</p>
            </div>

            <p className="text-xs text-muted">Keyboard: Ctrl+S save draft · Ctrl+Enter submit</p>
          </div>

          <div className="p-4 border-t border-gray-200 bg-white flex justify-between items-center gap-3 shrink-0" role="toolbar" aria-label="Review actions">
            <div className="flex items-center gap-2">
              <Button variant="outline" onClick={() => void handleSaveDraft()} disabled={isSaving || !workspaceMeta?.canSaveDraft} className="flex items-center gap-2" aria-label="Save draft">
                <Save className="w-4 h-4" /> {isSaving ? 'Saving...' : 'Save Draft'}
              </Button>
              <Button variant="outline" onClick={() => setShowReworkPrompt((current) => !current)} disabled={!workspaceMeta?.canRequestRework} className="flex items-center gap-2" aria-label="Request rework">
                <RotateCcw className="w-4 h-4" /> Rework
              </Button>
            </div>
            <Button onClick={() => void handleSubmit()} disabled={isSubmitting || !workspaceMeta?.canSubmit} className="flex items-center gap-2" aria-label="Submit review">
              <Send className="w-4 h-4" /> {isSubmitting ? 'Submitting...' : 'Submit Review'}
            </Button>
          </div>

          {showReworkPrompt && (
            <div className="px-4 pb-4 bg-amber-50 border-t border-amber-200">
              <p className="text-sm font-semibold text-amber-800 my-2">Request Rework</p>
              <textarea
                className="w-full text-sm border border-amber-300 rounded-md px-3 py-2 focus:outline-none focus:ring-2 focus:ring-amber-400/30 bg-white"
                rows={2}
                placeholder="Reason for rework..."
                value={reworkReason}
                onChange={(event) => setReworkReason(event.target.value)}
                aria-label="Rework reason"
              />
              <div className="flex justify-end gap-2 mt-2">
                <Button size="sm" variant="outline" onClick={() => { setShowReworkPrompt(false); setReworkReason(''); }}>Cancel</Button>
                <Button size="sm" onClick={() => void handleRework()} disabled={isReworking}>{isReworking ? 'Sending...' : 'Submit Rework'}</Button>
              </div>
            </div>
          )}
        </div>
      </AsyncStateWrapper>
    </div>
  );
}
