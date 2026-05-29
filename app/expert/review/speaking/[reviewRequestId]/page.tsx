'use client';

import { useState, useEffect, useCallback, useMemo, useRef } from 'react';
import { Button } from '@/components/ui/button';
import { Tabs, TabPanel } from '@/components/ui/tabs';
import { Select, Textarea } from '@/components/ui/form-controls';
import { Badge } from '@/components/ui/badge';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Timer } from '@/components/ui/timer';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Save, Send, Flag, PlayCircle, MessageSquare, RotateCcw, Mic, Square, Trash2, FileAudio } from 'lucide-react';
import { useParams, useRouter } from 'next/navigation';
import { AudioPlayerWaveform } from '@/components/domain/audio-player-waveform';
import { SpeakingRoleCard } from '@/components/domain/speaking-role-card';
import { VoiceNoteRecorder } from '@/components/domain/expert/VoiceNoteRecorder';
import { fetchAuthorizedObjectUrl, fetchExpertLearnerReviewContext, fetchExpertReviewHistory, fetchSpeakingReviewDetail, isApiError, requestRework, saveDraftReview, submitExpertSpeakingReview } from '@/lib/api';
import { ensureFreshAccessToken } from '@/lib/auth-client';
import { env } from '@/lib/env';
import { analytics } from '@/lib/analytics';
import { useExpertStore } from '@/lib/stores/expert-store';
import type { ExpertChecklistItem, ExpertLearnerReviewContext, ExpertReviewHistory, ExpertSavedDraft, ExpertTranscriptLine, SpeakingCriterionKey, SpeakingReviewDetail, TimestampComment } from '@/lib/types/expert';

type AsyncStatus = 'loading' | 'error' | 'partial' | 'success';

const LINGUISTIC_CRITERIA: { key: SpeakingCriterionKey; label: string }[] = [
  { key: 'intelligibility', label: 'Intelligibility' },
  { key: 'fluency', label: 'Fluency' },
  { key: 'appropriateness', label: 'Appropriateness of Language' },
  { key: 'grammar', label: 'Resources of Grammar & Expression' },
];

// OET Clinical Communication — 5 separate criteria, each scored 0–3
// (NOT a single aggregate). Source: rulebooks/speaking/common/assessment-criteria.json.
const CLINICAL_CRITERIA: { key: SpeakingCriterionKey; label: string }[] = [
  { key: 'relationshipBuilding', label: 'Relationship Building' },
  { key: 'patientPerspective', label: "Understanding & Incorporating Patient's Perspective" },
  { key: 'providingStructure', label: 'Providing Structure' },
  { key: 'informationGathering', label: 'Information Gathering' },
  { key: 'informationGiving', label: 'Information Giving' },
];

const ALL_CRITERIA = [...LINGUISTIC_CRITERIA, ...CLINICAL_CRITERIA];

const LINGUISTIC_BAND_OPTIONS = [
  { value: '6', label: '6 (Excellent)' },
  { value: '5', label: '5 (Good)' },
  { value: '4', label: '4 (Satisfactory)' },
  { value: '3', label: '3 (Borderline)' },
  { value: '2', label: '2 (Poor)' },
  { value: '1', label: '1 (Very Poor)' },
  { value: '0', label: '0 (Unscorable)' },
];

// Clinical Communication cluster level descriptors (OET CBLA official).
const CLINICAL_BAND_OPTIONS = [
  { value: '3', label: '3 (Adept use)' },
  { value: '2', label: '2 (Competent use)' },
  { value: '1', label: '1 (Partially effective use)' },
  { value: '0', label: '0 (Ineffective use)' },
];

type DraftCandidate = {
  reviewId: string;
  scores: Record<string, number>;
  criterionComments: Record<string, string>;
  finalComment: string;
  anchoredComments: unknown[];
  timestampComments: TimestampComment[];
  scratchpad: string;
  checklistItems: ExpertChecklistItem[];
  version?: number;
  updatedAt: string;
};

function toDraftSnapshot(reviewId: string, draft: ExpertSavedDraft | null | undefined): DraftCandidate | null {
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
    checklistItems: [],
    version: draft.version,
    updatedAt: draft.savedAt,
  };
}

function pickLatestDraft(localDraft: DraftCandidate | null, serverDraft: DraftCandidate | null): DraftCandidate | null {
  if (!localDraft) return serverDraft;
  if (!serverDraft) return localDraft;
  return new Date(localDraft.updatedAt).getTime() >= new Date(serverDraft.updatedAt).getTime() ? localDraft : serverDraft;
}

function stringList(value: unknown): string[] {
  return Array.isArray(value) ? value.map(String).filter(Boolean) : [];
}

// ── Speaking voice-note feedback (Phase 4) ──
// Local shape — Agent W2-B owns the backend contract. We accept the broad
// envelope { items: SpeakingVoiceNote[] } and tolerate either `url` or
// `mediaUrl` and either `id` or `voiceNoteId` for forward-compatibility.
interface SpeakingVoiceNote {
  id: string;
  reviewRequestId: string;
  fileName: string;
  durationSeconds?: number | null;
  status: string;
  createdAt: string;
  url: string;
  isOwner?: boolean;
}

