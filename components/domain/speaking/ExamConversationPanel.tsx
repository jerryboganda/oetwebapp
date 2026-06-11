'use client';

/**
 * Speaking module rebuild (2026-06-11 spec).
 *
 * Embedded AI-patient conversation panel for the Active phase of one exam card.
 * Connects to ConversationHub keyed on the exam's current child SpeakingSession,
 * renders the live caption strip, and provides push-to-talk. The exam page owns
 * the countdown + phase machine; this panel is purely the conversation surface.
 *
 * Adapted from the proven flow in `app/speaking/sessions/[id]/page.tsx` so the
 * audio capture + hub contract stay identical.
 */
import { useCallback, useEffect, useRef, useState } from 'react';
import { Loader2, Mic } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';

interface PatientUtterance {
  speaker: 'patient' | 'interlocutor';
  phase: 'warmup' | 'roleplay';
  text: string;
  audioUrl?: string | null;
  emotionHint?: string | null;
  shouldEnd?: boolean;
}

interface LearnerCaption {
  speaker: 'candidate';
  text: string;
  confidence?: number;
}

interface ConversationHubBridge {
  start: (sessionId: string) => Promise<void>;
  stop: () => Promise<void>;
  sendTurn: (sessionId: string, audioBase64: string, mimeType: string) => Promise<void>;
  onUtterance: (cb: (u: PatientUtterance) => void) => void;
  onLearnerCaption: (cb: (c: LearnerCaption) => void) => void;
  onShouldEnd: (cb: () => void) => void;
  onError: (cb: (code: string, message: string) => void) => void;
}

async function loadConversationHub(): Promise<ConversationHubBridge | null> {
  try {
    const { HubConnectionBuilder, LogLevel } = await import('@microsoft/signalr');
    const { ensureFreshAccessToken } = await import('@/lib/auth-client');
    const connection = new HubConnectionBuilder()
      .withUrl('/api/backend/v1/conversations/hub', {
        accessTokenFactory: async () => (await ensureFreshAccessToken()) ?? '',
      })
      .configureLogging(LogLevel.None)
      .withAutomaticReconnect([0, 2_000, 5_000])
      .build();

    let utter: ((u: PatientUtterance) => void) | null = null;
    let learner: ((c: LearnerCaption) => void) | null = null;
    let shouldEnd: (() => void) | null = null;
    let error: ((code: string, message: string) => void) | null = null;

    connection.on('PatientUtterance', (p: PatientUtterance) => {
      if (p?.text) utter?.(p);
    });
    connection.on('LearnerCaption', (p: LearnerCaption) => {
      if (p?.text) learner?.(p);
    });
    connection.on('SpeakingShouldEnd', () => shouldEnd?.());
    connection.on('SpeakingRoleplayError', (code: string, message: string) => error?.(code, message));

    return {
      start: async (sessionId) => {
        await connection.start();
        await connection.invoke('StartSpeakingRoleplay', sessionId);
      },
      stop: async () => {
        try {
          await connection.stop();
        } catch {
          /* ignore */
        }
      },
      sendTurn: async (sessionId, audioBase64, mimeType) => {
        await connection.invoke('SendSpeakingRoleplayTurn', sessionId, audioBase64, mimeType);
      },
      onUtterance: (cb) => {
        utter = cb;
      },
      onLearnerCaption: (cb) => {
        learner = cb;
      },
      onShouldEnd: (cb) => {
        shouldEnd = cb;
      },
      onError: (cb) => {
        error = cb;
      },
    };
  } catch {
    return null;
  }
}

function resolveMediaUrl(url: string): string {
  if (/^https?:\/\//i.test(url)) return url;
  if (url.startsWith('/api/backend')) return url;
  if (url.startsWith('/')) return `/api/backend${url}`;
  return `/api/backend/${url}`;
}

function playUtteranceAudio(url: string): void {
  try {
    const audio = new Audio(resolveMediaUrl(url));
    void audio.play().catch(() => undefined);
  } catch {
    /* ignore */
  }
}

function pickRecorderMime(): string | undefined {
  if (typeof MediaRecorder === 'undefined') return undefined;
  return ['audio/webm;codecs=opus', 'audio/webm', 'audio/mp4', 'audio/ogg'].find((c) =>
    MediaRecorder.isTypeSupported(c),
  );
}

function blobToDataUrl(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onloadend = () => resolve(String(reader.result));
    reader.onerror = () => reject(new Error('Could not read audio.'));
    reader.readAsDataURL(blob);
  });
}

function dataUrlToBase64(dataUrl: string): string {
  const comma = dataUrl.indexOf(',');
  return comma >= 0 ? dataUrl.slice(comma + 1) : dataUrl;
}

export interface ExamConversationPanelProps {
  /** The child SpeakingSession id for the current card. */
  sessionId: string;
  className?: string;
}

