'use client';

/**
 * Speaking module — real-time AI patient voice conversation.
 *
 * Encapsulates the full hands-free voice loop so the exam panel and the
 * standalone session/roleplay surfaces share one implementation:
 *
 *   mic (always-on) ──▶ client VAD auto-segments each utterance on a short
 *                       trailing silence
 *                   ──▶ SendSpeakingRoleplayTurn (backend: Whisper → Claude →
 *                       ElevenLabs → PatientUtterance{ text, audioUrl })
 *                   ──▶ authorized blob fetch of audioUrl (the media endpoint
 *                       is LearnerOnly, so a bare <audio src> would 401)
 *                   ──▶ auto-play the reply, then auto-resume listening.
 *
 * Start semantics (owner rule 2026-07-03): in the ROLE-PLAY the candidate
 * opens the consultation — the server signals `SpeakingRoleplayReady
 * { patientSpeaksFirst: false }` and the loop goes straight to listening
 * (the patient stays silent until spoken to). The WARM-UP keeps the
 * interlocutor-speaks-first opening, matching the real OET exam.
 *
 * Barge-in: the candidate MAY talk over the patient. A confirmed speech
 * onset during playback stops the patient's audio, keeps capturing, and
 * flags the turn `interruptedPatient` — the AI grader weighs interruptions
 * in feedback. The high onset bar (see SPEAKING_START_*) stops residual
 * speaker echo from triggering false barge-ins.
 *
 * Whisper is a batch API, so "continuous" here means always-listening with
 * VAD segmentation + one Whisper call per utterance — not token streaming.
 */
import { useCallback, useEffect, useRef, useState } from 'react';
import { fetchAuthorizedObjectUrl } from '@/lib/api';

export type ConversationConnection = 'connecting' | 'connected' | 'error';
export type ConversationPhase = 'idle' | 'listening' | 'thinking' | 'speaking';

export interface SpeakingCaption {
  id: string;
  speaker: 'patient' | 'candidate';
  text: string;
}

interface PatientUtterance {
  speaker: 'patient' | 'interlocutor';
  phase: 'warmup' | 'roleplay';
  text: string;
  audioUrl?: string | null;
  emotionHint?: string | null;
  shouldEnd?: boolean;
}

interface LearnerCaptionMsg {
  speaker: 'candidate';
  text: string;
  confidence?: number;
}

interface RoleplayReadyMsg {
  phase: 'warmup' | 'roleplay';
  patientSpeaksFirst: boolean;
}

/** Client-side turn metadata sent alongside the audio — the backend records
 * it on the transcript segment so the AI grader can weigh interruptions. */
interface TurnMeta {
  interruptedPatient: boolean;
  speechDurationMs: number;
}

interface ConversationHubBridge {
  connect: () => Promise<void>;
  begin: (sessionId: string) => Promise<void>;
  stop: () => Promise<void>;
  sendTurn: (sessionId: string, audioBase64: string, mimeType: string, metaJson: string | null) => Promise<void>;
  sendText: (sessionId: string, text: string) => Promise<void>;
  onUtterance: (cb: (u: PatientUtterance) => void) => void;
  onLearnerCaption: (cb: (c: LearnerCaptionMsg) => void) => void;
  onReady: (cb: (r: RoleplayReadyMsg) => void) => void;
  onShouldEnd: (cb: () => void) => void;
  onError: (cb: (code: string, message: string) => void) => void;
}

