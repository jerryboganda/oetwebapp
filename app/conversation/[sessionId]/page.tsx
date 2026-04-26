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
  ConversationResumeResponse,
  ConversationSessionTurn,
} from '@/lib/types/conversation';

const PREP_DURATION_DEFAULT = 120;
const DEFAULT_TIME_LIMIT = 300;
const MAX_TURN_MS = 60_000;

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

  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const prepTimerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const hubRef = useRef<import('@microsoft/signalr').HubConnection | null>(null);
  const mediaRecorderRef = useRef<MediaRecorder | null>(null);
  const audioChunksRef = useRef<Blob[]>([]);
  const currentAudioRef = useRef<HTMLAudioElement | null>(null);
  const hasStartedRef = useRef(false);
  const resumeTokenRef = useRef<string | null>(null);

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
        if (prev <= 1) { handleStartConversation(); return 0; }
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
    audio.onplay = () => setAiSpeakingTurn(turnNumber);
    audio.onended = () => setAiSpeakingTurn((t) => (t === turnNumber ? null : t));
    audio.onerror = () => setAiSpeakingTurn((t) => (t === turnNumber ? null : t));
    audio.play().catch(() => { /* ignore autoplay rejections */ });
  }

  const handleStartConversation = useCallback(async () => {
    if (hasStartedRef.current) return;
    hasStartedRef.current = true;
    if (prepTimerRef.current) clearInterval(prepTimerRef.current);
    setState('active');
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

      connection.on('ReceiveTranscript', (turnNumber: number, text: string, _confidence?: number, meta?: ConversationTranscriptMeta) => {
        setTurns((prev) => [...prev, { turnNumber, role: 'learner', content: text, timestamp: Date.now(), audioUrl: meta?.audioUrl ?? null }]);
        setRecording(false);
        setAiThinking(true);
      });

      connection.on('ReceiveAIResponse', (turnNumber: number, text: string, meta?: ConversationAiMeta) => {
        setAiThinking(false);
        setTurns((prev) => [...prev, { turnNumber, role: 'ai', content: text, timestamp: Date.now(), audioUrl: meta?.audioUrl ?? null, appliedRuleIds: meta?.appliedRuleIds ?? [] }]);
        if (meta?.audioUrl) playAiAudio(turnNumber, meta.audioUrl);
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
        setError(message); setRecording(false); setAiThinking(false);
      });

      await connection.start();
      hubRef.current = connection;
      await connection.invoke('StartSession', sessionId);
    } catch (err) {
      console.error('SignalR error:', err);
      hasStartedRef.current = false;
      setError('Could not connect to the conversation server. Please refresh and try again.');
    }
  }, [sessionId, router]);

  const handleRecord = useCallback(async () => {
    if (recording) {
      try { mediaRecorderRef.current?.stop(); } catch { /* noop */ }
      return;
    }
    setRecording(true); setError(null);
    audioChunksRef.current = [];
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      const mimeType = MediaRecorder.isTypeSupported('audio/webm;codecs=opus')
        ? 'audio/webm;codecs=opus' : 'audio/webm';
      const mediaRecorder = new MediaRecorder(stream, { mimeType });
      mediaRecorderRef.current = mediaRecorder;
      mediaRecorder.ondataavailable = (e) => { if (e.data.size > 0) audioChunksRef.current.push(e.data); };
      mediaRecorder.onstop = async () => {
        stream.getTracks().forEach((t) => t.stop());
        const blob = new Blob(audioChunksRef.current, { type: 'audio/webm' });
        const reader = new FileReader();
        reader.onloadend = async () => {
          const result = reader.result as string;
          const base64 = result.includes(',') ? result.split(',')[1] : result;
          if (hubRef.current && base64) {
            try { await hubRef.current.invoke('SendAudio', sessionId, base64, 'audio/webm'); }
            catch { setError('Failed to send audio. Please try again.'); setRecording(false); setAiThinking(false); }
          }
        };
        reader.readAsDataURL(blob);
      };
      mediaRecorder.start();
      setTimeout(() => { if (mediaRecorder.state === 'recording') mediaRecorder.stop(); }, MAX_TURN_MS);
    } catch {
      setError('Microphone access denied. Please enable microphone permissions.'); setRecording(false);
    }
  }, [recording, sessionId]);

  const handleEnd = useCallback(async () => {
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
  }, [sessionId, turns.length, elapsed, router]);

  const timeLimit = scenario?.timeLimitSeconds ?? scenario?.timeLimit ?? DEFAULT_TIME_LIMIT;

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
            <ConversationPrepCard scenario={scenario} prepCountdown={prepCountdown} onStart={handleStartConversation} />
          </MotionSection>
        )}

        {state === 'active' && (
          <div className="flex flex-col h-[calc(100dvh-200px)]">
            <ConversationTimerBar elapsed={elapsed} timeLimit={timeLimit} turns={turns.length} scenarioTitle={scenario?.title} />
            <ConversationChatView turns={turns} aiThinking={aiThinking} aiSpeakingTurn={aiSpeakingTurn}
              onReplay={(turn) => playAiAudio(turn.turnNumber, turn.audioUrl)} />
            <ConversationMicControl recording={recording} disabled={aiThinking || ending} ending={ending}
              canEnd={turns.length >= 2} onRecord={handleRecord} onEnd={handleEnd} />
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