export function ExamConversationPanel({ sessionId, className }: ExamConversationPanelProps) {
  const [hubReady, setHubReady] = useState(false);
  const [hubError, setHubError] = useState<string | null>(null);
  const [captions, setCaptions] = useState<Array<{ id: string; text: string; speaker: string }>>([]);
  const [recording, setRecording] = useState(false);
  const [sending, setSending] = useState(false);

  const hubRef = useRef<ConversationHubBridge | null>(null);
  const recorderRef = useRef<MediaRecorder | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const chunksRef = useRef<Blob[]>([]);

  useEffect(() => {
    if (!sessionId) return;
    let cancelled = false;
    loadConversationHub().then(async (hub) => {
      if (cancelled || !hub) {
        if (!hub) setHubError('Could not reach the AI patient. Please refresh.');
        return;
      }
      hubRef.current = hub;
      hub.onLearnerCaption((c) =>
        setCaptions((prev) =>
          [...prev, { id: `${prev.length}-c`, text: c.text, speaker: 'candidate' }].slice(-12),
        ),
      );
      hub.onUtterance((u) => {
        setCaptions((prev) =>
          [...prev, { id: `${prev.length}-p`, text: u.text, speaker: 'patient' }].slice(-12),
        );
        if (u.audioUrl) playUtteranceAudio(u.audioUrl);
      });
      hub.onError((_code, message) => setHubError(message));
      try {
        await hub.start(sessionId);
        if (!cancelled) setHubReady(true);
      } catch (err) {
        if (!cancelled) setHubError(err instanceof Error ? err.message : 'Could not start the AI patient.');
      }
    });
    return () => {
      cancelled = true;
      hubRef.current?.stop().catch(() => undefined);
      hubRef.current = null;
    };
  }, [sessionId]);

  // Stop any in-flight recorder on unmount.
  useEffect(() => {
    return () => {
      try {
        recorderRef.current?.stop();
      } catch {
        /* ignore */
      }
      streamRef.current?.getTracks().forEach((t) => t.stop());
      streamRef.current = null;
    };
  }, []);

  const toggleRecording = useCallback(async () => {
    if (!hubReady) return;
    const recorder = recorderRef.current;
    if (recording && recorder) {
      recorder.stop();
      return;
    }
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      streamRef.current = stream;
      const mimeType = pickRecorderMime();
      const rec = new MediaRecorder(stream, mimeType ? { mimeType } : undefined);
      chunksRef.current = [];
      rec.ondataavailable = (e) => {
        if (e.data.size > 0) chunksRef.current.push(e.data);
      };
      rec.onstop = async () => {
        setRecording(false);
        streamRef.current?.getTracks().forEach((t) => t.stop());
        streamRef.current = null;
        const chunks = chunksRef.current;
        chunksRef.current = [];
        if (chunks.length === 0) return;
        const blob = new Blob(chunks, { type: rec.mimeType || 'audio/webm' });
        if (blob.size === 0) return;
        setSending(true);
        try {
          const dataUrl = await blobToDataUrl(blob);
          await hubRef.current?.sendTurn(sessionId, dataUrlToBase64(dataUrl), blob.type || 'audio/webm');
        } catch (err) {
          setHubError(err instanceof Error ? err.message : 'Could not send your turn.');
        } finally {
          setSending(false);
        }
      };
      recorderRef.current = rec;
      rec.start();
      setRecording(true);
    } catch {
      setHubError('Microphone access is required to speak. Please enable it and try again.');
    }
  }, [hubReady, recording, sessionId]);

  return (
    <div className={cn('flex flex-col gap-3 rounded-xl border border-border bg-surface p-4', className)}>
      <div className="flex items-center justify-between">
        <span className="text-sm font-medium text-foreground">AI patient</span>
        <span
          className={cn(
            'inline-flex items-center gap-1.5 text-xs',
            hubReady ? 'text-emerald-600' : 'text-muted',
          )}
        >
          <span
            className={cn(
              'h-2 w-2 rounded-full',
              hubReady ? 'bg-emerald-500' : 'bg-amber-400',
            )}
          />
          {hubReady ? 'Connected' : 'Connecting…'}
        </span>
      </div>

      {hubError ? (
        <p className="rounded-md bg-rose-50 px-3 py-2 text-sm text-rose-700" role="alert">
          {hubError}
        </p>
      ) : null}

      <div
        className="max-h-56 min-h-[6rem] space-y-2 overflow-y-auto rounded-md bg-background/60 p-3"
        aria-live="polite"
      >
        {captions.length === 0 ? (
          <p className="text-sm text-muted">
            The patient will speak first. Tap the mic to respond.
          </p>
        ) : (
          captions.map((c) => (
            <p key={c.id} className="text-sm">
              <span
                className={cn(
                  'mr-2 font-semibold',
                  c.speaker === 'patient' ? 'text-sky-700' : 'text-emerald-700',
                )}
              >
                {c.speaker === 'patient' ? 'Patient' : 'You'}:
              </span>
              <span className="text-foreground">{c.text}</span>
            </p>
          ))
        )}
      </div>

      <Button
        type="button"
        onClick={toggleRecording}
        disabled={!hubReady || sending}
        variant={recording ? 'destructive' : 'primary'}
        className="w-full"
      >
        {sending ? (
          <>
            <Loader2 className="mr-2 h-4 w-4 animate-spin" /> Sending…
          </>
        ) : recording ? (
          <>
            <Mic className="mr-2 h-4 w-4 animate-pulse" /> Stop &amp; send
          </>
        ) : (
          <>
            <Mic className="mr-2 h-4 w-4" /> Hold to speak
          </>
        )}
      </Button>
    </div>
  );
}

export default ExamConversationPanel;
