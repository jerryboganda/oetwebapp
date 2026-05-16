'use client';

import { useState, useEffect, useCallback, useMemo, useRef } from 'react';
import { AnimatePresence, MotionConfig, motion, useReducedMotion } from 'motion/react';
import type { Transition } from 'motion/react';
import { BookOpen, Brain, ChevronLeft, ClipboardCheck, FileCheck2, GraduationCap, Maximize, Minimize, UploadCloud, UserRoundCheck } from 'lucide-react';
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
import { InlineAlert } from '@/components/ui/alert';
import { fetchAuthorizedObjectUrl, fetchWritingTask, fetchWritingChecklist, ensureWritingAttempt, submitWritingDraft, submitWritingTask, completeMockSection, fetchWritingEntitlement, lintWritingViaApi, uploadMedia, attachWritingPaperAssets, fetchWritingPaperAssets, ApiError, type WritingAssessorType, type WritingEntitlement, type WritingExamMode } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { WritingTask } from '@/lib/mock-data';
import type { WritingPaperAsset } from '@/lib/types/expert';
import { useIsMobile } from '@/hooks/use-mobile';
import { inferWritingLetterType, type ExamProfession, type LintFinding } from '@/lib/rulebook';
import {
  normalizeWritingPracticeMode,
  WRITING_CRITERIA,
  WRITING_READING_WINDOW_SECONDS,
  WRITING_WINDOW_SECONDS,
} from '@/lib/writing/workflow';

type SaveStatus = 'idle' | 'saving' | 'saved' | 'failed';

function normalizeExamProfession(value: string | null | undefined): ExamProfession {
  const normalized = (value ?? '').toLowerCase().replace(/[^a-z0-9]/g, '');
  const professions: Record<string, ExamProfession> = {
    medicine: 'medicine',
    nursing: 'nursing',
    dentistry: 'dentistry',
    pharmacy: 'pharmacy',
    physiotherapy: 'physiotherapy',
    veterinary: 'veterinary',
    optometry: 'optometry',
    radiography: 'radiography',
    occupationaltherapy: 'occupational-therapy',
    speechpathology: 'speech-pathology',
    podiatry: 'podiatry',
    dietetics: 'dietetics',
    otheralliedhealth: 'other-allied-health',
  };

  return professions[normalized] ?? 'medicine';
}

function writingRulebookHref(ruleId: string, profession: string | null | undefined) {
  return `/writing/rulebook/${encodeURIComponent(ruleId)}?profession=${encodeURIComponent(normalizeExamProfession(profession))}`;
}

type WritingPhase = 'reading' | 'writing';

interface ExamTimerState {
  startedAt: string;
  phase: WritingPhase;
  initialSeconds: number;
}

