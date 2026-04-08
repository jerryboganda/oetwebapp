'use client';

import { useEffect, useRef, useState, useCallback } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { MotionSection } from '@/components/ui/motion-primitives';
import { Mic, MicOff, Send, Square, Clock, MessageSquare, AlertCircle, Loader2 } from 'lucide-react';
import { useParams, useRouter } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { InlineAlert } from '@/components/ui/alert';
import { analytics } from '@/lib/analytics';
import { getConversation, completeConversation } from '@/lib/api';
import { Skeleton } from '@/components/ui/skeleton';

type Turn = {
  turnNumber: number;
  role: 'learner' | 'ai' | 'system';
  content: string;
  timestamp: number;
};

type SessionState = 'preparing' | 'active' | 'completed' | 'evaluating' | 'evaluated' | 'abandoned';

type Scenario = {
  title: string;
  setting: string;
  patientRole: string;
  clinicianRole: string;
  context: string;
  objectives: string[];
  timeLimit: number;
};

export default function ConversationSessionPage() {
  const params = useParams();
  const router = useRouter();
  const sessionId = params.sessionId as string;

  const [scenario, setScenario] = useState<Scenario | null>(null);
  const [state, setState] = useState<SessionState>('preparing');
  const [turns, setTurns] = useState<Turn[]>([]);
  const [loading, setLoading] = useState(true);
  const [recording, setRecording] = useState(false);
  const [aiThinking, setAiThinking] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [elapsed, setElapsed] = useState(0);
  const [prepCountdown, setPrepCountdown] = useState(120);
  const [ending, setEnding] = useState(false);

  const chatEndRef = useRef<HTMLDivElement>(null);
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const prepTimerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const hubRef = useRef<import('@microsoft/signalr').HubConnection | null>(null);
  const mediaRecorderRef = useRef<MediaRecorder | null>(null);
  const audioChunksRef = useRef<Blob[]>([]);

  // Load session
  useEffect(() => {
    getConversation(sessionId)
      .then((data: Record<string, unknown>) => {
        try {
          const scenarioData = typeof data.scenarioJson === 'string' ? JSON.parse(data.scenarioJson as string) : null;
          setScenario(scenarioData);
        } catch { /* noop */ }
        setState(data.state as SessionState || 'preparing');
        if (data.state === 'evaluated' || data.state === 'completed') {
          router.replace(`/conversation/${sessionId}/results`);
        }
      })
      .catch(() => setError('Failed to load conversation session.'))
      .finally(() => setLoading(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sessionId]);

  // Scroll to bottom on new turns
  useEffect(() => {
    chatEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [turns, aiThinking]);

  // Prep countdown
  useEffect(() => {
    if (state !== 'preparing') return;
    prepTimerRef.current = setInterval(() => {
      setPrepCountdown(prev => {
        if (prev <= 1) {
          handleStartConversation();
          return 0;
        }
        return prev - 1;
      });
    }, 1000);
    return () => { if (prepTimerRef.current) clearInterval(prepTimerRef.current); };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [state]);

  // Active timer
  useEffect(() => {
    if (state !== 'active') return;
    timerRef.current = setInterval(() => setElapsed(e => e + 1), 1000);
    return () => { if (timerRef.current) clearInterval(timerRef.current); };
  }, [state]);

  const handleStartConversation = useCallback(async () => {
    if (prepTimerRef.current) clearInterval(prepTimerRef.current);
    setState('active');
    analytics.track('conversation_active', { sessionId });

    try {
      const signalR = await import('@microsoft/signalr');
      const connection = new signalR.HubConnectionBuilder()
        .withUrl(`${process.env.NEXT_PUBLIC_API_BASE_URL || ''}/v1/conversations/hub`, {
          accessTokenFactory: async () => {
            const { ensureFreshAccessToken } = await import('@/lib/auth-client');
            return await ensureFreshAccessToken() || '';
          },
        })
        .withAutomaticReconnect()
        .build();

      connection.on('ReceiveTranscript', (turnNumber: number, text: string) => {
        setTurns(prev => [...prev, { turnNumber, role: 'learner', content: text, timestamp: Date.now() }]);
        setRecording(false);
        setAiThinking(true);
      });

      connection.on('ReceiveAIResponse', (turnNumber: number, text: string) => {
        setAiThinking(false);
        setTurns(prev => [...prev, { turnNumber, role: 'ai', content: text, timestamp: Date.now() }]);
      });

      connection.on('SessionStateChanged', (newState: SessionState) => {
        setState(newState);
        if (newState === 'evaluating' || newState === 'evaluated') {
          router.push(`/conversation/${sessionId}/results`);
        }
      });

      connection.on('ConversationError', (code: string, message: string) => {
        setError(message);
        setRecording(false);
        setAiThinking(false);
      });

      await connection.start();
      hubRef.current = connection;
      await connection.invoke('StartSession', sessionId);

      // AI opens the conversation
      setTurns([{
        turnNumber: 0,
        role: 'ai',
        content: scenario?.context
          ? `Good morning. ${scenario.context.split('.')[0]}. How can I help you today?`
          : 'Good morning. Thank you for coming in today. How can I help you?',
        timestamp: Date.now(),
      }]);
    } catch (err) {
      console.error('SignalR connection error:', err);
      setError('Could not connect to conversation server. Please refresh and try again.');
    }
  }, [sessionId, scenario, router]);

  const handleRecord = useCallback(async () => {
    if (recording) {
      // Stop recording
      mediaRecorderRef.current?.stop();
      return;
    }

    setRecording(true);
    setError(null);
    audioChunksRef.current = [];

    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      const mediaRecorder = new MediaRecorder(stream, { mimeType: 'audio/webm;codecs=opus' });
      mediaRecorderRef.current = mediaRecorder;

      mediaRecorder.ondataavailable = (e) => {
        if (e.data.size > 0) audioChunksRef.current.push(e.data);
      };

      mediaRecorder.onstop = async () => {
        stream.getTracks().forEach(t => t.stop());
        const blob = new Blob(audioChunksRef.current, { type: 'audio/webm' });
        const reader = new FileReader();
        reader.onloadend = async () => {
          const base64 = (reader.result as string).split(',')[1];
          if (hubRef.current && base64) {
            try {
              await hubRef.current.invoke('SendAudio', sessionId, base64);
            } catch {
              setError('Failed to send audio. Please try again.');
              setRecording(false);
            }
          }
        };
        reader.readAsDataURL(blob);
      };

      mediaRecorder.start();

      // Auto-stop after 30 seconds
      setTimeout(() => {
        if (mediaRecorder.state === 'recording') {
          mediaRecorder.stop();
        }
      }, 30000);
    } catch {
      setError('Microphone access denied. Please enable microphone permissions.');
      setRecording(false);
    }
  }, [recording, sessionId]);

  const handleEnd = useCallback(async () => {
    setEnding(true);
    if (timerRef.current) clearInterval(timerRef.current);

    try {
      if (hubRef.current) {
        await hubRef.current.invoke('EndSession', sessionId);
        await hubRef.current.stop();
      } else {
        await completeConversation(sessionId);
      }
      analytics.track('conversation_ended', { sessionId, turns: turns.length, elapsed });
      router.push(`/conversation/${sessionId}/results`);
    } catch {
      setError('Failed to end session. Please try again.');
      setEnding(false);
    }
  }, [sessionId, turns.length, elapsed, router]);

  const formatTime = (s: number) => `${Math.floor(s / 60).toString().padStart(2, '0')}:${(s % 60).toString().padStart(2, '0')}`;
  const timeLimit = scenario?.timeLimit ?? 300;
  const timePercent = Math.min(100, (elapsed / timeLimit) * 100);

  if (loading) {
    return (
      <LearnerDashboardShell>
        <div className="max-w-4xl mx-auto space-y-4">
          <Skeleton className="h-40 rounded-2xl" />
          <Skeleton className="h-[280px] rounded-2xl sm:h-[340px] lg:h-96" />
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell>
      <div className="max-w-4xl mx-auto">
        {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

        {/* Prep Phase */}
        {state === 'preparing' && scenario && (
          <MotionSection className="space-y-6">
            <div className="bg-gradient-to-br from-purple-50 to-indigo-50 dark:from-purple-950/40 dark:to-indigo-950/40 rounded-2xl border border-purple-200/60 dark:border-purple-800/40 p-6">
              <div className="text-sm font-bold uppercase tracking-wider text-purple-600 dark:text-purple-400 mb-2">
                Preparation Phase
              </div>
              <h1 className="text-2xl font-bold text-gray-900 dark:text-white mb-2">{scenario.title}</h1>
              <p className="text-gray-600 dark:text-gray-300 mb-4">{scenario.context}</p>

              <div className="grid grid-cols-2 gap-4 mb-4">
                <div className="bg-white/70 dark:bg-gray-800/70 rounded-xl p-3">
                  <div className="text-xs font-semibold text-gray-500 mb-1 uppercase">Your Role</div>
                  <div className="text-sm font-bold text-gray-900 dark:text-white">{scenario.clinicianRole}</div>
                </div>
                <div className="bg-white/70 dark:bg-gray-800/70 rounded-xl p-3">
                  <div className="text-xs font-semibold text-gray-500 mb-1 uppercase">Patient</div>
                  <div className="text-sm font-bold text-gray-900 dark:text-white">{scenario.patientRole}</div>
                </div>
              </div>

              <div className="bg-white/70 dark:bg-gray-800/70 rounded-xl p-3 mb-4">
                <div className="text-xs font-semibold text-gray-500 mb-2 uppercase">Objectives</div>
                <ul className="space-y-1">
                  {scenario.objectives.map((obj, i) => (
                    <li key={i} className="text-sm text-gray-700 dark:text-gray-300 flex items-start gap-2">
                      <span className="w-5 h-5 rounded-full bg-purple-100 dark:bg-purple-900/40 text-purple-600 dark:text-purple-400 flex items-center justify-center text-xs font-bold shrink-0 mt-0.5">{i + 1}</span>
                      {obj}
                    </li>
                  ))}
                </ul>
              </div>

              <div className="flex items-center justify-between">
                <div className="text-3xl font-bold text-purple-600 dark:text-purple-400 tabular-nums">
                  {formatTime(prepCountdown)}
                </div>
                <button
                  onClick={handleStartConversation}
                  className="px-6 py-2.5 bg-purple-600 hover:bg-purple-700 text-white rounded-xl font-semibold flex items-center gap-2 transition-colors"
                >
                  <Mic className="w-4 h-4" /> Start Now
                </button>
              </div>
            </div>
          </MotionSection>
        )}

        {/* Active Conversation */}
        {state === 'active' && (
          <div className="flex flex-col h-[calc(100dvh-200px)]">
            {/* Timer bar */}
            <div className="flex items-center justify-between mb-3 px-1">
              <div className="flex items-center gap-2 text-sm text-gray-500">
                <Clock className="w-4 h-4" />
                <span className="tabular-nums font-semibold">{formatTime(elapsed)} / {formatTime(timeLimit)}</span>
              </div>
              <div className="flex items-center gap-2">
                <span className="text-xs text-gray-400">{turns.length} turns</span>
                {scenario && <span className="text-xs font-semibold text-purple-500">{scenario.title}</span>}
              </div>
            </div>
            <div className="w-full bg-gray-200 dark:bg-gray-700 rounded-full h-1.5 mb-4">
              <div
                className={`h-1.5 rounded-full transition-all duration-1000 ${timePercent > 80 ? 'bg-red-500' : timePercent > 60 ? 'bg-yellow-500' : 'bg-purple-500'}`}
                style={{ width: `${timePercent}%` }}
              />
            </div>

            {/* Chat */}
            <div className="flex-1 overflow-y-auto space-y-3 px-1 pb-4">
              <AnimatePresence>
                {turns.map((turn, i) => (
                  <motion.div
                    key={`${turn.turnNumber}-${i}`}
                    initial={{ opacity: 0, y: 12, scale: 0.97 }}
                    animate={{ opacity: 1, y: 0, scale: 1 }}
                    transition={{ type: 'spring', damping: 20 }}
                    className={`flex ${turn.role === 'learner' ? 'justify-end' : 'justify-start'}`}
                  >
                    <div className={`max-w-[75%] rounded-2xl px-4 py-3 ${
                      turn.role === 'learner'
                        ? 'bg-purple-600 text-white rounded-br-sm'
                        : 'bg-gray-100 dark:bg-gray-800 text-gray-900 dark:text-white rounded-bl-sm border border-gray-200 dark:border-gray-700'
                    }`}>
                      <div className="text-xs font-semibold mb-1 opacity-70">
                        {turn.role === 'learner' ? 'You' : 'AI Partner'}
                      </div>
                      <p className="text-sm leading-relaxed">{turn.content}</p>
                    </div>
                  </motion.div>
                ))}

                {/* AI Thinking Indicator */}
                {aiThinking && (
                  <motion.div
                    initial={{ opacity: 0, y: 8 }}
                    animate={{ opacity: 1, y: 0 }}
                    className="flex justify-start"
                  >
                    <div className="bg-gray-100 dark:bg-gray-800 rounded-2xl rounded-bl-sm px-4 py-3 border border-gray-200 dark:border-gray-700">
                      <div className="flex items-center gap-2 text-sm text-gray-500">
                        <Loader2 className="w-4 h-4 animate-spin" />
                        AI is thinking...
                      </div>
                    </div>
                  </motion.div>
                )}
              </AnimatePresence>
              <div ref={chatEndRef} />
            </div>

            {/* Controls */}
            <div className="border-t border-gray-200 dark:border-gray-700 pt-4 flex items-center justify-center gap-4">
              <button
                onClick={handleRecord}
                disabled={aiThinking || ending}
                className={`group relative w-16 h-16 rounded-full flex items-center justify-center transition-all ${
                  recording
                    ? 'bg-red-500 hover:bg-red-600 text-white shadow-lg shadow-red-500/30 animate-pulse'
                    : 'bg-purple-600 hover:bg-purple-700 text-white shadow-lg shadow-purple-500/20'
                } disabled:opacity-50`}
                aria-label={recording ? 'Stop recording' : 'Start recording'}
              >
                {recording ? <MicOff className="w-7 h-7" /> : <Mic className="w-7 h-7" />}
                {recording && (
                  <span className="absolute -inset-1 rounded-full border-2 border-red-500/50 animate-ping" />
                )}
              </button>

              <button
                onClick={handleEnd}
                disabled={ending || turns.length < 2}
                className="px-5 py-2.5 bg-gray-800 hover:bg-gray-900 dark:bg-gray-700 dark:hover:bg-gray-600 text-white rounded-xl font-semibold flex items-center gap-2 transition-colors disabled:opacity-50"
              >
                {ending ? <Loader2 className="w-4 h-4 animate-spin" /> : <Square className="w-4 h-4" />}
                End Conversation
              </button>
            </div>
          </div>
        )}

        {/* Evaluating State */}
        {state === 'evaluating' && (
          <MotionSection className="text-center py-16">
            <Loader2 className="w-12 h-12 text-purple-500 mx-auto mb-4 animate-spin" />
            <h2 className="text-xl font-bold text-gray-900 dark:text-white mb-2">Evaluating Your Conversation</h2>
            <p className="text-gray-500">Our AI is analysing your performance. This usually takes a few seconds.</p>
          </MotionSection>
        )}

        {/* No scenario */}
        {!scenario && !loading && (
          <div className="text-center py-16">
            <AlertCircle className="w-12 h-12 text-gray-300 mx-auto mb-4" />
            <p className="text-gray-500">Could not load conversation scenario.</p>
          </div>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
