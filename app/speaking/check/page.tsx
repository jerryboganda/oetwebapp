'use client';

import { useState, useEffect, useRef, Suspense } from 'react';
import { Capacitor } from '@capacitor/core';
import { motion, AnimatePresence } from 'motion/react';
import {
  Mic, Volume2, Play, Square, AlertTriangle, CheckCircle2,
  ChevronRight, RefreshCw, BarChart3, Home, ShieldCheck,
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
  // Computer-based Speaking is always taken at home. The learner must explicitly
  // acknowledge the CBT environment + paper-handling requirements (rulebook
  // RULE_59..RULE_62 / RULE_73..RULE_76) before the readiness gate unlocks.
  const [cbtEnvConfirmed, setCbtEnvConfirmed] = useState(false);
  const [cbtPaperAcknowledged, setCbtPaperAcknowledged] = useState(false);
  
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
        void postSpeakingDeviceCheck({
          microphoneGranted: true,
          networkStable: navigator.onLine,
          deviceType: 'native',
          taskId,
          noiseLevel: 0,
          noiseAcceptable: true,
        });
        return;
      }

      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
  streamRef.current = stream;
      setPermissionStatus('success');
      analytics.track('task_started', { subtest: 'speaking', phase: 'mic_permission_granted' });
      startNoiseMonitoring(stream);
      void postSpeakingDeviceCheck({
        microphoneGranted: true,
        networkStable: navigator.onLine,
        deviceType: 'web',
        taskId,
        noiseLevel,
        noiseAcceptable: !isNoisy,
      });
    } catch (err) {
      console.error('Mic permission denied:', err);
      setPermissionStatus('error');
      analytics.track('task_started', { subtest: 'speaking', phase: 'mic_permission_denied' });
      void postSpeakingDeviceCheck({
        microphoneGranted: false,
        networkStable: navigator.onLine,
        deviceType: isNativePlatform ? 'native' : 'web',
        taskId,
        noiseLevel,
        noiseAcceptable: !isNoisy,
      });
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

  const allChecksPassed = permissionStatus === 'success' && recordingStatus === 'finished' && !isNoisy && isCompatible && cbtEnvConfirmed && cbtPaperAcknowledged;
  const heroHighlights = [
    { icon: Home, label: 'Location', value: 'At-home exam' },
    { icon: Mic, label: 'Check type', value: 'Microphone setup' },
    { icon: BarChart3, label: 'Environment', value: 'Noise monitoring' },
  ];

  const handleContinue = () => {
    if (allChecksPassed) {
      analytics.track('task_started', { subtest: 'speaking', phase: 'mic_check_passed', taskId });
      void postSpeakingDeviceCheck({
        microphoneGranted: true,
        networkStable: navigator.onLine,
        deviceType: isNativePlatform ? 'native' : 'web',
        taskId,
        noiseLevel,
        noiseAcceptable: !isNoisy,
      });
      router.push(`/speaking/roleplay/${taskId}`);
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
          <Card className="border-danger/30 bg-danger/10 p-5 shadow-sm">
            <InlineAlert variant="error">
              Your browser or device does not support the required audio recording features. Please try using a modern browser like Chrome or Safari.
            </InlineAlert>
          </Card>
        )}

        <LearnerSurfaceSectionHeader
          eyebrow="System checks"
          title="Complete each readiness step"
          description="Complete each step before you start your speaking attempt."
        />

          {/* Step 0: Computer-based exam environment acknowledgement (at-home rule + paper rule) */}
          <section className={`rounded-3xl border bg-surface p-6 shadow-sm transition-all ${cbtEnvConfirmed && cbtPaperAcknowledged ? 'border-success/30' : 'border-warning/30'}`}>
            <div className="flex items-start justify-between mb-6">
              <div className="flex items-center gap-4">
                <div className={`flex h-12 w-12 items-center justify-center rounded-2xl transition-colors ${cbtEnvConfirmed && cbtPaperAcknowledged ? 'bg-success/10' : 'bg-amber-50'}`}>
                  <Home className={`h-6 w-6 ${cbtEnvConfirmed && cbtPaperAcknowledged ? 'text-success' : 'text-warning'}`} />
                </div>
                <div>
                  <h2 className="font-bold text-navy">At-home exam environment</h2>
                  <p className="text-sm text-muted">Computer-based OET Speaking is always taken at home, never at a test centre.</p>
                </div>
              </div>
              {cbtEnvConfirmed && cbtPaperAcknowledged && <CheckCircle2 className="w-6 h-6 text-success" />}
            </div>

            <div className="space-y-4">
              <div>
                <p className="text-xs font-bold text-muted uppercase tracking-widest mb-2">Environment requirements</p>
                <ul className="grid grid-cols-1 gap-y-1.5 text-sm text-navy sm:grid-cols-2 sm:gap-x-6">
                  <li className="flex gap-2"><span className="text-primary">•</span><span><strong>Location:</strong> at home only — never at a test centre.</span></li>
                  <li className="flex gap-2"><span className="text-primary">•</span><span><strong>Room:</strong> indoors, well-lit, solid walls (no glass panels or transparent partitions).</span></li>
                  <li className="flex gap-2"><span className="text-primary">•</span><span><strong>Completely alone</strong> — if anyone enters the room, the exam is stopped.</span></li>
                  <li className="flex gap-2"><span className="text-primary">•</span><span><strong>Single monitor</strong> only. Dual monitors are not allowed.</span></li>
                  <li className="flex gap-2"><span className="text-primary">•</span><span><strong>Webcam:</strong> wired or built-in that can move. Bluetooth webcam not permitted.</span></li>
                  <li className="flex gap-2"><span className="text-primary">•</span><span><strong>Microphone &amp; speakers:</strong> built-in or USB plug-in only. Bluetooth not allowed. No headsets during Speaking.</span></li>
                  <li className="flex gap-2"><span className="text-primary">•</span><span><strong>Power:</strong> device plugged directly into a power source — no docking station, no battery only.</span></li>
                  <li className="flex gap-2"><span className="text-primary">•</span><span><strong>VPN / virtual machines</strong> fully disabled before the exam starts.</span></li>
                  <li className="flex gap-2"><span className="text-primary">•</span><span><strong>Extra screens</strong> (TV, second monitor) unplugged.</span></li>
                </ul>
                <label className="mt-3 flex items-start gap-3 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={cbtEnvConfirmed}
                    onChange={(e) => {
                      setCbtEnvConfirmed(e.target.checked);
                      if (e.target.checked) analytics.track('speaking_cbt_environment_confirmed', { taskId });
                    }}
                    className="mt-0.5 h-4 w-4 rounded border-border-hover text-primary focus:ring-primary"
                    aria-label="I confirm all environment requirements are met"
                  />
                  <span className="text-sm text-navy">I confirm my exam environment meets ALL of the requirements above.</span>
                </label>
              </div>

              <div className="rounded-2xl border border-warning/30 bg-amber-50 p-4">
                <div className="flex items-start gap-3">
                  <ShieldCheck className="h-5 w-5 text-warning shrink-0 mt-0.5" />
                  <div className="space-y-1">
                    <p className="text-sm font-bold text-navy">Paper &amp; pen rule (computer-based only)</p>
                    <p className="text-sm text-navy/80 leading-relaxed">
                      You <strong>cannot</strong> highlight or annotate the role-play card on screen. You <strong>are permitted</strong> <em>one</em> blank piece of paper and a pen to take notes during preparation time and the role play.
                    </p>
                    <p className="text-sm text-navy/80 leading-relaxed">
                      At the end of the exam you <strong>must tear or cut the paper in front of the camera</strong> to confirm nothing is removed from the test environment.
                    </p>
                  </div>
                </div>
                <label className="mt-3 flex items-start gap-3 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={cbtPaperAcknowledged}
                    onChange={(e) => {
                      setCbtPaperAcknowledged(e.target.checked);
                      if (e.target.checked) analytics.track('speaking_cbt_paper_acknowledged', { taskId });
                    }}
                    className="mt-0.5 h-4 w-4 rounded border-border-hover text-primary focus:ring-primary"
                    aria-label="I understand the paper and pen rule"
                  />
                  <span className="text-sm text-navy">I have one blank paper + pen ready, understand the card cannot be annotated on screen, and will destroy the paper in front of the camera at the end.</span>
                </label>
              </div>
            </div>
          </section>

          {/* Step 1: Permission */}
          <section className={`rounded-3xl border bg-surface p-6 shadow-sm transition-all ${permissionStatus === 'success' ? 'border-success/30' : 'border-border'}`}>
            <div className="flex items-start justify-between mb-6">
              <div className="flex items-center gap-4">
                <div className={`flex h-12 w-12 items-center justify-center rounded-2xl transition-colors ${permissionStatus === 'success' ? 'bg-success/10' : 'bg-info/10'}`}>
                  <Mic className={`h-6 w-6 ${permissionStatus === 'success' ? 'text-success' : 'text-info'}`} />
                </div>
                <div>
                  <h2 className="font-bold text-navy">Microphone Access</h2>
                  <p className="text-sm text-muted">Allow access to your microphone to record your speech.</p>
                </div>
              </div>
              {permissionStatus === 'success' && <CheckCircle2 className="w-6 h-6 text-success" />}
            </div>

            {permissionStatus !== 'success' && (
              <Button onClick={requestPermission} loading={permissionStatus === 'checking'}>
                Grant Permission
              </Button>
            )}
            
            {permissionStatus === 'error' && (
              <p className="mt-3 text-xs text-danger font-medium flex items-center gap-1">
                <AlertTriangle className="w-3 h-3" /> Permission denied. Please enable mic access in your browser settings.
              </p>
            )}
          </section>

          {/* Step 2: Recording & Playback Test */}
          <section className={`rounded-3xl border bg-surface p-6 shadow-sm transition-all ${recordingStatus === 'finished' ? 'border-success/30' : 'border-border'} ${permissionStatus !== 'success' ? 'pointer-events-none opacity-50' : ''}`}>
            <div className="flex items-start justify-between mb-6">
              <div className="flex items-center gap-4">
                <div className={`flex h-12 w-12 items-center justify-center rounded-2xl transition-colors ${recordingStatus === 'finished' ? 'bg-success/10' : 'bg-primary/10'}`}>
                  <Volume2 className={`h-6 w-6 ${recordingStatus === 'finished' ? 'text-success' : 'text-primary'}`} />
                </div>
                <div>
                  <h2 className="font-bold text-navy">Recording & Playback</h2>
                  <p className="text-sm text-muted">Record a 3-second sample to ensure your audio is clear.</p>
                </div>
              </div>
              {recordingStatus === 'finished' && <CheckCircle2 className="w-6 h-6 text-success" />}
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
          <section className={`rounded-3xl border bg-surface p-6 shadow-sm transition-all ${isNoisy ? 'border-warning/30' : 'border-border'} ${permissionStatus !== 'success' ? 'pointer-events-none opacity-50' : ''}`}>
            <div className="flex items-center justify-between mb-6">
              <div className="flex items-center gap-4">
                <div className={`flex h-12 w-12 items-center justify-center rounded-2xl transition-colors ${isNoisy ? 'bg-amber-50' : 'bg-info/10'}`}>
                  <BarChart3 className={`h-6 w-6 ${isNoisy ? 'text-warning' : 'text-info'}`} />
                </div>
                <div>
                  <h2 className="font-bold text-navy">Environment Noise</h2>
                  <p className="text-sm text-muted">We&apos;re monitoring background noise levels.</p>
                </div>
              </div>
              {!isNoisy && permissionStatus === 'success' && <CheckCircle2 className="w-6 h-6 text-success" />}
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
                    className={`flex-1 rounded-t-sm ${isNoisy ? 'bg-warning' : 'bg-primary'}`}
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
                  className="mt-4 flex items-start gap-3 rounded-2xl border border-warning/30 bg-amber-50 p-3"
                >
                  <AlertTriangle className="w-5 h-5 text-warning shrink-0" />
                  <p className="text-xs text-warning leading-relaxed">
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
