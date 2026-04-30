'use client';

import { useEffect, useState, useCallback, useRef } from 'react';
import { Capacitor } from '@capacitor/core';
import { useRouter } from 'next/navigation';
import { AppShell } from '@/components/layout';
import { AsyncStateWrapper } from '@/components/state';
import { MicCheckPanel } from '@/components/domain/mic-check-panel';
import { SpeakingRoleCard } from '@/components/domain/speaking-role-card';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Timer } from '@/components/ui/timer';
import { Modal } from '@/components/ui/modal';
import { InlineAlert } from '@/components/ui/alert';
import { useAnalytics } from '@/hooks/use-analytics';
import { fetchRoleCard, submitSpeakingRecording } from '@/lib/api';
import { SpeakingRecorder, base64ToBlob } from '@/lib/mobile/speaking-recorder';
import type { RoleCard } from '@/lib/mock-data';
import {
  Mic,
  MicOff,
  Upload,
  LogOut,
  CheckCircle2,
  Square,
  Send,
  ShieldCheck,
} from 'lucide-react';

const DIAGNOSTIC_SPEAKING_TASK_ID = 'st-001';

type SpeakingPhase = 'mic-check' | 'role-card' | 'recording' | 'review' | 'uploading' | 'done';

export default function DiagnosticSpeakingPage() {
  const router = useRouter();
  const { track } = useAnalytics();
  const isNativePlatform = Capacitor.isNativePlatform();

  const [roleCard, setRoleCard] = useState<RoleCard | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  const [phase, setPhase] = useState<SpeakingPhase>('mic-check');
  const [isRecording, setIsRecording] = useState(false);
  const [recordingSeconds, setRecordingSeconds] = useState(0);
  const [showLeaveModal, setShowLeaveModal] = useState(false);
  const [showStopModal, setShowStopModal] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [uploadProgress, setUploadProgress] = useState(0);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);

  const recordingRef = useRef(false);
  const mediaRecorderRef = useRef<MediaRecorder | null>(null);
  const audioChunksRef = useRef<Blob[]>([]);
  const streamRef = useRef<MediaStream | null>(null);
  const recordedBlobRef = useRef<Blob | null>(null);
  const autoStopTimeoutRef = useRef<number | null>(null);

  const load = useCallback(async () => {
    try {
      setLoading(true);
      setError(undefined);
      const card = await fetchRoleCard(DIAGNOSTIC_SPEAKING_TASK_ID);
      setRoleCard(card);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load speaking task');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  useEffect(() => {
    return () => {
      if (autoStopTimeoutRef.current !== null) {
        window.clearTimeout(autoStopTimeoutRef.current);
        autoStopTimeoutRef.current = null;
      }
      if (isNativePlatform) {
        void SpeakingRecorder.cancel().catch(() => undefined);
      } else if (mediaRecorderRef.current && mediaRecorderRef.current.state !== 'inactive') {
        mediaRecorderRef.current.stop();
      }
      if (streamRef.current) {
        streamRef.current.getTracks().forEach((track) => track.stop());
      }
      if (previewUrl) {
        URL.revokeObjectURL(previewUrl);
      }
    };
  }, [isNativePlatform, previewUrl]);

  const handleMicCheckComplete = () => {
    track('task_started', { subTest: 'Speaking', mode: 'diagnostic', taskId: DIAGNOSTIC_SPEAKING_TASK_ID });
    setPhase('role-card');
  };

  const handleStartRecording = async () => {
    try {
      setError(undefined);
      if (autoStopTimeoutRef.current !== null) {
        window.clearTimeout(autoStopTimeoutRef.current);
        autoStopTimeoutRef.current = null;
      }

      recordedBlobRef.current = null;

      if (isNativePlatform) {
        await SpeakingRecorder.start({ mimeType: 'audio/mp4' });
        mediaRecorderRef.current = null;
        audioChunksRef.current = [];
      } else {
        const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        streamRef.current = stream;
        const recorder = new MediaRecorder(stream);
        mediaRecorderRef.current = recorder;
        audioChunksRef.current = [];

        recorder.ondataavailable = (event) => {
          if (event.data.size > 0) {
            audioChunksRef.current.push(event.data);
          }
        };

        recorder.start(100);
      }

      autoStopTimeoutRef.current = window.setTimeout(() => {
        if (recordingRef.current) {
          void handleStopRecording();
        }
      }, 3000);

      setPhase('recording');
      setIsRecording(true);
      recordingRef.current = true;
      setRecordingSeconds(0);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Microphone access failed');
    }
  };

  const handleStopRecording = async () => {
    try {
      if (autoStopTimeoutRef.current !== null) {
        window.clearTimeout(autoStopTimeoutRef.current);
        autoStopTimeoutRef.current = null;
      }

      if (isNativePlatform) {
        const recording = await SpeakingRecorder.stop();
        const blob = base64ToBlob(recording.base64, recording.mimeType);
        recordedBlobRef.current = blob;
        if (previewUrl) {
          URL.revokeObjectURL(previewUrl);
        }
        setPreviewUrl(URL.createObjectURL(blob));
      } else {
        const recorder = mediaRecorderRef.current;
        if (recorder && recorder.state !== 'inactive') {
          await new Promise<void>((resolve) => {
            recorder.addEventListener('stop', () => resolve(), { once: true });
            recorder.stop();
          });
        }

        const blob = new Blob(audioChunksRef.current, { type: recorder?.mimeType || 'audio/webm' });
        recordedBlobRef.current = blob;
        if (previewUrl) {
          URL.revokeObjectURL(previewUrl);
        }
        setPreviewUrl(URL.createObjectURL(blob));
      }

      if (streamRef.current) {
        streamRef.current.getTracks().forEach((track) => track.stop());
        streamRef.current = null;
      }

      setPhase('review');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to stop recording');
      recordedBlobRef.current = null;
      if (isNativePlatform) {
        void SpeakingRecorder.cancel().catch(() => undefined);
      }
      if (streamRef.current) {
        streamRef.current.getTracks().forEach((track) => track.stop());
        streamRef.current = null;
      }
      setPhase('role-card');
    } finally {
      setIsRecording(false);
      recordingRef.current = false;
      setShowStopModal(false);
    }
  };

  const handleSubmit = async () => {
    try {
      setSubmitting(true);
      setPhase('uploading');

      // Simulate upload progress
      const interval = setInterval(() => {
        setUploadProgress((prev) => {
          if (prev >= 100) {
            clearInterval(interval);
            return 100;
          }
          return prev + 20;
        });
      }, 400);

      if (!recordedBlobRef.current || recordedBlobRef.current.size === 0) {
        throw new Error('No diagnostic speaking audio was captured.');
      }

      await submitSpeakingRecording(
        DIAGNOSTIC_SPEAKING_TASK_ID,
        recordedBlobRef.current,
        recordingSeconds || 120,
        'exam',
        {
          accepted: true,
          text: 'Diagnostic speaking audio consent accepted during microphone setup.',
        },
      );
      clearInterval(interval);
      setUploadProgress(100);

      track('task_submitted', { subTest: 'Speaking', mode: 'diagnostic', taskId: DIAGNOSTIC_SPEAKING_TASK_ID });
      setPhase('done');

      setTimeout(() => router.push('/diagnostic/hub'), 1500);
    } catch {
      setError('Upload failed. Please try again.');
      setPhase('review');
    } finally {
      setSubmitting(false);
    }
  };

  const handleLeave = () => {
    if (isRecording || phase === 'review') {
      setShowLeaveModal(true);
    } else {
      router.push('/diagnostic/hub');
    }
  };

  const status: 'loading' | 'error' | 'success' =
    loading ? 'loading' : error ? 'error' : 'success';

  return (
    <AppShell
      pageTitle="Diagnostic — Speaking"
      distractionFree
      navActions={
        <div className="flex items-center gap-3">
          {phase === 'recording' && isRecording && (
            <Timer mode="elapsed" size="sm" running={isRecording} onTick={setRecordingSeconds} />
          )}
          <Button variant="ghost" size="sm" onClick={handleLeave} className="gap-1.5">
            <LogOut className="w-3.5 h-3.5" /> Exit
          </Button>
        </div>
      }
    >
      <div className="max-w-3xl mx-auto px-4 sm:px-6 py-8">
        <AsyncStateWrapper status={status} onRetry={load} errorMessage={error}>
          {/* Phase: Mic Check */}
          {phase === 'mic-check' && (
            <div className="space-y-6">
              <div className="text-center">
                <h2 className="text-xl font-bold text-navy">Microphone Setup</h2>
                <p className="text-sm text-muted mt-1">
                  Complete the checks below before starting the speaking task.
                </p>
              </div>

              <InlineAlert variant="info">
                <div className="flex items-start gap-2">
                  <ShieldCheck className="w-4 h-4 text-primary shrink-0 mt-0.5" />
                  <div>
                    <p className="font-semibold text-sm text-navy">Audio Consent</p>
                    <p className="text-xs text-muted mt-0.5">
                      Your audio recording will be processed by AI for evaluation only.
                      Recordings are not shared with other users and are deleted after analysis.
                    </p>
                  </div>
                </div>
              </InlineAlert>

              <MicCheckPanel onComplete={handleMicCheckComplete} />
            </div>
          )}

          {/* Phase: Role Card Preview */}
          {phase === 'role-card' && roleCard && (
            <div className="space-y-6">
              <div className="text-center">
                <h2 className="text-xl font-bold text-navy">Role Card</h2>
                <p className="text-sm text-muted mt-1">
                  Read the scenario below. You&apos;ll have the role card visible during recording.
                </p>
              </div>

              <Card className="bg-amber-50/30 border-warning/30">
                <div className="flex items-center justify-between mb-4">
                  <p className="text-xs font-semibold text-warning">PREPARATION TIME</p>
                  <Timer mode="countdown" initialSeconds={60} size="sm" />
                </div>
                <SpeakingRoleCard
                  role={roleCard.title}
                  setting={roleCard.setting}
                  patient={roleCard.patient}
                  task={roleCard.brief}
                  background={roleCard.background}
                />
                {roleCard.tasks.length > 0 && (
                  <div className="mt-4 pt-3 border-t border-warning/30">
                    <p className="text-xs font-semibold text-primary uppercase tracking-wide mb-2">Tasks</p>
                    <ol className="space-y-1.5">
                      {roleCard.tasks.map((t, i) => (
                        <li key={i} className="text-sm text-navy flex items-start gap-2">
                          <span className="text-primary font-bold shrink-0">{i + 1}.</span> {t}
                        </li>
                      ))}
                    </ol>
                  </div>
                )}
              </Card>

              <Card className="bg-background-light/50">
                <h4 className="text-sm font-bold text-navy mb-2">Your Notes</h4>
                <textarea
                  className="w-full min-h-[100px] text-sm border border-border rounded p-3 resize-none focus:outline-none focus:ring-2 focus:ring-primary/20 text-navy"
                  placeholder="Jot down key points for your response..."
                />
              </Card>

              <div className="flex justify-center">
                <Button size="lg" onClick={handleStartRecording} className="gap-2 shadow-lg">
                  <Mic className="w-4 h-4" /> Start Recording
                </Button>
              </div>
            </div>
          )}

          {/* Phase: Recording */}
          {phase === 'recording' && roleCard && (
            <div className="space-y-6">
              <div className="text-center space-y-2">
                <div className="inline-flex items-center gap-2 px-4 py-2 rounded-full bg-danger/10 text-danger text-sm font-bold">
                  <span className="relative flex h-3 w-3">
                    <span className="absolute inline-flex h-full w-full rounded-full bg-danger opacity-75 animate-ping" />
                    <span className="relative inline-flex rounded-full h-3 w-3 bg-danger" />
                  </span>
                  Recording in Progress
                </div>
                <p className="text-sm text-muted">
                  Speak clearly. The role card is visible below for reference.
                </p>
              </div>

              {/* Compact role card during recording */}
              <Card className="bg-background-light/50 border-border">
                <p className="text-xs font-semibold text-muted mb-2">ROLE CARD REFERENCE</p>
                <p className="text-sm text-navy font-semibold">{roleCard.title}</p>
                <p className="text-xs text-muted mt-1">{roleCard.brief}</p>
                <ul className="mt-2 space-y-1">
                  {roleCard.tasks.map((t, i) => (
                    <li key={i} className="text-xs text-navy flex items-start gap-1.5">
                      <span className="text-primary font-bold">{i + 1}.</span> {t}
                    </li>
                  ))}
                </ul>
              </Card>

              {/* Recording timer & controls */}
              <div className="flex flex-col items-center gap-4">
                <div className="text-2xl font-bold text-navy tabular-nums">
                  {Math.floor(recordingSeconds / 60).toString().padStart(2, '0')}:
                  {(recordingSeconds % 60).toString().padStart(2, '0')}
                </div>
                <Button
                  variant="destructive"
                  size="lg"
                  onClick={() => setShowStopModal(true)}
                  className="gap-2"
                >
                  <Square className="w-4 h-4" /> Stop Recording
                </Button>
              </div>
            </div>
          )}

          {/* Phase: Review */}
          {phase === 'review' && (
            <div className="space-y-6">
              <div className="text-center">
                <CheckCircle2 className="w-12 h-12 text-success mx-auto mb-3" />
                <h2 className="text-xl font-bold text-navy">Recording Complete</h2>
                <p className="text-sm text-muted mt-1">
                  Duration: {Math.floor(recordingSeconds / 60)}:{(recordingSeconds % 60).toString().padStart(2, '0')}
                </p>
              </div>

              <Card className="text-center">
                <p className="text-sm text-muted mb-4">
                  Listen to your recording to check audio quality, then submit for evaluation.
                </p>
                {previewUrl ? (
                  <audio controls src={previewUrl} className="w-full" />
                ) : (
                  <div className="h-12 bg-background-light rounded-lg flex items-center justify-center text-xs text-muted">
                    Audio preview will appear here after recording.
                  </div>
                )}
              </Card>

              <div className="flex gap-3 justify-center">
                <Button variant="outline" onClick={() => { setPhase('role-card'); setRecordingSeconds(0); }}>
                  Re-record
                </Button>
                <Button onClick={handleSubmit} loading={submitting} className="gap-1.5">
                  <Send className="w-3.5 h-3.5" /> Submit Recording
                </Button>
              </div>
            </div>
          )}

          {/* Phase: Uploading */}
          {phase === 'uploading' && (
            <div className="text-center space-y-6 py-12">
              <Upload className="w-12 h-12 text-primary mx-auto animate-pulse" />
              <div>
                <h2 className="text-xl font-bold text-navy">Uploading Recording…</h2>
                <p className="text-sm text-muted mt-1">Please don&apos;t close this page.</p>
              </div>
              <div className="w-full max-w-sm mx-auto h-2 bg-border rounded-full overflow-hidden">
                <div
                  className="h-full bg-primary rounded-full transition-all duration-300"
                  style={{ width: `${uploadProgress}%` }}
                />
              </div>
              <p className="text-xs text-muted">{uploadProgress}%</p>
            </div>
          )}

          {/* Phase: Done */}
          {phase === 'done' && (
            <div className="text-center space-y-4 py-12">
              <CheckCircle2 className="w-16 h-16 text-success mx-auto" />
              <h2 className="text-xl font-bold text-navy">Submitted Successfully</h2>
              <p className="text-sm text-muted">Redirecting to diagnostic hub…</p>
            </div>
          )}
        </AsyncStateWrapper>
      </div>

      {/* Stop Recording Confirmation */}
      <Modal
        open={showStopModal}
        onClose={() => setShowStopModal(false)}
        title="Stop Recording?"
      >
        <div className="space-y-4">
          <p className="text-sm text-muted">
            Are you sure you want to stop recording? You&apos;ll be able to review and re-record if needed.
          </p>
          <div className="flex gap-3 justify-end">
            <Button variant="outline" onClick={() => setShowStopModal(false)}>
              Continue Recording
            </Button>
            <Button variant="destructive" onClick={handleStopRecording} className="gap-1.5">
              <MicOff className="w-3.5 h-3.5" /> Stop
            </Button>
          </div>
        </div>
      </Modal>

      {/* Leave Confirmation */}
      <Modal
        open={showLeaveModal}
        onClose={() => setShowLeaveModal(false)}
        title="Leave Speaking Task?"
      >
        <div className="space-y-4">
          <p className="text-sm text-muted">
            {isRecording
              ? 'Your recording is in progress and will be lost if you leave.'
              : 'Your recording has not been submitted. It will be lost if you leave.'}
          </p>
          <div className="flex gap-3 justify-end">
            <Button variant="outline" onClick={() => setShowLeaveModal(false)}>Stay</Button>
            <Button variant="destructive" onClick={() => router.push('/diagnostic/hub')}>Leave</Button>
          </div>
        </div>
      </Modal>
    </AppShell>
  );
}