function normalizeSpeakingVoiceNote(raw: unknown): SpeakingVoiceNote | null {
  if (!raw || typeof raw !== 'object') return null;
  const r = raw as Record<string, unknown>;
  const id = typeof r.id === 'string' ? r.id : typeof r.voiceNoteId === 'string' ? r.voiceNoteId : null;
  const url = typeof r.url === 'string' ? r.url : typeof r.mediaUrl === 'string' ? r.mediaUrl : null;
  if (!id || !url) return null;
  return {
    id,
    reviewRequestId: typeof r.reviewRequestId === 'string' ? r.reviewRequestId : '',
    fileName: typeof r.fileName === 'string' ? r.fileName : 'voice-note',
    durationSeconds: typeof r.durationSeconds === 'number' ? r.durationSeconds : null,
    status: typeof r.status === 'string' ? r.status : 'ready',
    createdAt: typeof r.createdAt === 'string' ? r.createdAt : new Date().toISOString(),
    url,
    isOwner: typeof r.isOwner === 'boolean' ? r.isOwner : true,
  };
}

function formatDuration(seconds?: number | null): string {
  if (seconds == null || !Number.isFinite(seconds) || seconds <= 0) return '0:00';
  const total = Math.round(seconds);
  const mins = Math.floor(total / 60);
  const secs = total % 60;
  return `${mins}:${secs.toString().padStart(2, '0')}`;
}

