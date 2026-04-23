'use client';

import { useState, useEffect, useRef, Suspense, useCallback } from 'react';
import { Capacitor } from '@capacitor/core';
import { useParams, useRouter, useSearchParams } from 'next/navigation';
import { motion, AnimatePresence } from 'motion/react';
import {
  Mic, Square, RotateCcw, CheckCircle2, AlertCircle,
  FileText, Edit3, ChevronUp, ChevronDown,
  Wifi, WifiOff, User, ShieldCheck, Loader2, Play, Pause,
  Scissors,
} from 'lucide-react';
import { AppShell } from '@/components/layout/app-shell';
import { Button } from '@/components/ui/button';
import { Timer } from '@/components/ui/timer';
import { fetchRoleCard, submitSpeakingRecording } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { SpeakingRecorder, base64ToBlob } from '@/lib/mobile/speaking-recorder';
import type { RoleCard } from '@/lib/mock-data';

// --- Types ---
type TaskMode = 'self' | 'exam';
type ConnectionStatus = 'connecting' | 'connected' | 'disconnected' | 'error';
type RecordingState = 'idle' | 'recording' | 'paused' | 'finished';

function LiveSpeakingTaskContent() {
  const params = useParams();
  const router = useRouter();
  const searchParams = useSearchParams();
  const id = params?.id as string;
  const requestedMode = searchParams?.get('mode');
  const mode: TaskMode = requestedMode === 'exam' ? 'exam' : 'self';

  // --- Card State ---
  const [card, setCard] = useState<RoleCard | null>(null);
  const [cardLoading, setCardLoading] = useState(true);

  useEffect(() => {
    fetchRoleCard(id)
      .then(setCard)
      .catch(() => setCard(null))
      .finally(() => setCardLoading(false));
  }, [id]);

  // --- State ---
  const [recordingState, setRecordingState] = useState<RecordingState>('idle');
  const recordingStateRef = useRef<RecordingState>('idle');
  const [connectionStatus] = useState<ConnectionStatus>('connected');
  const [showRoleCard, setShowRoleCard] = useState(false);
  const [showNotes, setShowNotes] = useState(false);
  const [showStopConfirm, setShowStopConfirm] = useState(false);
  const [showSubmitConfirm, setShowSubmitConfirm] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  // CBT at-home rule: candidate must destroy (tear/cut) any scratch paper in view of the camera
  // before submission. Enforced in exam mode; optional acknowledgement in self-study mode.
  const [paperDestroyed, setPaperDestroyed] = useState(false);
  const paperRuleRequired = mode === 'exam';
  const [audioLevels, setAudioLevels] = useState<number[]>([10, 10, 10, 10, 10]);
  const [elapsedSeconds, setElapsedSeconds] = useState(0);

  // --- Refs ---
  const mediaRecorderRef = useRef<MediaRecorder | null>(null);
  const audioChunksRef = useRef<Blob[]>([]);
  const nativePulseRef = useRef<number | null>(null);
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

  // --- Timer ---
  useEffect(() => {
    recordingStateRef.current = recordingState;
  }, [recordingState]);

  const buildRecordingBlob = () =>
    new Blob(audioChunksRef.current, { type: mediaRecorderRef.current?.mimeType || 'audio/webm' });

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

  const stopRecorderAsync = async () => {
    if (isNativeRecorder) {
      const recording = await SpeakingRecorder.stop();
      return base64ToBlob(recording.base64, recording.mimeType);
    }

    const recorder = mediaRecorderRef.current;
    if (!recorder || recorder.state === 'inactive') {
      return buildRecordingBlob();
    }

    return new Promise<Blob>((resolve, reject) => {
      const handleStop = () => {
        recorder.removeEventListener('error', handleError);
        resolve(buildRecordingBlob());
      };

      const handleError = () => {
        recorder.removeEventListener('stop', handleStop);
        reject(new Error('Recording failed to stop cleanly.'));
      };

      recorder.addEventListener('stop', handleStop, { once: true });
      recorder.addEventListener('error', handleError, { once: true });
      recorder.stop();
    });
  };

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

    if (recordingState === 'paused' && (isNativeRecorder || mediaRecorderRef.current)) {
      if (isNativeRecorder) {
        await SpeakingRecorder.resume();
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
      if (isNativeRecorder) {
        await SpeakingRecorder.start({ mimeType: 'audio/mp4' });
        mediaRecorderRef.current = null;
        audioChunksRef.current = [];
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
    }
  };

  const handlePauseRecording = () => {
    if (isNativeRecorder) {
      void SpeakingRecorder.pause().catch(() => undefined);
      stopNativeVisualizerPulse();
      pauseDurationClock();
      setRecordingState('paused');
      return;
    }

    if (mediaRecorderRef.current && mediaRecorderRef.current.state === 'recording') {
      mediaRecorderRef.current.pause();
      pauseDurationClock();
      setRecordingState('paused');
    }
  };

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
    analytics.track('task_submitted', { taskId: id, subtest: 'speaking', mode, durationSeconds });

    try {
      const recording = await stopRecorderAsync();
      cleanupAudio(false);
      if (recording.size === 0) {
        throw new Error('No speaking audio was captured.');
      }

      const { submissionId } = await submitSpeakingRecording(id, recording, durationSeconds);
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

  if (cardLoading) {
    return (
      <AppShell pageTitle="Speaking Task" workspaceRole="learner" className="px-3 sm:px-4 lg:px-6">
        <div className="flex min-h-[420px] items-center justify-center rounded-3xl border border-border/80 bg-surface shadow-sm">
          <Loader2 className="w-8 h-8 text-primary animate-spin" />
        </div>
      </AppShell>
    );
  }

  return (
    <AppShell pageTitle={card?.title ?? 'Speaking Task'} workspaceRole="learner" className="px-3 sm:px-4 lg:px-6">
    <div className="mx-auto flex min-h-[calc(100vh-7rem)] w-full max-w-[1280px] flex-col overflow-hidden rounded-2xl border border-border/80 bg-surface text-navy shadow-sm">
      {/* Top Bar */}
      <header className="z-20 flex items-center justify-between gap-3 border-b border-border/80 bg-white/85 px-4 py-3 backdrop-blur-md sm:px-6 sm:py-4">
        <div className="flex items-center gap-3 min-w-0 overflow-hidden">
          <div className={`px-3 py-1 rounded-full text-[10px] font-black uppercase tracking-widest flex items-center gap-2 ${
            mode === 'self' ? 'bg-primary/10 text-primary border border-primary/30' :
            'bg-amber-50 text-warning border border-warning/30'
          }`}>
            {mode === 'self' ? <User className="w-3 h-3" /> : <ShieldCheck className="w-3 h-3" />}
            {mode === 'self' ? 'Self Practice' : 'Exam Simulation'}
          </div>
          <div className="hidden h-4 w-px bg-border sm:block" />
          <div className="hidden sm:flex items-center gap-2 text-muted">
            {connectionStatus === 'connected' ? <Wifi className="w-4 h-4 text-success" /> : 
             connectionStatus === 'connecting' ? <Loader2 className="w-4 h-4 animate-spin text-info" /> :
             <WifiOff className="w-4 h-4 text-danger" />}
            <span className="text-[10px] font-bold uppercase tracking-wider">
              {connectionStatus === 'connected' ? 'Live' : connectionStatus === 'connecting' ? 'Connecting' : 'Disconnected'}
            </span>
          </div>
        </div>

        <div className="flex items-center gap-6">
          <div className="flex flex-col items-end">
            <span className="text-[10px] font-black text-muted uppercase tracking-widest">Elapsed Time</span>
            <Timer mode="elapsed" running={recordingState === 'recording'} size="lg" />
          </div>
        </div>
      </header>

      {/* Main Content Area */}
      <main className="relative flex flex-1 flex-col items-center justify-center bg-background-light p-6">

        {/* Visualizer / AI State */}
        <div className="relative z-10 mb-24 flex flex-col items-center gap-12">
          <div className="relative">
            <motion.div 
              animate={recordingState === 'recording' ? { scale: [1, 1.05, 1] } : {}}
              transition={{ repeat: Infinity, duration: 2 }}
              className={`w-48 h-48 rounded-full border-2 flex items-center justify-center transition-all duration-500 ${
                recordingState === 'recording' ? 'border-danger/30 bg-danger/10 shadow-[0_0_40px_rgba(239,68,68,0.14)]' :
                recordingState === 'paused' ? 'border-warning/30 bg-amber-50 shadow-[0_0_40px_rgba(245,158,11,0.14)]' : 'border-border bg-surface'
              }`}
            >
              <div className={`w-40 h-40 rounded-full flex items-center justify-center transition-all ${
                recordingState === 'recording' ? 'bg-danger/10' :
                recordingState === 'paused' ? 'bg-warning/10' : 'bg-background-light'
              }`}>
                {recordingState === 'recording' ? (
                  <div className="flex items-center gap-1 h-10 items-end">
                    {audioLevels.map((level, i) => (
                      <motion.div
                        key={i}
                        animate={{ height: level }}
                        transition={{ type: 'tween', duration: 0.1 }}
                        className="w-1.5 bg-danger rounded-full"
                      />
                    ))}
                  </div>
                ) : recordingState === 'paused' ? (
                  <Pause className="w-12 h-12 text-warning" />
                ) : (
                  <Mic className="w-12 h-12 text-muted" />
                )}
              </div>
            </motion.div>
            
            {recordingState === 'recording' && (
              <motion.div 
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                className="absolute -bottom-8 left-1/2 -translate-x-1/2 flex items-center gap-2"
              >
                <div className="w-2 h-2 rounded-full bg-danger animate-pulse" />
                <span className="text-[10px] font-black uppercase tracking-[0.2em] text-danger">Recording</span>
              </motion.div>
            )}
            {recordingState === 'paused' && (
              <motion.div 
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                className="absolute -bottom-8 left-1/2 -translate-x-1/2 flex items-center gap-2"
              >
                <div className="w-2 h-2 rounded-full bg-warning" />
                <span className="text-[10px] font-black uppercase tracking-[0.2em] text-warning">Paused</span>
              </motion.div>
            )}
          </div>

          <div className="text-center max-w-md">
            <h2 className="text-xl font-bold mb-2 text-navy">
              {recordingState === 'idle' ? "Ready to record" :
               recordingState === 'paused' ? "Recording paused" :
               "Recording your response..."}
            </h2>
            <p className="text-sm text-muted leading-relaxed">
              Complete the tasks on your role card. Your recording will be saved for transcript review and speaking feedback.
            </p>
            <p className="mt-2 text-xs font-bold uppercase tracking-widest text-muted">Captured duration: {elapsedSeconds}s</p>
          </div>
          
          {/* Manual Recording Controls */}
          <div className="flex items-center gap-4 mt-4">
            {recordingState === 'idle' ? (
              <button
                onClick={handleStartRecording}
                className="w-16 h-16 rounded-full bg-danger hover:bg-danger/90 flex items-center justify-center transition-all shadow-lg shadow-danger/20"
                aria-label="Start recording"
              >
                <Mic className="w-6 h-6 text-white" />
              </button>
            ) : recordingState === 'paused' ? (
              <button
                onClick={handleStartRecording}
                className="w-16 h-16 rounded-full bg-danger hover:bg-danger/90 flex items-center justify-center transition-all shadow-lg shadow-danger/20"
                aria-label="Resume recording"
              >
                <Play className="w-6 h-6 text-white ml-1" />
              </button>
            ) : recordingState === 'recording' ? (
              <button
                onClick={handlePauseRecording}
                className="w-16 h-16 rounded-full border border-border bg-surface hover:bg-background-light flex items-center justify-center transition-all shadow-sm"
                aria-label="Pause recording"
              >
                <Pause className="w-6 h-6 text-navy" />
              </button>
            ) : null}
          </div>
        </div>

        {/* Role Card & Notes Toggles */}
        <div className="absolute bottom-20 sm:bottom-20 left-1/2 -translate-x-1/2 flex gap-3 z-20">
          <button 
            onClick={() => setShowRoleCard(!showRoleCard)}
            className={`px-5 py-3 min-h-[44px] rounded-full text-xs font-bold flex items-center gap-2 transition-all ${
              showRoleCard ? 'bg-primary text-white shadow-sm' : 'border border-border bg-surface text-navy shadow-sm hover:bg-background-light'
            }`}
          >
            <FileText className="w-4 h-4" /> Role Card {showRoleCard ? <ChevronDown className="w-3 h-3" /> : <ChevronUp className="w-3 h-3" />}
          </button>
          <button 
            onClick={() => setShowNotes(!showNotes)}
            className={`px-5 py-3 min-h-[44px] rounded-full text-xs font-bold flex items-center gap-2 transition-all ${
              showNotes ? 'bg-primary text-white shadow-sm' : 'border border-border bg-surface text-navy shadow-sm hover:bg-background-light'
            }`}
          >
            <Edit3 className="w-4 h-4" /> Notes {showNotes ? <ChevronDown className="w-3 h-3" /> : <ChevronUp className="w-3 h-3" />}
          </button>
        </div>

        {/* Overlays */}
        <AnimatePresence>
          {showRoleCard && (
            <motion.div 
              initial={{ opacity: 0, y: 100 }}
              animate={{ opacity: 1, y: 0 }}
              exit={{ opacity: 0, y: 100 }}
              className="absolute bottom-48 left-6 right-6 max-w-2xl mx-auto bg-surface border border-border rounded-3xl p-6 shadow-2xl z-30 max-h-[50vh] overflow-y-auto"
            >
              <div className="flex items-center justify-between mb-4">
                <h3 className="text-sm font-black uppercase tracking-widest text-muted">Role Card Reference</h3>
                <button onClick={() => setShowRoleCard(false)} className="p-2.5 -m-1 rounded-lg text-muted hover:text-navy hover:bg-background-light"><Square className="w-4 h-4 rotate-45" /></button>
              </div>
              <div className="space-y-4">
                <div>
                  <h4 className="text-lg font-bold text-navy mb-1">{card?.title}</h4>
                  <p className="text-xs text-muted uppercase font-bold tracking-wider">{card?.profession} - {card?.setting}</p>
                </div>
                <p className="text-sm text-navy/80 leading-relaxed">{card?.brief}</p>
                <ul className="space-y-2">
                  {card?.tasks.map((t, i) => (
                    <li key={i} className="text-sm text-muted flex gap-3">
                      <span className="text-primary font-bold">{i+1}.</span> {t}
                    </li>
                  ))}
                </ul>
              </div>
            </motion.div>
          )}

          {showNotes && (
            <motion.div 
              initial={{ opacity: 0, y: 100 }}
              animate={{ opacity: 1, y: 0 }}
              exit={{ opacity: 0, y: 100 }}
              className="absolute bottom-48 left-6 right-6 max-w-2xl mx-auto bg-surface border border-border rounded-3xl p-6 shadow-2xl z-30 h-[40vh] flex flex-col"
            >
              <div className="flex items-center justify-between mb-4">
                <h3 className="text-sm font-black uppercase tracking-widest text-muted">Your Notes</h3>
                <button onClick={() => setShowNotes(false)} className="p-2.5 -m-1 rounded-lg text-muted hover:text-navy hover:bg-background-light"><Square className="w-4 h-4 rotate-45" /></button>
              </div>
              <textarea 
                placeholder="Type your notes here..."
                className="flex-1 resize-none rounded-2xl border border-border bg-background-light p-4 text-sm leading-relaxed text-navy focus:outline-none focus:ring-2 focus:ring-primary/20"
              />
            </motion.div>
          )}
        </AnimatePresence>
      </main>

      {/* Bottom Controls */}
      <footer className="z-40 border-t border-border/80 bg-white/90 px-8 py-8 pb-[calc(2rem+env(safe-area-inset-bottom))] backdrop-blur-xl">
        <div className="max-w-4xl mx-auto flex items-center justify-between">
          <button 
            onClick={handleStop}
            ref={stopTriggerRef}
            className="flex flex-col items-center gap-2 group"
            aria-label="Cancel task"
          >
            <div className="w-14 h-14 rounded-full border border-border bg-surface flex items-center justify-center group-hover:bg-background-light transition-all">
              <RotateCcw className="w-6 h-6 text-muted group-hover:text-navy" />
            </div>
            <span className="text-[10px] font-black uppercase tracking-widest text-muted group-hover:text-navy">Cancel Task</span>
          </button>

          <div className="flex flex-col items-center gap-4">
            <Button
              size="lg"
              onClick={handleSubmit}
              disabled={recordingState === 'idle' || isSubmitting}
              className="px-12 py-4 rounded-2xl font-black text-lg"
              ref={submitTriggerRef}
            >
              {isSubmitting ? 'Submitting...' : 'Submit Recording'}
            </Button>
            {submitError && (
              <p role="alert" className="max-w-sm text-center text-xs font-bold leading-relaxed text-danger">
                {submitError}
              </p>
            )}
            <p className="text-[10px] text-muted font-bold uppercase tracking-[0.3em]">OET Speaking Simulation</p>
          </div>

          <div className="w-14 h-14" /> {/* Spacer for balance */}
        </div>
      </footer>

      {/* Modals */}
      <AnimatePresence>
        {showStopConfirm && (
          <div className="fixed inset-0 z-[100] flex items-center justify-center p-6">
            <motion.div 
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              className="absolute inset-0 bg-black/80 backdrop-blur-sm"
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
              className="relative bg-surface border border-border rounded-2xl p-8 max-w-md w-full shadow-2xl"
            >
              <div className="w-16 h-16 bg-danger/10 rounded-2xl flex items-center justify-center mb-6">
                <AlertCircle className="w-8 h-8 text-danger" />
              </div>
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
          <div className="fixed inset-0 z-[100] flex items-center justify-center p-6">
            <motion.div 
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              className="absolute inset-0 bg-black/80 backdrop-blur-sm"
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
              className="relative bg-surface border border-border rounded-2xl p-8 max-w-md w-full shadow-2xl"
            >
              <div className="w-16 h-16 bg-success/10 rounded-2xl flex items-center justify-center mb-6">
                <CheckCircle2 className="w-8 h-8 text-success" />
              </div>
              <h3 id="speaking-submit-dialog-title" className="text-2xl font-black mb-2">Finish Task?</h3>
              <p className="text-muted text-sm leading-relaxed mb-6">
                Are you ready to submit your recording for evaluation? You won&apos;t be able to make changes after this.
              </p>

              <div className="mb-6 rounded-2xl border border-warning/30 bg-amber-50 p-4">
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
      <AppShell pageTitle="Speaking Task" workspaceRole="learner" className="px-3 sm:px-4 lg:px-6">
        <div className="flex min-h-[420px] items-center justify-center rounded-3xl border border-border/80 bg-surface shadow-sm">
          <Loader2 className="w-8 h-8 text-primary animate-spin" />
        </div>
      </AppShell>
    }>
      <LiveSpeakingTaskContent />
    </Suspense>
  );
}
