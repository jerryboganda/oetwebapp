'use client';

import { useEffect, useRef, useState, useCallback } from 'react';
import { AlertCircle, Loader2 } from 'lucide-react';
import { useParams, useRouter } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { MotionSection } from '@/components/ui/motion-primitives';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { completeConversation, resumeConversation } from '@/lib/api';
import { resolveApiMediaUrl } from '@/lib/media-url';
import {
  ConversationPrepCard,
  ConversationChatView,
  ConversationMicControl,
  ConversationTimerBar,
  type ChatTurn,
} from '@/components/domain/conversation';
import type {
  ConversationState,
  ConversationScenario,
  ConversationAiMeta,
  ConversationTranscriptMeta,
  ConversationTurnState,
  ConversationResumeResponse,
  ConversationSessionTurn,
  PartialTranscriptDraft,
  RealtimeSttMode,
  SpeakingSessionConnectionState,
} from '@/lib/types/conversation';

const PREP_DURATION_DEFAULT = 120;
const DEFAULT_TIME_LIMIT = 300;
const MAX_TURN_MS = 60_000;
const REALTIME_CHUNK_TIMESLICE_MS = 1000;
const DEFAULT_REALTIME_MAX_CHUNK_BYTES = 256 * 1024;
const DEFAULT_AUDIO_CONSENT_VERSION = 'realtime-stt-v1-2026-05-14';

type RecorderFormat = { recorderMimeType: string; apiMimeType: string };
type RealtimeStartResult = 'realtime' | 'fallback' | 'denied';
type RealtimeCommitResult = { status?: 'committed' | 'already-committed' | 'failed' | 'denied'; streamId?: string };

function pickRecorderFormat(): RecorderFormat {
  const candidates: RecorderFormat[] = [
    { recorderMimeType: 'audio/webm;codecs=opus', apiMimeType: 'audio/webm' },
    { recorderMimeType: 'audio/webm', apiMimeType: 'audio/webm' },
    { recorderMimeType: 'audio/mp4', apiMimeType: 'audio/mp4' },
    { recorderMimeType: 'audio/ogg;codecs=opus', apiMimeType: 'audio/ogg' },
  ];
  const isSupported = typeof MediaRecorder !== 'undefined' && typeof MediaRecorder.isTypeSupported === 'function'
    ? MediaRecorder.isTypeSupported.bind(MediaRecorder)
    : () => false;
  return candidates.find((candidate) => isSupported(candidate.recorderMimeType)) ?? { recorderMimeType: '', apiMimeType: 'audio/webm' };
}

function makeStreamId(sessionId: string) {
  const suffix = typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID().replaceAll('-', '').slice(0, 24)
    : `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 10)}`;
  return `rt-${sessionId.replace(/[^a-z0-9]/gi, '').slice(0, 24)}-${suffix}`.slice(0, 96);
}

function blobToBase64Payload(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onerror = () => reject(new Error('AUDIO_READ_FAILED'));
    reader.onloadend = () => {
      const result = typeof reader.result === 'string' ? reader.result : '';
      resolve(result.includes(',') ? result.split(',')[1] : result);
    };
    reader.readAsDataURL(blob);
  });
}

function readableFallbackReason(reason: string) {
  const normalized = reason.toLowerCase();
  if (normalized.includes('not_configured') || normalized.includes('unavailable')) {
    return 'Live captions are unavailable. Record one answer, then stop to transcribe.';
  }
  if (normalized.includes('disabled') || normalized.includes('gated')) {
    return 'Using normal recording for this practice session.';
  }
  if (normalized.includes('chunk') || normalized.includes('size')) {
    return 'Live captions paused for this turn because one audio chunk was too large.';
  }
  return reason.replaceAll('_', ' ');
}

function stopStream(stream: MediaStream | null) {
  stream?.getTracks().forEach((track) => track.stop());
}

