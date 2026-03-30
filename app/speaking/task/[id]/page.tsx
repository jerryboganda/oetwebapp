'use client';

import { useState, useEffect, useRef, Suspense, useCallback } from 'react';
import { useParams, useRouter, useSearchParams } from 'next/navigation';
import { motion, AnimatePresence } from 'motion/react';
import {
  Mic, Square, RotateCcw, CheckCircle2, AlertCircle,
  FileText, Edit3, ChevronUp, ChevronDown,
  Wifi, WifiOff, User, ShieldCheck, Loader2, Play, Pause,
} from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Modal } from '@/components/ui/modal';
import { Timer } from '@/components/ui/timer';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchRoleCard, submitSpeakingRecording } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { RoleCard } from '@/lib/mock-data';

// --- Types ---
type TaskMode = 'ai' | 'self' | 'exam';
type ConnectionStatus = 'connecting' | 'connected' | 'disconnected' | 'error';
type RecordingState = 'idle' | 'recording' | 'paused' | 'finished';

function LiveSpeakingTaskContent() {
  const params = useParams();
  const router = useRouter();
  const searchParams = useSearchParams();
  const id = params?.id as string;
  const requestedMode = (searchParams?.get('mode') as TaskMode) || 'self';
  const aiUnavailable = requestedMode === 'ai';
  const mode: Exclude<TaskMode, 'ai'> = requestedMode === 'ai' ? 'self' : requestedMode;

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
  const [audioLevels, setAudioLevels] = useState<number[]>([10, 10, 10, 10, 10]);

  // --- Refs ---
  const mediaRecorderRef = useRef<MediaRecorder | null>(null);
  const audioChunksRef = useRef<Blob[]>([]);

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

  // --- Timer ---
  useEffect(() => {
    recordingStateRef.current = recordingState;
  }, [recordingState]);

  const buildRecordingBlob = () =>
    new Blob(audioChunksRef.current, { type: mediaRecorderRef.current?.mimeType || 'audio/webm' });

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

  const cleanupAudio = (stopRecorder = true) => {
    if (animationFrameRef.current) {
      cancelAnimationFrame(animationFrameRef.current);
    }
    if (audioContextRef.current && audioContextRef.current.state !== 'closed') {
      audioContextRef.current.close().catch(console.error);
    }
    if (stopRecorder && mediaRecorderRef.current && mediaRecorderRef.current.state !== 'inactive') {
      mediaRecorderRef.current.stop();
    }
    if (streamRef.current) {
      streamRef.current.getTracks().forEach(track => track.stop());
    }
  };

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
      cleanupAudio();
    };
  }, []);

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
    if (recordingState === 'paused' && mediaRecorderRef.current) {
      mediaRecorderRef.current.resume();
      setRecordingState('recording');
      return;
    }

    try {
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
      setRecordingState('recording');

      const audioContext = new AudioContext();
      audioContextRef.current = audioContext;
      setupVisualizer(stream, audioContext);

    } catch (err) {
      console.error('Local recording failed:', err);
    }
  };

  const handlePauseRecording = () => {
    if (mediaRecorderRef.current && mediaRecorderRef.current.state === 'recording') {
      mediaRecorderRef.current.pause();
      setRecordingState('paused');
    }
  };

  // --- Handlers ---
  const handleStop = () => setShowStopConfirm(true);
  const handleSubmit = () => setShowSubmitConfirm(true);

  const confirmStop = () => {
    setRecordingState('finished');
    cleanupAudio();
    router.push('/speaking/selection');
  };

  const confirmSubmit = async () => {
    setRecordingState('finished');
    analytics.track('task_submitted', { taskId: id, subtest: 'speaking', mode });
    try {
      const recording = await stopRecorderAsync();
      cleanupAudio(false);
      if (recording.size === 0) {
        throw new Error('No speaking audio was captured.');
      }

      const { submissionId } = await submitSpeakingRecording(id, recording);
      router.push(`/speaking/results/${submissionId}`);
    } catch {
      cleanupAudio(false);
      router.push(`/speaking/results/${id}`);
    }
  };

  if (cardLoading) {
    return (
      <div className="min-h-screen bg-[#0a0a0a] flex items-center justify-center">
        <Loader2 className="w-8 h-8 text-primary animate-spin" />
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-[#0a0a0a] text-white flex flex-col font-sans overflow-hidden">
      {/* Top Bar */}
      <header className="bg-black/40 backdrop-blur-md border-b border-white/10 px-6 py-4 flex items-center justify-between z-20">
        <div className="flex items-center gap-4">
          <div className={`px-3 py-1 rounded-full text-[10px] font-black uppercase tracking-widest flex items-center gap-2 ${
            mode === 'self' ? 'bg-purple-500/20 text-purple-400 border border-purple-500/30' :
            'bg-amber-500/20 text-amber-400 border border-amber-500/30'
          }`}>
            {mode === 'self' ? <User className="w-3 h-3" /> : <ShieldCheck className="w-3 h-3" />}
            {aiUnavailable ? 'Guided Self Practice' : mode === 'self' ? 'Self Practice' : 'Exam Simulation'}
          </div>
          <div className="h-4 w-px bg-white/10" />
          <div className="flex items-center gap-2 text-white/60">
            {connectionStatus === 'connected' ? <Wifi className="w-4 h-4 text-green-500" /> : 
             connectionStatus === 'connecting' ? <Loader2 className="w-4 h-4 animate-spin text-blue-500" /> :
             <WifiOff className="w-4 h-4 text-red-500" />}
            <span className="text-[10px] font-bold uppercase tracking-wider">
              {connectionStatus === 'connected' ? 'Live' : connectionStatus === 'connecting' ? 'Connecting' : 'Disconnected'}
            </span>
          </div>
        </div>

        <div className="flex items-center gap-6">
          <div className="flex flex-col items-end">
            <span className="text-[10px] font-black text-white/40 uppercase tracking-widest">Elapsed Time</span>
            <Timer mode="elapsed" running={recordingState === 'recording'} size="lg" />
          </div>
        </div>
      </header>

      {/* Main Content Area */}
      <main className="flex-1 relative flex flex-col items-center justify-center p-6">
        
        {/* Background Atmosphere */}
        <div className="absolute inset-0 overflow-hidden pointer-events-none">
          <div className={`absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[600px] h-[600px] rounded-full blur-[120px] transition-all duration-1000 ${
            recordingState === 'recording' ? 'bg-red-500/10' : 
            recordingState === 'paused' ? 'bg-amber-500/10' : 'bg-gray-500/5'
          }`} />
        </div>

        {/* Visualizer / AI State */}
        <div className="relative z-10 flex flex-col items-center gap-12">
          <div className="relative">
            <motion.div 
              animate={recordingState === 'recording' ? { scale: [1, 1.05, 1] } : {}}
              transition={{ repeat: Infinity, duration: 2 }}
              className={`w-48 h-48 rounded-full border-2 flex items-center justify-center transition-all duration-500 ${
                recordingState === 'recording' ? 'border-red-500 shadow-[0_0_40px_rgba(239,68,68,0.2)]' : 
                recordingState === 'paused' ? 'border-amber-500 shadow-[0_0_40px_rgba(245,158,11,0.2)]' : 'border-white/10'
              }`}
            >
              <div className={`w-40 h-40 rounded-full flex items-center justify-center transition-all ${
                recordingState === 'recording' ? 'bg-red-500/10' : 
                recordingState === 'paused' ? 'bg-amber-500/10' : 'bg-white/5'
              }`}>
                {recordingState === 'recording' ? (
                  <div className="flex items-center gap-1 h-10 items-end">
                    {audioLevels.map((level, i) => (
                      <motion.div
                        key={i}
                        animate={{ height: level }}
                        transition={{ type: 'tween', duration: 0.1 }}
                        className="w-1.5 bg-red-500 rounded-full"
                      />
                    ))}
                  </div>
                ) : recordingState === 'paused' ? (
                  <Pause className="w-12 h-12 text-amber-500" />
                ) : (
                  <Mic className="w-12 h-12 text-white/20" />
                )}
              </div>
            </motion.div>
            
            {recordingState === 'recording' && (
              <motion.div 
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                className="absolute -bottom-8 left-1/2 -translate-x-1/2 flex items-center gap-2"
              >
                <div className="w-2 h-2 rounded-full bg-red-500 animate-pulse" />
                <span className="text-[10px] font-black uppercase tracking-[0.2em] text-red-500">Recording</span>
              </motion.div>
            )}
            {recordingState === 'paused' && (
              <motion.div 
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                className="absolute -bottom-8 left-1/2 -translate-x-1/2 flex items-center gap-2"
              >
                <div className="w-2 h-2 rounded-full bg-amber-500" />
                <span className="text-[10px] font-black uppercase tracking-[0.2em] text-amber-500">Paused</span>
              </motion.div>
            )}
          </div>

          <div className="text-center max-w-md">
            <h2 className="text-xl font-bold mb-2">
              {recordingState === 'idle' ? "Ready to record" :
               recordingState === 'paused' ? "Recording paused" :
               "Recording your response..."}
            </h2>
            <p className="text-sm text-white/40 leading-relaxed">
              {aiUnavailable
                ? 'Live AI patient mode is not available in this build yet, so this session is running as guided self-practice with recording and transcript review.'
                : 'Complete the tasks on your role card. Your recording will be saved for review.'}
            </p>
          </div>
          
          {/* Manual Recording Controls */}
          <div className="flex items-center gap-4 mt-4">
            {recordingState === 'idle' ? (
              <button
                onClick={handleStartRecording}
                className="w-16 h-16 rounded-full bg-red-500 hover:bg-red-600 flex items-center justify-center transition-all shadow-lg shadow-red-500/20"
                aria-label="Start recording"
              >
                <Mic className="w-6 h-6 text-white" />
              </button>
            ) : recordingState === 'paused' ? (
              <button
                onClick={handleStartRecording}
                className="w-16 h-16 rounded-full bg-red-500 hover:bg-red-600 flex items-center justify-center transition-all shadow-lg shadow-red-500/20"
                aria-label="Resume recording"
              >
                <Play className="w-6 h-6 text-white ml-1" />
              </button>
            ) : recordingState === 'recording' ? (
              <button
                onClick={handlePauseRecording}
                className="w-16 h-16 rounded-full bg-white/10 hover:bg-white/20 flex items-center justify-center transition-all"
                aria-label="Pause recording"
              >
                <Pause className="w-6 h-6 text-white" />
              </button>
            ) : null}
          </div>
        </div>

        {/* Role Card & Notes Toggles */}
        <div className="absolute bottom-32 left-1/2 -translate-x-1/2 flex gap-4 z-20">
          <button 
            onClick={() => setShowRoleCard(!showRoleCard)}
            className={`px-6 py-2 rounded-full text-xs font-bold flex items-center gap-2 transition-all ${
              showRoleCard ? 'bg-white text-black' : 'bg-white/10 text-white hover:bg-white/20'
            }`}
          >
            <FileText className="w-4 h-4" /> Role Card {showRoleCard ? <ChevronDown className="w-3 h-3" /> : <ChevronUp className="w-3 h-3" />}
          </button>
          <button 
            onClick={() => setShowNotes(!showNotes)}
            className={`px-6 py-2 rounded-full text-xs font-bold flex items-center gap-2 transition-all ${
              showNotes ? 'bg-white text-black' : 'bg-white/10 text-white hover:bg-white/20'
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
              className="absolute bottom-48 left-6 right-6 max-w-2xl mx-auto bg-zinc-900 border border-white/10 rounded-3xl p-6 shadow-2xl z-30 max-h-[50vh] overflow-y-auto"
            >
              <div className="flex items-center justify-between mb-4">
                <h3 className="text-sm font-black uppercase tracking-widest text-white/40">Role Card Reference</h3>
                <button onClick={() => setShowRoleCard(false)} className="text-white/40 hover:text-white"><Square className="w-4 h-4 rotate-45" /></button>
              </div>
              <div className="space-y-4">
                <div>
                  <h4 className="text-lg font-bold text-white mb-1">{card?.title}</h4>
                  <p className="text-xs text-white/60 uppercase font-bold tracking-wider">{card?.profession} • {card?.setting}</p>
                </div>
                <p className="text-sm text-white/80 leading-relaxed">{card?.brief}</p>
                <ul className="space-y-2">
                  {card?.tasks.map((t, i) => (
                    <li key={i} className="text-sm text-white/60 flex gap-3">
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
              className="absolute bottom-48 left-6 right-6 max-w-2xl mx-auto bg-zinc-900 border border-white/10 rounded-3xl p-6 shadow-2xl z-30 h-[40vh] flex flex-col"
            >
              <div className="flex items-center justify-between mb-4">
                <h3 className="text-sm font-black uppercase tracking-widest text-white/40">Your Notes</h3>
                <button onClick={() => setShowNotes(false)} className="text-white/40 hover:text-white"><Square className="w-4 h-4 rotate-45" /></button>
              </div>
              <textarea 
                placeholder="Type your notes here..."
                className="flex-1 bg-transparent border-none resize-none focus:outline-none text-sm text-white/80 leading-relaxed"
              />
            </motion.div>
          )}
        </AnimatePresence>
      </main>

      {/* Bottom Controls */}
      <footer className="bg-black/60 backdrop-blur-xl border-t border-white/10 px-8 py-8 z-40">
        <div className="max-w-4xl mx-auto flex items-center justify-between">
          <button 
            onClick={handleStop}
            ref={stopTriggerRef}
            className="flex flex-col items-center gap-2 group"
            aria-label="Cancel task"
          >
            <div className="w-14 h-14 rounded-full border border-white/10 flex items-center justify-center group-hover:bg-white/5 transition-all">
              <RotateCcw className="w-6 h-6 text-white/40 group-hover:text-white" />
            </div>
            <span className="text-[10px] font-black uppercase tracking-widest text-white/40 group-hover:text-white">Cancel Task</span>
          </button>

          <div className="flex flex-col items-center gap-4">
            <Button
              size="lg"
              onClick={handleSubmit}
              disabled={recordingState === 'idle'}
              className="px-12 py-4 bg-white text-black hover:bg-gray-200 rounded-2xl font-black text-lg shadow-[0_0_30px_rgba(255,255,255,0.1)]"
              ref={submitTriggerRef}
            >
              Submit Recording
            </Button>
            <p className="text-[10px] text-white/20 font-bold uppercase tracking-[0.3em]">OET Speaking Simulation</p>
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
              className="relative bg-zinc-900 border border-white/10 rounded-[32px] p-8 max-w-md w-full shadow-2xl"
            >
              <div className="w-16 h-16 bg-red-500/10 rounded-2xl flex items-center justify-center mb-6">
                <AlertCircle className="w-8 h-8 text-red-500" />
              </div>
              <h3 id="speaking-stop-dialog-title" className="text-2xl font-black mb-2">Stop Practice?</h3>
              <p className="text-white/60 text-sm leading-relaxed mb-8">
                Your current recording will be discarded. You will need to start the task again from the beginning.
              </p>
              <div className="flex flex-col gap-3">
                <Button ref={stopPrimaryActionRef} variant="destructive" fullWidth onClick={confirmStop} className="py-4 rounded-2xl font-black">
                  Yes, Discard and Exit
                </Button>
                <Button variant="ghost" fullWidth onClick={() => setShowStopConfirm(false)} className="py-4 rounded-2xl text-white bg-white/5 hover:bg-white/10">
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
              className="relative bg-zinc-900 border border-white/10 rounded-[32px] p-8 max-w-md w-full shadow-2xl"
            >
              <div className="w-16 h-16 bg-green-500/10 rounded-2xl flex items-center justify-center mb-6">
                <CheckCircle2 className="w-8 h-8 text-green-500" />
              </div>
              <h3 id="speaking-submit-dialog-title" className="text-2xl font-black mb-2">Finish Task?</h3>
              <p className="text-white/60 text-sm leading-relaxed mb-8">
                Are you ready to submit your recording for evaluation? You won&apos;t be able to make changes after this.
              </p>
              <div className="flex flex-col gap-3">
                <Button ref={submitPrimaryActionRef} fullWidth onClick={confirmSubmit} className="py-4 rounded-2xl font-black">
                  Submit for Evaluation
                </Button>
                <Button variant="ghost" fullWidth onClick={() => setShowSubmitConfirm(false)} className="py-4 rounded-2xl text-white bg-white/5 hover:bg-white/10">
                  Not Yet, Keep Going
                </Button>
              </div>
            </motion.div>
          </div>
        )}
      </AnimatePresence>
    </div>
  );
}

export default function LiveSpeakingTask() {
  return (
    <Suspense fallback={
      <div className="min-h-screen bg-[#0a0a0a] flex items-center justify-center">
        <Loader2 className="w-8 h-8 text-primary animate-spin" />
      </div>
    }>
      <LiveSpeakingTaskContent />
    </Suspense>
  );
}