// ── VAD tuning (normalised 0-1 level = min(1, rms * 3)) ───────────────────
// The recorder runs CONTINUOUSLY while the phase is 'listening' — VAD only
// decides when an utterance has ENDED (trailing silence after speech) or when
// a speechless buffer should be discarded. Starting the recorder on speech
// onset (the previous design) lost the first words of every reply because
// capture began ~3 frames after the learner had already started talking.
const START_LEVEL = 0.16; // sustained level to treat as speech onset
const CONTINUE_LEVEL = 0.09; // above this keeps an utterance "alive"
const START_FRAMES = 3; // consecutive frames above threshold to commit onset
// While the patient is speaking the mic stays live (echoCancellation strips
// the AI's own audio) — but onset needs a much higher, longer signal so
// residual echo can't false-trigger. A CONFIRMED onset during playback is a
// barge-in: playback stops, the turn is captured from the first word, and
// the interruption is recorded for the grader.
const SPEAKING_START_LEVEL = 0.3;
const SPEAKING_START_FRAMES = 6;
const SILENCE_MS = 1000; // trailing silence that ends an utterance (owner target: 1–2s)
const MIN_SPEECH_MS = 450; // ignore blips/coughs (measured from speech onset)
// Candidates are NEVER cut off (owner rule): this is a safety cap that in
// practice never fires — and when it does, the turn is SENT, not discarded.
const MAX_UTTERANCE_MS = 210_000;
const SILENT_BUFFER_RESET_MS = 8_000; // drop + restart a buffer with no speech
const THINKING_TIMEOUT_MS = 25_000; // watchdog if no reply arrives
// Speech-only bitrate. Browsers default MediaRecorder/Opus to ~128kbps —
// 32kbps mono is ample for Whisper and keeps a worst-case 210s turn around
// ~840KB binary (~1.12MB base64), inside the hub's 2MB max message.
const RECORDER_BITS_PER_SECOND = 32_000;
// Hub max receive message is 2MB (ConversationRealtimeTransportLimits).
// Base64 inflates 4/3, so refuse to send blobs that would exceed it — better
// a visible "too long" error than SignalR silently killing the connection.
const MAX_TURN_BLOB_BYTES = 1_400_000;
// Some browsers reject the bitrate hint and record at default ~128kbps. Track
// the running buffer size and end the turn EARLY (still sending it) before it
// can outgrow the hard cap — a long turn must never be silently refused.
const MAX_TURN_SOFT_BYTES = 1_200_000;

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
    let learner: ((c: LearnerCaptionMsg) => void) | null = null;
    let ready: ((r: RoleplayReadyMsg) => void) | null = null;
    let shouldEnd: (() => void) | null = null;
    let error: ((code: string, message: string) => void) | null = null;

    connection.on('PatientUtterance', (p: PatientUtterance) => {
      if (p?.text) utter?.(p);
    });
    connection.on('LearnerCaption', (p: LearnerCaptionMsg) => {
      if (p?.text) learner?.(p);
    });
    connection.on('SpeakingRoleplayReady', (r: RoleplayReadyMsg) => {
      if (r) ready?.(r);
    });
    connection.on('SpeakingShouldEnd', () => shouldEnd?.());
    connection.on('SpeakingRoleplayError', (code: string, message: string) => error?.(code, message));

    return {
      connect: async () => {
        await connection.start();
      },
      begin: async (sessionId) => {
        await connection.invoke('StartSpeakingRoleplay', sessionId);
      },
      stop: async () => {
        try {
          await connection.stop();
        } catch {
          /* ignore */
        }
      },
      sendTurn: async (sessionId, audioBase64, mimeType, metaJson) => {
        await connection.invoke('SendSpeakingRoleplayTurn', sessionId, audioBase64, mimeType, metaJson);
      },
      sendText: async (sessionId, text) => {
        await connection.invoke('SendSpeakingRoleplayText', sessionId, text);
      },
      onUtterance: (cb) => {
        utter = cb;
      },
      onLearnerCaption: (cb) => {
        learner = cb;
      },
      onReady: (cb) => {
        ready = cb;
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

function pickRecorderMime(): string | undefined {
  if (typeof MediaRecorder === 'undefined') return undefined;
  return ['audio/webm;codecs=opus', 'audio/webm', 'audio/mp4', 'audio/ogg'].find((c) =>
    MediaRecorder.isTypeSupported(c),
  );
}

function blobToBase64(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onloadend = () => {
      const dataUrl = String(reader.result);
      const comma = dataUrl.indexOf(',');
      resolve(comma >= 0 ? dataUrl.slice(comma + 1) : dataUrl);
    };
    reader.onerror = () => reject(new Error('Could not read audio.'));
    reader.readAsDataURL(blob);
  });
}