function deriveExamTimerState(startedAt: string): ExamTimerState {
  const elapsedSeconds = Math.max(0, Math.floor((Date.now() - Date.parse(startedAt)) / 1000));
  if (elapsedSeconds < WRITING_READING_WINDOW_SECONDS) {
    return {
      startedAt,
      phase: 'reading',
      initialSeconds: WRITING_READING_WINDOW_SECONDS - elapsedSeconds,
    };
  }

  const writingElapsedSeconds = elapsedSeconds - WRITING_READING_WINDOW_SECONDS;
  return {
    startedAt,
    phase: 'writing',
    initialSeconds: Math.max(0, WRITING_WINDOW_SECONDS - writingElapsedSeconds),
  };
}

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
  const taskId = searchParams?.get('taskId') ?? '';
  const practiceMode = normalizeWritingPracticeMode(searchParams?.get('mode'));
  const examMode: WritingExamMode = searchParams?.get('examMode') === 'paper' ? 'paper' : 'computer';
  const assessorType: WritingAssessorType = (searchParams?.get('assessorType') ?? searchParams?.get('assessor')) === 'instructor' ? 'instructor' : 'ai';
  const isPaperMode = examMode === 'paper';
  const isInstructorReview = assessorType === 'instructor';
  const isExamMode = practiceMode === 'exam';
  // Mocks V2 — BuildLaunchRoute attaches mockAttemptId/mockSectionId when
  // this player is launched as a section of a mock attempt. Writing scores
  // are tutor-graded asynchronously, so we POST nulls and mark the section
  // Completed (the mock report renders this as Provisional).
  const mockAttemptId = searchParams?.get('mockAttemptId') ?? null;
  const mockSectionId = searchParams?.get('mockSectionId') ?? null;

  const [task, setTask] = useState<WritingTask | null>(null);
  const [checklist, setChecklist] = useState<{ id: string; label: string; checked: boolean }[]>([]);
  const [loading, setLoading] = useState(() => taskId.length > 0);
  const [content, setContent] = useState('');
  const [scratchpad, setScratchpad] = useState('');
  const [saveStatus, setSaveStatus] = useState<SaveStatus>('idle');
  const [lintFindings, setLintFindings] = useState<LintFinding[]>([]);
  const [lintError, setLintError] = useState(false);
  const [fontSize, setFontSize] = useState(16);
  const [isDistractionFree, setIsDistractionFree] = useState(false);
  const [activeTab, setActiveTab] = useState<'notes' | 'scratchpad' | 'checklist'>('notes');
  const [showLeaveModal, setShowLeaveModal] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [paperAssets, setPaperAssets] = useState<WritingPaperAsset[]>([]);
  const [paperAssetUrls, setPaperAssetUrls] = useState<Record<string, string>>({});
  const [paperExtractedText, setPaperExtractedText] = useState('');
  const [paperError, setPaperError] = useState<string | null>(null);
  const [uploadingPaper, setUploadingPaper] = useState(false);
  const [learnerNotes, setLearnerNotes] = useState('');
  /**
   * Entitlement gate — server is the source of truth (HTTP 402 with
   * `writing_quota_exceeded` is enforced regardless), but we surface a
   * friendly modal up-front when the cached entitlement says the learner
   * cannot grade right now.
   */
  const [entitlementBlock, setEntitlementBlock] = useState<
    | { reason: 'premium_required' | 'quota_exceeded'; resetAt: string | null }
    | null
  >(null);
  const [timerRunning, setTimerRunning] = useState(true);
  const [mobileView, setMobileView] = useState<'notes' | 'editor'>('notes');
  /**
   * OET Writing phase enforcement. The exam is split into a 5-minute reading
   * window (editor + highlighting locked) followed by a 40-minute writing
   * window. Once the reading window ends it cannot be re-entered.
   *
   * Source: Dr. Ahmed Hesham corrections (applies to ALL OET professions).
   */
  const [phase, setPhase] = useState<WritingPhase>(() => (isExamMode ? 'reading' : 'writing'));
  const [examTimerState, setExamTimerState] = useState<ExamTimerState | null>(null);
  const isReadingPhase = isExamMode && !isPaperMode && phase === 'reading';
  const saveTimerRef = useRef<NodeJS.Timeout | undefined>(undefined);
  const examAttemptStartedAtRef = useRef<string | null>(null);
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

  useEffect(() => {
    if (!taskId) {
      setLoading(false);
      return;
    }
    let cancelled = false;

    if (!task || !lintReady) {
      setLintFindings([]);
      setLintError(false);
      return;
    }

    const timer = window.setTimeout(async () => {
      const minorAgeMatch = task.caseNotes.match(/\b(\d+)\s*(years? old|year-old)\b/i);
      const patientAge = minorAgeMatch ? Number(minorAgeMatch[1]) : null;

      try {
        const result = await lintWritingViaApi({
          letterText: content,
          contentId: task.id,
          letterType: inferredLetterType,
          profession: normalizeExamProfession(task.profession),
          recipientSpecialty: task.title,
          recipientName: task.recipient ?? null,
          patientAge,
          patientIsMinor: patientAge !== null && patientAge < 18,
        });

        if (!cancelled) {
          setLintFindings(result.findings);
          setLintError(false);
        }
      } catch {
        if (!cancelled) {
          setLintFindings([]);
          setLintError(true);
        }
      }
    }, 500);

    return () => {
      cancelled = true;
      window.clearTimeout(timer);
    };
  }, [taskId, task, lintReady, content, inferredLetterType]);

  const lintInactiveMessage = lintError
    ? 'The live rulebook checker could not refresh. Your draft is still saved normally.'
    : !lintReady
    ? 'The live rulebook checker starts once you have written a few sentences, so early brainstorming does not create noisy warnings.'
    : undefined;

  useEffect(() => {
    let cancelled = false;
    setLoading(true);

    const attemptPromise = isExamMode ? ensureWritingAttempt(taskId, practiceMode) : Promise.resolve(null);
    Promise.all([fetchWritingTask(taskId), fetchWritingChecklist(), attemptPromise])
      .then(([t, cl, attempt]) => {
        if (cancelled) return;
        setTask(t);
        setChecklist(cl.map(c => ({ id: String(c.id), label: c.text, checked: c.completed })));
        if (attempt) {
          examAttemptStartedAtRef.current = attempt.startedAt;
          const nextTimerState = deriveExamTimerState(attempt.startedAt);
          setExamTimerState(nextTimerState);
          setPhase(nextTimerState.phase);
          if (nextTimerState.phase === 'writing') {
            setMobileView('editor');
          }
          if (attempt.draftContent && !latestContentRef.current) {
            latestContentRef.current = attempt.draftContent;
            setContent(attempt.draftContent);
            hasUnsavedChanges.current = false;
          }
        } else {
          examAttemptStartedAtRef.current = null;
          setExamTimerState(null);
          setPhase('writing');
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [isExamMode, practiceMode, taskId]);

  useEffect(() => {
    return () => {
      if (saveTimerRef.current) {
        clearTimeout(saveTimerRef.current);
      }
    };
  }, []);

  // Auto-save with 3s debounce
  const triggerAutoSave = useCallback((nextContent = latestContentRef.current) => {
    if (!taskId) return;
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

  useEffect(() => {
    if (!isPaperMode || !taskId) return;
    let cancelled = false;
    fetchWritingPaperAssets(taskId, practiceMode)
      .then((result) => {
        if (cancelled) return;
        setPaperAssets(result.assets ?? []);
        setPaperExtractedText(result.extractedText ?? '');
        if (result.extractedText && !latestContentRef.current) {
          latestContentRef.current = result.extractedText;
          setContent(result.extractedText);
        }
      })
      .catch(() => {
        if (!cancelled) setPaperAssets([]);
      });
    return () => {
      cancelled = true;
    };
  }, [isPaperMode, practiceMode, taskId]);

  const handlePaperUpload = useCallback(async (files: FileList | null) => {
    if (!taskId || !files?.length || uploadingPaper) return;
    const selectedFiles = Array.from(files);
    setUploadingPaper(true);
    setPaperError(null);
    try {
      const uploads = await Promise.all(selectedFiles.map((file) => uploadMedia(file)));
      const attached = await attachWritingPaperAssets(taskId, uploads.map((asset) => asset.id), practiceMode, true);
      setPaperAssets(attached.assets ?? []);
      const extractedText = attached.extractedText ?? '';
      setPaperExtractedText(extractedText);
      latestContentRef.current = extractedText;
      setContent(extractedText);
      analytics.track('writing_paper_uploaded', { taskId, fileCount: selectedFiles.length, extractionState: attached.extractionState });
    } catch (err) {
      setPaperError(err instanceof ApiError ? err.userMessage : err instanceof Error ? err.message : 'Could not read the uploaded pages.');
    } finally {
      setUploadingPaper(false);
    }
  }, [practiceMode, taskId, uploadingPaper]);

  useEffect(() => {
    if (!paperAssets.length) {
      return;
    }

    let cancelled = false;
    const createdUrls: string[] = [];
    Promise.all(paperAssets.map(async (asset) => {
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
  }, [paperAssets]);

  // Prevent accidental navigation
  useEffect(() => {
    const handler = (e: BeforeUnloadEvent) => {
      if (hasUnsavedChanges.current) { e.preventDefault(); e.returnValue = ''; }
    };
    window.addEventListener('beforeunload', handler);
    return () => window.removeEventListener('beforeunload', handler);
  }, []);

  const paperAssetsComplete = !isPaperMode || (paperAssets.length > 0 && paperAssets.every((asset) => asset.extractionState === 'completed'));
  const paperHasExtractedText = !isPaperMode || paperExtractedText.trim().length > 0;
  const paperSubmitBlockedReason = !isPaperMode
    ? null
    : paperAssets.length === 0
      ? 'Upload the handwritten response before submitting this paper-mode attempt.'
      : !paperAssetsComplete
        ? 'Wait for OCR to complete successfully for every uploaded page before submitting.'
        : !paperHasExtractedText
          ? 'OCR did not find enough readable text. Re-upload clearer JPG, PNG, or PDF pages.'
          : null;
  const submitDisabled = isReadingPhase || submitting || uploadingPaper || Boolean(paperSubmitBlockedReason);

  const handleSubmit = async () => {
    if (!taskId) return;
    if (submitting) return;
    if (isReadingPhase) {
      setSaveStatus('idle');
      return;
    }

    if (isPaperMode && paperSubmitBlockedReason) {
      setPaperError(paperSubmitBlockedReason);
      return;
    }

    // Pre-flight entitlement check for instant AI grading. Dr. Ahmed review is
    // charged through review credits on the review-request path.
    if (!isInstructorReview) {
      try {
        const ent: WritingEntitlement = await fetchWritingEntitlement();
        if (!ent.allowed && (ent.reason === 'premium_required' || ent.reason === 'quota_exceeded')) {
          setEntitlementBlock({ reason: ent.reason, resetAt: ent.resetAt });
          return;
        }
      } catch {
        // Non-fatal: fall through to submit and let the server enforce.
      }
    }

    isSubmittingRef.current = true;
    autosaveSequenceRef.current += 1;
    if (saveTimerRef.current) {
      clearTimeout(saveTimerRef.current);
      saveTimerRef.current = undefined;
    }

    const submittedContent = isPaperMode ? paperExtractedText : latestContentRef.current;

    setSubmitting(true);
    setSaveStatus('saving');
    try {
      const result = await submitWritingTask(taskId, submittedContent, practiceMode, {
        examMode,
        assessorType,
        paperAssetIds: isPaperMode ? paperAssets.map((asset) => asset.mediaAssetId) : [],
        learnerNotes: learnerNotes.trim() || undefined,
      });
      analytics.track('task_submitted', { taskId, subtest: 'writing', mode: practiceMode, examMode, assessorType });
      hasUnsavedChanges.current = false;
      setSaveStatus('saved');
      setTimerRunning(false);
      if (mockAttemptId && mockSectionId) {
        try {
          await completeMockSection(mockAttemptId, mockSectionId, {
            contentAttemptId: result.attemptId ?? result.id,
            rawScore: null,
            rawScoreMax: null,
            scaledScore: null,
            grade: null,
            evidence: { source: 'writing_player', submissionId: result.attemptId ?? result.id, reviewRequestId: result.reviewRequestId ?? null, awaitingTutorReview: Boolean(result.reviewRequestId) },
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
      const resultUrl = result.reviewRequestId
        ? `/submissions/${encodeURIComponent(result.attemptId ?? result.id)}`
        : `/writing/result?id=${encodeURIComponent(result.id)}`;
      router.replace(resultUrl);
      window.setTimeout(() => {
        const expectedPath = result.reviewRequestId ? `/submissions/${encodeURIComponent(result.attemptId ?? result.id)}` : '/writing/result';
        if (window.location.pathname !== expectedPath) {
          window.location.assign(resultUrl);
        }
      }, 500);
    } catch (err) {
      isSubmittingRef.current = false;
      setSubmitting(false);
      setSaveStatus('failed');
      // Surface server-side quota gate (HTTP 402) gracefully rather than as
      // a generic submission failure.
      if (err instanceof ApiError && err.status === 402 && err.code === 'writing_quota_exceeded') {
        setEntitlementBlock({ reason: 'quota_exceeded', resetAt: null });
      } else if (err instanceof ApiError && err.code === 'writing_reading_window_active') {
        setSaveStatus('idle');
      } else if (err instanceof ApiError && (err.code === 'paper_ocr_incomplete' || err.code === 'paper_ocr_required')) {
        setPaperError(err.userMessage);
      }
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
    if (!isExamMode || !taskId) return;
    const startedAt = examAttemptStartedAtRef.current;
    if (startedAt) {
      const nextTimerState = deriveExamTimerState(startedAt);
      setExamTimerState({
        ...nextTimerState,
        phase: 'writing',
        initialSeconds: nextTimerState.phase === 'writing' ? nextTimerState.initialSeconds : WRITING_WINDOW_SECONDS,
      });
    }
    setPhase(prev => {
      if (prev !== 'reading') return prev;
      analytics.track('writing_reading_window_ended', { taskId, mode: practiceMode });
      return 'writing';
    });
    // When the reading window ends we auto-surface the editor on mobile so
    // the learner immediately sees where to start writing.
    setMobileView(prev => (prev === 'notes' ? 'editor' : prev));
  }, [isExamMode, practiceMode, taskId]);

  const timerInitialSeconds = isExamMode
    ? examTimerState?.initialSeconds ?? (isReadingPhase ? WRITING_READING_WINDOW_SECONDS : WRITING_WINDOW_SECONDS)
    : 0;
  const timerKey = isExamMode
    ? `${examTimerState?.startedAt ?? 'pending'}-${phase}-${timerInitialSeconds}`
    : 'learning';

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

  if (!taskId) {
    return (
      <div className="flex min-h-[var(--app-viewport-height,100dvh)] items-center justify-center bg-background-light p-6">
        <div className="w-full max-w-xl space-y-4 rounded-2xl border border-border bg-surface p-6 shadow-sm">
          <InlineAlert variant="warning">
            This writing task is unavailable. Please open a published task from the Writing library.
          </InlineAlert>
          <Button variant="primary" onClick={() => router.push('/writing/library')}>
            Open Writing library
          </Button>
        </div>
      </div>
    );
  }

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
                      <Badge variant="outline" size="sm">{isPaperMode ? 'Paper upload' : 'Computer typed'}</Badge>
                      <Badge variant={isInstructorReview ? 'warning' : 'success'} size="sm">
                        {isInstructorReview ? 'Dr. Ahmed' : 'AI assessor'}
                      </Badge>
                    </div>
                  </div>
                </div>

                <div className="flex items-center gap-2 self-end sm:gap-3 sm:self-auto sm:justify-end">
                  <Timer
                    key={`writing-timer-${timerKey}`}
                    initialSeconds={timerInitialSeconds}
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
                  <Button variant="primary" size={isMobile ? 'sm' : 'md'} onClick={handleSubmit} loading={submitting} disabled={submitDisabled} className="shrink-0 whitespace-nowrap touch-target">
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

        <AnimatePresence initial={false}>
          {isPaperMode && !isDistractionFree ? (
            <motion.section
              key="writing-paper-upload"
              initial={{ opacity: 0, y: -8 }}
              animate={{ opacity: 1, y: 0 }}
              exit={{ opacity: 0, y: -8 }}
              transition={panelTransition}
              className="border-b border-border bg-surface/95 px-4 py-3"
            >
              <div className="grid gap-3 lg:grid-cols-[minmax(0,0.9fr)_minmax(0,1.1fr)]">
                <div className="rounded-2xl border border-border bg-background-light p-3">
                  <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                    <div>
                      <div className="flex items-center gap-2 text-xs font-black uppercase tracking-widest text-navy">
                        <UploadCloud className="h-4 w-4 text-primary" /> Paper response
                      </div>
                      <p className="mt-1 text-xs leading-5 text-muted">Upload PDF or clear page photos for OCR before submission.</p>
                    </div>
                    <label className="inline-flex cursor-pointer items-center justify-center gap-2 rounded-xl bg-primary px-4 py-2 text-sm font-bold text-white shadow-sm transition-colors hover:bg-primary-dark">
                      {uploadingPaper ? 'Reading pages...' : 'Upload pages'}
                      <input
                        type="file"
                        accept="application/pdf,image/png,image/jpeg"
                        multiple
                        className="sr-only"
                        disabled={uploadingPaper || submitting}
                        onChange={(event) => void handlePaperUpload(event.currentTarget.files)}
                      />
                    </label>
                  </div>
                  {paperError ? <p className="mt-3 rounded-xl border border-danger/30 bg-danger/10 p-3 text-sm text-danger">{paperError}</p> : null}
                  {paperAssets.length ? (
                    <div className="mt-3 grid gap-2 sm:grid-cols-2">
                      {paperAssets.map((asset) => (
                        <a key={asset.id} href={paperAssetUrls[asset.id]} target="_blank" rel="noreferrer" aria-disabled={!paperAssetUrls[asset.id]} className="flex items-center gap-2 rounded-xl border border-border bg-surface p-2 text-sm font-semibold text-navy hover:border-primary/40">
                          <FileCheck2 className="h-4 w-4 text-success" />
                          <span className="min-w-0 flex-1 truncate">Page {asset.pageNumber}: {asset.fileName}</span>
                          <Badge variant={asset.extractionState === 'completed' ? 'success' : 'warning'} size="sm">{asset.extractionState}</Badge>
                        </a>
                      ))}
                    </div>
                  ) : null}
                </div>

                <div className="rounded-2xl border border-border bg-background-light p-3">
                  <div className="mb-2 flex items-center gap-2 text-xs font-black uppercase tracking-widest text-navy">
                    {isInstructorReview ? <UserRoundCheck className="h-4 w-4 text-primary" /> : <Brain className="h-4 w-4 text-primary" />}
                    {isInstructorReview ? 'Dr. Ahmed notes' : 'AI grading notes'}
                  </div>
                  <textarea
                    value={learnerNotes}
                    onChange={(event) => setLearnerNotes(event.target.value)}
                    rows={3}
                    className="w-full resize-none rounded-xl border border-border bg-surface p-3 text-sm text-navy outline-none focus:border-primary focus:ring-2 focus:ring-primary/15"
                    placeholder="Optional context for the assessor"
                  />
                  <p className="mt-2 text-xs text-muted">
                    OCR text available: {paperExtractedText.trim().length ? `${paperExtractedText.trim().length} characters` : 'not yet'}.
                  </p>
                  {paperSubmitBlockedReason ? <p className="mt-2 text-xs font-semibold text-warning">{paperSubmitBlockedReason}</p> : null}
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
                key={`writing-timer-df-${timerKey}`}
                initialSeconds={timerInitialSeconds}
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
                  {isPaperMode ? 'OCR' : 'Editor'}
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
                        placeholder={isPaperMode ? 'Upload paper pages to preview extracted text...' : isReadingPhase ? 'Writing is locked for the 5-minute reading window\u2026' : 'Begin writing your response...'}
                        disabled={isReadingPhase || isPaperMode}
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
                          ruleHref={(ruleId) => writingRulebookHref(ruleId, task.profession)}
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
                  placeholder={isPaperMode ? 'Upload paper pages to preview extracted text...' : isReadingPhase ? 'Writing is locked for the 5-minute reading window\u2026' : 'Begin writing your response...'}
                  disabled={isReadingPhase || isPaperMode}
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
                    ruleHref={(ruleId) => writingRulebookHref(ruleId, task.profession)}
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

        {/* Entitlement Gate Modal — premium-required or quota-exceeded. */}
        <Modal
          open={entitlementBlock !== null}
          onClose={() => setEntitlementBlock(null)}
          title={entitlementBlock?.reason === 'premium_required' ? 'AI grading is a premium feature' : 'Free quota reached'}
        >
          <p className="mb-6 text-sm text-muted">
            {entitlementBlock?.reason === 'premium_required'
              ? 'Upgrade to get instant rule-cited feedback on every writing submission. Your draft has been saved.'
              : entitlementBlock?.resetAt
                ? `You\u2019ve used all your free AI gradings for this week. Quota resets ${new Date(entitlementBlock.resetAt).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}. Upgrade for unlimited grading.`
                : 'You\u2019ve reached the free grading quota. Upgrade for unlimited rule-cited feedback.'}
          </p>
          <div className="flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
            <Button variant="secondary" onClick={() => setEntitlementBlock(null)}>Not now</Button>
            <Button variant="primary" onClick={() => router.push('/billing/subscribe')}>Upgrade</Button>
          </div>
        </Modal>
      </div>
    </MotionConfig>
  );
}
