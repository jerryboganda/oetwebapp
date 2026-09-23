'use client';

import { useState, useEffect, useRef, Suspense, useCallback } from 'react';
import { Capacitor } from '@capacitor/core';
import { useParams, useRouter, useSearchParams } from 'next/navigation';
import { motion, AnimatePresence, useReducedMotion } from 'motion/react';
import {
  Mic, RotateCcw, CheckCircle2, AlertCircle,
  User, ShieldCheck, Loader2,
  Scissors,
} from 'lucide-react';
import { AppShell } from '@/components/layout/app-shell';
import { Button } from '@/components/ui/button';
import { Timer } from '@/components/ui/timer';
import { SpeakingRoleCard } from '@/components/domain/speaking-role-card';
import { SpeakingRulesConsent } from '@/components/domain/speaking/SpeakingRulesConsent';
import { fetchRoleCard, fetchSpeakingCompliance, submitSpeakingRecording, completeMockSection, type SpeakingComplianceCopy } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import {
  SpeakingRecorder,
  capturedSpeakingRecordingFromNativeStop,
  capturedSpeakingRecordingFromWebBlob,
  nativeSpeakingPauseSupported,
  tryPauseNativeSpeakingRecorder,
  tryResumeNativeSpeakingRecorder,
  type CapturedSpeakingRecording,
} from '@/lib/mobile/speaking-recorder';
import { getRealtimeValueTransition, prefersReducedMotion } from '@/lib/motion';
import type { RoleCard } from '@/lib/mock-data';
import { deriveDeliveryMode, deliveryModeLabel } from '@/lib/mocks/delivery-mode';
import { getFreeSpeakingCard } from '@/lib/api/speaking-role-play-cards';

// --- Types ---
type TaskMode = 'self' | 'exam';
type RecordingState = 'idle' | 'recording' | 'paused' | 'finished';

