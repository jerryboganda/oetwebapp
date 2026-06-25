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
import { fetchDiagnosticTaskId, fetchRoleCard, submitSpeakingRecording } from '@/lib/api';
import {
  SpeakingRecorder,
  capturedSpeakingRecordingFromNativeStop,
  capturedSpeakingRecordingFromWebBlob,
  type CapturedSpeakingRecording,
} from '@/lib/mobile/speaking-recorder';
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
  Edit3,
  RotateCcw,
  Loader2,
  Volume2,
} from 'lucide-react';

type SpeakingPhase = 'mic-check' | 'role-card' | 'recording' | 'review' | 'uploading' | 'done';
const DIAGNOSTIC_RECORDING_LIMIT_MS = 5 * 60 * 1000;

export default function DiagnosticSpeakingPage() {
  const router = useRouter();
  const { track } = useAnalytics();
  const isNativePlatform = Capacitor.isNativePlatform();

  const [roleCard, setRoleCard] = useState<RoleCard | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  const [taskId, setTaskId] = useState<string | null>(null);
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
  const recordedRecordingRef = useRef<CapturedSpeakingRecording | null>(null);
  const autoStopTimeoutRef = useRef<number | null>(null);

  const load = useCallback(async () => {
    try {
      setLoading(true);
      setError(undefined);
      const resolvedId = await fetchDiagnosticTaskId('Speaking');
      setTaskId(resolvedId);
      const card = await fetchRoleCard(resolvedId);
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
    if (taskId) {
      track('task_started', { subTest: 'Speaking', mode: 'diagnostic', taskId });
    }
    setPhase('role-card');
  };

  const handleStartRecording = async () => {
    try {
      setError(undefined);
      if (autoStopTimeoutRef.current !== null) {
        window.clearTimeout(autoStopTimeoutRef.current);
        autoStopTimeoutRef.current = null;
      }

      recordedRecordingRef.current = null;

      if (isNativePlatform) {
        await SpeakingRecorder.start({ mimeType: 'audio/mp4', fileName: `${taskId ?? 'diagnostic-speaking'}.m4a` });
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
      }, DIAGNOSTIC_RECORDING_LIMIT_MS);

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
        const captured = capturedSpeakingRecordingFromNativeStop(recording, `${taskId ?? 'diagnostic-speaking'}.m4a`);
        recordedRecordingRef.current = captured;
        if (previewUrl) {
          URL.revokeObjectURL(previewUrl);
        }
        setPreviewUrl(URL.createObjectURL(captured.blob));
      } else {
        const recorder = mediaRecorderRef.current;
        if (recorder && recorder.state !== 'inactive') {
          await new Promise<void>((resolve) => {
            recorder.addEventListener('stop', () => resolve(), { once: true });
            recorder.stop();
          });
        }

        const blob = new Blob(audioChunksRef.current, { type: recorder?.mimeType || 'audio/webm' });
        const captured = capturedSpeakingRecordingFromWebBlob(
          blob,
          `${taskId ?? 'diagnostic-speaking'}.webm`,
          Math.max(1, recordingSeconds) * 1000,
        );
        recordedRecordingRef.current = captured;
        if (previewUrl) {
          URL.revokeObjectURL(previewUrl);
        }
        setPreviewUrl(URL.createObjectURL(captured.blob));
      }

      if (streamRef.current) {
        streamRef.current.getTracks().forEach((track) => track.stop());
        streamRef.current = null;
      }

      setPhase('review');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to stop recording');
      recordedRecordingRef.current = null;
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

      const captured = recordedRecordingRef.current;
      if (!captured || captured.blob.size === 0) {
        throw new Error('No diagnostic speaking audio was captured.');
      }

      if (!taskId) throw new Error('No diagnostic speaking task ID available.');
      const uploadDurationSeconds = Math.max(recordingSeconds || 120, Math.round(captured.durationMs / 1000) || 0);
      const submission = await submitSpeakingRecording(
        taskId,
        captured.blob,
        uploadDurationSeconds,
        'diagnostic',
        {
          accepted: true,
          text: 'Diagnostic speaking audio consent accepted during microphone setup.',
        },
        {
          fileName: captured.fileName,
          captureMethod: captured.captureMethod,
          contentType: captured.mimeType,
        },
      );
      clearInterval(interval);
      setUploadProgress(100);

      track('task_submitted', { subTest: 'Speaking', mode: 'diagnostic', taskId });
      setPhase('done');

      setTimeout(() => router.push(`/speaking/results/${submission.submissionId}?source=diagnostic`), 1500);
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
      pageTitle="Diagnostic: Speaking"
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
            <div className="space-y-6 max-w-2xl mx-auto">
              <div className="text-center pb-2">
                <h2 className="text-2xl font-black text-navy tracking-tight">Microphone Setup</h2>
                <p className="text-sm text-muted mt-2 font-medium">
                  Complete the checks below before starting the speaking task.
                </p>
              </div>

              <div className="bg-primary/5 border border-primary/20 rounded-xl p-5 shadow-sm">
                <div className="flex items-start gap-3">
                  <ShieldCheck className="w-5 h-5 text-primary shrink-0 mt-0.5" aria-hidden="true" />
                  <div>
                    <h3 className="font-bold text-sm text-navy mb-1">Audio Consent</h3>
                    <p className="text-xs text-muted leading-relaxed">
                      Your audio recording will be processed by AI for evaluation only.
                      Recordings are not shared with other users and are deleted after analysis.
                    </p>
                  </div>
                </div>
              </div>

              <MicCheckPanel onComplete={handleMicCheckComplete} />
            </div>
          )}

          {/* Phase: Role Card Preview */}
          {phase === 'role-card' && roleCard && (
            <div className="space-y-5 sm:space-y-8 max-w-3xl mx-auto">
              <div className="text-center pb-2">
                <h2 className="text-3xl font-black text-navy tracking-tight">Role Card</h2>
                <p className="text-sm font-medium text-muted mt-2">
                  Read the scenario below. You&apos;ll have the role card visible during recording.
                </p>
              </div>

              <Card className="bg-surface border border-border shadow-sm rounded-2xl overflow-hidden">

                  {/* Preparation header strip */}
                  <div className="flex items-center justify-between bg-warning/10 px-6 py-4 border-b border-warning/20">
                    <p className="text-xs font-black text-warning-dark uppercase tracking-widest">Preparation Time</p>
                    <div className="bg-surface px-4 py-1.5 rounded-full shadow-sm border border-warning/20">
                      <Timer mode="countdown" initialSeconds={60} size="sm" className="text-warning-dark font-black" />
                    </div>
                  </div>

                  <div className="p-6 sm:p-8 space-y-6">
                    <SpeakingRoleCard
                      role={roleCard.title}
                      setting={roleCard.setting}
                      patient={roleCard.patient}
                      task={roleCard.brief}
                      background={roleCard.background}
                      className="border-none bg-transparent shadow-none p-0"
                    />

                    {roleCard.tasks.length > 0 && (
                      <div className="mt-8 pt-6 border-t border-border">
                        <div className="flex items-center gap-2 mb-4">
                          <CheckCircle2 className="w-4 h-4 text-primary shrink-0" aria-hidden="true" />
                          <p className="text-xs font-black text-primary uppercase tracking-widest">Tasks to Complete</p>
                        </div>

                        <div className="grid gap-3">
                          {roleCard.tasks.map((t, i) => (
                            <div key={i} className="flex items-start gap-3 p-4 bg-background-light border border-border rounded-xl hover:border-border-hover hover:bg-primary/5 transition-colors">
                              <div className="w-6 h-6 rounded-full bg-primary text-white dark:bg-violet-700 flex items-center justify-center text-xs font-black shrink-0 shadow-sm mt-0.5">
                                {i + 1}
                              </div>
                              <span className="text-sm font-medium text-navy leading-relaxed">{t}</span>
                            </div>
                          ))}
                        </div>
                      </div>
                    )}
                  </div>
                </Card>

              <Card className="bg-surface border border-border shadow-sm rounded-2xl overflow-hidden focus-within:ring-2 focus-within:ring-primary/20 transition-[color,background-color,border-color,box-shadow,opacity] duration-200">
                <div className="bg-background-light px-5 py-3 border-b border-border flex items-center gap-2">
                  <Edit3 className="w-4 h-4 text-muted" aria-hidden="true" />
                  <h4 className="text-xs font-black text-muted uppercase tracking-widest">Your Notes</h4>
                </div>
                <textarea
                  className="w-full min-h-[120px] text-sm p-5 resize-none focus:outline-none text-navy bg-transparent"
                  placeholder="Jot down key points for your response... (These will be available during the recording)"
                />
              </Card>

              <div className="flex justify-center pt-4">
                <Button size="lg" onClick={handleStartRecording} className="gap-2 shadow-lg shadow-primary/20 hover:shadow-primary/30 px-10 py-6 rounded-full text-base font-black hoverable:-translate-y-0.5 transition-[color,background-color,border-color,box-shadow,transform,opacity] duration-200">
                  <Mic className="w-5 h-5 fill-white" aria-hidden="true" /> Start Recording
                </Button>
              </div>
            </div>
          )}

          {/* Phase: Recording */}
          {phase === 'recording' && roleCard && (
            <div className="space-y-5 sm:space-y-8 max-w-2xl mx-auto">
              <div className="text-center space-y-3 pb-4">
                <div className="inline-flex items-center gap-2.5 px-5 py-2.5 rounded-full bg-danger/10 border border-danger/20 text-danger text-sm font-black tracking-widest uppercase shadow-sm">
                  <span className="relative flex h-3 w-3">
                    <span className="absolute inline-flex h-full w-full rounded-full bg-danger opacity-75 motion-safe:animate-ping" />
                    <span className="relative inline-flex rounded-full h-3 w-3 bg-danger" />
                  </span>
                  Recording in Progress
                </div>
                <p className="text-sm font-medium text-muted">
                  Speak clearly. The role card is visible below for reference.
                </p>
              </div>

              {/* Compact role card during recording */}
              <Card className="bg-surface border border-border shadow-sm rounded-2xl overflow-hidden transition-[color,background-color,border-color,box-shadow,opacity] duration-200 hover:shadow-md">
                <div className="bg-primary/5 px-5 py-3 border-b border-primary/10">
                  <p className="text-[10px] font-black text-primary uppercase tracking-[0.2em]">Role Card Reference</p>
                </div>
                <div className="p-5 sm:p-6">
                  <p className="text-base text-navy font-black tracking-tight">{roleCard.title}</p>
                  <p className="text-sm font-medium text-muted mt-1.5 leading-relaxed">{roleCard.brief}</p>
                  <ul className="mt-5 grid gap-2.5">
                    {roleCard.tasks.map((t, i) => (
                      <li key={i} className="text-xs sm:text-sm font-medium text-navy flex items-start gap-3 bg-background-light p-3 rounded-xl border border-border">
                        <span className="w-5 h-5 flex items-center justify-center rounded-full bg-primary/10 text-primary font-black shrink-0 text-[10px] mt-0.5">{i + 1}</span>
                        <span className="leading-relaxed">{t}</span>
                      </li>
                    ))}
                  </ul>
                </div>
              </Card>

              {/* Recording timer & controls */}
              <div className="flex flex-col items-center gap-6 pt-6">
                <div className="text-6xl font-black text-navy tabular-nums tracking-tighter" role="timer" aria-label="Recording elapsed time">
                  {Math.floor(recordingSeconds / 60).toString().padStart(2, '0')}:
                  {(recordingSeconds % 60).toString().padStart(2, '0')}
                </div>
                <Button
                  variant="destructive"
                  size="lg"
                  onClick={() => setShowStopModal(true)}
                  className="gap-2.5 px-12 py-7 rounded-full text-base font-black shadow-lg shadow-danger/20 hover:shadow-danger/30 hoverable:-translate-y-0.5 transition-[color,background-color,border-color,box-shadow,transform,opacity] duration-200"
                >
                  <Square className="w-5 h-5 fill-white" aria-hidden="true" /> Stop Recording
                </Button>
              </div>
            </div>
          )}

          {/* Phase: Review */}
          {phase === 'review' && (
            <div className="space-y-5 sm:space-y-8 max-w-xl mx-auto pt-6">
              <div className="text-center pb-2">
                <div className="inline-flex items-center justify-center w-24 h-24 rounded-full bg-success/10 mb-6 ring-1 ring-success/20">
                  <CheckCircle2 className="w-12 h-12 text-success" aria-hidden="true" />
                </div>
                <h2 className="text-4xl font-black text-navy tracking-tight">Review Recording</h2>

                <div className="mt-4 flex justify-center">
                  <div className="inline-flex items-center gap-2.5 bg-surface px-5 py-2 rounded-full border border-border shadow-sm">
                    <div className="w-2 h-2 rounded-full bg-success motion-safe:animate-pulse" />
                    <span className="text-xs font-black uppercase tracking-widest text-muted">Duration</span>
                    <span className="text-sm text-navy font-bold">{Math.floor(recordingSeconds / 60)}:{(recordingSeconds % 60).toString().padStart(2, '0')}</span>
                  </div>
                </div>
              </div>

              <Card className="text-center p-6 sm:p-10 bg-surface shadow-md border border-border rounded-[2rem] transition-[color,background-color,border-color,box-shadow,opacity] duration-200 hover:border-border-hover">
                <Volume2 className="w-10 h-10 text-primary mx-auto mb-5" aria-hidden="true" />
                <h3 className="text-lg font-black text-navy mb-2 tracking-tight">Audio Quality Check</h3>
                <p className="text-sm font-medium text-muted leading-relaxed max-w-sm mx-auto mb-8 px-4">
                  Listen back to ensure your voice is clear, then submit your simulation for expert AI evaluation.
                </p>
                {previewUrl ? (
                  <div className="bg-background-light py-3 px-4 rounded-[1.5rem] border border-border">
                    <audio controls src={previewUrl} className="w-full h-12 outline-none focus:outline-none" />
                  </div>
                ) : (
                  <div className="h-[72px] bg-background-light rounded-[1.5rem] border border-border border-dashed flex items-center justify-center text-xs font-black text-muted tracking-widest uppercase gap-3">
                    <Loader2 className="w-4 h-4 animate-spin text-primary" aria-hidden="true" />
                    <span>Processing audio…</span>
                  </div>
                )}
              </Card>

              <div className="flex flex-col sm:flex-row gap-4 justify-center pt-4">
                <Button
                  variant="outline"
                  size="lg"
                  onClick={() => { setPhase('role-card'); setRecordingSeconds(0); }}
                  className="rounded-full px-8 py-7 font-black tracking-wide shadow-sm hover:bg-background-light transition-[color,background-color,border-color,box-shadow,transform,opacity] duration-200 text-sm group"
                >
                  <RotateCcw className="w-4 h-4 mr-2 text-muted group-hover:-rotate-45 transition-transform duration-300" aria-hidden="true" /> Re-record
                </Button>
                <Button
                  size="lg"
                  onClick={handleSubmit}
                  loading={submitting}
                  className="gap-2.5 rounded-full px-12 py-7 font-black shadow-lg shadow-primary/20 hover:shadow-primary/30 hoverable:-translate-y-0.5 transition-[color,background-color,border-color,box-shadow,transform,opacity] duration-200 text-base group"
                >
                  <Send className="w-5 h-5 -rotate-12 group-hover:rotate-0 transition-transform" aria-hidden="true" /> Submit for Evaluation
                </Button>
              </div>
            </div>
          )}

          {/* Phase: Uploading */}
          {phase === 'uploading' && (
            <div className="text-center space-y-6 sm:space-y-10 py-16 max-w-sm mx-auto">
              <div className="inline-flex items-center justify-center w-28 h-28 rounded-full bg-primary/5 border border-primary/20 relative">
                <div className="absolute inset-0 rounded-full border-2 border-primary/20 border-t-primary animate-spin" />
                <Upload className="w-10 h-10 text-primary motion-safe:animate-pulse" aria-hidden="true" />
              </div>

              <div className="space-y-4">
                <h2 className="text-3xl font-black text-navy tracking-tight">Uploading Recording</h2>
                <p className="text-sm font-medium text-muted leading-relaxed bg-background-light inline-block px-5 py-2 rounded-2xl border border-border">
                  Please don&apos;t close this page.<br/>This may take a few moments.
                </p>
              </div>

              <Card className="w-full bg-surface border border-border p-6 shadow-sm rounded-3xl space-y-4">
                <div
                  className="w-full h-3.5 bg-background-light rounded-full overflow-hidden border border-border"
                  role="progressbar"
                  aria-valuenow={uploadProgress}
                  aria-valuemin={0}
                  aria-valuemax={100}
                  aria-label="Upload progress"
                >
                  <div
                    className="h-full bg-primary transition-[width] duration-300"
                    style={{ width: `${uploadProgress}%` }}
                  />
                </div>
                <div className="flex justify-between items-center px-1">
                  <span className="text-[10px] font-black uppercase tracking-widest text-muted">Upload Status</span>
                  <span className="text-sm font-black text-primary tabular-nums tracking-tighter">{uploadProgress}%</span>
                </div>
              </Card>
            </div>
          )}

          {/* Phase: Done */}
          {phase === 'done' && (
            <div className="text-center space-y-5 sm:space-y-8 py-20">
              <div className="inline-flex items-center justify-center w-32 h-32 rounded-full bg-success/5 shadow-sm shadow-success/15 ring-1 ring-success/20 motion-safe:animate-[zoom-in_0.5s_ease-out]">
                <CheckCircle2 className="w-16 h-16 text-success" aria-hidden="true" />
              </div>

              <div className="space-y-4 max-w-sm mx-auto">
                <h2 className="text-4xl font-black text-navy tracking-tight leading-tight">Submitted <br/><span className="text-success">Successfully</span></h2>
                <div className="bg-surface border border-success/10 px-5 py-3 rounded-2xl shadow-sm">
                  <p className="text-sm font-bold text-muted uppercase tracking-widest">
                    Redirecting to diagnostic hub
                    <span className="inline-flex ml-2">
                      <span className="motion-safe:animate-pulse">.</span>
                      <span className="motion-safe:animate-pulse" style={{ animationDelay: '0.1s' }}>.</span>
                      <span className="motion-safe:animate-pulse" style={{ animationDelay: '0.2s' }}>.</span>
                    </span>
                  </p>
                </div>
              </div>
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
