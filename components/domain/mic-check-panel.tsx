'use client';

import { useEffect, useRef, useState } from 'react';
import { cn } from '@/lib/utils';
import { Mic, Volume2, CheckCircle2, AlertCircle, Loader2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { motion, AnimatePresence } from 'motion/react';

declare global {
  interface Window {
    webkitAudioContext?: typeof AudioContext;
  }
}

type CheckStep = 'permission' | 'record' | 'playback' | 'noise';
type StepStatus = 'pending' | 'active' | 'checking' | 'passed' | 'failed';

interface MicCheckPanelProps {
  onComplete?: () => void;
  className?: string;
}

const RECORDING_TEST_SECONDS = 3;
const NOISE_CHECK_SECONDS = 2;
const NOISE_WARNING_THRESHOLD = 42;

const stepOrder: CheckStep[] = ['permission', 'record', 'playback', 'noise'];

const stepLabels: Record<CheckStep, { label: string; icon: typeof Mic; action: string }> = {
  permission: { label: 'Microphone Permission', icon: Mic, action: 'Allow Access' },
  record: { label: 'Recording Test', icon: Mic, action: 'Record 3 seconds' },
  playback: { label: 'Playback Verification', icon: Volume2, action: 'Play Back' },
  noise: { label: 'Background Noise Check', icon: Volume2, action: 'Check Noise' },
};

function supportedMimeType(): string | undefined {
  if (typeof MediaRecorder === 'undefined' || typeof MediaRecorder.isTypeSupported !== 'function') {
    return undefined;
  }

  return [
    'audio/webm;codecs=opus',
    'audio/webm',
    'audio/mp4',
  ].find((type) => MediaRecorder.isTypeSupported(type));
}

function stopStream(stream: MediaStream | null) {
  stream?.getTracks().forEach((track) => track.stop());
}

export function MicCheckPanel({ onComplete, className }: MicCheckPanelProps) {
  const [steps, setSteps] = useState<Record<CheckStep, StepStatus>>({
    permission: 'active',
    record: 'pending',
    playback: 'pending',
    noise: 'pending',
  });
  const [error, setError] = useState<string>();
  const [recordCountdown, setRecordCountdown] = useState(0);
  const [noiseLevel, setNoiseLevel] = useState<number | null>(null);
  const [audioUrl, setAudioUrl] = useState<string | null>(null);

  const streamRef = useRef<MediaStream | null>(null);
  const recorderRef = useRef<MediaRecorder | null>(null);
  const chunksRef = useRef<Blob[]>([]);
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const audioContextRef = useRef<AudioContext | null>(null);

  useEffect(() => () => {
    stopStream(streamRef.current);
    if (audioContextRef.current?.state !== 'closed') {
      void audioContextRef.current?.close().catch(() => undefined);
    }
    if (audioUrl) URL.revokeObjectURL(audioUrl);
  }, [audioUrl]);

  const setStepStatus = (step: CheckStep, status: StepStatus) => {
    setSteps((prev) => ({ ...prev, [step]: status }));
  };

  const passStep = (current: CheckStep, next: CheckStep | null) => {
    setSteps((prev) => ({
      ...prev,
      [current]: 'passed',
      ...(next ? { [next]: 'active' } : {}),
    }));
    if (!next) onComplete?.();
  };

  const failStep = (step: CheckStep, message: string) => {
    setError(message);
    setStepStatus(step, 'failed');
  };

  const ensureBrowserSupport = () => {
    if (!navigator.mediaDevices?.getUserMedia || typeof MediaRecorder === 'undefined') {
      throw new Error('Your browser does not support microphone recording. Please use the latest Chrome, Edge, or Safari.');
    }
  };

  const requestPermission = async () => {
    setError(undefined);
    setStepStatus('permission', 'checking');
    try {
      ensureBrowserSupport();
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true },
      });
      stopStream(streamRef.current);
      streamRef.current = stream;
      passStep('permission', 'record');
    } catch (err) {
      failStep('permission', err instanceof Error ? err.message : 'Microphone access was blocked. Please allow microphone access and try again.');
    }
  };

  const recordSample = async () => {
    setError(undefined);
    setStepStatus('record', 'checking');
    setRecordCountdown(RECORDING_TEST_SECONDS);
    chunksRef.current = [];
    if (audioUrl) {
      URL.revokeObjectURL(audioUrl);
      setAudioUrl(null);
    }

    try {
      ensureBrowserSupport();
      const stream = streamRef.current ?? await navigator.mediaDevices.getUserMedia({
        audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true },
      });
      streamRef.current = stream;

      const mimeType = supportedMimeType();
      const recorder = mimeType ? new MediaRecorder(stream, { mimeType }) : new MediaRecorder(stream);
      recorderRef.current = recorder;

      recorder.ondataavailable = (event) => {
        if (event.data.size > 0) chunksRef.current.push(event.data);
      };

      const stopped = new Promise<void>((resolve) => {
        recorder.onstop = () => resolve();
      });

      recorder.start(250);
      const countdown = window.setInterval(() => {
        setRecordCountdown((value) => Math.max(0, value - 1));
      }, 1000);

      await new Promise((resolve) => window.setTimeout(resolve, RECORDING_TEST_SECONDS * 1000));
      window.clearInterval(countdown);
      if (recorder.state !== 'inactive') recorder.stop();
      await stopped;

      const blob = new Blob(chunksRef.current, { type: recorder.mimeType || 'audio/webm' });
      if (blob.size === 0) {
        throw new Error('No audio was captured. Please check that your microphone is not muted.');
      }

      setAudioUrl(URL.createObjectURL(blob));
      setRecordCountdown(0);
      passStep('record', 'playback');
    } catch (err) {
      setRecordCountdown(0);
      failStep('record', err instanceof Error ? err.message : 'Recording test failed. Please try again.');
    }
  };

  const playBackSample = async () => {
    setError(undefined);
    if (!audioUrl || !audioRef.current) {
      failStep('playback', 'No test recording is available. Please record again.');
      return;
    }

    setStepStatus('playback', 'checking');
    try {
      audioRef.current.currentTime = 0;
      await audioRef.current.play();
    } catch {
      setStepStatus('playback', 'active');
      setError('Playback was blocked by the browser. Press Play Back again, or use the audio controls.');
    }
  };

  const checkNoise = async () => {
    setError(undefined);
    setStepStatus('noise', 'checking');

    try {
      ensureBrowserSupport();
      const stream = streamRef.current ?? await navigator.mediaDevices.getUserMedia({ audio: true });
      streamRef.current = stream;
      const AudioContextCtor = window.AudioContext || window.webkitAudioContext;
      const context = audioContextRef.current ?? new AudioContextCtor();
      audioContextRef.current = context;

      if (context.state === 'suspended') await context.resume();
      const analyser = context.createAnalyser();
      analyser.fftSize = 512;
      context.createMediaStreamSource(stream).connect(analyser);

      const samples = new Uint8Array(analyser.frequencyBinCount);
      const levels: number[] = [];
      const startedAt = performance.now();

      await new Promise<void>((resolve) => {
        const sample = () => {
          analyser.getByteFrequencyData(samples);
          const average = samples.reduce((sum, value) => sum + value, 0) / samples.length;
          levels.push(average);
          setNoiseLevel(Math.round(average));
          if (performance.now() - startedAt >= NOISE_CHECK_SECONDS * 1000) {
            resolve();
            return;
          }
          requestAnimationFrame(sample);
        };
        sample();
      });

      const averageNoise = levels.reduce((sum, value) => sum + value, 0) / Math.max(1, levels.length);
      setNoiseLevel(Math.round(averageNoise));
      if (averageNoise > NOISE_WARNING_THRESHOLD) {
        failStep('noise', 'Background noise is high. Move to a quieter place or reduce fan/TV/traffic noise, then retry.');
        return;
      }

      passStep('noise', null);
    } catch (err) {
      failStep('noise', err instanceof Error ? err.message : 'Noise check failed. Please try again.');
    }
  };

  const handleActiveStep = (step: CheckStep) => {
    if (step === 'permission') void requestPermission();
    if (step === 'record') void recordSample();
    if (step === 'playback') void playBackSample();
    if (step === 'noise') void checkNoise();
  };

  return (
    <motion.div 
      initial={{ opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0 }}
      className={cn('flex flex-col gap-4', className)}
    >
      <div className="bg-primary/5 border border-primary/20 rounded-xl p-4 text-sm text-navy shadow-sm">
        <p className="flex items-start gap-2">
          <Mic className="w-5 h-5 text-primary shrink-0" />
          <span>
            We need to check your microphone and environment before you start the speaking task. This ensures your recording will be clear.
          </span>
        </p>
      </div>

      {audioUrl && (
        <audio
          ref={audioRef}
          src={audioUrl}
          className="sr-only"
          onEnded={() => passStep('playback', 'noise')}
          onPause={() => {
            if (steps.playback === 'checking' && audioRef.current?.ended) {
              passStep('playback', 'noise');
            }
          }}
        />
      )}

      <div className="flex flex-col gap-3">
        <AnimatePresence mode="popLayout">
          {stepOrder.map((step, index) => {
            const status = steps[step];
            const config = stepLabels[step];
            const Icon = config.icon;
            const isBusy = status === 'checking';

            return (
              <motion.div
                layout
                key={step}
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: index * 0.1 }}
                className={cn(
                  'flex items-center gap-4 p-4 rounded-xl border transition-all duration-300 relative overflow-hidden',
                  status === 'active' && 'border-primary/40 bg-gradient-to-r from-primary/10 to-transparent shadow-sm',
                  status === 'checking' && 'border-primary bg-primary/5 shadow-md',
                  status === 'passed' && 'border-emerald-200 bg-emerald-50/50',
                  status === 'failed' && 'border-red-200 bg-red-50/50',
                  status === 'pending' && 'border-border bg-background-light opacity-50'
                )}
              >
                {/* Active Indicator Bar */}
                {(status === 'active' || status === 'checking') && (
                  <motion.div layoutId="activeStep" className="absolute left-0 top-0 bottom-0 w-1 bg-primary" />
                )}

                <div className={cn(
                  'w-10 h-10 rounded-full flex items-center justify-center shrink-0 shadow-sm transition-transform duration-300',
                  status === 'passed' && 'bg-emerald-100 text-emerald-600',
                  status === 'failed' && 'bg-red-100 text-red-600',
                  (status === 'active' || status === 'checking') && 'bg-primary text-white scale-110',
                  status === 'pending' && 'bg-muted/20 text-muted'
                )}>
                  {status === 'passed' ? <CheckCircle2 className="w-5 h-5" /> :
                   status === 'failed' ? <AlertCircle className="w-5 h-5" /> :
                   isBusy ? <Loader2 className="w-5 h-5 animate-spin" /> :
                   <Icon className="w-5 h-5" />}
                </div>
                
                <div className="flex-1">
                  <p className={cn("text-sm font-semibold transition-colors", 
                    (status === 'active' || status === 'checking') ? "text-primary" : "text-navy"
                  )}>{config.label}</p>
                  
                  {status === 'passed' && <p className="text-xs text-emerald-600 font-medium tracking-wide">Passed</p>}
                  
                  {status === 'checking' && step === 'record' && (
                    <div className="flex items-center gap-2 mt-1">
                      <span className="relative flex h-2 w-2">
                        <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-red-400 opacity-75"></span>
                        <span className="relative inline-flex rounded-full h-2 w-2 bg-red-500"></span>
                      </span>
                      <p className="text-xs font-semibold text-red-600">Recording... {recordCountdown}s</p>
                    </div>
                  )}
                  
                  {status === 'checking' && step === 'playback' && <p className="text-xs text-primary font-medium">Playing your test recording…</p>}
                  
                  {status === 'checking' && step === 'noise' && (
                    <div className="flex items-center gap-2 mt-1">
                      <div className="flex gap-1 h-3 items-end">
                        {[...Array(5)].map((_, i) => (
                          <motion.div 
                            key={i}
                            animate={{ height: ['20%', '100%', '20%'] }}
                            transition={{ repeat: Infinity, duration: 0.5 + Math.random() * 0.5 }}
                            className={cn('w-1 rounded-full', (noiseLevel ?? 0) > NOISE_WARNING_THRESHOLD ? 'bg-red-500' : 'bg-primary/60')}
                          />
                        ))}
                      </div>
                      <p className="text-xs text-primary font-medium">Listening to room noise… <span className="opacity-70">({noiseLevel ?? 0} dB)</span></p>
                    </div>
                  )}
                  
                  {status === 'checking' && step === 'permission' && <p className="text-xs text-primary font-medium">Waiting for browser permission…</p>}
                  {status === 'failed' && <p className="text-xs text-red-600 font-medium">Failed — please try again</p>}
                </div>
                
                {(status === 'active' || status === 'failed') && (
                  <Button size="sm" variant={status === 'failed' ? 'outline' : 'primary'} onClick={() => handleActiveStep(step)} className="shadow-sm">
                    {status === 'failed' ? 'Retry' : config.action}
                  </Button>
                )}
              </motion.div>
            );
          })}
        </AnimatePresence>
      </div>

      <AnimatePresence>
        {error && (
          <motion.div initial={{ opacity: 0, height: 0 }} animate={{ opacity: 1, height: 'auto' }} exit={{ opacity: 0, height: 0 }}>
            <InlineAlert variant="error" dismissible className="shadow-sm rounded-xl mt-2 border-red-200">
              {error}
            </InlineAlert>
          </motion.div>
        )}
      </AnimatePresence>
    </motion.div>
  );
}
