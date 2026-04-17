'use client';

import { useState, useEffect, useRef, Suspense } from 'react';
import { Capacitor } from '@capacitor/core';
import { motion, AnimatePresence } from 'motion/react';
import {
  Mic, Volume2, Play, Square, AlertTriangle, CheckCircle2,
  ChevronRight, Info, RefreshCw, BarChart3,
} from 'lucide-react';
import { useSearchParams, useRouter } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { InlineAlert } from '@/components/ui/alert';
import { analytics } from '@/lib/analytics';
import { postSpeakingDeviceCheck } from '@/lib/api';
import { SpeakingRecorder, base64ToBlob } from '@/lib/mobile/speaking-recorder';

declare global {
  interface Window {
    webkitAudioContext?: typeof AudioContext;
  }
}

type CheckStatus = 'pending' | 'checking' | 'success' | 'warning' | 'error';

function MicEnvironmentCheckContent() {
  const searchParams = useSearchParams();
  const router = useRouter();
  const taskId = searchParams?.get('taskId') || 'st-001';
  const isNativePlatform = Capacitor.isNativePlatform();

  // --- State ---
  const [permissionStatus, setPermissionStatus] = useState<CheckStatus>('pending');
  const [recordingStatus, setRecordingStatus] = useState<'idle' | 'recording' | 'finished'>('idle');
  const [audioUrl, setAudioUrl] = useState<string | null>(null);
  const [isPlaying, setIsPlaying] = useState(false);
  const [noiseLevel, setNoiseLevel] = useState(0);
  const [isNoisy, setIsNoisy] = useState(false);
  const [isCompatible, setIsCompatible] = useState<boolean | null>(null);
  
  const mediaRecorderRef = useRef<MediaRecorder | null>(null);
  const audioChunksRef = useRef<Blob[]>([]);
  const streamRef = useRef<MediaStream | null>(null);
  const audioContextRef = useRef<AudioContext | null>(null);
  const analyserRef = useRef<AnalyserNode | null>(null);
  const animationFrameRef = useRef<number | null>(null);
  const audioPlayerRef = useRef<HTMLAudioElement | null>(null);
  const autoStopTimeoutRef = useRef<number | null>(null);
  const isRecordingRef = useRef(false);
  const recordedBlobRef = useRef<Blob | null>(null);

  // --- Initial Compatibility Check ---
  useEffect(() => {
    const checkCompatibility = () => {
      if (isNativePlatform) {
        setIsCompatible(true);
        return;
      }

      const hasMediaRecorder = typeof window !== 'undefined' && !!window.MediaRecorder;
      const hasAudioContext = typeof window !== 'undefined' && (!!window.AudioContext || !!window.webkitAudioContext);
      setIsCompatible(hasMediaRecorder && hasAudioContext);
    };
    checkCompatibility();
  }, [isNativePlatform]);

  // --- Noise Monitoring ---
  const startNoiseMonitoring = (stream: MediaStream) => {
    if (!audioContextRef.current) {
      audioContextRef.current = new (window.AudioContext || window.webkitAudioContext!)();
    }
    
    const source = audioContextRef.current.createMediaStreamSource(stream);
    analyserRef.current = audioContextRef.current.createAnalyser();
    analyserRef.current.fftSize = 256;
    source.connect(analyserRef.current);

    const bufferLength = analyserRef.current.frequencyBinCount;
    const dataArray = new Uint8Array(bufferLength);

    const checkNoise = () => {
      if (!analyserRef.current) return;
      analyserRef.current.getByteFrequencyData(dataArray);
      
      let sum = 0;
      for (let i = 0; i < bufferLength; i++) {
        sum += dataArray[i];
      }
      const average = sum / bufferLength;
      setNoiseLevel(average);
      
      // Threshold for "noisy" environment (arbitrary value for demo)
      if (average > 40) {
        setIsNoisy(true);
      } else {
        setIsNoisy(false);
      }
      
      animationFrameRef.current = requestAnimationFrame(checkNoise);
    };

    checkNoise();
  };

  // --- Handlers ---
  const requestPermission = async () => {
    setPermissionStatus('checking');
    try {
      if (isNativePlatform) {
        await SpeakingRecorder.start({ mimeType: 'audio/mp4' });
        await SpeakingRecorder.cancel();
        setPermissionStatus('success');
        setIsCompatible(true);
        setNoiseLevel(0);
        setIsNoisy(false);
        analytics.track('task_started', { subtest: 'speaking', phase: 'mic_permission_granted' });
        void postSpeakingDeviceCheck({ microphoneGranted: true, networkStable: navigator.onLine, deviceType: 'native' });
        return;
      }

      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
  streamRef.current = stream;
      setPermissionStatus('success');
      analytics.track('task_started', { subtest: 'speaking', phase: 'mic_permission_granted' });
      startNoiseMonitoring(stream);
      void postSpeakingDeviceCheck({ microphoneGranted: true, networkStable: navigator.onLine, deviceType: 'web' });
    } catch (err) {
      console.error('Mic permission denied:', err);
      setPermissionStatus('error');
      analytics.track('task_started', { subtest: 'speaking', phase: 'mic_permission_denied' });
      void postSpeakingDeviceCheck({ microphoneGranted: false, networkStable: navigator.onLine, deviceType: 'web' });
    }
  };

  const startRecording = async () => {
    try {
      if (autoStopTimeoutRef.current !== null) {
        window.clearTimeout(autoStopTimeoutRef.current);
        autoStopTimeoutRef.current = null;
      }

      if (audioUrl) {
        URL.revokeObjectURL(audioUrl);
        setAudioUrl(null);
      }

      recordedBlobRef.current = null;

      if (isNativePlatform) {
        await SpeakingRecorder.start({ mimeType: 'audio/mp4' });
        audioChunksRef.current = [];
        mediaRecorderRef.current = null;
      } else {
        const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        mediaRecorderRef.current = new MediaRecorder(stream);
        audioChunksRef.current = [];

        mediaRecorderRef.current.ondataavailable = (event) => {
          if (event.data.size > 0) {
            audioChunksRef.current.push(event.data);
          }
        };

        mediaRecorderRef.current.start(100);
      }

      setRecordingStatus('recording');
      isRecordingRef.current = true;

      autoStopTimeoutRef.current = window.setTimeout(() => {
        if (isRecordingRef.current) {
          void stopRecording();
        }
      }, 3000);
    } catch (err) {
      console.error('Failed to start recording:', err);
    }
  };

  const stopRecording = async () => {
    if (autoStopTimeoutRef.current !== null) {
      window.clearTimeout(autoStopTimeoutRef.current);
      autoStopTimeoutRef.current = null;
    }

    try {
      if (isNativePlatform) {
        const recording = await SpeakingRecorder.stop();
        recordedBlobRef.current = base64ToBlob(recording.base64, recording.mimeType);
      } else if (mediaRecorderRef.current && mediaRecorderRef.current.state === 'recording') {
        await new Promise<void>((resolve) => {
          mediaRecorderRef.current?.addEventListener('stop', () => resolve(), { once: true });
          mediaRecorderRef.current?.stop();
        });

        recordedBlobRef.current = new Blob(audioChunksRef.current, { type: mediaRecorderRef.current?.mimeType || 'audio/webm' });
      }

      const audioBlob = recordedBlobRef.current;
      if (!audioBlob || audioBlob.size === 0) {
        throw new Error('No audio was captured.');
      }

      const url = URL.createObjectURL(audioBlob);
      setAudioUrl(url);
      setRecordingStatus('finished');
      if (!isNativePlatform) {
        mediaRecorderRef.current = null;
      }
    } catch (err) {
      console.error('Failed to stop recording:', err);
      setRecordingStatus('idle');
      recordedBlobRef.current = null;
      if (isNativePlatform) {
        void SpeakingRecorder.cancel().catch(() => undefined);
      }
    } finally {
      isRecordingRef.current = false;
      if (!isNativePlatform && mediaRecorderRef.current) {
        mediaRecorderRef.current = null;
      }
    }
  };

  const playSample = () => {
    if (audioUrl && audioPlayerRef.current) {
      audioPlayerRef.current.play();
      setIsPlaying(true);
    }
  };

  const handlePlaybackEnded = () => {
    setIsPlaying(false);
  };

  // Cleanup
  useEffect(() => {
    return () => {
      if (autoStopTimeoutRef.current !== null) {
        window.clearTimeout(autoStopTimeoutRef.current);
        autoStopTimeoutRef.current = null;
      }
      if (animationFrameRef.current) cancelAnimationFrame(animationFrameRef.current);
      if (isNativePlatform) {
        void SpeakingRecorder.cancel().catch(() => undefined);
      }
      if (audioContextRef.current) audioContextRef.current.close();
      if (streamRef.current) {
        streamRef.current.getTracks().forEach((track) => track.stop());
        streamRef.current = null;
      }
    };
  }, [isNativePlatform]);

  const allChecksPassed = permissionStatus === 'success' && recordingStatus === 'finished' && !isNoisy && isCompatible;
  const heroHighlights = [
    { icon: Mic, label: 'Check type', value: 'Microphone setup' },
    { icon: Volume2, label: 'Playback', value: 'Sample recording' },
    { icon: BarChart3, label: 'Environment', value: 'Noise monitoring' },
  ];

  const handleContinue = () => {
    if (allChecksPassed) {
      analytics.track('task_started', { subtest: 'speaking', phase: 'mic_check_passed', taskId });
      void postSpeakingDeviceCheck({ microphoneGranted: true, networkStable: navigator.onLine, deviceType: isNativePlatform ? 'native' : 'web' });
      router.push(`/speaking/task/${taskId}?mode=self`);
    }
  };

  return (
    <LearnerDashboardShell pageTitle="Readiness Check">
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Practice"
          icon={Mic}
          title="Readiness Check"
          description="Confirm that your microphone, playback, and surroundings are ready before you start speaking."
          highlights={heroHighlights}
        />

        <Card className="border-border bg-surface p-5 shadow-sm">
          <InlineAlert variant="info">
            These checks ensure the AI can accurately transcribe and evaluate your speech. If you encounter issues, check your device settings or try a different microphone.
          </InlineAlert>
        </Card>

        {/* Compatibility Warning */}
        {isCompatible === false && (
          <Card className="border-rose-200 bg-rose-50/60 p-5 shadow-sm">
            <InlineAlert variant="error">
              Your browser or device does not support the required audio recording features. Please try using a modern browser like Chrome or Safari.
            </InlineAlert>
          </Card>
        )}

        <LearnerSurfaceSectionHeader
          eyebrow="System checks"
          title="Complete each readiness step"
          description="Each stage uses the same card rhythm and border language as the dashboard."
        />

          {/* Step 1: Permission */}
          <section className={`rounded-3xl border bg-surface p-6 shadow-sm transition-all ${permissionStatus === 'success' ? 'border-green-200' : 'border-border'}`}>
            <div className="flex items-start justify-between mb-6">
              <div className="flex items-center gap-4">
                <div className={`flex h-12 w-12 items-center justify-center rounded-2xl transition-colors ${permissionStatus === 'success' ? 'bg-green-50' : 'bg-blue-50'}`}>
                  <Mic className={`h-6 w-6 ${permissionStatus === 'success' ? 'text-green-600' : 'text-blue-600'}`} />
                </div>
                <div>
                  <h2 className="font-bold text-navy">Microphone Access</h2>
                  <p className="text-sm text-muted">Allow access to your microphone to record your speech.</p>
                </div>
              </div>
              {permissionStatus === 'success' && <CheckCircle2 className="w-6 h-6 text-green-500" />}
            </div>

            {permissionStatus !== 'success' && (
              <Button onClick={requestPermission} loading={permissionStatus === 'checking'}>
                Grant Permission
              </Button>
            )}
            
            {permissionStatus === 'error' && (
              <p className="mt-3 text-xs text-red-600 font-medium flex items-center gap-1">
                <AlertTriangle className="w-3 h-3" /> Permission denied. Please enable mic access in your browser settings.
              </p>
            )}
          </section>

          {/* Step 2: Recording & Playback Test */}
          <section className={`rounded-3xl border bg-surface p-6 shadow-sm transition-all ${recordingStatus === 'finished' ? 'border-green-200' : 'border-border'} ${permissionStatus !== 'success' ? 'pointer-events-none opacity-50' : ''}`}>
            <div className="flex items-start justify-between mb-6">
              <div className="flex items-center gap-4">
                <div className={`flex h-12 w-12 items-center justify-center rounded-2xl transition-colors ${recordingStatus === 'finished' ? 'bg-green-50' : 'bg-purple-50'}`}>
                  <Volume2 className={`h-6 w-6 ${recordingStatus === 'finished' ? 'text-green-600' : 'text-purple-600'}`} />
                </div>
                <div>
                  <h2 className="font-bold text-navy">Recording & Playback</h2>
                  <p className="text-sm text-muted">Record a 3-second sample to ensure your audio is clear.</p>
                </div>
              </div>
              {recordingStatus === 'finished' && <CheckCircle2 className="w-6 h-6 text-green-500" />}
            </div>

            <div className="flex flex-wrap gap-4">
              {recordingStatus !== 'recording' && (
                <Button variant="outline" onClick={startRecording}>
                  <Mic className="w-4 h-4" /> {recordingStatus === 'finished' ? 'Re-record Sample' : 'Record Sample'}
                </Button>
              )}

              {recordingStatus === 'recording' && (
                <Button variant="destructive" onClick={stopRecording}>
                  <Square className="w-4 h-4 fill-current" /> Stop Recording (3s)
                </Button>
              )}

              {recordingStatus === 'finished' && audioUrl && (
                <Button onClick={playSample} loading={isPlaying}>
                  <Play className="w-4 h-4 fill-current" /> Play Back Sample
                </Button>
              )}
            </div>

            <audio ref={audioPlayerRef} src={audioUrl || ''} onEnded={handlePlaybackEnded} className="hidden" />
          </section>

          {/* Step 3: Environment Check */}
          <section className={`rounded-3xl border bg-surface p-6 shadow-sm transition-all ${isNoisy ? 'border-amber-200' : 'border-border'} ${permissionStatus !== 'success' ? 'pointer-events-none opacity-50' : ''}`}>
            <div className="flex items-center justify-between mb-6">
              <div className="flex items-center gap-4">
                <div className={`flex h-12 w-12 items-center justify-center rounded-2xl transition-colors ${isNoisy ? 'bg-amber-50' : 'bg-blue-50'}`}>
                  <BarChart3 className={`h-6 w-6 ${isNoisy ? 'text-amber-600' : 'text-blue-600'}`} />
                </div>
                <div>
                  <h2 className="font-bold text-navy">Environment Noise</h2>
                  <p className="text-sm text-muted">We&apos;re monitoring background noise levels.</p>
                </div>
              </div>
              {!isNoisy && permissionStatus === 'success' && <CheckCircle2 className="w-6 h-6 text-green-500" />}
            </div>

            {/* Visualizer */}
            <div className="flex h-12 items-end gap-1 overflow-hidden rounded-2xl bg-background-light p-2">
              {Array.from({ length: 40 }).map((_, i) => {
                // Use a deterministic "random" variation based on index
                const variation = (Math.sin(i * 0.5) + 1) / 2;
                const height = Math.max(10, noiseLevel * variation * 3);
                return (
                  <motion.div 
                    key={i}
                    className={`flex-1 rounded-t-sm ${isNoisy ? 'bg-amber-400' : 'bg-primary'}`}
                    animate={{ height: `${height}%` }}
                    transition={{ duration: 0.1 }}
                  />
                );
              })}
            </div>

            <AnimatePresence>
              {isNoisy && (
                <motion.div 
                  initial={{ height: 0, opacity: 0 }}
                  animate={{ height: 'auto', opacity: 1 }}
                  exit={{ height: 0, opacity: 0 }}
                  className="mt-4 flex items-start gap-3 rounded-2xl border border-amber-100 bg-amber-50 p-3"
                >
                  <AlertTriangle className="w-5 h-5 text-amber-600 shrink-0" />
                  <p className="text-xs text-amber-800 leading-relaxed">
                    <strong>High background noise detected.</strong> For the best results, please move to a quieter location or use a headset with a microphone.
                  </p>
                </motion.div>
              )}
            </AnimatePresence>
          </section>

          {/* Action Button */}
          <div className="pt-4">
            <Button
              fullWidth
              size="lg"
              disabled={!allChecksPassed}
              onClick={handleContinue}
            >
              Start Speaking Task <ChevronRight className="w-5 h-5" />
            </Button>
            {!allChecksPassed && permissionStatus === 'success' && (
              <p className="text-center text-xs text-muted mt-3">Complete all checks to continue</p>
            )}
          </div>

      </div>
      </LearnerDashboardShell>
  );
}

export default function MicEnvironmentCheck() {
  return (
    <Suspense fallback={
      <LearnerDashboardShell pageTitle="Readiness Check">
        <div className="flex items-center justify-center py-20">
          <RefreshCw className="w-8 h-8 text-primary animate-spin" />
        </div>
      </LearnerDashboardShell>
    }>
      <MicEnvironmentCheckContent />
    </Suspense>
  );
}