export interface UseSpeakingConversationResult {
  connection: ConversationConnection;
  phase: ConversationPhase;
  captions: SpeakingCaption[];
  error: string | null;
  micEnabled: boolean;
  micLevel: number;
  /** True when a reply arrived without audio — TTS is off/misconfigured. */
  voiceUnavailable: boolean;
  ended: boolean;
  /**
   * True while the role-play waits for the candidate's opening line — the
   * patient stays silent until spoken to. Surfaces should hint
   * "The patient is waiting — introduce yourself to begin."
   */
  awaitingCandidateStart: boolean;
  /**
   * User-gesture entry point: grants mic and starts VAD. In the warm-up the
   * interlocutor speaks first; in the role-play the candidate opens.
   */
  enableMic: () => Promise<void>;
  disableMic: () => void;
  sendText: (text: string) => Promise<void>;
}

export function useSpeakingConversation(sessionId: string): UseSpeakingConversationResult {
  const [connection, setConnection] = useState<ConversationConnection>('connecting');
  const [phase, setPhaseState] = useState<ConversationPhase>('idle');
  const [captions, setCaptions] = useState<SpeakingCaption[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [micEnabled, setMicEnabled] = useState(false);
  const [micLevel, setMicLevel] = useState(0);
  const [voiceUnavailable, setVoiceUnavailable] = useState(false);
  const [ended, setEnded] = useState(false);
  const [awaitingCandidateStart, setAwaitingCandidateStart] = useState(false);

  const hubRef = useRef<ConversationHubBridge | null>(null);
  const sessionIdRef = useRef(sessionId);
  sessionIdRef.current = sessionId;

  const phaseRef = useRef<ConversationPhase>('idle');
  const micEnabledRef = useRef(false);

  const streamRef = useRef<MediaStream | null>(null);
  const audioCtxRef = useRef<AudioContext | null>(null);
  const analyserRef = useRef<AnalyserNode | null>(null);
  const rafRef = useRef<number | null>(null);

  const recorderRef = useRef<MediaRecorder | null>(null);
  const chunksRef = useRef<Blob[]>([]);
  const bufferBytesRef = useRef(0); // running size of the current buffer
  const utteranceStartRef = useRef(0); // when the (continuous) buffer began
  const speechStartRef = useRef(0); // when VAD confirmed speech onset
  const lastVoiceRef = useRef(0);
  const startFramesRef = useRef(0);
  const hadSpeechRef = useRef(false); // buffer contains confirmed speech
  const discardStopRef = useRef(false); // next recorder stop discards the buffer
  // Set when a confirmed onset stopped the patient's playback (barge-in) —
  // attached to the next SENT turn so the grader sees the interruption.
  const interruptedPatientRef = useRef(false);

  const currentAudioRef = useRef<HTMLAudioElement | null>(null);
  const currentAudioUrlRef = useRef<string | null>(null);
  const thinkingTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const setPhase = useCallback((p: ConversationPhase) => {
    phaseRef.current = p;
    setPhaseState(p);
    // Mic gate: capture while listening AND while the patient is speaking —
    // echoCancellation strips the AI's own audio from the mic signal, and the
    // VAD uses a much higher onset bar during playback, so the patient is
    // heard in full while a learner who replies early is still captured from
    // their first word. Only 'thinking'/'idle' silence the mic entirely.
    const track = streamRef.current?.getAudioTracks?.()[0];
    if (track) track.enabled = p === 'listening' || p === 'speaking';
  }, []);

  const pushCaption = useCallback((speaker: 'patient' | 'candidate', text: string) => {
    setCaptions((prev) => [...prev, { id: `${prev.length}-${speaker}`, speaker, text }].slice(-14));
  }, []);

  const stopPlayback = useCallback(() => {
    const audio = currentAudioRef.current;
    if (audio) {
      try {
        audio.pause();
        audio.src = '';
      } catch {
        /* ignore */
      }
    }
    currentAudioRef.current = null;
    if (currentAudioUrlRef.current) {
      URL.revokeObjectURL(currentAudioUrlRef.current);
      currentAudioUrlRef.current = null;
    }
  }, []);

  const clearThinkingWatchdog = useCallback(() => {
    if (thinkingTimerRef.current) {
      clearTimeout(thinkingTimerRef.current);
      thinkingTimerRef.current = null;
    }
  }, []);

  // ── Send one captured utterance ──────────────────────────────────────
  const sendUtterance = useCallback(
    async (blob: Blob, mimeType: string, speechDurationMs: number) => {
      const hub = hubRef.current;
      if (!hub || !sessionIdRef.current) {
        setPhase('listening');
        return;
      }
      setPhase('thinking');
      clearThinkingWatchdog();
      thinkingTimerRef.current = setTimeout(() => {
        // No reply arrived — recover so the learner isn't stuck.
        if (phaseRef.current === 'thinking') {
          setError('The patient did not respond. Please try again.');
          setPhase(micEnabledRef.current ? 'listening' : 'idle');
        }
      }, THINKING_TIMEOUT_MS);
      try {
        const base64 = await blobToBase64(blob);
        const meta: TurnMeta = {
          interruptedPatient: interruptedPatientRef.current,
          speechDurationMs: Math.max(0, Math.round(speechDurationMs)),
        };
        await hub.sendTurn(
          sessionIdRef.current,
          base64,
          mimeType || 'audio/webm',
          JSON.stringify(meta),
        );
        interruptedPatientRef.current = false;
      } catch (err) {
        clearThinkingWatchdog();
        setError(err instanceof Error ? err.message : 'Could not send your turn.');
        setPhase(micEnabledRef.current ? 'listening' : 'idle');
      }
    },
    [clearThinkingWatchdog, setPhase],
  );

  // ── Capture lifecycle ────────────────────────────────────────────────
  // Starts a continuous buffer for the whole 'listening' window. The buffer
  // is only SENT if VAD confirmed speech inside it; otherwise it is quietly
  // discarded — so nothing the learner says is ever missed, and silence is
  // never submitted.
  const beginCapture = useCallback(() => {
    const stream = streamRef.current;
    if (!stream || recorderRef.current) return;
    const mimeType = pickRecorderMime();
    let recorder: MediaRecorder;
    try {
      recorder = new MediaRecorder(stream, {
        ...(mimeType ? { mimeType } : {}),
        audioBitsPerSecond: RECORDER_BITS_PER_SECOND,
      });
    } catch {
      // Some browsers reject bitrate hints — retry without one.
      try {
        recorder = new MediaRecorder(stream, mimeType ? { mimeType } : undefined);
      } catch {
        return;
      }
    }
    chunksRef.current = [];
    bufferBytesRef.current = 0;
    utteranceStartRef.current = performance.now();
    lastVoiceRef.current = performance.now();
    hadSpeechRef.current = false;
    discardStopRef.current = false;
    startFramesRef.current = 0;
    recorder.ondataavailable = (e) => {
      if (e.data.size > 0) {
        chunksRef.current.push(e.data);
        bufferBytesRef.current += e.data.size;
      }
    };
    recorder.onstop = () => {
      const chunks = chunksRef.current;
      chunksRef.current = [];
      bufferBytesRef.current = 0;
      recorderRef.current = null;
      const send = hadSpeechRef.current && !discardStopRef.current;
      // Speech span of this buffer (onset → last voice), captured before the
      // refs reset — reported to the backend so segments get real timings.
      const speechDurationMs = send ? Math.max(0, lastVoiceRef.current - speechStartRef.current) : 0;
      hadSpeechRef.current = false;
      discardStopRef.current = false;
      if (!send) return; // silent/discarded buffer — the frame loop restarts capture
      const blob = new Blob(chunks, { type: recorder.mimeType || 'audio/webm' });
      if (blob.size < 1_200) return; // corrupt/empty despite VAD — keep listening
      if (blob.size > MAX_TURN_BLOB_BYTES) {
        // Oversized payloads make SignalR close the whole connection — refuse
        // client-side and keep the conversation alive instead. (The soft-cap
        // check in the frame loop should end the turn long before this.)
        setError('That turn was too long to send. Please make your point in shorter turns.');
        return;
      }
      void sendUtterance(blob, blob.type, speechDurationMs);
    };
    recorderRef.current = recorder;
    try {
      recorder.start(200);
    } catch {
      recorderRef.current = null;
    }
  }, [sendUtterance]);

  const finishCapture = useCallback(() => {
    const recorder = recorderRef.current;
    if (recorder && recorder.state === 'recording') {
      try {
        recorder.stop();
      } catch {
        /* ignore */
      }
    }
  }, []);

  // ── VAD frame loop ────────────────────────────────────────────────────
  const runVadFrame = useCallback(() => {
    const analyser = analyserRef.current;
    if (!analyser) return;
    const data = new Uint8Array(analyser.fftSize);
    analyser.getByteTimeDomainData(data);
    let sum = 0;
    for (let i = 0; i < data.length; i++) {
      const v = (data[i] - 128) / 128;
      sum += v * v;
    }
    const level = Math.min(1, Math.sqrt(sum / data.length) * 3);
    setMicLevel(level);

    const now = performance.now();
    const phaseNow = phaseRef.current;
    const isRecording = recorderRef.current?.state === 'recording';

    // While the patient is speaking the recorder ALSO runs (mic live, AEC
    // strips the AI's own audio) so a learner who starts replying early is
    // captured from their first word. Onset detection during playback uses a
    // higher, longer bar so residual echo can't false-trigger. A CONFIRMED
    // onset is a barge-in: the patient's playback stops, the interruption is
    // recorded for the grader, and the loop drops to 'listening' where the
    // normal end-of-turn silence logic takes over.
    if (phaseNow === 'speaking') {
      if (!isRecording) {
        beginCapture();
      } else if (!hadSpeechRef.current) {
        if (level >= SPEAKING_START_LEVEL) {
          startFramesRef.current += 1;
          if (startFramesRef.current >= SPEAKING_START_FRAMES) {
            startFramesRef.current = 0;
            hadSpeechRef.current = true;
            speechStartRef.current = now;
            lastVoiceRef.current = now;
            interruptedPatientRef.current = true;
            stopPlayback();
            setPhase('listening');
          }
        } else {
          startFramesRef.current = 0;
          // Bound speechless buffers during long patient turns too.
          if (now - utteranceStartRef.current >= SILENT_BUFFER_RESET_MS) {
            discardStopRef.current = true;
            finishCapture(); // onstop discards; next frame restarts capture
          }
        }
      }
    } else if (phaseNow === 'listening') {
      if (!isRecording) {
        beginCapture();
      } else if (!hadSpeechRef.current) {
        // Waiting for speech onset inside the running buffer.
        if (level >= START_LEVEL) {
          startFramesRef.current += 1;
          if (startFramesRef.current >= START_FRAMES) {
            startFramesRef.current = 0;
            hadSpeechRef.current = true;
            speechStartRef.current = now;
            lastVoiceRef.current = now;
          }
        } else {
          startFramesRef.current = 0;
          // Periodically drop a speechless buffer so it can't grow unbounded.
          if (now - utteranceStartRef.current >= SILENT_BUFFER_RESET_MS) {
            discardStopRef.current = true;
            finishCapture(); // onstop discards; next frame restarts capture
          }
        }
      } else {
        // Speech confirmed — wait for the trailing silence that ends the turn.
        if (level >= CONTINUE_LEVEL) lastVoiceRef.current = now;
        const silentFor = now - lastVoiceRef.current;
        const speechFor = now - speechStartRef.current;
        if (silentFor >= SILENCE_MS) {
          if (speechFor - silentFor < MIN_SPEECH_MS) {
            // Blip/cough — reset the buffer instead of submitting it.
            discardStopRef.current = true;
            finishCapture();
          } else {
            finishCapture(); // onstop sends the full buffer (leading silence is fine for Whisper)
          }
        } else if (speechFor >= MAX_UTTERANCE_MS || bufferBytesRef.current >= MAX_TURN_SOFT_BYTES) {
          // Safety caps only — the turn is SENT, never discarded. The byte cap
          // covers browsers that ignored the 32kbps hint (~128kbps default).
          finishCapture();
        }
      }
    }
    // 'thinking' / 'idle': ignore onsets so turns never overlap.

    rafRef.current = requestAnimationFrame(runVadFrame);
  }, [beginCapture, finishCapture, setPhase, stopPlayback]);

  // ── Play an AI reply, then resume listening ───────────────────────────
  const playReply = useCallback(
    async (url: string) => {
      stopPlayback();
      setPhase('speaking');
      let objectUrl: string;
      try {
        objectUrl = await fetchAuthorizedObjectUrl(url);
      } catch {
        // Audio was generated but could not be fetched/played — surface it
        // (don't fail silently) and keep the loop alive.
        setVoiceUnavailable(true);
        setPhase(micEnabledRef.current ? 'listening' : 'idle');
        return;
      }
      currentAudioUrlRef.current = objectUrl;
      const audio = new Audio(objectUrl);
      currentAudioRef.current = audio;
      const resume = () => {
        if (currentAudioRef.current === audio) {
          stopPlayback();
          setPhase(micEnabledRef.current ? 'listening' : 'idle');
        }
      };
      audio.onended = resume;
      audio.onerror = resume;
      try {
        await audio.play();
      } catch {
        resume();
      }
    },
    [setPhase, stopPlayback],
  );

  // ── Connect the hub on mount ──────────────────────────────────────────
  useEffect(() => {
    if (!sessionId) return;
    let cancelled = false;
    setConnection('connecting');
    loadConversationHub().then(async (hub) => {
      if (cancelled || !hub) {
        if (!hub) {
          setConnection('error');
          setError('Could not reach the AI patient. Please refresh.');
        }
        return;
      }
      hubRef.current = hub;
      hub.onLearnerCaption((c) => {
        setAwaitingCandidateStart(false);
        pushCaption('candidate', c.text);
      });
      hub.onReady((r) => {
        if (r.patientSpeaksFirst) return; // warm-up: opening utterance is on its way
        // Role-play: the candidate opens. Go straight to listening — the
        // patient stays silent until spoken to.
        clearThinkingWatchdog();
        setAwaitingCandidateStart(true);
        if (micEnabledRef.current) setPhase('listening');
      });
      hub.onUtterance((u) => {
        clearThinkingWatchdog();
        setAwaitingCandidateStart(false);
        pushCaption('patient', u.text);
        if (u.audioUrl) {
          setVoiceUnavailable(false);
          void playReply(u.audioUrl);
        } else {
          // Reply came back with no audio → TTS is off/misconfigured.
          setVoiceUnavailable(true);
          setPhase(micEnabledRef.current ? 'listening' : 'idle');
        }
        if (u.shouldEnd) setEnded(true);
      });
      hub.onShouldEnd(() => setEnded(true));
      hub.onError((_code, message) => {
        clearThinkingWatchdog();
        setError(message);
        if (phaseRef.current === 'thinking') setPhase(micEnabledRef.current ? 'listening' : 'idle');
      });
      try {
        await hub.connect();
        if (!cancelled) setConnection('connected');
      } catch {
        if (!cancelled) {
          setConnection('error');
          setError('Could not connect to the AI patient.');
        }
      }
    });
    return () => {
      cancelled = true;
      hubRef.current?.stop().catch(() => undefined);
      hubRef.current = null;
    };
  }, [sessionId, pushCaption, playReply, clearThinkingWatchdog, setPhase]);

  // ── Teardown of audio/mic resources ───────────────────────────────────
  const teardownAudio = useCallback(() => {
    if (rafRef.current !== null) {
      cancelAnimationFrame(rafRef.current);
      rafRef.current = null;
    }
    discardStopRef.current = true; // never submit a partial turn on teardown
    finishCapture();
    recorderRef.current = null;
    if (streamRef.current) {
      streamRef.current.getTracks().forEach((t) => t.stop());
      streamRef.current = null;
    }
    if (audioCtxRef.current) {
      audioCtxRef.current.close().catch(() => undefined);
      audioCtxRef.current = null;
    }
    analyserRef.current = null;
    stopPlayback();
    clearThinkingWatchdog();
    setMicLevel(0);
  }, [clearThinkingWatchdog, finishCapture, stopPlayback]);

  useEffect(() => () => teardownAudio(), [teardownAudio]);

  // ── Public: grant mic + start the conversation (must be a user gesture) ──
  const enableMic = useCallback(async () => {
    if (micEnabledRef.current) return;
    if (typeof navigator === 'undefined' || !navigator.mediaDevices?.getUserMedia) {
      setError('Your browser does not support microphone access.');
      return;
    }
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true, autoGainControl: true },
      });
      streamRef.current = stream;
      const AudioCtor =
        window.AudioContext || (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext;
      const ctx = new AudioCtor();
      audioCtxRef.current = ctx;
      const source = ctx.createMediaStreamSource(stream);
      const analyser = ctx.createAnalyser();
      analyser.fftSize = 512;
      source.connect(analyser);
      analyserRef.current = analyser;

      micEnabledRef.current = true;
      setMicEnabled(true);
      setError(null);
      // Hold the mic closed until the server says who opens. Warm-up: the
      // interlocutor's opening line plays first (mic stays gated so it is
      // never clipped). Role-play: SpeakingRoleplayReady flips us straight
      // to listening — the candidate opens. If neither signal arrives
      // (old backend / dropped event), the watchdog falls back to listening.
      setPhase('thinking');
      rafRef.current = requestAnimationFrame(runVadFrame);
      clearThinkingWatchdog();
      thinkingTimerRef.current = setTimeout(() => {
        if (phaseRef.current === 'thinking') setPhase('listening');
      }, THINKING_TIMEOUT_MS);

      // Signal the server we are ready (it responds with
      // SpeakingRoleplayReady and, in the warm-up, the opening line).
      try {
        await hubRef.current?.begin(sessionIdRef.current);
      } catch {
        /* the loop still works from the first learner turn */
      }
    } catch (err) {
      const name = err instanceof Error ? err.name : '';
      setError(
        name === 'NotAllowedError'
          ? 'Microphone access was denied. Enable it in your browser settings to speak.'
          : 'Could not access the microphone.',
      );
    }
  }, [runVadFrame, setPhase, clearThinkingWatchdog]);

  const disableMic = useCallback(() => {
    micEnabledRef.current = false;
    setMicEnabled(false);
    teardownAudio();
    setPhase('idle');
  }, [setPhase, teardownAudio]);

  const sendText = useCallback(
    async (text: string) => {
      const hub = hubRef.current;
      const trimmed = text.trim();
      if (!hub || !trimmed || !sessionIdRef.current) return;
      pushCaption('candidate', trimmed);
      setPhase('thinking');
      clearThinkingWatchdog();
      thinkingTimerRef.current = setTimeout(() => {
        if (phaseRef.current === 'thinking') {
          setError('The patient did not respond. Please try again.');
          setPhase(micEnabledRef.current ? 'listening' : 'idle');
        }
      }, THINKING_TIMEOUT_MS);
      try {
        await hub.sendText(sessionIdRef.current, trimmed);
      } catch (err) {
        clearThinkingWatchdog();
        setError(err instanceof Error ? err.message : 'Could not send your message.');
        setPhase(micEnabledRef.current ? 'listening' : 'idle');
      }
    },
    [clearThinkingWatchdog, pushCaption, setPhase],
  );

  return {
    connection,
    phase,
    captions,
    error,
    micEnabled,
    micLevel,
    voiceUnavailable,
    ended,
    awaitingCandidateStart,
    enableMic,
    disableMic,
    sendText,
  };
}