function normalizeTurns(turns?: ConversationSessionTurn[]): ChatTurn[] {
  if (!Array.isArray(turns)) return [];
  return turns
    .filter((turn) => typeof turn.content === 'string' && typeof turn.turnNumber === 'number')
    .map((turn) => ({
      turnNumber: turn.turnNumber,
      role: turn.role,
      content: turn.content,
      timestamp: turn.createdAt ? new Date(turn.createdAt).getTime() : Date.now(),
      audioUrl: turn.audioUrl ?? null,
    }));
}

export default function ConversationSessionPage() {
  const params = useParams<{ sessionId: string }>();
  const router = useRouter();
  const sessionId = params?.sessionId as string;

  const [scenario, setScenario] = useState<ConversationScenario | null>(null);
  const [state, setState] = useState<ConversationState>('preparing');
  const [turns, setTurns] = useState<ChatTurn[]>([]);
  const [loading, setLoading] = useState(true);
  const [recording, setRecording] = useState(false);
  const [aiThinking, setAiThinking] = useState(false);
  const [aiSpeakingTurn, setAiSpeakingTurn] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [elapsed, setElapsed] = useState(0);
  const [prepCountdown, setPrepCountdown] = useState(PREP_DURATION_DEFAULT);
  const [ending, setEnding] = useState(false);
  const [recordingConsentAccepted, setRecordingConsentAccepted] = useState(false);
  const [vendorConsentAccepted, setVendorConsentAccepted] = useState(false);
  const [partialTranscript, setPartialTranscript] = useState<PartialTranscriptDraft | null>(null);
  const [connectionState, setConnectionState] = useState<SpeakingSessionConnectionState>('idle');
  const [sttMode, setSttMode] = useState<RealtimeSttMode>('batch-fallback');
  const [fallbackReason, setFallbackReason] = useState<string | null>('Realtime STT is gated; using normal recording.');
  const [consentVersion, setConsentVersion] = useState(DEFAULT_AUDIO_CONSENT_VERSION);
  const [audioRetentionDays, setAudioRetentionDays] = useState(30);
  const [hubReady, setHubReady] = useState(false);

  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const prepTimerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const hubRef = useRef<import('@microsoft/signalr').HubConnection | null>(null);
  const mediaRecorderRef = useRef<MediaRecorder | null>(null);
  const activeMediaStreamRef = useRef<MediaStream | null>(null);
  const audioChunksRef = useRef<Blob[]>([]);
  const currentAudioRef = useRef<HTMLAudioElement | null>(null);
  const currentStreamIdRef = useRef<string | null>(null);
  const realtimeTurnActiveRef = useRef(false);
  const realtimeMaxChunkBytesRef = useRef(DEFAULT_REALTIME_MAX_CHUNK_BYTES);
  const chunkSequenceRef = useRef(0);
  const chunkSendQueueRef = useRef<Promise<void>>(Promise.resolve());
  const discardCurrentRecordingRef = useRef(false);
  const recordingStartedAtRef = useRef(0);
  const sttModeRef = useRef<RealtimeSttMode>('batch-fallback');
  const hasStartedRef = useRef(false);
  const resumeTokenRef = useRef<string | null>(null);
  const prepConsentsAccepted = recordingConsentAccepted && vendorConsentAccepted;

  useEffect(() => {
    sttModeRef.current = sttMode;
  }, [sttMode]);

  useEffect(() => {
    if (!sessionId) return;
    let cancelled = false;
    resumeConversation(sessionId, resumeTokenRef.current ?? undefined)
      .then((data: ConversationResumeResponse | Record<string, unknown>) => {
        if (cancelled) return;
        const payload = 'session' in data && data.session ? data.session as Record<string, unknown> : data as Record<string, unknown>;
        if ('resumeToken' in data && typeof data.resumeToken === 'string') {
          resumeTokenRef.current = data.resumeToken;
        }
        try {
          const scenarioData =
            typeof payload.scenarioJson === 'string' && payload.scenarioJson.length > 0
              ? (JSON.parse(payload.scenarioJson as string) as ConversationScenario)
              : null;
          setScenario(scenarioData);
        } catch { /* noop */ }
        const hydratedTurns = normalizeTurns(
          (Array.isArray((data as ConversationResumeResponse).turns) ? (data as ConversationResumeResponse).turns : payload.turns) as ConversationSessionTurn[] | undefined,
        );
        if (hydratedTurns.length > 0) setTurns(hydratedTurns);
        const s = (payload.state as ConversationState) ?? 'preparing';
        if (typeof payload.requiredAudioConsentVersion === 'string') setConsentVersion(payload.requiredAudioConsentVersion);
        if (typeof payload.audioRetentionDays === 'number') setAudioRetentionDays(payload.audioRetentionDays);
        if (payload.realtimeSttEnabled === true && payload.realtimeAsrProvider === 'mock') {
          setFallbackReason('Mock live captions are available for internal testing.');
        }
        setState(s);
        if (('resumeAllowed' in data && data.resumeAllowed === false && typeof data.redirectTo === 'string') ||
            s === 'evaluated' || s === 'completed' || s === 'evaluating') {
          router.replace(`/conversation/${sessionId}/results`);
        }
      })
      .catch(() => !cancelled && setError('Failed to load conversation session.'))
      .finally(() => !cancelled && setLoading(false));
    return () => { cancelled = true; };
  }, [sessionId, router]);

  useEffect(() => {
    if (state !== 'preparing') return;
    prepTimerRef.current = setInterval(() => {
      setPrepCountdown((prev) => {
        if (prev <= 1) {
          if (prepConsentsAccepted) handleStartConversation();
          return 0;
        }
        return prev - 1;
      });
    }, 1000);
    return () => { if (prepTimerRef.current) clearInterval(prepTimerRef.current); };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [state]);

  useEffect(() => {
    if (state !== 'active') return;
    timerRef.current = setInterval(() => setElapsed((e) => e + 1), 1000);
    return () => { if (timerRef.current) clearInterval(timerRef.current); };
  }, [state]);

  useEffect(() => {
    return () => {
      if (prepTimerRef.current) clearInterval(prepTimerRef.current);
      if (timerRef.current) clearInterval(timerRef.current);
      try { hubRef.current?.stop(); } catch { /* noop */ }
      hubRef.current = null;
      setHubReady(false);
      try { mediaRecorderRef.current?.stream?.getTracks().forEach((t) => t.stop()); } catch { /* noop */ }
      try { currentAudioRef.current?.pause(); } catch { /* noop */ }
      currentAudioRef.current = null;
    };
  }, []);

  function playAiAudio(turnNumber: number, audioUrl: string | null | undefined) {
    if (!audioUrl) return;
    const resolved = resolveApiMediaUrl(audioUrl);
    if (!resolved) return;
    try { currentAudioRef.current?.pause(); } catch { /* noop */ }
    const audio = new Audio(resolved);
    audio.preload = 'auto';
    currentAudioRef.current = audio;
    audio.onplay = () => { setAiSpeakingTurn(turnNumber); setConnectionState('ai-speaking'); };
    audio.onended = () => { setAiSpeakingTurn((t) => (t === turnNumber ? null : t)); setConnectionState('live'); };
    audio.onerror = () => { setAiSpeakingTurn((t) => (t === turnNumber ? null : t)); setConnectionState('live'); };
    audio.play().catch(() => { /* ignore autoplay rejections */ });
  }

  const handleStartConversation = useCallback(async () => {
    if (hasStartedRef.current) return;
    hasStartedRef.current = true;
    if (prepTimerRef.current) clearInterval(prepTimerRef.current);
    setHubReady(false);
    setConnectionState('connecting');
    analytics.track('conversation_active', { sessionId });

    try {
      const signalR = await import('@microsoft/signalr');
      const connection = new signalR.HubConnectionBuilder()
        .withUrl(`${process.env.NEXT_PUBLIC_API_BASE_URL || ''}/v1/conversations/hub`, {
          accessTokenFactory: async () => {
            const { ensureFreshAccessToken } = await import('@/lib/auth-client');
            return (await ensureFreshAccessToken()) || '';
          },
        })
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .build();

      connection.onreconnecting(() => { setHubReady(false); setConnectionState('reconnecting'); });
      connection.onreconnected(() => { setHubReady(true); setConnectionState(sttModeRef.current === 'realtime' ? 'live' : 'fallback'); });
      connection.onclose(() => { setHubReady(false); setConnectionState('offline'); });

      connection.on('ReceiveTranscript', (turnNumber: number, text: string, _confidence?: number, meta?: ConversationTranscriptMeta) => {
        setPartialTranscript(null);
        setTurns((prev) => [...prev, { turnNumber, role: 'learner', content: text, timestamp: Date.now(), audioUrl: meta?.audioUrl ?? null }]);
        setRecording(false);
        setAiThinking(true);
        setConnectionState('ai-thinking');
      });

      connection.on('ReceiveAIResponse', (turnNumber: number, text: string, meta?: ConversationAiMeta) => {
        setAiThinking(false);
        setConnectionState(sttModeRef.current === 'realtime' ? 'live' : 'fallback');
        setTurns((prev) => [...prev, { turnNumber, role: 'ai', content: text, timestamp: Date.now(), audioUrl: meta?.audioUrl ?? null, appliedRuleIds: meta?.appliedRuleIds ?? [] }]);
        if (meta?.audioUrl) playAiAudio(turnNumber, meta.audioUrl);
      });

      connection.on('RealtimeTranscriptPartial', (turnClientId: string, text: string, confidence?: number | null) => {
        if (currentStreamIdRef.current && turnClientId !== currentStreamIdRef.current) return;
        setPartialTranscript({ turnClientId, text, confidence, receivedAt: Date.now() });
        setConnectionState('listening');
      });

      connection.on('RealtimeSttStarted', (streamId: string, _providerName?: string, maxChunkBytes?: number) => {
        if (currentStreamIdRef.current && streamId !== currentStreamIdRef.current) return;
        realtimeTurnActiveRef.current = true;
        realtimeMaxChunkBytesRef.current = typeof maxChunkBytes === 'number' && maxChunkBytes > 0
          ? maxChunkBytes
          : DEFAULT_REALTIME_MAX_CHUNK_BYTES;
        setSttMode('realtime');
        setFallbackReason(null);
        setConnectionState('live');
      });

      connection.on('RealtimeSttFallback', (streamId: string, reason: string) => {
        if (currentStreamIdRef.current && streamId !== currentStreamIdRef.current) return;
        realtimeTurnActiveRef.current = false;
        setSttMode('batch-fallback');
        setFallbackReason(readableFallbackReason(reason));
        setConnectionState('fallback');
      });

      connection.on('RealtimeSttStopped', (streamId?: string) => {
        if (currentStreamIdRef.current && streamId && streamId !== currentStreamIdRef.current) return;
        setPartialTranscript(null);
        setConnectionState('transcribing');
      });

      connection.on('SessionStateChanged', (newState: ConversationState) => {
        setState(newState);
        if (newState === 'evaluating' || newState === 'evaluated') {
          router.push(`/conversation/${sessionId}/results`);
        }
      });

      connection.on('SessionShouldEnd', () => {
        setError('The AI partner has signalled the end of this scenario. Tap End conversation to grade.');
      });

      connection.on('ConversationError', (_code: string, message: string) => {
        realtimeTurnActiveRef.current = false;
        discardCurrentRecordingRef.current = true;
        const recorder = mediaRecorderRef.current;
        if (recorder?.state === 'recording') {
          try { recorder.stop(); } catch { /* noop */ }
        } else {
          stopStream(activeMediaStreamRef.current);
          activeMediaStreamRef.current = null;
          setRecording(false);
        }
        setError(message); setAiThinking(false); setConnectionState('error');
      });

      await connection.start();
      setConnectionState(sttModeRef.current === 'realtime' ? 'live' : 'fallback');
      hubRef.current = connection;
      const consentAccepted = await connection.invoke<boolean>('AcknowledgeAudioConsent', sessionId, consentVersion);
      if (!consentAccepted) {
        setError('Please refresh and accept the latest recording consent before starting.');
        await connection.stop();
        hubRef.current = null;
        setHubReady(false);
        hasStartedRef.current = false;
        return;
      }
      await connection.invoke('StartSession', sessionId);
      setHubReady(true);
      setState('active');
    } catch (err) {
      console.error('SignalR error:', err);
      hasStartedRef.current = false;
      setHubReady(false);
      setConnectionState('error');
      setError('Could not connect to the conversation server. Please refresh and try again.');
    }
  }, [consentVersion, sessionId, router]);

  const handleRecord = useCallback(async () => {
    if (recording) {
      try { mediaRecorderRef.current?.stop(); } catch { /* noop */ }
      return;
    }
    if (!hubRef.current || !hubReady) {
      setError('Please reconnect before recording another turn.');
      setConnectionState('reconnecting');
      return;
    }
    setRecording(true); setError(null); setConnectionState('listening'); setPartialTranscript(null);
    audioChunksRef.current = [];
    discardCurrentRecordingRef.current = false;
    chunkSequenceRef.current = 0;
    chunkSendQueueRef.current = Promise.resolve();
    currentStreamIdRef.current = makeStreamId(sessionId);
    realtimeTurnActiveRef.current = Boolean(hubRef.current);
    realtimeMaxChunkBytesRef.current = DEFAULT_REALTIME_MAX_CHUNK_BYTES;
    try {
      const format = pickRecorderFormat();
      if (hubRef.current && currentStreamIdRef.current) {
        try {
          const startResult = await hubRef.current.invoke<RealtimeStartResult>('BeginRealtimeTurn', sessionId, currentStreamIdRef.current, format.apiMimeType, 'en-GB');
          if (startResult === 'denied') {
            realtimeTurnActiveRef.current = false;
            currentStreamIdRef.current = null;
            setRecording(false);
            return;
          }
          realtimeTurnActiveRef.current = startResult === 'realtime';
        } catch {
          realtimeTurnActiveRef.current = false;
          currentStreamIdRef.current = null;
          setError('Could not prepare audio capture. Please reconnect before recording.');
          setRecording(false);
          setConnectionState('reconnecting');
          return;
        }
      }
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: {
          echoCancellation: true,
          noiseSuppression: true,
          autoGainControl: true,
          channelCount: { ideal: 1 },
        },
      });
      activeMediaStreamRef.current = stream;
      const mediaRecorder = format.recorderMimeType
        ? new MediaRecorder(stream, { mimeType: format.recorderMimeType })
        : new MediaRecorder(stream);
      mediaRecorderRef.current = mediaRecorder;
      mediaRecorder.ondataavailable = (e) => {
        if (e.data.size <= 0) return;
        audioChunksRef.current.push(e.data);
        if (!realtimeTurnActiveRef.current || !hubRef.current || !currentStreamIdRef.current) return;
        if (e.data.size > realtimeMaxChunkBytesRef.current) {
          realtimeTurnActiveRef.current = false;
          setSttMode('batch-fallback');
          setFallbackReason('Live captions paused because an audio chunk was too large. Your answer will still transcribe normally.');
          hubRef.current.invoke('CancelRealtimeTurn', sessionId, currentStreamIdRef.current).catch(() => undefined);
          return;
        }
        const sequence = ++chunkSequenceRef.current;
        const streamId = currentStreamIdRef.current;
        chunkSendQueueRef.current = chunkSendQueueRef.current
          .then(async () => {
            if (!realtimeTurnActiveRef.current || !hubRef.current) return;
            const payload = await blobToBase64Payload(e.data);
            const offsetMs = Math.max(0, Date.now() - recordingStartedAtRef.current);
            await hubRef.current.invoke('SendRealtimeAudioChunk', sessionId, streamId, sequence, payload, offsetMs);
          })
          .catch(async () => {
            realtimeTurnActiveRef.current = false;
            setSttMode('batch-fallback');
            setFallbackReason('Live captions stopped. Your answer will still transcribe after you stop recording.');
            try { await hubRef.current?.invoke('CancelRealtimeTurn', sessionId, streamId); } catch { /* noop */ }
          });
      };
      mediaRecorder.onstop = async () => {
        setConnectionState('transcribing');
        stopStream(stream);
        if (activeMediaStreamRef.current === stream) activeMediaStreamRef.current = null;
        if (discardCurrentRecordingRef.current) {
          setRecording(false);
          setPartialTranscript(null);
          currentStreamIdRef.current = null;
          realtimeTurnActiveRef.current = false;
          setConnectionState('error');
          return;
        }
        await chunkSendQueueRef.current;
        const streamId = currentStreamIdRef.current;
        if (hubRef.current && streamId && realtimeTurnActiveRef.current) {
          try {
            const result = await hubRef.current.invoke<RealtimeCommitResult>('CompleteRealtimeTurn', sessionId, streamId);
            if (result.status !== 'committed' && result.status !== 'already-committed') {
              setError('Live transcription could not be committed. Please try that turn again.');
              setRecording(false); setAiThinking(false); setConnectionState('error');
              return;
            }
            setConnectionState('ai-thinking');
            return;
          } catch {
            setError('We could not confirm whether your last turn was saved. Refresh the session before recording again.');
            setRecording(false); setAiThinking(false); setConnectionState('reconnecting');
            return;
          }
        }
        const blob = new Blob(audioChunksRef.current, { type: format.apiMimeType });
        const base64 = await blobToBase64Payload(blob);
        if (hubRef.current && base64) {
          try { await hubRef.current.invoke('SendAudio', sessionId, base64, format.apiMimeType); setConnectionState('ai-thinking'); }
          catch { setError('Failed to send audio. Please try again.'); setRecording(false); setAiThinking(false); setConnectionState('error'); }
        }
      };
      recordingStartedAtRef.current = Date.now();
      mediaRecorder.start(REALTIME_CHUNK_TIMESLICE_MS);
      setTimeout(() => { if (mediaRecorder.state === 'recording') mediaRecorder.stop(); }, MAX_TURN_MS);
    } catch {
      const streamId = currentStreamIdRef.current;
      if (hubRef.current && streamId && realtimeTurnActiveRef.current) {
        try { await hubRef.current.invoke('CancelRealtimeTurn', sessionId, streamId); } catch { /* noop */ }
      }
      currentStreamIdRef.current = null;
      realtimeTurnActiveRef.current = false;
      chunkSendQueueRef.current = Promise.resolve();
      audioChunksRef.current = [];
      stopStream(activeMediaStreamRef.current);
      activeMediaStreamRef.current = null;
      setError('Microphone access denied. Please enable microphone permissions.'); setRecording(false); setConnectionState('error');
    }
  }, [hubReady, recording, sessionId]);

  const handleEnd = useCallback(async () => {
    if (recording || aiThinking || connectionState === 'transcribing') {
      setError('Please finish the current turn before ending the conversation.');
      return;
    }
    setEnding(true);
    if (timerRef.current) clearInterval(timerRef.current);
    try { mediaRecorderRef.current?.stop(); } catch { /* noop */ }
    try {
      if (hubRef.current) {
        try { await hubRef.current.invoke('EndSession', sessionId); }
        finally { await hubRef.current.stop(); hubRef.current = null; }
      } else {
        await completeConversation(sessionId);
      }
      analytics.track('conversation_ended', { sessionId, turns: turns.length, elapsed });
      router.push(`/conversation/${sessionId}/results`);
    } catch {
      setError('Failed to end session. Please try again.'); setEnding(false);
    }
  }, [recording, aiThinking, connectionState, sessionId, turns.length, elapsed, router]);

  const timeLimit = scenario?.timeLimitSeconds ?? scenario?.timeLimit ?? DEFAULT_TIME_LIMIT;
  const micDisabled = !hubReady || aiThinking || ending || aiSpeakingTurn !== null || connectionState === 'connecting' || connectionState === 'reconnecting' || connectionState === 'offline' || connectionState === 'error' || connectionState === 'transcribing';
  const canEndSession = hubReady && turns.length >= 2 && !recording && !aiThinking && connectionState !== 'transcribing';
  const turnState: ConversationTurnState = recording
    ? 'listening'
    : connectionState === 'transcribing'
      ? 'sending'
    : aiThinking
      ? 'ai-thinking'
      : aiSpeakingTurn !== null
        ? 'ai-speaking'
        : connectionState === 'reconnecting'
          ? 'reconnecting'
          : connectionState === 'error'
            ? 'error'
            : sttMode === 'batch-fallback'
              ? 'fallback'
              : 'ready';
  const micDisabledReason = aiSpeakingTurn !== null
    ? 'Mic is locked while the AI partner is speaking.'
    : !hubReady
      ? 'Connect to the conversation server before recording.'
    : connectionState === 'reconnecting'
      ? 'Reconnecting before accepting another turn.'
      : connectionState === 'transcribing'
        ? 'Submitting and transcribing your last answer.'
      : null;

  if (loading) {
    return (
      <LearnerDashboardShell>
        <div className="max-w-4xl mx-auto space-y-4">
          <Skeleton className="h-40 rounded-2xl" />
          <Skeleton className="h-[340px] rounded-2xl" />
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell>
      <div className="max-w-4xl mx-auto">
        {error && (<InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>)}

        {state === 'preparing' && scenario && (
          <MotionSection>
            <ConversationPrepCard
              scenario={scenario}
              prepCountdown={prepCountdown}
              recordingConsentAccepted={recordingConsentAccepted}
              vendorConsentAccepted={vendorConsentAccepted}
              consentVersion={consentVersion}
              audioRetentionDays={audioRetentionDays}
              startDisabled={!prepConsentsAccepted}
              onConsentChange={(key, accepted) => {
                if (key === 'recording') setRecordingConsentAccepted(accepted);
                else setVendorConsentAccepted(accepted);
              }}
              onStart={handleStartConversation}
            />
          </MotionSection>
        )}

        {state === 'active' && (
          <div className="flex flex-col h-[calc(100dvh-200px)]">
            <ConversationTimerBar elapsed={elapsed} timeLimit={timeLimit} turns={turns.length} scenarioTitle={scenario?.title}
              connectionState={connectionState} sttMode={sttMode} fallbackReason={fallbackReason} />
            <ConversationChatView turns={turns} aiThinking={aiThinking} aiSpeakingTurn={aiSpeakingTurn}
              partialTranscript={partialTranscript} turnState={turnState}
              onReplay={(turn) => playAiAudio(turn.turnNumber, turn.audioUrl)} />
            <ConversationMicControl recording={recording} disabled={micDisabled} ending={ending}
              turnState={turnState} disabledReason={micDisabledReason} micLevel={recording ? 0.45 : 0}
              canEnd={canEndSession} onRecord={handleRecord} onEnd={handleEnd} />
          </div>
        )}

        {state === 'evaluating' && (
          <MotionSection className="text-center py-16">
            <Loader2 className="w-12 h-12 text-primary mx-auto mb-4 animate-spin" />
            <h2 className="text-xl font-bold text-navy mb-2">Evaluating your conversation</h2>
            <p className="text-muted">Our AI is analysing your performance. This usually takes a few seconds.</p>
          </MotionSection>
        )}

        {!scenario && !loading && (
          <div className="text-center py-16">
            <AlertCircle className="w-12 h-12 text-muted/40 mx-auto mb-4" />
            <p className="text-muted">Could not load conversation scenario.</p>
          </div>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
