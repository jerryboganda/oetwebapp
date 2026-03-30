'use client';

import { useState, useEffect, useRef, Suspense } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  Mic, Volume2, Play, Square, AlertTriangle, CheckCircle2,
  ChevronRight, Info, RefreshCw, BarChart3,
} from 'lucide-react';
import { useSearchParams, useRouter } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { InlineAlert } from '@/components/ui/alert';
import { analytics } from '@/lib/analytics';
import { postSpeakingDeviceCheck } from '@/lib/api';

type CheckStatus = 'pending' | 'checking' | 'success' | 'warning' | 'error';

function MicEnvironmentCheckContent() {
  const searchParams = useSearchParams();
  const router = useRouter();
  const taskId = searchParams?.get('taskId') || 'st-001';

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
  const audioContextRef = useRef<AudioContext | null>(null);
  const analyserRef = useRef<AnalyserNode | null>(null);
  const animationFrameRef = useRef<number | null>(null);
  const audioPlayerRef = useRef<HTMLAudioElement | null>(null);

  // --- Initial Compatibility Check ---
  useEffect(() => {
    const checkCompatibility = () => {
      const hasMediaRecorder = typeof window !== 'undefined' && !!window.MediaRecorder;
      const hasAudioContext = typeof window !== 'undefined' && (!!window.AudioContext || !!(window as any).webkitAudioContext);
      setIsCompatible(hasMediaRecorder && hasAudioContext);
    };
    checkCompatibility();
  }, []);

  // --- Noise Monitoring ---
  const startNoiseMonitoring = (stream: MediaStream) => {
    if (!audioContextRef.current) {
      audioContextRef.current = new (window.AudioContext || (window as any).webkitAudioContext)();
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
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
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
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      mediaRecorderRef.current = new MediaRecorder(stream);
      audioChunksRef.current = [];

      mediaRecorderRef.current.ondataavailable = (event) => {
        if (event.data.size > 0) {
          audioChunksRef.current.push(event.data);
        }
      };

      mediaRecorderRef.current.onstop = () => {
        const audioBlob = new Blob(audioChunksRef.current, { type: 'audio/wav' });
        const url = URL.createObjectURL(audioBlob);
        setAudioUrl(url);
        setRecordingStatus('finished');
      };

      mediaRecorderRef.current.start();
      setRecordingStatus('recording');
      
      // Auto-stop after 3 seconds
      setTimeout(() => {
        if (mediaRecorderRef.current?.state === 'recording') {
          stopRecording();
        }
      }, 3000);
    } catch (err) {
      console.error('Failed to start recording:', err);
    }
  };

  const stopRecording = () => {
    if (mediaRecorderRef.current && mediaRecorderRef.current.state === 'recording') {
      mediaRecorderRef.current.stop();
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
      if (animationFrameRef.current) cancelAnimationFrame(animationFrameRef.current);
      if (audioContextRef.current) audioContextRef.current.close();
    };
  }, []);

  const allChecksPassed = permissionStatus === 'success' && recordingStatus === 'finished' && !isNoisy && isCompatible;

  const handleContinue = () => {
    if (allChecksPassed) {
      analytics.track('task_started', { subtest: 'speaking', phase: 'mic_check_passed', taskId });
      void postSpeakingDeviceCheck({ microphoneGranted: true, networkStable: navigator.onLine, deviceType: 'web' });
      router.push(`/speaking/task/${taskId}?mode=self`);
    }
  };

  return (
    <LearnerDashboardShell pageTitle="Readiness Check">
      <div className="space-y-6">

        <InlineAlert variant="info">
          These checks ensure the AI can accurately transcribe and evaluate your speech. If you encounter issues, check your device settings or try a different microphone.
        </InlineAlert>

        {/* Compatibility Warning */}
        {isCompatible === false && (
          <InlineAlert variant="error">
            Your browser or device does not support the required audio recording features. Please try using a modern browser like Chrome or Safari.
          </InlineAlert>
        )}

          {/* Step 1: Permission */}
          <section className={`bg-white rounded-2xl border p-6 transition-all ${permissionStatus === 'success' ? 'border-green-200' : 'border-gray-200 shadow-sm'}`}>
            <div className="flex items-start justify-between mb-6">
              <div className="flex items-center gap-4">
                <div className={`w-12 h-12 rounded-2xl flex items-center justify-center transition-colors ${permissionStatus === 'success' ? 'bg-green-50' : 'bg-blue-50'}`}>
                  <Mic className={`w-6 h-6 ${permissionStatus === 'success' ? 'text-green-600' : 'text-blue-600'}`} />
                </div>
                <div>
                  <h2 className="font-bold text-gray-900">Microphone Access</h2>
                  <p className="text-sm text-gray-500">Allow access to your microphone to record your speech.</p>
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
          <section className={`bg-white rounded-2xl border p-6 transition-all ${recordingStatus === 'finished' ? 'border-green-200' : 'border-gray-200 shadow-sm'} ${permissionStatus !== 'success' ? 'opacity-50 pointer-events-none' : ''}`}>
            <div className="flex items-start justify-between mb-6">
              <div className="flex items-center gap-4">
                <div className={`w-12 h-12 rounded-2xl flex items-center justify-center transition-colors ${recordingStatus === 'finished' ? 'bg-green-50' : 'bg-purple-50'}`}>
                  <Volume2 className={`w-6 h-6 ${recordingStatus === 'finished' ? 'text-green-600' : 'text-purple-600'}`} />
                </div>
                <div>
                  <h2 className="font-bold text-gray-900">Recording & Playback</h2>
                  <p className="text-sm text-gray-500">Record a 3-second sample to ensure your audio is clear.</p>
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
          <section className={`bg-white rounded-2xl border p-6 transition-all ${isNoisy ? 'border-amber-200' : 'border-gray-200 shadow-sm'} ${permissionStatus !== 'success' ? 'opacity-50 pointer-events-none' : ''}`}>
            <div className="flex items-center justify-between mb-6">
              <div className="flex items-center gap-4">
                <div className={`w-12 h-12 rounded-2xl flex items-center justify-center transition-colors ${isNoisy ? 'bg-amber-50' : 'bg-blue-50'}`}>
                  <BarChart3 className={`w-6 h-6 ${isNoisy ? 'text-amber-600' : 'text-blue-600'}`} />
                </div>
                <div>
                  <h2 className="font-bold text-gray-900">Environment Noise</h2>
                  <p className="text-sm text-gray-500">We&apos;re monitoring background noise levels.</p>
                </div>
              </div>
              {!isNoisy && permissionStatus === 'success' && <CheckCircle2 className="w-6 h-6 text-green-500" />}
            </div>

            {/* Visualizer */}
            <div className="h-12 bg-gray-100 rounded-xl overflow-hidden flex items-end gap-1 p-2">
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
                  className="mt-4 flex items-start gap-3 bg-amber-50 border border-amber-100 rounded-xl p-3"
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