export default function SpeakingReviewWorkspace() {
  const params = useParams();
  const rawReviewRequestId = params?.reviewRequestId;
  const reviewRequestId = Array.isArray(rawReviewRequestId) ? rawReviewRequestId[0] ?? '' : rawReviewRequestId ?? '';
  const router = useRouter();
  const { getReviewDraft, upsertReviewDraft, clearReviewDraft } = useExpertStore();
  const activeTranscriptLineId = useRef<string | null>(null);

  const [reviewDetail, setReviewDetail] = useState<SpeakingReviewDetail | null>(null);
  const [pageStatus, setPageStatus] = useState<AsyncStatus>('loading');
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState('transcript');
  const [currentTime, setCurrentTime] = useState(0);
  const [seekTo, setSeekTo] = useState<number | null>(null);
  const [scores, setScores] = useState<Record<string, string>>({});
  const [criterionComments, setCriterionComments] = useState<Record<string, string>>({});
  const [finalComment, setFinalComment] = useState('');
  const [validationErrors, setValidationErrors] = useState<Set<string>>(new Set());
  const [timestampComments, setTimestampComments] = useState<TimestampComment[]>([]);
  const [activeCommentLine, setActiveCommentLine] = useState<string | null>(null);
  const [draftVersion, setDraftVersion] = useState<number | undefined>(undefined);
  const [isDirty, setIsDirty] = useState(false);
  const [lastSavedAt, setLastSavedAt] = useState<string | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const [isSaving, setIsSaving] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isReworking, setIsReworking] = useState(false);
  const [showReworkPrompt, setShowReworkPrompt] = useState(false);
  const [reworkReason, setReworkReason] = useState('');
  const [slaSeconds, setSlaSeconds] = useState(0);
  const [reloadToken, setReloadToken] = useState(0);
  const [isInitialized, setIsInitialized] = useState(false);
  const [reviewHistory, setReviewHistory] = useState<ExpertReviewHistory | null>(null);
  const [learnerContext, setLearnerContext] = useState<ExpertLearnerReviewContext | null>(null);

  // Voice-note feedback (Phase 4 — task W2-G)
  const [voiceNotes, setVoiceNotes] = useState<SpeakingVoiceNote[]>([]);
  const [voiceNoteUrls, setVoiceNoteUrls] = useState<Record<string, string>>({});
  const [voiceNotesLoading, setVoiceNotesLoading] = useState(false);
  const [isRecording, setIsRecording] = useState(false);
  const [isUploadingVoiceNote, setIsUploadingVoiceNote] = useState(false);
  const [deletingVoiceNoteId, setDeletingVoiceNoteId] = useState<string | null>(null);
  const [recordingStartedAt, setRecordingStartedAt] = useState<number | null>(null);
  const recorderRef = useRef<MediaRecorder | null>(null);
  const recordingChunksRef = useRef<Blob[]>([]);
  const recordingStreamRef = useRef<MediaStream | null>(null);

  const applyDraft = useCallback((draft: DraftCandidate | null) => {
    if (!draft) return;
    setScores(Object.fromEntries(Object.entries(draft.scores).map(([key, value]) => [key, String(value)])));
    setCriterionComments(draft.criterionComments);
    setFinalComment(draft.finalComment);
    setTimestampComments(draft.timestampComments);
    setDraftVersion(draft.version);
    setLastSavedAt(draft.updatedAt);
  }, []);

  const partialMessage = reviewDetail?.artifactStatus?.transcript?.message
    ?? reviewDetail?.artifactStatus?.aiFlags?.message
    ?? 'Some speaking review artifacts are still being prepared.';

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
        setTimestampComments([]);
        setDraftVersion(undefined);
        setLastSavedAt(null);
        setIsDirty(false);
        const detail = await fetchSpeakingReviewDetail(reviewRequestId);
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

        const isPartial = Object.values(detail.artifactStatus ?? {}).some((artifact) => artifact.state !== 'completed');
        setPageStatus(isPartial ? 'partial' : 'success');
        analytics.track('review_started', { reviewRequestId, type: 'speaking' });
      } catch (error) {
        if (!cancelled) {
          setErrorMsg(isApiError(error) ? error.userMessage : 'Failed to load review details.');
          setPageStatus('error');
        }
      }
    })();
    return () => { cancelled = true; };
  }, [applyDraft, getReviewDraft, reloadToken, reviewRequestId]);

  useEffect(() => {
    if (!reviewRequestId || !isInitialized) return;
    upsertReviewDraft(reviewRequestId, {
      scores: Object.fromEntries(Object.entries(scores).filter(([, value]) => value).map(([key, value]) => [key, Number(value)])),
      criterionComments,
      finalComment,
      anchoredComments: [],
      timestampComments,
      scratchpad: '',
      checklistItems: [],
      version: draftVersion,
    });
  }, [criterionComments, draftVersion, finalComment, isInitialized, reviewRequestId, scores, timestampComments, upsertReviewDraft]);

  useEffect(() => {
    const handleBeforeUnload = (event: BeforeUnloadEvent) => {
      if (!isDirty) return;
      event.preventDefault();
      event.returnValue = '';
    };
    window.addEventListener('beforeunload', handleBeforeUnload);
    return () => window.removeEventListener('beforeunload', handleBeforeUnload);
  }, [isDirty]);

  const persistDraft = useCallback(async (options?: { quiet?: boolean }) => {
    if (!reviewRequestId) return null;
    const payload = {
      reviewRequestId,
      scores: Object.fromEntries(Object.entries(scores).filter(([, value]) => value).map(([key, value]) => [key, Number(value)])),
      criterionComments,
      finalComment,
      comments: timestampComments,
      scratchpad: '',
      checklistItems: [],
      savedAt: new Date().toISOString(),
      version: draftVersion,
    };

    const savedDraft = await saveDraftReview(payload);
    setDraftVersion(savedDraft.version);
    setLastSavedAt(savedDraft.savedAt);
    setIsDirty(false);
    upsertReviewDraft(reviewRequestId, {
      scores: savedDraft.scores,
      criterionComments: savedDraft.criterionComments,
      finalComment: savedDraft.finalComment,
      anchoredComments: [],
      timestampComments: savedDraft.comments as TimestampComment[],
      scratchpad: '',
      checklistItems: [],
      version: savedDraft.version,
      updatedAt: savedDraft.savedAt,
    });
    if (!options?.quiet) {
      setToast({ variant: 'success', message: 'Draft saved successfully.' });
    }
    analytics.track('review_draft_saved', { reviewRequestId });
    return savedDraft;
  }, [criterionComments, draftVersion, finalComment, reviewRequestId, scores, timestampComments, upsertReviewDraft]);

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
    ALL_CRITERIA.forEach((criterion) => {
      if (!scores[criterion.key]) {
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

    setIsSubmitting(true);
    try {
      const savedDraft = await persistDraft({ quiet: true });
      const normalizedScores = Object.fromEntries(Object.entries(scores).map(([key, value]) => [key, Number(value)]));
      await submitExpertSpeakingReview(reviewRequestId, {
        scores: normalizedScores,
        criterionComments,
        finalComment,
        version: savedDraft?.version ?? draftVersion,
      });
      analytics.track('review_submitted', { reviewRequestId, type: 'speaking' });
      clearReviewDraft(reviewRequestId);
      setIsDirty(false);
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
  }, [clearReviewDraft, criterionComments, draftVersion, finalComment, persistDraft, reviewRequestId, router, scores]);

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
      setIsDirty(false);
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
  }, [clearReviewDraft, isDirty, persistDraft, reviewRequestId, reworkReason, router]);

  // ── Voice-note feedback (Phase 4 — task W2-G) ──
  // Direct inline fetch per W2-G ownership boundary (cannot extend lib/api.ts).
  // Endpoint owner: Agent W2-B.
  const buildAuthHeaders = useCallback(async (extras?: Record<string, string>): Promise<Headers> => {
    const token = await ensureFreshAccessToken();
    const headers = new Headers({ Accept: 'application/json' });
    if (token) headers.set('Authorization', `Bearer ${token}`);
    if (typeof document !== 'undefined') {
      const csrfMatch = document.cookie.match(/(?:^|;\s*)oet_csrf=([^;]+)/);
      if (csrfMatch) headers.set('x-csrf-token', csrfMatch[1]);
    }
    if (extras) {
      Object.entries(extras).forEach(([key, value]) => headers.set(key, value));
    }
    return headers;
  }, []);

  const speakingVoiceNotesUrl = useMemo(() => {
    if (!reviewRequestId) return null;
    const base = (env.apiBaseUrl || '').replace(/\/$/, '');
    return `${base}/v1/expert/speaking/reviews/${encodeURIComponent(reviewRequestId)}/voice-notes`;
  }, [reviewRequestId]);

  const loadVoiceNotes = useCallback(async () => {
    if (!speakingVoiceNotesUrl) return;
    setVoiceNotesLoading(true);
    try {
      const headers = await buildAuthHeaders();
      const response = await fetch(speakingVoiceNotesUrl, { method: 'GET', headers, credentials: 'include' });
      if (response.status === 404) {
        setVoiceNotes([]);
        return;
      }
      if (!response.ok) {
        throw new Error(`Failed to load voice notes (${response.status}).`);
      }
      const body = (await response.json().catch(() => ({}))) as Record<string, unknown> | unknown[];
      const items: unknown[] = Array.isArray(body)
        ? body
        : Array.isArray((body as Record<string, unknown>).items)
          ? ((body as Record<string, unknown>).items as unknown[])
          : [];
      const normalized = items.map(normalizeSpeakingVoiceNote).filter((item): item is SpeakingVoiceNote => item !== null);
      setVoiceNotes(normalized);
    } catch (error) {
      // Quiet on first load — empty list is acceptable if endpoint not yet live.
      console.warn('[Speaking review] voice notes load failed:', error);
      setVoiceNotes([]);
    } finally {
      setVoiceNotesLoading(false);
    }
  }, [buildAuthHeaders, speakingVoiceNotesUrl]);

  useEffect(() => {
    if (!reviewRequestId) return;
    void loadVoiceNotes();
  }, [loadVoiceNotes, reviewRequestId]);

  // Resolve authorised object URLs for playback (mirrors writing review pattern).
  useEffect(() => {
    if (voiceNotes.length === 0) {
      setVoiceNoteUrls({});
      return;
    }
    let cancelled = false;
    const createdUrls: string[] = [];
    Promise.all(voiceNotes.map(async (note) => {
      try {
        const objectUrl = await fetchAuthorizedObjectUrl(note.url);
        createdUrls.push(objectUrl);
        return [note.id, objectUrl] as const;
      } catch {
        return [note.id, ''] as const;
      }
    }))
      .then((entries) => {
        if (!cancelled) {
          setVoiceNoteUrls(Object.fromEntries(entries.filter(([, url]) => url)));
        }
      });
    return () => {
      cancelled = true;
      createdUrls.forEach((url) => URL.revokeObjectURL(url));
    };
  }, [voiceNotes]);

  const stopRecordingTracks = useCallback(() => {
    recordingStreamRef.current?.getTracks().forEach((track) => track.stop());
    recordingStreamRef.current = null;
    recorderRef.current = null;
  }, []);

  useEffect(() => () => stopRecordingTracks(), [stopRecordingTracks]);

  const uploadVoiceNote = useCallback(async (file: File, durationSeconds: number) => {
    if (!speakingVoiceNotesUrl) return;
    setIsUploadingVoiceNote(true);
    try {
      const headers = await buildAuthHeaders();
      // Let the browser set the multipart boundary automatically.
      headers.delete('Content-Type');
      const form = new FormData();
      form.append('file', file, file.name);
      if (Number.isFinite(durationSeconds) && durationSeconds > 0) {
        form.append('durationSeconds', String(Math.round(durationSeconds)));
      }
      const response = await fetch(speakingVoiceNotesUrl, { method: 'POST', headers, body: form, credentials: 'include' });
      if (!response.ok) {
        let message = `Voice-note upload failed (${response.status}).`;
        try {
          const err = await response.json();
          message = err?.message ?? err?.title ?? message;
        } catch {
          // ignore
        }
        throw new Error(message);
      }
      setToast({ variant: 'success', message: 'Voice note attached to this review.' });
      analytics.track('speaking_voice_note_added', { reviewRequestId });
      await loadVoiceNotes();
    } catch (error) {
      setToast({ variant: 'error', message: error instanceof Error ? error.message : 'Could not save the voice note.' });
    } finally {
      setIsUploadingVoiceNote(false);
    }
  }, [buildAuthHeaders, loadVoiceNotes, reviewRequestId, speakingVoiceNotesUrl]);

  const handleStartRecording = useCallback(async () => {
    if (!navigator.mediaDevices?.getUserMedia || typeof MediaRecorder === 'undefined') {
      setToast({ variant: 'error', message: 'Audio recording is not available in this browser.' });
      return;
    }
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      const recorder = new MediaRecorder(stream);
      recordingChunksRef.current = [];
      const startedAt = Date.now();
      recorder.ondataavailable = (event) => {
        if (event.data.size > 0) recordingChunksRef.current.push(event.data);
      };
      recorder.onstop = () => {
        const durationSeconds = (Date.now() - startedAt) / 1000;
        const blob = new Blob(recordingChunksRef.current, { type: recorder.mimeType || 'audio/webm' });
        const file = new File([blob], `speaking-review-voice-note-${Date.now()}.webm`, { type: blob.type || 'audio/webm' });
        stopRecordingTracks();
        setIsRecording(false);
        setRecordingStartedAt(null);
        void uploadVoiceNote(file, durationSeconds);
      };
      recordingStreamRef.current = stream;
      recorderRef.current = recorder;
      recorder.start();
      setIsRecording(true);
      setRecordingStartedAt(startedAt);
    } catch {
      setToast({ variant: 'error', message: 'Microphone permission was not granted.' });
    }
  }, [stopRecordingTracks, uploadVoiceNote]);

  const handleStopRecording = useCallback(() => {
    recorderRef.current?.stop();
  }, []);

  const handleDeleteVoiceNote = useCallback(async (noteId: string) => {
    if (!speakingVoiceNotesUrl) return;
    setDeletingVoiceNoteId(noteId);
    try {
      const headers = await buildAuthHeaders();
      const response = await fetch(`${speakingVoiceNotesUrl}/${encodeURIComponent(noteId)}`, {
        method: 'DELETE',
        headers,
        credentials: 'include',
      });
      if (!response.ok && response.status !== 204) {
        let message = `Delete failed (${response.status}).`;
        try {
          const err = await response.json();
          message = err?.message ?? err?.title ?? message;
        } catch {
          // ignore
        }
        throw new Error(message);
      }
      setVoiceNotes((current) => current.filter((note) => note.id !== noteId));
      setVoiceNoteUrls((current) => {
        const next = { ...current };
        const stale = next[noteId];
        if (stale) URL.revokeObjectURL(stale);
        delete next[noteId];
        return next;
      });
      setToast({ variant: 'success', message: 'Voice note removed.' });
      analytics.track('speaking_voice_note_deleted', { reviewRequestId, voiceNoteId: noteId });
    } catch (error) {
      setToast({ variant: 'error', message: error instanceof Error ? error.message : 'Could not delete voice note.' });
    } finally {
      setDeletingVoiceNoteId(null);
    }
  }, [buildAuthHeaders, reviewRequestId, speakingVoiceNotesUrl]);

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

  useEffect(() => {
    if (!reviewDetail || activeTab !== 'transcript') return;
    const activeLine = reviewDetail.transcriptLines.find((line) => currentTime >= line.startTime && currentTime <= line.endTime);
    if (!activeLine || activeTranscriptLineId.current === activeLine.id) return;
    activeTranscriptLineId.current = activeLine.id;
    const element = document.getElementById(`transcript-line-${activeLine.id}`);
    element?.scrollIntoView({ behavior: 'auto', block: 'nearest' });
  }, [activeTab, currentTime, reviewDetail]);

  const handleScoreChange = (criterion: string, value: string) => {
    setScores((current) => ({ ...current, [criterion]: value }));
    setValidationErrors((current) => {
      const next = new Set(current);
      next.delete(criterion);
      return next;
    });
    setIsDirty(true);
  };

  const handleTranscriptClick = (time: number) => {
    setSeekTo(time);
    window.setTimeout(() => setSeekTo(null), 120);
  };

  const addTimestampComment = (line: ExpertTranscriptLine, commentText: string) => {
    if (!commentText.trim()) return;
    const comment: TimestampComment = {
      id: `tc-${Date.now()}`,
      text: commentText.trim(),
      timestampStart: line.startTime,
      timestampEnd: line.endTime,
      createdAt: new Date().toISOString(),
    };
    setTimestampComments((current) => [...current, comment]);
    setActiveCommentLine(null);
    setIsDirty(true);
  };

  const tabOptions = [
    { id: 'transcript', label: 'Transcript & Audio' },
    { id: 'rolecard', label: 'Role Card' },
    { id: 'aiflags', label: 'AI Flags' },
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

  const renderCriteriaGroup = (title: string, criteria: { key: SpeakingCriterionKey; label: string }[], bandOptions = LINGUISTIC_BAND_OPTIONS) => (
    <>
      <h3 className="font-bold text-navy border-b border-border pb-2">{title}</h3>
      {criteria.map(({ key, label }) => (
        <div key={key} className={`p-3 bg-surface border rounded-md ${validationErrors.has(key) ? 'border-danger ring-1 ring-danger/20' : 'border-border'}`}>
          <Select
            label={label}
            value={scores[key] ?? ''}
            onChange={(event) => handleScoreChange(key, event.target.value)}
            options={bandOptions}
            placeholder="Select band..."
            error={validationErrors.has(key) ? 'Score required' : undefined}
            aria-label={`Score for ${label}`}
            disabled={workspaceMeta?.isReadOnly}
          />
          <Textarea
            placeholder={`Comment on ${label.toLowerCase()}...`}
            value={criterionComments[key] ?? ''}
            onChange={(event) => { setCriterionComments((current) => ({ ...current, [key]: event.target.value })); setIsDirty(true); }}
            rows={2}
            className="mt-2"
            aria-label={`Comment for ${label}`}
            disabled={workspaceMeta?.isReadOnly}
          />
          {/* Phase 7b — per-criterion voice note (collapsed by default; additive to the existing review-level recorder). */}
          {!workspaceMeta?.isReadOnly && reviewRequestId ? (
            <div className="mt-2">
              <VoiceNoteRecorder
                reviewRequestId={reviewRequestId}
                criterionCode={key}
                subtest="speaking"
                collapsed
                onUploaded={() => { void loadVoiceNotes(); }}
              />
            </div>
          ) : null}
        </div>
      ))}
    </>
  );

  return (
    <div className="min-h-[var(--app-viewport-height,100dvh)] flex flex-col lg:flex-row bg-background-light">
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => setReloadToken((current) => current + 1)}
        errorMessage={errorMsg ?? undefined}
        partialMessage={partialMessage}
      >
        <div className="flex-1 min-w-0 flex flex-col border-b border-border overflow-hidden lg:border-b-0 lg:border-r lg:w-1/2">
          <div className="p-4 bg-surface border-b border-border shrink-0">
            <h3 className="text-sm font-semibold text-navy mb-2">Candidate Audio Submission</h3>
            {reviewDetail && <AudioPlayerWaveform audioUrl={reviewDetail.audioUrl} onTimeUpdate={setCurrentTime} seekToTime={seekTo} />}
            {reviewDetail?.artifactStatus && (
              <div className="mt-3 grid grid-cols-1 gap-2 sm:grid-cols-3">
                {Object.entries(reviewDetail.artifactStatus).map(([artifact, artifactState]) => (
                  <div key={artifact} className="rounded-lg bg-muted px-3 py-2 text-xs text-muted">
                    <p className="font-semibold text-foreground">{artifact}</p>
                    <p>{artifactState.state}{artifactState.isStale ? ' • stale' : ''}</p>
                    {artifactState.message ? <p className="mt-1 text-muted">{artifactState.message}</p> : null}
                  </div>
                ))}
              </div>
            )}
          </div>

          <Tabs tabs={tabOptions} activeTab={activeTab} onChange={setActiveTab} className="bg-surface shrink-0" />

          <div className="flex-1 overflow-y-auto p-4 bg-surface" role="region" aria-label="Review content">
            <TabPanel id="transcript" activeTab={activeTab} className="h-full space-y-2">
              {!reviewDetail?.transcriptLines.length && (
                <InlineAlert variant="warning">Transcript is still being processed. You can continue reviewing with the audio and save a draft.</InlineAlert>
              )}

              {reviewDetail?.transcriptLines.map((line) => {
                const isActive = currentTime >= line.startTime && currentTime <= line.endTime;
                const lineComments = timestampComments.filter((comment) => comment.timestampStart === line.startTime);
                return (
                  <div key={line.id} id={`transcript-line-${line.id}`}>
                    <button
                      type="button"
                      className={`w-full text-left p-3 rounded-lg border transition-colors ${isActive ? 'bg-blue-50 border-blue-200 shadow-sm' : 'bg-surface border-transparent hover:border-border'} focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary`}
                      onClick={() => handleTranscriptClick(line.startTime)}
                      aria-label={`Seek to ${line.startTime.toFixed(1)} seconds for ${line.speaker}`}
                    >
                      <div className="flex items-center justify-between mb-1 gap-3">
                        <div className="flex items-center gap-2">
                          <span className={`text-xs font-bold ${line.speaker === 'candidate' ? 'text-primary' : 'text-muted'}`}>{line.speaker === 'candidate' ? 'Candidate' : 'Interlocutor'}</span>
                          <span className="text-xs text-muted flex items-center gap-1"><PlayCircle className="w-3 h-3" /> {line.startTime.toFixed(1)}s</span>
                        </div>
                        {!workspaceMeta?.isReadOnly && (
                          <span className="text-xs text-muted flex items-center gap-1"><MessageSquare className="w-3 h-3" /> Add note</span>
                        )}
                      </div>
                      <p className="text-sm text-navy">{line.text}</p>
                    </button>

                    {!workspaceMeta?.isReadOnly && (
                      <div className="mt-1 flex justify-end">
                        <Button size="sm" variant="outline" onClick={() => setActiveCommentLine((current) => current === line.id ? null : line.id)} aria-label={`Add comment at ${line.startTime.toFixed(1)} seconds`}>
                          <MessageSquare className="w-3 h-3 mr-1" /> Comment
                        </Button>
                      </div>
                    )}

                    {activeCommentLine === line.id && !workspaceMeta?.isReadOnly && (
                      <div className="ml-4 mt-1 p-2 bg-blue-50 border border-blue-200 rounded">
                        <div className="flex gap-2">
                          <input
                            type="text"
                            placeholder="Your comment..."
                            className="flex-1 text-sm border border-border rounded px-2 py-1 focus:outline-none focus:ring-2 focus:ring-primary/30"
                            autoFocus
                            onKeyDown={(event) => {
                              if (event.key === 'Enter') {
                                addTimestampComment(line, (event.target as HTMLInputElement).value);
                                (event.target as HTMLInputElement).value = '';
                              }
                            }}
                            aria-label="Timestamp comment text"
                          />
                          <Button size="sm" onClick={(event) => { const input = (event.currentTarget.previousElementSibling as HTMLInputElement | null); addTimestampComment(line, input?.value ?? ''); if (input) input.value = ''; }}>Add</Button>
                          <Button size="sm" variant="outline" onClick={() => setActiveCommentLine(null)}>Cancel</Button>
                        </div>
                      </div>
                    )}

                    {lineComments.map((comment) => (
                      <div key={comment.id} className="ml-4 mt-1 p-2 bg-yellow-50 border border-yellow-200 rounded text-sm flex justify-between items-start gap-3">
                        <p className="text-navy">{comment.text}</p>
                        {!workspaceMeta?.isReadOnly && (
                          <button onClick={() => { setTimestampComments((current) => current.filter((item) => item.id !== comment.id)); setIsDirty(true); }} className="text-muted hover:text-error text-xs shrink-0" aria-label="Remove comment">&times;</button>
                        )}
                      </div>
                    ))}
                  </div>
                );
              })}
            </TabPanel>

            <TabPanel id="rolecard" activeTab={activeTab} className="h-full">
              {reviewDetail && (
                <div className="space-y-4">
                  <SpeakingRoleCard
                    role={reviewDetail.roleCard.role}
                    setting={reviewDetail.roleCard.setting}
                    patient={reviewDetail.roleCard.patient}
                    task={reviewDetail.roleCard.task}
                    background={reviewDetail.roleCard.background}
                    tasks={reviewDetail.roleCard.tasks}
                    patientEmotion={reviewDetail.roleCard.patientEmotion}
                    communicationGoal={reviewDetail.roleCard.communicationGoal}
                    clinicalTopic={reviewDetail.roleCard.clinicalTopic}
                    prepTimeSeconds={reviewDetail.roleCard.prepTimeSeconds}
                    roleplayTimeSeconds={reviewDetail.roleCard.roleplayTimeSeconds}
                    disclaimer={reviewDetail.roleCard.disclaimer}
                  />

                  {(reviewDetail.roleCard.warmUpQuestions?.length ?? 0) > 0 ? (
                    <div className="rounded-2xl border border-border bg-background-light p-4">
                      <p className="text-xs font-black uppercase tracking-widest text-muted">Warm-up questions</p>
                      <ul className="mt-3 space-y-2 text-sm text-navy">
                        {reviewDetail.roleCard.warmUpQuestions?.map((question, index) => (
                          <li key={`${question}-${index}`}><span className="font-bold text-primary">{index + 1}.</span> {question}</li>
                        ))}
                      </ul>
                    </div>
                  ) : null}

                  {reviewDetail.roleCard.interlocutorCard ? (
                    <div className="rounded-2xl border border-warning/30 bg-amber-50 p-4">
                      <div className="flex items-center justify-between gap-3">
                        <p className="text-sm font-black text-navy">Hidden interlocutor card</p>
                        <Badge variant="warning">Tutor only</Badge>
                      </div>
                      <p className="mt-2 text-xs leading-relaxed text-warning/80">
                        This card is intentionally hidden from learner task endpoints and is used only for expert/tutor context.
                      </p>
                      <div className="mt-4 space-y-3 text-sm text-navy">
                        {String((reviewDetail.roleCard.interlocutorCard as Record<string, unknown>).patientProfile ?? (reviewDetail.roleCard.interlocutorCard as Record<string, unknown>).background ?? (reviewDetail.roleCard.interlocutorCard as Record<string, unknown>).hiddenInformation ?? '').trim() ? (
                          <div>
                            <p className="text-xs font-bold uppercase tracking-widest text-muted">Patient profile</p>
                            <p className="mt-1 leading-relaxed">
                              {String((reviewDetail.roleCard.interlocutorCard as Record<string, unknown>).patientProfile ?? (reviewDetail.roleCard.interlocutorCard as Record<string, unknown>).background ?? (reviewDetail.roleCard.interlocutorCard as Record<string, unknown>).hiddenInformation)}
                            </p>
                          </div>
                        ) : null}
                        {stringList((reviewDetail.roleCard.interlocutorCard as Record<string, unknown>).cuePrompts ?? (reviewDetail.roleCard.interlocutorCard as Record<string, unknown>).prompts ?? (reviewDetail.roleCard.interlocutorCard as Record<string, unknown>).objectives).length > 0 ? (
                          <div>
                            <p className="text-xs font-bold uppercase tracking-widest text-muted">Cue prompts</p>
                            <ul className="mt-2 space-y-2">
                              {stringList((reviewDetail.roleCard.interlocutorCard as Record<string, unknown>).cuePrompts ?? (reviewDetail.roleCard.interlocutorCard as Record<string, unknown>).prompts ?? (reviewDetail.roleCard.interlocutorCard as Record<string, unknown>).objectives).map((prompt, index) => (
                                <li key={`${prompt}-${index}`}><span className="font-bold text-primary">{index + 1}.</span> {prompt}</li>
                              ))}
                            </ul>
                          </div>
                        ) : null}
                        {String((reviewDetail.roleCard.interlocutorCard as Record<string, unknown>).privateNotes ?? '').trim() ? (
                          <div>
                            <p className="text-xs font-bold uppercase tracking-widest text-muted">Private notes</p>
                            <p className="mt-1 leading-relaxed">{String((reviewDetail.roleCard.interlocutorCard as Record<string, unknown>).privateNotes)}</p>
                          </div>
                        ) : null}
                      </div>
                    </div>
                  ) : null}
                </div>
              )}
            </TabPanel>

            <TabPanel id="aiflags" activeTab={activeTab} className="h-full">
              {!reviewDetail?.aiFlags.length ? (
                <InlineAlert variant="info">AI analysis is still pending. Flags will appear once processing completes.</InlineAlert>
              ) : (
                <div className="space-y-3" role="list" aria-label="AI-detected flags">
                  {reviewDetail.aiFlags.map((flag) => (
                    <div key={flag.id} className={`p-3 border rounded-md ${flag.severity === 'warning' ? 'border-amber-200 bg-amber-50' : 'border-blue-200 bg-blue-50'}`} role="listitem">
                      <div className="flex items-center gap-2 font-semibold text-sm mb-1" style={{ color: flag.severity === 'warning' ? '#92400e' : '#1e40af' }}>
                        <Flag className="w-4 h-4" /> {flag.type}
                      </div>
                      <p className="text-xs" style={{ color: flag.severity === 'warning' ? '#78350f' : '#1e3a8a' }}>{flag.message}</p>
                      <Button variant="outline" size="sm" className="mt-2 h-7 text-xs" onClick={() => handleTranscriptClick(flag.timestampStart)} aria-label={`Go to ${flag.timestampStart.toFixed(1)} seconds`}>
                        Go to {flag.timestampStart.toFixed(1)}s
                      </Button>
                    </div>
                  ))}
                </div>
              )}
            </TabPanel>
          </div>
        </div>

        <div className="w-full lg:w-[520px] flex flex-col bg-surface">
          <div className="p-4 border-b border-border flex justify-between items-center bg-surface shrink-0" role="banner">
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
              <div className="rounded-xl border border-border bg-surface p-3">
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
              </div>
            )}

            {reviewDetail?.aiSuggestedScores && Object.keys(reviewDetail.aiSuggestedScores).length > 0 && (
              <div className="rounded-xl border border-blue-200 bg-blue-50 p-3">
                <p className="text-sm font-semibold text-blue-900">AI Reference Scores</p>
                <p className="mt-1 text-xs text-blue-700">These scores are advisory AI guidance and are visually separated from your final rubric judgment.</p>
                <div className="mt-3 grid grid-cols-2 gap-2">
                  {ALL_CRITERIA.map(({ key, label }) => (
                    <div key={`ai-${key}`} className="rounded-lg bg-surface px-3 py-2 text-sm text-navy">
                      <span className="font-medium">{label}</span>
                      <span className="ml-2 text-blue-700">{reviewDetail.aiSuggestedScores?.[key] ?? '-'}</span>
                    </div>
                  ))}
                </div>
              </div>
            )}

            {reviewHistory && (
              <div className="rounded-xl border border-border bg-surface p-3">
                <div className="flex items-center justify-between gap-3">
                  <p className="text-sm font-semibold text-navy">Review History</p>
                  <span className="text-xs text-muted">{reviewHistory.draftVersionCount} draft version(s)</span>
                </div>
                <div className="mt-3 space-y-2">
                  {reviewHistory.entries.slice(-4).reverse().map((entry) => (
                    <div key={`${entry.timestamp}-${entry.action}`} className="rounded-lg bg-muted px-3 py-2 text-xs text-muted">
                      <p className="font-medium text-foreground">{entry.action.replace(/_/g, ' ')}</p>
                      <p>{entry.actorName ?? 'System'} • {new Date(entry.timestamp).toLocaleString()}</p>
                      {entry.details ? <p className="mt-1">{entry.details}</p> : null}
                    </div>
                  ))}
                </div>
              </div>
            )}

            {renderCriteriaGroup('Linguistic Criteria', LINGUISTIC_CRITERIA)}
            <div className="mt-2">{renderCriteriaGroup('Clinical Communication', CLINICAL_CRITERIA, CLINICAL_BAND_OPTIONS)}</div>

            <div className="p-3 bg-surface border border-border rounded-md">
              <Textarea
                label="Final Overall Comment"
                placeholder="Provide a summary of the learner's performance..."
                value={finalComment}
                onChange={(event) => { setFinalComment(event.target.value); setIsDirty(true); }}
                rows={5}
                aria-label="Final overall comment"
                disabled={workspaceMeta?.isReadOnly}
              />
              <p className="text-xs text-muted mt-1 text-right">{finalComment.length} characters</p>
            </div>

            <div className="rounded-xl border border-border bg-surface p-3" role="region" aria-label="Voice-note feedback">
              <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
                <div className="min-w-0">
                  <p className="flex items-center gap-2 text-sm font-semibold text-navy">
                    <FileAudio className="h-4 w-4 text-primary" aria-hidden="true" /> Voice-note feedback
                  </p>
                  <p className="mt-1 text-xs text-muted">
                    Record spoken feedback for the candidate. Notes save automatically when you stop recording.
                  </p>
                  {isRecording && recordingStartedAt && (
                    <p className="mt-2 inline-flex items-center gap-2 rounded-full bg-red-50 px-2.5 py-1 text-xs font-semibold text-red-700">
                      <span className="inline-block h-2 w-2 animate-pulse rounded-full bg-red-500" aria-hidden="true" />
                      Recording...
                    </p>
                  )}
                </div>
                <div className="flex shrink-0 items-center gap-2">
                  {isRecording ? (
                    <Button
                      type="button"
                      size="sm"
                      variant="destructive"
                      onClick={handleStopRecording}
                      disabled={isUploadingVoiceNote}
                      aria-label="Stop recording voice note"
                    >
                      <Square className="h-4 w-4" /> Stop
                    </Button>
                  ) : (
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      onClick={() => void handleStartRecording()}
                      disabled={workspaceMeta?.isReadOnly || isUploadingVoiceNote}
                      loading={isUploadingVoiceNote}
                      aria-label="Record voice note"
                    >
                      <Mic className="h-4 w-4" /> {isUploadingVoiceNote ? 'Uploading...' : 'Record'}
                    </Button>
                  )}
                </div>
              </div>

              <div className="mt-3 space-y-2" role="list" aria-label="Saved voice notes">
                {voiceNotesLoading ? (
                  <p className="text-xs text-muted">Loading voice notes...</p>
                ) : voiceNotes.length === 0 ? (
                  <p className="rounded-lg border border-dashed border-border bg-muted p-3 text-xs text-muted">
                    No voice notes yet. Record one above to attach spoken feedback.
                  </p>
                ) : (
                  voiceNotes.map((note) => (
                    <div key={note.id} className="rounded-lg border border-border bg-muted p-3" role="listitem">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <div className="min-w-0">
                          <p className="truncate text-xs font-semibold text-navy">{note.fileName}</p>
                          <p className="text-[11px] text-muted">
                            {formatDuration(note.durationSeconds)} • {new Date(note.createdAt).toLocaleString()}
                          </p>
                        </div>
                        <div className="flex items-center gap-2">
                          <Badge variant={note.status === 'ready' || note.status === 'completed' ? 'success' : 'info'}>{note.status}</Badge>
                          {note.isOwner && !workspaceMeta?.isReadOnly && (
                            <button
                              type="button"
                              onClick={() => void handleDeleteVoiceNote(note.id)}
                              disabled={deletingVoiceNoteId === note.id}
                              className="inline-flex items-center gap-1 rounded-md border border-border bg-surface px-2 py-1 text-[11px] font-semibold text-danger hover:border-danger hover:bg-danger/5 disabled:opacity-50"
                              aria-label={`Delete voice note ${note.fileName}`}
                            >
                              <Trash2 className="h-3 w-3" /> {deletingVoiceNoteId === note.id ? 'Deleting...' : 'Delete'}
                            </button>
                          )}
                        </div>
                      </div>
                      <audio
                        controls
                        src={voiceNoteUrls[note.id]}
                        className="mt-2 w-full"
                        preload="metadata"
                        aria-label={`Voice note recorded ${new Date(note.createdAt).toLocaleString()}`}
                      />
                    </div>
                  ))
                )}
              </div>
            </div>

            <p className="text-xs text-muted">Keyboard: Ctrl+S save draft · Ctrl+Enter submit</p>
          </div>

          <div className="p-4 border-t border-border bg-surface flex justify-between items-center gap-3 shrink-0" role="toolbar" aria-label="Review actions">
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
                className="w-full text-sm border border-amber-300 rounded-md px-3 py-2 focus:outline-none focus:ring-2 focus:ring-amber-400/30 bg-surface"
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
