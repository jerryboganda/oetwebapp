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
  Edit3,
  RotateCcw,
  Loader2,
  Volume2,
} from 'lucide-react';

type SpeakingPhase = 'mic-check' | 'role-card' | 'recording' | 'review' | 'uploading' | 'done';

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
  const recordedBlobRef = useRef<Blob | null>(null);
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

      if (!taskId) throw new Error('No diagnostic speaking task ID available.');
      await submitSpeakingRecording(
        taskId,
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

      track('task_submitted', { subTest: 'Speaking', mode: 'diagnostic', taskId });
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
            <div className="space-y-6 max-w-2xl mx-auto">
              <div className="text-center pb-2 relative">
                <div className="absolute inset-0 -z-10 bg-[radial-gradient(ellipse_at_center,_var(--tw-gradient-stops))] from-primary/10 via-transparent to-transparent opacity-80 blur-xl"></div>
                <h2 className="text-2xl font-black text-navy/90 tracking-tight">Microphone Setup</h2>
                <p className="text-sm text-muted mt-2 font-medium">
                  Complete the checks below before starting the speaking task.
                </p>
              </div>

              <div className="bg-primary/5 border border-primary/20 rounded-xl p-5 shadow-sm">
                <div className="flex items-start gap-3">
                  <div className="bg-primary/10 rounded-full p-2 shrink-0">
                    <ShieldCheck className="w-5 h-5 text-primary" />
                  </div>
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
            <div className="space-y-8 max-w-3xl mx-auto">
              <div className="text-center pb-2 relative">
                <div className="absolute inset-0 -z-10 bg-[radial-gradient(ellipse_at_center,_var(--tw-gradient-stops))] from-primary/10 via-transparent to-transparent opacity-80 blur-xl"></div>
                <h2 className="text-3xl font-black text-navy/90 tracking-tight">Role Card</h2>
                <p className="text-sm font-medium text-navy/70 mt-2">
                  Read the scenario below. You&apos;ll have the role card visible during recording.
                </p>
              </div>

              <div className="relative">
                <div className="absolute -inset-1 bg-gradient-to-r from-primary/10 via-warning/10 to-primary/10 rounded-[1.5rem] blur-md opacity-70"></div>
                <Card className="relative bg-white/95 backdrop-blur-xl border border-border/60 shadow-xl shadow-black/5 rounded-2xl overflow-hidden">
                  
                  {/* Premium Header Strip */}
                  <div className="flex items-center justify-between bg-gradient-to-r from-warning/10 to-amber-500/10 px-6 py-4 border-b border-warning/20">
                    <div className="flex items-center gap-2">
                      <div className="w-1.5 h-4 bg-warning rounded-full" />
                      <p className="text-xs font-black text-warning-dark uppercase tracking-widest">Preparation Time</p>
                    </div>
                    <div className="bg-white/80 px-4 py-1.5 rounded-full shadow-sm border border-warning/20">
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
                      <div className="mt-8 pt-6 border-t border-border/60">
                        <div className="flex items-center gap-2 mb-4">
                          <div className="p-1.5 rounded-md bg-primary/10">
                            <CheckCircle2 className="w-4 h-4 text-primary" />
                          </div>
                          <p className="text-xs font-black text-primary uppercase tracking-widest">Tasks to Complete</p>
                        </div>
                        
                        <div className="grid gap-3">
                          {roleCard.tasks.map((t, i) => (
                            <Card key={i} className="flex items-start gap-3 p-4 bg-background-light/30 border border-border/50 rounded-xl hover:border-primary/20 hover:bg-primary/5 transition-colors">
                              <div className="w-6 h-6 rounded-full bg-primary text-white flex items-center justify-center text-xs font-black shrink-0 shadow-sm mt-0.5">
                                {i + 1}
                              </div>
                              <span className="text-sm font-medium text-navy/90 leading-relaxed">{t}</span>
                            </Card>
                          ))}
                        </div>
                      </div>
                    )}
                  </div>
                </Card>
              </div>

              <Card className="bg-white border-border/60 shadow-lg shadow-black/[0.02] rounded-2xl overflow-hidden focus-within:ring-2 focus-within:ring-primary/20 transition-all">
                <div className="bg-background-light px-5 py-3 border-b border-border/60 flex items-center gap-2">
                  <Edit3 className="w-4 h-4 text-muted" />
                  <h4 className="text-xs font-black text-muted uppercase tracking-widest">Your Notes</h4>
                </div>
                <textarea
                  className="w-full min-h-[120px] text-sm p-5 resize-none focus:outline-none text-navy bg-transparent"
                  placeholder="Jot down key points for your response... (These will be available during the recording)"
                />
              </Card>

              <div className="flex justify-center pt-4">
                <Button size="lg" onClick={handleStartRecording} className="gap-2 shadow-[0_8px_30px_rgba(124,58,237,0.25)] hover:shadow-[0_8px_40px_rgba(124,58,237,0.35)] px-10 py-6 rounded-full text-base font-black hover:-translate-y-0.5 transition-all">
                  <Mic className="w-5 h-5 fill-white" /> Start Recording
                </Button>
              </div>
            </div>
          )}

          {/* Phase: Recording */}
          {phase === 'recording' && roleCard && (
            <div className="space-y-8 max-w-2xl mx-auto">
              <div className="text-center space-y-3 relative pb-4">
                <div className="absolute inset-0 -z-10 bg-[radial-gradient(ellipse_at_center,_var(--tw-gradient-stops))] from-danger/10 via-transparent to-transparent opacity-80 blur-xl"></div>
                
                <div className="inline-flex items-center gap-2.5 px-5 py-2.5 rounded-full bg-danger/10 border border-danger/20 text-danger text-sm font-black tracking-widest uppercase shadow-[0_4px_20px_rgba(239,68,68,0.1)]">
                  <span className="relative flex h-3 w-3">
                    <span className="absolute inline-flex h-full w-full rounded-full bg-danger opacity-75 animate-ping" />
                    <span className="relative inline-flex rounded-full h-3 w-3 bg-danger shadow-[0_0_8px_rgba(239,68,68,0.8)]" />
                  </span>
                  Recording in Progress
                </div>
                <p className="text-sm font-medium text-navy/70">
                  Speak clearly. The role card is visible below for reference.
                </p>
              </div>

              {/* Compact role card during recording */}
              <Card className="bg-white/90 backdrop-blur-md border border-primary/20 shadow-xl shadow-black/[0.03] rounded-2xl overflow-hidden transition-all hover:shadow-black/[0.05]">
                <div className="bg-gradient-to-r from-primary/5 to-transparent px-5 py-3 border-b border-primary/10">
                  <p className="text-[10px] font-black text-primary uppercase tracking-[0.2em]">Role Card Reference</p>
                </div>
                <div className="p-5 sm:p-6">
                  <p className="text-base text-navy font-black tracking-tight">{roleCard.title}</p>
                  <p className="text-sm font-medium text-navy/70 mt-1.5 leading-relaxed">{roleCard.brief}</p>
                  <ul className="mt-5 grid gap-2.5">
                    {roleCard.tasks.map((t, i) => (
                      <li key={i} className="text-xs sm:text-sm font-medium text-navy/90 flex items-start gap-3 bg-background-light/60 p-3 rounded-xl border border-border/50">
                        <span className="w-5 h-5 flex items-center justify-center rounded-full bg-primary/10 text-primary font-black shrink-0 text-[10px] mt-0.5">{i + 1}</span> 
                        <span className="leading-relaxed">{t}</span>
                      </li>
                    ))}
                  </ul>
                </div>
              </Card>

              {/* Recording timer & controls */}
              <div className="flex flex-col items-center gap-6 pt-6">
                <div className="text-6xl font-black text-navy tabular-nums tracking-tighter drop-shadow-sm">
                  {Math.floor(recordingSeconds / 60).toString().padStart(2, '0')}:
                  {(recordingSeconds % 60).toString().padStart(2, '0')}
                </div>
                <Button
                  variant="destructive"
                  size="lg"
                  onClick={() => setShowStopModal(true)}
                  className="gap-2.5 px-12 py-7 rounded-full text-base font-black shadow-[0_8px_30px_rgba(239,68,68,0.25)] hover:shadow-[0_8px_40px_rgba(239,68,68,0.35)] hover:-translate-y-0.5 transition-all"
                >
                  <Square className="w-5 h-5 fill-white" /> Stop Recording
                </Button>
              </div>
            </div>
          )}

          {/* Phase: Review */}
          {phase === 'review' && (
            <div className="space-y-8 max-w-xl mx-auto pt-6">
              <div className="text-center relative pb-2">
                <div className="absolute inset-0 -z-10 bg-[radial-gradient(circle_at_center,_var(--tw-gradient-stops))] from-success/20 via-transparent to-transparent opacity-80 blur-2xl"></div>
                <div className="inline-flex items-center justify-center w-24 h-24 rounded-full bg-success/10 mb-6 border-4 border-white shadow-[0_4px_30px_rgba(34,197,94,0.15)] ring-1 ring-success/20">
                  <CheckCircle2 className="w-12 h-12 text-success drop-shadow-sm" />
                </div>
                <h2 className="text-4xl font-black text-navy tracking-tight drop-shadow-sm">Review Recording</h2>
                
                <div className="mt-4 flex justify-center">
                  <div className="inline-flex items-center gap-2.5 bg-white/70 px-5 py-2 rounded-full border border-border/80 shadow-sm backdrop-blur-md">
                    <div className="w-2 h-2 rounded-full bg-success animate-pulse" />
                    <span className="text-xs font-black uppercase tracking-widest text-navy/70">Duration</span>
                    <span className="text-sm text-navy font-bold">{Math.floor(recordingSeconds / 60)}:{(recordingSeconds % 60).toString().padStart(2, '0')}</span>
                  </div>
                </div>
              </div>

              <Card className="text-center p-6 sm:p-10 bg-white/95 backdrop-blur-xl shadow-2xl shadow-black/[0.04] border border-primary/10 rounded-[2rem] relative z-10 transition-all hover:border-primary/20">
                <div className="absolute top-0 right-0 w-32 h-32 bg-primary/5 rounded-bl-[4rem] -z-10 blur-xl"></div>
                <div className="bg-gradient-to-br from-primary/10 to-primary/5 w-14 h-14 rounded-2xl flex items-center justify-center mx-auto mb-5 border border-primary/10 shadow-sm">
                  <Volume2 className="w-6 h-6 text-primary" />
                </div>
                <h3 className="text-lg font-black text-navy mb-2 tracking-tight">Audio Quality Check</h3>
                <p className="text-sm font-medium text-navy/60 leading-relaxed max-w-sm mx-auto mb-8 px-4">
                  Listen back to ensure your voice is clear, then submit your simulation for expert AI evaluation.
                </p>
                {previewUrl ? (
                  <div className="bg-background-light/50 py-3 px-4 rounded-[1.5rem] border border-border/60 shadow-inner">
                    <audio controls src={previewUrl} className="w-full h-12 outline-none focus:outline-none" />
                  </div>
                ) : (
                  <div className="h-[72px] bg-background-light/50 rounded-[1.5rem] border border-border/40 border-dashed flex items-center justify-center text-xs font-black text-muted tracking-widest uppercase gap-3 shadow-inner">
                    <Loader2 className="w-4 h-4 animate-spin text-primary" />
                    <span>Processing audio...</span>
                  </div>
                )}
              </Card>

              <div className="flex flex-col sm:flex-row gap-4 justify-center pt-4">
                <Button 
                  variant="outline" 
                  size="lg"
                  onClick={() => { setPhase('role-card'); setRecordingSeconds(0); }}
                  className="rounded-full px-8 py-7 font-black tracking-wide border-border/80 shadow-sm hover:bg-background-light transition-all text-sm group"
                >
                  <RotateCcw className="w-4 h-4 mr-2 text-muted group-hover:-rotate-45 transition-transform duration-300" /> Re-record
                </Button>
                <Button 
                  size="lg"
                  onClick={handleSubmit} 
                  loading={submitting} 
                  className="gap-2.5 rounded-full px-12 py-7 font-black shadow-[0_8px_30px_rgba(124,58,237,0.25)] hover:shadow-[0_8px_40px_rgba(124,58,237,0.35)] hover:-translate-y-0.5 transition-all text-base"
                >
                  <Send className="w-5 h-5 -rotate-12 group-hover:rotate-0 transition-transform" /> Submit for Evaluation
                </Button>
              </div>
            </div>
          )}

          {/* Phase: Uploading */}
          {phase === 'uploading' && (
            <div className="text-center space-y-10 py-16 max-w-sm mx-auto relative">
              <div className="absolute inset-0 -z-10 bg-[radial-gradient(circle_at_center,_var(--tw-gradient-stops))] from-primary/10 via-transparent to-transparent opacity-80 blur-2xl scale-150"></div>
              
              <div className="inline-flex items-center justify-center w-28 h-28 rounded-full bg-primary/5 border border-primary/20 shadow-[0_0_50px_rgba(124,58,237,0.15)] relative">
                <div className="absolute inset-0 rounded-full border-t-2 border-primary animate-spin" />
                <Upload className="w-10 h-10 text-primary animate-pulse" />
              </div>
              
              <div className="space-y-4">
                <h2 className="text-3xl font-black text-navy tracking-tight">Uploading Recording</h2>
                <p className="text-sm font-medium text-navy/70 leading-relaxed bg-white/50 inline-block px-5 py-2 rounded-2xl border border-border/50">
                  Please don&apos;t close this page.<br/>This may take a few moments.
                </p>
              </div>
              
              <Card className="w-full bg-white/90 backdrop-blur-xl p-6 border-border/80 shadow-lg shadow-black/[0.03] rounded-3xl space-y-4">
                <div className="w-full h-3.5 bg-background-light rounded-full overflow-hidden shadow-inner border border-border/50">
                  <div
                    className="h-full bg-gradient-to-r from-primary to-primary/80 transition-all duration-300 relative"
                    style={{ width: `${uploadProgress}%` }}
                  >
                    <div className="absolute top-0 right-0 bottom-0 left-0 bg-[linear-gradient(45deg,rgba(255,255,255,.15)_25%,transparent_25%,transparent_50%,rgba(255,255,255,.15)_50%,rgba(255,255,255,.15)_75%,transparent_75%,transparent)] bg-[length:2rem_2rem] animate-[progress_1s_linear_infinite]" />
                  </div>
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
            <div className="text-center space-y-8 py-20 relative">
              <div className="absolute inset-0 -z-10 bg-[radial-gradient(circle_at_center,_var(--tw-gradient-stops))] from-success/10 via-transparent to-transparent opacity-80 blur-2xl scale-125"></div>
              
              <div className="inline-flex items-center justify-center w-32 h-32 rounded-full bg-success/5 border-[4px] border-white shadow-xl shadow-success/15 ring-1 ring-success/20 animate-[zoom-in_0.5s_ease-out]">
                <CheckCircle2 className="w-16 h-16 text-success drop-shadow-md" />
              </div>
              
              <div className="space-y-4 max-w-sm mx-auto">
                <h2 className="text-4xl font-black text-navy tracking-tight leading-tight">Submitted <br/><span className="text-success">Successfully</span></h2>
                <div className="bg-white/80 border border-success/10 px-5 py-3 rounded-2xl shadow-sm backdrop-blur-md">
                  <p className="text-sm font-bold text-navy/70 uppercase tracking-widest">
                    Redirecting to diagnostic hub
                    <span className="inline-flex ml-2">
                      <span className="animate-bounce">.</span>
                      <span className="animate-bounce" style={{ animationDelay: '0.1s' }}>.</span>
                      <span className="animate-bounce" style={{ animationDelay: '0.2s' }}>.</span>
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