function LiveSpeakingTaskContent() {
  const reducedMotion = prefersReducedMotion(useReducedMotion());
  const realtimeTransition = getRealtimeValueTransition(reducedMotion);
  const params = useParams();
  const router = useRouter();
  const searchParams = useSearchParams();
  const rawId = params?.id;
  const id = typeof rawId === 'string' ? rawId : '';
  const requestedMode = searchParams?.get('mode');
  const requestedFree = searchParams?.get('free') === '1';
  // Generic mocks use mockAttemptId/mockSectionId; the two-role-play Speaking
  // mock orchestrator binds directly to its paired attempt/session ids.
  const mockAttemptId = searchParams?.get('mockAttemptId') ?? undefined;
  const mockSectionId = searchParams?.get('mockSectionId') ?? undefined;
  const speakingMockAttemptId = searchParams?.get('attemptId') ?? undefined;
  const speakingMockSessionId = searchParams?.get('mockSession') ?? searchParams?.get('mockSessionId') ?? undefined;
  const speakingMockSetId = searchParams?.get('mockSetId') ?? undefined;
  // Delivery mode (paper | computer | oet_home) is attached by the mock launch.
  // Speaking content is identical across modes — the interlocutor is live in
  // all three — so this only drives an informational badge.
  const deliveryModeParam = searchParams?.get('deliveryMode');
  const deliveryMode = deriveDeliveryMode(searchParams);
  const mode: TaskMode = requestedMode === 'exam' ? 'exam' : 'self';

  // --- Card State ---
  const [card, setCard] = useState<RoleCard | null>(null);
  const [cardLoading, setCardLoading] = useState(true);
  const [freeAccessKnown, setFreeAccessKnown] = useState(!requestedFree);
  const [freeAccessGranted, setFreeAccessGranted] = useState(false);
  const roleplayTimeSeconds = card?.roleplayTimeSeconds ?? 300;
  const roleplayTimeLabel = `${Math.round(roleplayTimeSeconds / 60)} min`;

  useEffect(() => {
    if (!id) {
      setCard(null);
      setCardLoading(false);
      return;
    }
    fetchRoleCard(id)
      .then(setCard)
      .catch(() => setCard(null))
      .finally(() => setCardLoading(false));
  }, [id]);

  useEffect(() => {
    if (!requestedFree || !id) {
      setFreeAccessKnown(true);
      setFreeAccessGranted(false);
      return;
    }

    let active = true;
    setFreeAccessKnown(false);
    getFreeSpeakingCard()
      .then((freeCard) => {
        if (!active) return;
        setFreeAccessGranted(freeCard.cardId === id);
        setFreeAccessKnown(true);
      })
      .catch(() => {
        if (!active) return;
        setFreeAccessGranted(false);
        setFreeAccessKnown(true);
      });

    return () => {
      active = false;
    };
  }, [id, requestedFree]);

  useEffect(() => {
    if (!id || requestedFree || !freeAccessKnown || freeAccessGranted) return;
    router.replace(`/speaking/roleplay/${encodeURIComponent(id)}`);
  }, [freeAccessGranted, freeAccessKnown, id, requestedFree, router]);

  // --- State ---
  const [recordingState, setRecordingState] = useState<RecordingState>('idle');
  const recordingStateRef = useRef<RecordingState>('idle');
  const [showStopConfirm, setShowStopConfirm] = useState(false);
  const [showSubmitConfirm, setShowSubmitConfirm] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [compliance, setCompliance] = useState<SpeakingComplianceCopy | null>(null);
  const [recordingConsentAccepted, setRecordingConsentAccepted] = useState(false);
  // CBT at-home rule: candidate must destroy (tear/cut) any scratch paper in view of the camera
  // before submission. Enforced in exam mode; optional acknowledgement in self-study mode.
  const [paperDestroyed, setPaperDestroyed] = useState(false);
  const paperRuleRequired = mode === 'exam';
  const [audioLevels, setAudioLevels] = useState<number[]>([10, 10, 10, 10, 10]);
  const [elapsedSeconds, setElapsedSeconds] = useState(0);

  useEffect(() => {
    fetchSpeakingCompliance()
      .then(setCompliance)
      .catch(() => setCompliance(null));
  }, []);

  // --- Refs ---
  const mediaRecorderRef = useRef<MediaRecorder | null>(null);
  const audioChunksRef = useRef<Blob[]>([]);
  const nativePulseRef = useRef<number | null>(null);
  const pendingNativeRecordingRef = useRef<CapturedSpeakingRecording | null>(null);
  const pendingNativeRecordingPromiseRef = useRef<Promise<CapturedSpeakingRecording> | null>(null);
  const recordingStartedAtRef = useRef<number | null>(null);
  const accumulatedRecordingMsRef = useRef(0);

  const audioContextRef = useRef<AudioContext | null>(null);
  const analyserRef = useRef<AnalyserNode | null>(null);
  const animationFrameRef = useRef<number | null>(null);

  const streamRef = useRef<MediaStream | null>(null);
  const stopDialogRef = useRef<HTMLDivElement | null>(null);
  const submitDialogRef = useRef<HTMLDivElement | null>(null);
  const stopPrimaryActionRef = useRef<HTMLButtonElement | null>(null);
  const submitPrimaryActionRef = useRef<HTMLButtonElement | null>(null);
  const stopTriggerRef = useRef<HTMLButtonElement | null>(null);
  const submitTriggerRef = useRef<HTMLButtonElement | null>(null);
  const restoreFocusRef = useRef<HTMLElement | null>(null);
  const resultNavigationTimerRef = useRef<number | null>(null);
  const isNativeRecorder = Capacitor.isNativePlatform();
  const nativePauseSupported = isNativeRecorder && nativeSpeakingPauseSupported();

  // --- Timer ---
  useEffect(() => {
    recordingStateRef.current = recordingState;
  }, [recordingState]);

  const buildRecordingBlob = useCallback(() =>
    new Blob(audioChunksRef.current, { type: mediaRecorderRef.current?.mimeType || 'audio/webm' }), []);

  const getRecordedDurationSeconds = useCallback(() => {
    const liveMs = recordingStartedAtRef.current === null ? 0 : Date.now() - recordingStartedAtRef.current;
    return Math.max(1, Math.round((accumulatedRecordingMsRef.current + liveMs) / 1000));
  }, []);

  const startDurationClock = useCallback((reset = false) => {
    if (reset) {
      accumulatedRecordingMsRef.current = 0;
      setElapsedSeconds(0);
    }
    recordingStartedAtRef.current = Date.now();
  }, []);

  const pauseDurationClock = useCallback(() => {
    if (recordingStartedAtRef.current !== null) {
      accumulatedRecordingMsRef.current += Date.now() - recordingStartedAtRef.current;
      recordingStartedAtRef.current = null;
      setElapsedSeconds(Math.max(0, Math.round(accumulatedRecordingMsRef.current / 1000)));
    }
  }, []);

  useEffect(() => {
    if (recordingState !== 'recording') return undefined;
    const timer = window.setInterval(() => {
      setElapsedSeconds(getRecordedDurationSeconds());
    }, 500);
    return () => window.clearInterval(timer);
  }, [getRecordedDurationSeconds, recordingState]);

  const stopNativeVisualizerPulse = useCallback(() => {
    if (nativePulseRef.current !== null) {
      window.clearInterval(nativePulseRef.current);
      nativePulseRef.current = null;
    }
  }, []);

  const startNativeVisualizerPulse = useCallback(() => {
    stopNativeVisualizerPulse();

    nativePulseRef.current = window.setInterval(() => {
      setAudioLevels([
        12 + Math.random() * 8,
        18 + Math.random() * 12,
        24 + Math.random() * 10,
        18 + Math.random() * 12,
        12 + Math.random() * 8,
      ]);
    }, 160);
  }, [stopNativeVisualizerPulse]);

  const trapDialogFocus = useCallback((event: KeyboardEvent, dialog: HTMLDivElement | null) => {
    if (event.key !== 'Tab' || !dialog) return;

    const focusable = dialog.querySelectorAll<HTMLElement>(
      'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'
    );

    if (focusable.length === 0) return;

    const first = focusable[0];
    const last = focusable[focusable.length - 1];

    if (event.shiftKey) {
      if (document.activeElement === first) {
        event.preventDefault();
        last.focus();
      }
    } else if (document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }, []);

  const stopRecorderAsync = useCallback(async (): Promise<CapturedSpeakingRecording> => {
    if (isNativeRecorder) {
      const recording = await SpeakingRecorder.stop();
      return capturedSpeakingRecordingFromNativeStop(recording, `${id}.m4a`);
    }

    const recorder = mediaRecorderRef.current;
    if (!recorder || recorder.state === 'inactive') {
      return capturedSpeakingRecordingFromWebBlob(
        buildRecordingBlob(),
        `${id}.webm`,
        getRecordedDurationSeconds() * 1000,
      );
    }

    return new Promise<CapturedSpeakingRecording>((resolve, reject) => {
      const handleStop = () => {
        recorder.removeEventListener('error', handleError);
        resolve(capturedSpeakingRecordingFromWebBlob(
          buildRecordingBlob(),
          `${id}.webm`,
          getRecordedDurationSeconds() * 1000,
        ));
      };

      const handleError = () => {
        recorder.removeEventListener('stop', handleStop);
        reject(new Error('Recording failed to stop cleanly.'));
      };

      recorder.addEventListener('stop', handleStop, { once: true });
      recorder.addEventListener('error', handleError, { once: true });
      recorder.stop();
    });
  }, [buildRecordingBlob, getRecordedDurationSeconds, id, isNativeRecorder]);

  const cleanupAudio = useCallback((stopRecorder = true) => {
    stopNativeVisualizerPulse();

    if (animationFrameRef.current) {
      cancelAnimationFrame(animationFrameRef.current);
    }
    if (audioContextRef.current && audioContextRef.current.state !== 'closed') {
      audioContextRef.current.close().catch(console.error);
    }
    if (stopRecorder && isNativeRecorder) {
      void SpeakingRecorder.cancel().catch(() => undefined);
    }
    if (stopRecorder && mediaRecorderRef.current && mediaRecorderRef.current.state !== 'inactive') {
      mediaRecorderRef.current.stop();
    }
    if (streamRef.current) {
      streamRef.current.getTracks().forEach(track => track.stop());
    }
  }, [isNativeRecorder, stopNativeVisualizerPulse]);

  const setupVisualizer = (stream: MediaStream, audioContext: AudioContext) => {
    const source = audioContext.createMediaStreamSource(stream);
    const analyser = audioContext.createAnalyser();
    analyser.fftSize = 32;
    source.connect(analyser);
    analyserRef.current = analyser;

    const updateVisualizer = () => {
      if (!analyserRef.current) return;
      const dataArray = new Uint8Array(analyserRef.current.frequencyBinCount);
      analyserRef.current.getByteFrequencyData(dataArray);
      
      const step = Math.floor(dataArray.length / 5);
      const newLevels = [];
      for (let i = 0; i < 5; i++) {
        let sum = 0;
        for (let j = 0; j < step; j++) {
          sum += dataArray[i * step + j];
        }
        const avg = sum / step;
        newLevels.push(10 + (avg / 255) * 30);
      }
      setAudioLevels(newLevels);
      animationFrameRef.current = requestAnimationFrame(updateVisualizer);
    };
    updateVisualizer();
    return source;
  };

  useEffect(() => {
    return () => {
      if (resultNavigationTimerRef.current !== null) {
        window.clearTimeout(resultNavigationTimerRef.current);
      }
      cleanupAudio();
    };
  }, [cleanupAudio]);

  useEffect(() => {
    const activeDialogRef = showStopConfirm ? stopDialogRef : showSubmitConfirm ? submitDialogRef : null;
    const activePrimaryRef = showStopConfirm ? stopPrimaryActionRef : showSubmitConfirm ? submitPrimaryActionRef : null;
    const activeTriggerRef = showStopConfirm ? stopTriggerRef : showSubmitConfirm ? submitTriggerRef : null;

    if (!activeDialogRef || !activePrimaryRef) {
      return;
    }

    const fallbackTrigger = activeTriggerRef?.current ?? null;
    restoreFocusRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : fallbackTrigger;

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        if (showStopConfirm) {
          setShowStopConfirm(false);
        }
        if (showSubmitConfirm) {
          setShowSubmitConfirm(false);
        }
        return;
      }

      trapDialogFocus(event, activeDialogRef.current);
    };

    document.addEventListener('keydown', handleKeyDown);
    requestAnimationFrame(() => activePrimaryRef.current?.focus());

    return () => {
      document.removeEventListener('keydown', handleKeyDown);

      const restoreTarget = restoreFocusRef.current;
      if (restoreTarget) {
        window.setTimeout(() => {
          restoreTarget.focus();
          if (document.activeElement !== restoreTarget) {
            requestAnimationFrame(() => restoreTarget.focus());
          }
        }, 50);
      }
    };
  }, [showStopConfirm, showSubmitConfirm, trapDialogFocus]);

  // --- Local Recording Controls (Self/Exam Mode) ---
  const handleStartRecording = async () => {
    setSubmitError(null);

    if (!recordingConsentAccepted) {
      setSubmitError('Please accept the recording consent before starting.');
      return;
    }

    if (recordingState === 'paused' && (isNativeRecorder || mediaRecorderRef.current)) {
      if (isNativeRecorder) {
        const resumed = await tryResumeNativeSpeakingRecorder();
        if (!resumed) {
          setSubmitError('Pause and resume are not available on this device yet. Continue recording or finish this attempt.');
          return;
        }
        startNativeVisualizerPulse();
      } else {
        const recorder = mediaRecorderRef.current;
        if (!recorder) {
          return;
        }

        recorder.resume();
      }
      startDurationClock();
      setRecordingState('recording');
      return;
    }

    try {
      if (!isNativeRecorder && (typeof navigator === 'undefined' || !navigator.mediaDevices?.getUserMedia || typeof MediaRecorder === 'undefined')) {
        setSubmitError('Recording is not supported in this browser. Use an updated Chrome, Edge, Safari, or the mobile app, then try again.');
        return;
      }
      if (isNativeRecorder) {
        await SpeakingRecorder.start({ mimeType: 'audio/mp4', fileName: `${id}.m4a` });
        mediaRecorderRef.current = null;
        audioChunksRef.current = [];
        pendingNativeRecordingRef.current = null;
        pendingNativeRecordingPromiseRef.current = null;
        startNativeVisualizerPulse();
        startDurationClock(true);
        setRecordingState('recording');
        return;
      }

      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      streamRef.current = stream;
      mediaRecorderRef.current = new MediaRecorder(stream);
      audioChunksRef.current = [];

      mediaRecorderRef.current.ondataavailable = (e) => {
        if (e.data.size > 0) {
          audioChunksRef.current.push(e.data);
        }
      };

      mediaRecorderRef.current.start(100); // Collect data every 100ms
      startDurationClock(true);
      setRecordingState('recording');

      const audioContext = new AudioContext();
      audioContextRef.current = audioContext;
      setupVisualizer(stream, audioContext);

    } catch (err) {
      console.error('Local recording failed:', err);
      const message = err instanceof DOMException && err.name === 'NotAllowedError'
        ? 'Microphone permission was blocked. Allow microphone access in your browser settings, then press Start recording again.'
        : err instanceof DOMException && err.name === 'NotFoundError'
          ? 'No microphone was detected. Connect a microphone or choose a different input device, then try again.'
          : err instanceof Error
            ? `Recording could not start: ${err.message}`
            : 'Recording could not start. Check your microphone and try again.';
      setSubmitError(message);
      cleanupAudio(true);
      setRecordingState('idle');
    }
  };

  useEffect(() => {
    if (mode !== 'exam' || recordingState !== 'recording' || elapsedSeconds < roleplayTimeSeconds) {
      return;
    }

    if (isNativeRecorder && nativePauseSupported) {
      void tryPauseNativeSpeakingRecorder();
      stopNativeVisualizerPulse();
    } else if (isNativeRecorder) {
      stopNativeVisualizerPulse();
      pendingNativeRecordingPromiseRef.current = stopRecorderAsync()
        .then((recording) => {
          pendingNativeRecordingRef.current = recording;
          return recording;
        })
        .catch((err) => {
          setSubmitError(err instanceof Error ? err.message : 'Recording could not be stopped at the time limit.');
          throw err;
        });
    } else if (mediaRecorderRef.current?.state === 'recording') {
      mediaRecorderRef.current.pause();
    }

    pauseDurationClock();
    setRecordingState('finished');
    setShowSubmitConfirm(true);
  }, [elapsedSeconds, isNativeRecorder, mode, nativePauseSupported, pauseDurationClock, recordingState, roleplayTimeSeconds, stopNativeVisualizerPulse, stopRecorderAsync]);

  // Wave 2 of docs/SPEAKING-MODULE-PLAN.md: 30-second audible warning
  // before the role-play time cap expires (default 4:30 of 5:00) and a
  // 10-second auto-submit kicker once the cap is reached. Only active in
  // strict 'exam' mode; self-study mode keeps untimed practice.
  const WARN_BEFORE_END_SECONDS = 30;
  const AUTO_SUBMIT_GRACE_SECONDS = 10;
  const warningPlayedRef = useRef(false);
  const [autoSubmitCountdown, setAutoSubmitCountdown] = useState<number | null>(null);
  const confirmSubmitRef = useRef<() => void>(() => undefined);

  const playWarningBeep = useCallback(() => {
    if (typeof window === 'undefined') return;
    try {
      const Ctor = window.AudioContext || (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext;
      if (!Ctor) return;
      const ctx = new Ctor();
      const osc = ctx.createOscillator();
      const gain = ctx.createGain();
      osc.type = 'sine';
      osc.frequency.value = 880;
      gain.gain.setValueAtTime(0.0001, ctx.currentTime);
      gain.gain.exponentialRampToValueAtTime(0.25, ctx.currentTime + 0.02);
      gain.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + 0.6);
      osc.connect(gain).connect(ctx.destination);
      osc.start();
      osc.stop(ctx.currentTime + 0.65);
      osc.onended = () => { try { ctx.close(); } catch { /* noop */ } };
    } catch {
      // Audio cue is best-effort; fail silently if WebAudio is unavailable.
    }
  }, []);

  useEffect(() => {
    if (mode !== 'exam' || recordingState !== 'recording') {
      warningPlayedRef.current = false;
      return;
    }
    const warnAt = roleplayTimeSeconds - WARN_BEFORE_END_SECONDS;
    if (warnAt > 0 && elapsedSeconds >= warnAt && !warningPlayedRef.current) {
      warningPlayedRef.current = true;
      playWarningBeep();
      analytics.track('speaking_time_warning', { taskId: id, mode, secondsRemaining: WARN_BEFORE_END_SECONDS });
    }
  }, [elapsedSeconds, id, mode, playWarningBeep, recordingState, roleplayTimeSeconds]);

  useEffect(() => {
    if (mode !== 'exam' || recordingState !== 'finished' || !showSubmitConfirm || elapsedSeconds < roleplayTimeSeconds) {
      setAutoSubmitCountdown(null);
      return undefined;
    }
    setAutoSubmitCountdown(AUTO_SUBMIT_GRACE_SECONDS);
    const interval = window.setInterval(() => {
      setAutoSubmitCountdown((prev) => {
        if (prev === null) return null;
        if (prev <= 1) {
          window.clearInterval(interval);
          // Fire auto-submit. Existing paper-rule guard inside
          // confirmSubmit will short-circuit if the candidate has not yet
          // checked the paper-destroyed acknowledgement, surfacing a
          // friendly error instead of silently swallowing the attempt.
          confirmSubmitRef.current();
          return 0;
        }
        return prev - 1;
      });
    }, 1000);
    return () => window.clearInterval(interval);
  }, [elapsedSeconds, mode, recordingState, roleplayTimeSeconds, showSubmitConfirm]);

  // --- Handlers ---
  const handleStop = () => setShowStopConfirm(true);
  const handleSubmit = () => {
    setSubmitError(null);
    setShowSubmitConfirm(true);
  };

  const confirmStop = () => {
    pauseDurationClock();
    setRecordingState('finished');
    cleanupAudio();
    router.push('/speaking/selection');
  };

  const handlePaperDestroyedToggle = (checked: boolean) => {
    setPaperDestroyed(checked);
    if (checked) {
      analytics.track('speaking_cbt_paper_destroyed', { taskId: id, mode });
    }
  };

  const confirmSubmit = async () => {
    if (isSubmitting) return;
    if (paperRuleRequired && !paperDestroyed) {
      setSubmitError('Please confirm you have destroyed your scratch paper on camera before submitting.');
      return;
    }

    const durationSeconds = getRecordedDurationSeconds();
    pauseDurationClock();
    setIsSubmitting(true);
    setSubmitError(null);
    setRecordingState('finished');
    analytics.track('task_submitted', { taskId: id, subtest: 'speaking', mode, durationSeconds, mockAttemptId, speakingMockSessionId });

    try {
      const recording = pendingNativeRecordingRef.current ?? await (pendingNativeRecordingPromiseRef.current ?? stopRecorderAsync());
      pendingNativeRecordingRef.current = null;
      pendingNativeRecordingPromiseRef.current = null;
      cleanupAudio(false);
      if (recording.blob.size === 0) {
        throw new Error('No speaking audio was captured.');
      }

      const uploadDurationSeconds = Math.max(durationSeconds, Math.round(recording.durationMs / 1000) || durationSeconds);

      const { submissionId } = await submitSpeakingRecording(
        id,
        recording.blob,
        uploadDurationSeconds,
        mode,
        {
          accepted: recordingConsentAccepted,
          text: compliance?.consentText,
        },
        speakingMockAttemptId && speakingMockSessionId
          ? {
              attemptId: speakingMockAttemptId,
              mockSessionId: speakingMockSessionId,
              fileName: recording.fileName,
              captureMethod: recording.captureMethod,
              contentType: recording.mimeType,
            }
          : {
              fileName: recording.fileName,
              captureMethod: recording.captureMethod,
              contentType: recording.mimeType,
            },
      );
      if (mockAttemptId && mockSectionId) {
        try {
          await completeMockSection(mockAttemptId, mockSectionId, {
            contentAttemptId: submissionId,
            rawScore: null,
            rawScoreMax: null,
            scaledScore: null,
            grade: null,
            evidence: {
              source: 'speaking_player',
              sessionId: submissionId,
              awaitingAiAssessment: requestedFree,
              awaitingTutorReview: !requestedFree,
            },
          });
        } catch (mockErr) {
          // Do not lose the learner's submission on mock-write failure.
          console.warn('Could not mark mock speaking section complete', mockErr);
        }
        if (requestedFree) {
          setShowSubmitConfirm(false);
          router.replace(`/speaking/results/${submissionId}`);
          return;
        }
        const mockUrl = `/mocks/player/${mockAttemptId}`;
        setShowSubmitConfirm(false);
        router.replace(mockUrl);
        resultNavigationTimerRef.current = window.setTimeout(() => {
          if (!window.location.pathname.startsWith('/mocks/player/')) {
            window.location.assign(mockUrl);
          }
        }, 1500);
        return;
      }
      if (speakingMockSessionId && speakingMockSetId) {
        const mockUrl = `/speaking/mocks/${encodeURIComponent(speakingMockSetId)}?session=${encodeURIComponent(speakingMockSessionId)}`;
        setShowSubmitConfirm(false);
        router.replace(mockUrl);
        resultNavigationTimerRef.current = window.setTimeout(() => {
          if (!window.location.pathname.startsWith('/speaking/mocks/')) {
            window.location.assign(mockUrl);
          }
        }, 1500);
        return;
      }
      const resultUrl = `/speaking/results/${submissionId}`;
      setShowSubmitConfirm(false);
      router.replace(resultUrl);
      resultNavigationTimerRef.current = window.setTimeout(() => {
        if (!window.location.pathname.startsWith('/speaking/results/')) {
          window.location.assign(resultUrl);
        }
      }, 1500);
    } catch (error) {
      console.error('Speaking submission failed:', error);
      cleanupAudio(false);
      setShowSubmitConfirm(false);
      setSubmitError(
        error instanceof Error
          ? error.message
          : 'Could not submit your recording. Please try again.'
      );
    } finally {
      setIsSubmitting(false);
    }
  };

  // Keep the ref-stable handle in sync with the latest closure so the
  // exam-mode auto-submit kicker (defined above confirmSubmit) can call
  // the most up-to-date implementation without inverting hook ordering.
  useEffect(() => {
    confirmSubmitRef.current = () => { void confirmSubmit(); };
  });

  if (!requestedFree) {
    return (
      <AppShell pageTitle="Speaking" workspaceRole="learner">
        <div className="flex min-h-[420px] items-center justify-center text-sm text-muted">
          Opening the native realtime Speaking voice session...
        </div>
      </AppShell>
    );
  }

  if (freeAccessKnown && !freeAccessGranted) {
    return (
      <AppShell pageTitle="Speaking" workspaceRole="learner">
        <div className="flex min-h-[420px] items-center justify-center text-sm text-muted">
          This recorder is available only for the designated free Speaking card. Returning to Speaking...
        </div>
      </AppShell>
    );
  }

  if (cardLoading || !freeAccessKnown) {
    return (
      <AppShell pageTitle="Speaking Task" workspaceRole="learner" distractionFree className="px-3 sm:px-4 lg:px-6">
        <div className="flex min-h-[420px] items-center justify-center rounded-3xl border border-border/80 bg-surface shadow-sm">
          <Loader2 className="w-8 h-8 text-primary animate-spin" />
        </div>
      </AppShell>
    );
  }

  return (
    <AppShell pageTitle={card?.title ?? 'Speaking Task'} workspaceRole="learner" distractionFree className="px-3 sm:px-4 lg:px-6">
    <div className="mx-auto flex min-h-[calc(100vh-7rem)] w-full max-w-[1280px] flex-col overflow-hidden rounded-2xl border border-border/80 bg-surface text-navy shadow-sm">
      {mockAttemptId || speakingMockSessionId ? (
        <div
          role="status"
          className="flex items-start gap-3 border-b border-info/30 bg-info/10 px-4 py-2.5 text-sm text-info sm:px-6"
        >
          <ShieldCheck aria-hidden className="mt-0.5 h-4 w-4 shrink-0" />
          <p className="flex-1">
            You&rsquo;re taking this Speaking section as part of a mock. Submitting will mark this section complete and return you to the mock dashboard. Scoring continues asynchronously.
          </p>
        </div>
      ) : null}
      {/* Top Bar */}
      <header className="z-20 flex items-center justify-between gap-3 border-b border-border/80 bg-surface/85 px-4 py-3 backdrop-blur-md sm:px-6 sm:py-4">
        <div className="flex items-center gap-3 min-w-0 overflow-hidden">
          <div className={`px-3 py-1 rounded-full text-[10px] font-black uppercase tracking-widest flex items-center gap-2 ${
            mode === 'self' ? 'bg-primary/10 text-primary border border-primary/30' :
            'bg-warning/10 text-warning border border-warning/30'
          }`}>
            {mode === 'self' ? <User className="w-3 h-3" /> : <ShieldCheck className="w-3 h-3" />}
            {mode === 'self' ? 'Self Practice' : 'Exam Simulation'}
          </div>
          {deliveryModeParam ? (
            <div className="hidden items-center rounded-full border border-border bg-background-light px-3 py-1 text-[10px] font-black uppercase tracking-widest text-muted sm:flex">
              {deliveryModeLabel(deliveryMode)}
            </div>
          ) : null}
          <div className="hidden h-4 w-px bg-border sm:block" />
          <div className="hidden sm:flex items-center gap-2 text-muted">
            <ShieldCheck className="w-4 h-4 text-success" />
            <span className="text-[10px] font-bold uppercase tracking-wider">
              Recorder ready
            </span>
          </div>
        </div>

        <div className="flex items-center gap-6">
          <div className="flex flex-col items-end">
            <span className="text-[10px] font-black text-muted uppercase tracking-widest">
              {mode === 'exam' ? 'Exam timer' : 'Elapsed Time'}
            </span>
            <Timer mode="elapsed" running={recordingState === 'recording'} size="lg" />
            {mode === 'exam' && (
              <span
                className={`mt-1 text-[10px] font-bold uppercase tracking-widest ${
                  warningPlayedRef.current && recordingState === 'recording'
                    ? 'animate-pulse text-danger'
                    : 'text-warning'
                }`}
                aria-live="polite"
              >
                {Math.max(0, roleplayTimeSeconds - elapsedSeconds)}s left of {roleplayTimeLabel}
                {warningPlayedRef.current && recordingState === 'recording' ? ' · time warning' : ''}
              </span>
            )}
          </div>
        </div>
      </header>

      {/* Main Content Area — 23 Sep 2026 owner flow: the ONE Rules + consent
          step comes first (no timer runs until recording starts), then the
          exam-style role card stays visible inline (no toggle/drawer) above a
          single recording control and one level indicator. Submit lives in
          the fixed footer; pb-40 keeps content clear of it. */}
      <main className="relative flex flex-1 flex-col items-center justify-start gap-6 overflow-y-auto bg-background-light p-4 pb-40 sm:p-6 sm:pb-44">
        {!recordingConsentAccepted ? (
          <div className="w-full max-w-2xl">
            <SpeakingRulesConsent
              freeSample={requestedFree}
              startLabel="Continue to the recorder"
              onStart={() => setRecordingConsentAccepted(true)}
            />
          </div>
        ) : (
          <>
            {card ? (
              <SpeakingRoleCard
                role={card.profession}
                setting={card.setting}
                patient={card.patient}
                background={card.background}
                tasks={card.tasks}
                disclaimer={card.disclaimer}
                sourceAttribution={card.sourceAttribution}
                className="w-full max-w-2xl"
              />
            ) : null}

            <div className="flex w-full max-w-2xl flex-col items-center gap-4 text-center">
              <h2 className="text-2xl font-black tracking-tight text-navy">
                {recordingState === 'idle' ? 'Ready to record' : 'Recording your response...'}
              </h2>
              <p className="text-sm font-medium leading-relaxed text-navy/70">
                Complete the tasks on your role card. Your recording will be saved for transcript review and speaking feedback.
              </p>

              {/* The single activity indicator. */}
              <div className="flex items-center gap-3 rounded-full border border-border bg-surface px-4 py-2" aria-live="polite">
                <span className={`h-2.5 w-2.5 rounded-full ${recordingState === 'recording' ? 'animate-pulse bg-danger' : 'bg-border'}`} />
                <div className="flex h-8 items-center gap-1" aria-hidden>
                  {audioLevels.map((level, i) => (
                    <motion.div
                      key={i}
                      animate={{ height: recordingState === 'recording' ? level * 0.8 : 6 }}
                      transition={realtimeTransition}
                      className="w-1.5 rounded-full bg-danger/80"
                    />
                  ))}
                </div>
                <span className="text-xs font-bold uppercase tracking-widest text-navy">
                  {recordingState === 'recording' ? `Recording · ${elapsedSeconds}s` : `${elapsedSeconds}s`}
                </span>
              </div>

              {recordingState === 'idle' ? (
                <Button size="lg" onClick={handleStartRecording}>
                  <Mic className="mr-2 h-5 w-5" aria-hidden />
                  Start recording
                </Button>
              ) : null}
            </div>
          </>
        )}
      </main>

      {/* Bottom Controls */}
      <footer className="fixed bottom-0 left-0 right-0 z-40 border-t border-border/40 bg-surface/80 px-4 sm:px-8 py-5 pb-[calc(1.25rem+var(--safe-area-inset-bottom))] backdrop-blur-2xl shadow-[0_-10px_40px_rgba(0,0,0,0.02)]">
        <div className="max-w-5xl mx-auto flex items-center justify-between relative">
          <button 
            onClick={handleStop}
            ref={stopTriggerRef}
            className="flex flex-col items-center gap-2 group transition-[color,background-color,border-color,box-shadow,transform,opacity,filter] duration-200"
            aria-label="Cancel task"
          >
            <div className="w-12 h-12 sm:w-14 sm:h-14 rounded-full border border-border bg-surface flex items-center justify-center group-hover:bg-danger/5 group-hover:border-danger/30 transition-[color,background-color,border-color,box-shadow,transform,opacity,filter] duration-200 shadow-sm">
              <RotateCcw className="w-5 h-5 sm:w-6 sm:h-6 text-muted group-hover:text-danger transition-colors" />
            </div>
            <span className="text-[9px] sm:text-[10px] font-black uppercase tracking-widest text-muted group-hover:text-danger transition-colors">Cancel Task</span>
          </button>

          <div className="flex flex-col items-center gap-3 absolute left-1/2 -translate-x-1/2">
            <Button
              size="lg"
              onClick={handleSubmit}
              disabled={recordingState === 'idle' || isSubmitting}
              className={`px-8 sm:px-14 py-6 sm:py-7 rounded-[1.25rem] font-black text-sm sm:text-lg transition-[color,background-color,border-color,box-shadow,transform,opacity,filter] duration-300 ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:pointer-events-none disabled:opacity-50 ${
                recordingState === 'idle' 
                  ? 'bg-muted/10 text-muted pointer-events-none' 
                  : 'bg-primary hover:bg-primary/90 active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600 text-white shadow-xl shadow-primary/25 hover:shadow-primary/40 hoverable:-translate-y-0.5'
              }`}
              ref={submitTriggerRef}
            >
              {isSubmitting ? (
                <span className="flex items-center gap-2">
                  <div className="w-4 h-4 rounded-full border-2 border-white/30 border-t-white animate-spin" />
                  Submitting Payload...
                </span>
              ) : 'Submit Recording'}
            </Button>
            {submitError ? (
              <p role="alert" className="max-w-sm text-center text-xs font-bold leading-relaxed text-danger bg-danger/10 px-3 py-1.5 rounded-lg border border-danger/20">
                {submitError}
              </p>
            ) : (
              <p className="text-[9px] sm:text-[10px] text-muted font-bold uppercase tracking-[0.3em] opacity-80">OET Speaking Simulation</p>
            )}
          </div>

          <div className="w-12 h-12 sm:w-14 sm:h-14" /> {/* Spacer for balance */}
        </div>
      </footer>

      {/* Modals */}
      <AnimatePresence>
        {showStopConfirm && (
          <div className="overlay-safe-area fixed inset-0 z-[100] flex items-center justify-center">
            <motion.div
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              className="absolute inset-0 bg-navy/80 backdrop-blur-sm"
              onClick={() => setShowStopConfirm(false)}
            />
            <motion.div
              initial={{ opacity: 0, scale: 0.9 }}
              animate={{ opacity: 1, scale: 1 }}
              exit={{ opacity: 0, scale: 0.9 }}
              ref={stopDialogRef}
              role="dialog"
              aria-modal="true"
              aria-labelledby="speaking-stop-dialog-title"
              className="relative max-h-[calc(100dvh-2rem-var(--safe-area-inset-top)-var(--safe-area-inset-bottom))] w-full max-w-md overflow-y-auto rounded-2xl border border-border bg-surface p-8 shadow-2xl"
            >
              <AlertCircle className="w-8 h-8 text-danger mb-4" aria-hidden />
              <h3 id="speaking-stop-dialog-title" className="text-2xl font-black mb-2">Stop Practice?</h3>
              <p className="text-muted text-sm leading-relaxed mb-8">
                Your current recording will be discarded. You will need to start the task again from the beginning.
              </p>
              <div className="flex flex-col gap-3">
                <Button ref={stopPrimaryActionRef} variant="destructive" fullWidth onClick={confirmStop} className="py-4 rounded-2xl font-black">
                  Yes, Discard and Exit
                </Button>
                <Button variant="outline" fullWidth onClick={() => setShowStopConfirm(false)} className="py-4 rounded-2xl">
                  Continue Practice
                </Button>
              </div>
            </motion.div>
          </div>
        )}

        {showSubmitConfirm && (
          <div className="overlay-safe-area fixed inset-0 z-[100] flex items-center justify-center">
            <motion.div
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              className="absolute inset-0 bg-navy/80 backdrop-blur-sm"
              onClick={() => setShowSubmitConfirm(false)}
            />
            <motion.div
              initial={{ opacity: 0, scale: 0.9 }}
              animate={{ opacity: 1, scale: 1 }}
              exit={{ opacity: 0, scale: 0.9 }}
              ref={submitDialogRef}
              role="dialog"
              aria-modal="true"
              aria-labelledby="speaking-submit-dialog-title"
              className="relative max-h-[calc(100dvh-2rem-var(--safe-area-inset-top)-var(--safe-area-inset-bottom))] w-full max-w-md overflow-y-auto rounded-2xl border border-border bg-surface p-8 shadow-2xl"
            >
              <CheckCircle2 className="w-8 h-8 text-success mb-4" aria-hidden />
              <h3 id="speaking-submit-dialog-title" className="text-2xl font-black mb-2">Finish Task?</h3>
              <p className="text-muted text-sm leading-relaxed mb-6">
                Are you ready to submit your recording for evaluation? You won&apos;t be able to make changes after this.
              </p>

              {/*
                Wave 2 of docs/SPEAKING-MODULE-PLAN.md: when the strict
                exam-mode 5-minute cap expires, surface a visible
                auto-submit countdown so the candidate knows the recording
                is being submitted automatically. The kicker still respects
                the paper-destroyed acknowledgement guard inside
                confirmSubmit.
              */}
              {autoSubmitCountdown !== null ? (
                <p
                  role="status"
                  aria-live="polite"
                  className="mb-6 rounded-2xl border border-danger/30 bg-danger/10 p-3 text-center text-xs font-bold text-danger"
                >
                  Time is up. Auto-submitting in {autoSubmitCountdown}s
                  {paperRuleRequired && !paperDestroyed
                    ? ' (waiting for paper-destroyed confirmation)'
                    : '…'}
                </p>
              ) : null}

              <div className="mb-6 rounded-2xl border border-warning/30 bg-warning/10 p-4">
                <div className="flex items-start gap-3">
                  <Scissors className="mt-0.5 h-5 w-5 flex-shrink-0 text-warning" aria-hidden="true" />
                  <div className="flex-1">
                    <p className="text-sm font-bold text-warning">
                      Destroy your scratch paper on camera
                    </p>
                    <p className="mt-1 text-xs leading-relaxed text-warning/80">
                      OET rules for the at-home computer-based Speaking test require any paper notes to be
                      torn or cut in full view of the webcam before you submit. This is verified by the proctor
                      from the session recording.
                    </p>
                    <label className="mt-3 flex items-start gap-2 text-xs text-warning">
                      <input
                        type="checkbox"
                        checked={paperDestroyed}
                        onChange={(e) => handlePaperDestroyedToggle(e.target.checked)}
                        disabled={isSubmitting}
                        className="mt-0.5 h-4 w-4 rounded border-warning/30 text-warning focus:ring-warning"
                        aria-describedby="speaking-paper-destroy-hint"
                      />
                      <span id="speaking-paper-destroy-hint">
                        I have torn or cut my scratch paper in front of the camera
                        {paperRuleRequired ? ' (required)' : ' (recommended for exam realism)'}.
                      </span>
                    </label>
                  </div>
                </div>
              </div>

              {submitError && (
                <p role="alert" className="mb-4 rounded-xl bg-danger/10 p-3 text-xs font-semibold text-danger">
                  {submitError}
                </p>
              )}

              <div className="flex flex-col gap-3">
                <Button ref={submitPrimaryActionRef} fullWidth onClick={confirmSubmit} disabled={isSubmitting || (paperRuleRequired && !paperDestroyed)} className="py-4 rounded-2xl font-black">
                  {isSubmitting ? 'Submitting...' : 'Submit for Evaluation'}
                </Button>
                <Button variant="outline" fullWidth onClick={() => setShowSubmitConfirm(false)} disabled={isSubmitting} className="py-4 rounded-2xl">
                  Not Yet, Keep Going
                </Button>
              </div>
            </motion.div>
          </div>
        )}
      </AnimatePresence>
    </div>
    </AppShell>
  );
}

export default function LiveSpeakingTask() {
  return (
    <Suspense fallback={
      <AppShell pageTitle="Speaking Task" workspaceRole="learner" distractionFree className="px-3 sm:px-4 lg:px-6">
        <div className="flex min-h-[420px] items-center justify-center rounded-3xl border border-border/80 bg-surface shadow-sm">
          <Loader2 className="w-8 h-8 text-primary animate-spin" />
        </div>
      </AppShell>
    }>
      <LiveSpeakingTaskContent />
    </Suspense>
  );
}
