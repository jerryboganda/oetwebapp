'use client';

import { useCallback, useEffect, useRef, useState, type RefObject } from 'react';
import {
  createGeminiLiveToken,
  createOpenAiLiveOffer,
  getLiveVoicePreflight,
  persistLiveVoiceTranscript,
  persistLiveVoiceTurn,
  type LiveVoiceGeminiTokenResponse,
  type LiveVoiceOpenAiOfferResponse,
  type LiveVoicePreflight,
  type LiveVoiceProvider,
  type LiveVoiceTranscriptSegmentInput,
} from '@/lib/api/speaking-live-voice';

export type RealtimeVoiceConnection = 'idle' | 'preparing' | 'ready' | 'connecting' | 'connected' | 'ending' | 'ended' | 'error';
export type RealtimeVoicePhase = 'idle' | 'listening' | 'speaking';
export type RealtimeVoiceSpeaker = 'candidate' | 'patient';

export interface RealtimeVoiceCaption {
  id: string;
  speaker: RealtimeVoiceSpeaker;
  text: string;
}

export interface UseSpeakingRealtimeVoiceResult {
  connection: RealtimeVoiceConnection;
  phase: RealtimeVoicePhase;
  preflight: LiveVoicePreflight | null;
  captions: RealtimeVoiceCaption[];
  micLevel: number;
  micEnabled: boolean;
  awaitingCandidateStart: boolean;
  error: string | null;
  ended: boolean;
  audioRef: RefObject<HTMLAudioElement | null>;
  prepare: () => Promise<void>;
  /** Consent (incl. the provider disclosure) is recorded on the Rules + consent step before prep. */
  start: () => Promise<boolean>;
  stop: () => Promise<boolean>;
}

const TARGET_SAMPLE_RATE = 16_000;

function decodeBase64(value: string): Uint8Array {
  const binary = window.atob(value);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index);
  return bytes;
}

function encodeBase64(bytes: Uint8Array): string {
  let binary = '';
  const chunkSize = 0x8000;
  for (let offset = 0; offset < bytes.length; offset += chunkSize) {
    binary += String.fromCharCode(...bytes.subarray(offset, Math.min(offset + chunkSize, bytes.length)));
  }
  return window.btoa(binary);
}

function downsample(input: Float32Array, inputRate: number, outputRate: number): Float32Array {
  if (inputRate === outputRate) return input;
  const ratio = inputRate / outputRate;
  const outputLength = Math.round(input.length / ratio);
  const output = new Float32Array(outputLength);
  let inputOffset = 0;
  for (let outputOffset = 0; outputOffset < outputLength; outputOffset += 1) {
    const nextInputOffset = Math.round((outputOffset + 1) * ratio);
    let sum = 0;
    let count = 0;
    for (; inputOffset < nextInputOffset && inputOffset < input.length; inputOffset += 1) {
      sum += input[inputOffset];
      count += 1;
    }
    output[outputOffset] = count === 0 ? 0 : sum / count;
  }
  return output;
}

function pcm16Base64(input: Float32Array): string {
  const bytes = new Uint8Array(input.length * 2);
  const view = new DataView(bytes.buffer);
  for (let index = 0; index < input.length; index += 1) {
    const sample = Math.max(-1, Math.min(1, input[index]));
    view.setInt16(index * 2, sample < 0 ? sample * 0x8000 : sample * 0x7fff, true);
  }
  return encodeBase64(bytes);
}

function pcmToAudioBuffer(
  context: AudioContext,
  data: string,
  sampleRate: number,
): AudioBuffer {
  const bytes = decodeBase64(data);
  const samples = new Int16Array(bytes.buffer, bytes.byteOffset, Math.floor(bytes.byteLength / 2));
  const buffer = context.createBuffer(1, samples.length, sampleRate);
  const channel = buffer.getChannelData(0);
  for (let index = 0; index < samples.length; index += 1) channel[index] = samples[index] / 0x8000;
  return buffer;
}

function providerTranscriptText(value: unknown): string | null {
  if (typeof value === 'string') return value.trim() || null;
  if (!value || typeof value !== 'object') return null;
  const record = value as Record<string, unknown>;
  for (const key of ['transcript', 'text', 'delta']) {
    if (typeof record[key] === 'string' && record[key].trim()) return (record[key] as string).trim();
  }
  for (const key of ['item', 'content', 'part', 'parts', 'response', 'output', 'serverContent']) {
    const nested = record[key];
    if (Array.isArray(nested)) {
      for (const item of nested) {
        const text = providerTranscriptText(item);
        if (text) return text;
      }
    } else {
      const text = providerTranscriptText(nested);
      if (text) return text;
    }
  }
  return null;
}

function providerEventType(value: Record<string, unknown>): string {
  return typeof value.type === 'string'
    ? value.type
    : value.event && typeof value.event === 'object' && typeof (value.event as Record<string, unknown>).type === 'string'
      ? ((value.event as Record<string, unknown>).type as string)
      : '';
}

function audioRate(mimeType: unknown): number {
  if (typeof mimeType !== 'string') return 24_000;
  const match = mimeType.match(/rate\s*=\s*(\d+)/i);
  return match ? Number(match[1]) || 24_000 : 24_000;
}

/**
 * Appends a transcript fragment to the segment list. Consecutive fragments from
 * one speaker extend one segment. With a provider timeline interval (`spoken`),
 * a fragment spoken before the other speaker's latest segment began (late
 * transcription) joins its own speaker's previous segment instead of splitting
 * it. Returns true for such a late fragment.
 */
export function appendTranscriptFragment(
  segments: LiveVoiceTranscriptSegmentInput[],
  speaker: RealtimeVoiceSpeaker,
  fragment: string,
  exact: boolean,
  at: { startMs: number; endMs: number },
  spoken = false,
): boolean {
  const last = segments[segments.length - 1];
  const late = spoken && last !== undefined && last.speaker !== speaker && at.startMs < last.startMs;
  const target = late
    ? [...segments].reverse().find((segment) => segment.speaker === speaker)
    : last?.speaker === speaker ? last : undefined;
  if (target) {
    target.text = exact ? target.text + fragment : `${target.text} ${fragment}`;
    target.endMs = Math.max(target.endMs, at.endMs);
  } else {
    segments.push({ speaker, startMs: at.startMs, endMs: at.endMs, text: fragment });
  }
  return late && target !== undefined;
}

export function useSpeakingRealtimeVoice(
  sessionId: string,
  requestedProvider?: LiveVoiceProvider,
): UseSpeakingRealtimeVoiceResult {
  const [connection, setConnection] = useState<RealtimeVoiceConnection>('idle');
  const [phase, setPhase] = useState<RealtimeVoicePhase>('idle');
  const [preflight, setPreflight] = useState<LiveVoicePreflight | null>(null);
  const [captions, setCaptions] = useState<RealtimeVoiceCaption[]>([]);
  const [micLevel, setMicLevel] = useState(0);
  const [micEnabled, setMicEnabled] = useState(false);
  const [awaitingCandidateStart, setAwaitingCandidateStart] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [ended, setEnded] = useState(false);

  const audioRef = useRef<HTMLAudioElement | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const peerRef = useRef<RTCPeerConnection | null>(null);
  const dataChannelRef = useRef<RTCDataChannel | null>(null);
  const socketRef = useRef<WebSocket | null>(null);
  const inputContextRef = useRef<AudioContext | null>(null);
  const outputContextRef = useRef<AudioContext | null>(null);
  const inputSourceRef = useRef<MediaStreamAudioSourceNode | null>(null);
  const analyserRef = useRef<AnalyserNode | null>(null);
  const processorRef = useRef<ScriptProcessorNode | null>(null);
  const silentGainRef = useRef<GainNode | null>(null);
  const playbackSourcesRef = useRef<Set<AudioBufferSourceNode>>(new Set());
  const meterFrameRef = useRef<number | null>(null);
  const nextPlaybackTimeRef = useRef(0);
  const providerRef = useRef<LiveVoiceProvider | null>(null);
  const providerSessionIdRef = useRef<string | null>(null);
  const pendingCandidateRef = useRef('');
  const pendingPatientRef = useRef('');
  const pendingStartedAtRef = useRef<number | null>(null);
  const patientStartMsRef = useRef<number | null>(null);
  const turnIndexRef = useRef(0);
  const segmentsRef = useRef<LiveVoiceTranscriptSegmentInput[]>([]);
  const flushPromiseRef = useRef(Promise.resolve());
  const geminiReadyRef = useRef(false);
  const stoppingRef = useRef(false);
  const openAiClosedRef = useRef<(() => void) | null>(null);

  const closeTransport = useCallback(() => {
    geminiReadyRef.current = false;
    playbackSourcesRef.current.forEach((source) => {
      try {
        source.stop();
      } catch {}
    });
    playbackSourcesRef.current.clear();
    dataChannelRef.current?.close();
    dataChannelRef.current = null;
    peerRef.current?.close();
    peerRef.current = null;
    socketRef.current?.close();
    socketRef.current = null;
    streamRef.current?.getTracks().forEach((track) => track.stop());
    streamRef.current = null;
    processorRef.current?.disconnect();
    processorRef.current = null;
    inputSourceRef.current?.disconnect();
    inputSourceRef.current = null;
    analyserRef.current?.disconnect();
    analyserRef.current = null;
    silentGainRef.current?.disconnect();
    silentGainRef.current = null;
    if (meterFrameRef.current !== null) window.cancelAnimationFrame(meterFrameRef.current);
    meterFrameRef.current = null;
    void inputContextRef.current?.close().catch(() => undefined);
    void outputContextRef.current?.close().catch(() => undefined);
    inputContextRef.current = null;
    outputContextRef.current = null;
    nextPlaybackTimeRef.current = 0;
    if (audioRef.current) audioRef.current.srcObject = null;
    setMicEnabled(false);
    setMicLevel(0);
    setPhase('idle');
  }, []);

  const interruptPlayback = useCallback(() => {
    playbackSourcesRef.current.forEach((source) => {
      try {
        source.stop();
      } catch {}
    });
    playbackSourcesRef.current.clear();
    nextPlaybackTimeRef.current = outputContextRef.current?.currentTime ?? 0;
  }, []);

  const prepare = useCallback(async () => {
    if (!sessionId) return;
    setConnection('preparing');
    setError(null);
    try {
      const result = await getLiveVoicePreflight(sessionId, requestedProvider);
      setPreflight(result);
      providerRef.current = result.provider;
      setConnection('ready');
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : 'The realtime voice agent is unavailable.';
      setError(message);
      setConnection('error');
    }
  }, [requestedProvider, sessionId]);

  // One caption and one transcript segment per speaker run keeps a 5-minute
  // conversation far below the server's 600-segment limit. `exact` fragments
  // (GPT-Live deltas) carry their own spacing and provider timing; a late one
  // rejoins its speaker's text instead of splitting it (read as disfluency).
  const addCaption = useCallback((
    speaker: RealtimeVoiceSpeaker,
    text: string,
    exact = false,
    spoken?: { startMs: number; endMs: number },
  ) => {
    if (!text.trim()) return;
    const fragment = exact ? text : text.trim();
    const join = (existing: string) => (exact ? existing + fragment : `${existing} ${fragment}`);
    const now = Math.max(0, Math.round(performance.now()));
    if (pendingStartedAtRef.current === null) pendingStartedAtRef.current = now;
    const late = appendTranscriptFragment(segmentsRef.current, speaker, fragment, exact, spoken ?? { startMs: now, endMs: now }, Boolean(spoken));
    setCaptions((current) => {
      const index = late
        ? current.map((caption) => caption.speaker).lastIndexOf(speaker)
        : current[current.length - 1]?.speaker === speaker ? current.length - 1 : -1;
      if (index >= 0) return current.map((caption, i) => (i === index ? { ...caption, text: join(caption.text) } : caption));
      const id = `${speaker}-${Date.now()}-${Math.random().toString(36).slice(2)}`;
      return [...current, { id, speaker, text: fragment }].slice(-80);
    });
    if (speaker === 'candidate') setAwaitingCandidateStart(false);
  }, []);

  // Takes the pending turn synchronously, so fragments that arrive while a save
  // is in flight start the next turn instead of being cleared with this one.
  const takePendingTurn = useCallback((): Parameters<typeof persistLiveVoiceTurn>[1] | null => {
    const provider = providerRef.current;
    const providerSessionId = providerSessionIdRef.current;
    const candidateText = pendingCandidateRef.current.trim();
    const patientText = pendingPatientRef.current.trim();
    if (!provider || !providerSessionId || (!candidateText && !patientText)) return null;

    const startedAt = pendingStartedAtRef.current;
    const endedAt = Math.max(0, Math.round(performance.now()));
    turnIndexRef.current += 1;
    pendingCandidateRef.current = '';
    pendingPatientRef.current = '';
    pendingStartedAtRef.current = null;
    patientStartMsRef.current = null;
    return {
      provider,
      providerSessionId,
      candidateText: candidateText || null,
      patientText: patientText || null,
      clientTurnId: `voice-turn:${turnIndexRef.current}`,
      turnIndex: turnIndexRef.current,
      startedAt: startedAt === null ? undefined : new Date(Date.now() - Math.max(0, endedAt - startedAt)).toISOString(),
      endedAt: new Date().toISOString(),
    };
  }, []);

  const flushPendingTurn = useCallback(async () => {
    const turn = takePendingTurn();
    if (turn) await persistLiveVoiceTurn(sessionId, turn);
  }, [sessionId, takePendingTurn]);

  const queueFlush = useCallback(() => {
    const turn = takePendingTurn();
    flushPromiseRef.current = flushPromiseRef.current
      .catch(() => undefined)
      .then(async () => {
        if (turn) await persistLiveVoiceTurn(sessionId, turn);
      })
      .catch((caught) => {
        setError(caught instanceof Error ? caught.message : 'The voice transcript could not be saved.');
        throw caught;
      });
    return flushPromiseRef.current;
  }, [sessionId, takePendingTurn]);

  // Gemini streams incremental chunks: append, never de-duplicate.
  const captureTranscript = useCallback((speaker: RealtimeVoiceSpeaker, text: string) => {
    const chunk = text.trim();
    if (!chunk) return;
    addCaption(speaker, chunk);
    if (speaker === 'candidate') pendingCandidateRef.current = `${pendingCandidateRef.current} ${chunk}`.trim();
    else pendingPatientRef.current = `${pendingPatientRef.current} ${chunk}`.trim();
  }, [addCaption]);

  const handleOpenAiEvent = useCallback((value: Record<string, unknown>) => {
    const type = providerEventType(value).toLowerCase();
    if (type.includes('error')) {
      const message = providerTranscriptText(value) ?? 'The OpenAI realtime voice session reported an error.';
      setError(message);
      setConnection('error');
      return;
    }
    if (type.includes('session.closed') || type.includes('session.ended')) {
      if (stoppingRef.current) {
        openAiClosedRef.current?.();
        return;
      }
      setError('The OpenAI realtime voice session ended. End the role-play to save the completed transcript.');
      setConnection('error');
      return;
    }
    if (type.includes('session.started') || type.includes('session.created')) {
      setConnection('connected');
      setPhase('listening');
      return;
    }

    // GPT-Live sends exact transcript fragments and no turn-complete event: a
    // turn (candidate, then patient) closes when the candidate speaks again.
    const speaker: RealtimeVoiceSpeaker | null = type === 'session.input_transcript.delta'
      ? 'candidate'
      : type === 'session.output_transcript.delta' ? 'patient' : null;
    const delta = typeof value.delta === 'string' ? value.delta : '';
    if (!speaker || !delta) return;
    const spoken = typeof value.start_ms === 'number' && typeof value.end_ms === 'number'
      ? { startMs: value.start_ms, endMs: value.end_ms }
      : undefined;
    // A late candidate fragment (spoken before the patient's reply began) still
    // belongs to the current turn; only new candidate speech closes it.
    const lateCandidate = spoken !== undefined && patientStartMsRef.current !== null && spoken.startMs < patientStartMsRef.current;
    if (speaker === 'candidate' && pendingPatientRef.current.trim() && !lateCandidate) void queueFlush();
    addCaption(speaker, delta, true, spoken);
    if (speaker === 'candidate') pendingCandidateRef.current += delta;
    else {
      if (!pendingPatientRef.current) patientStartMsRef.current = spoken?.startMs ?? null;
      pendingPatientRef.current += delta;
    }
    setPhase(speaker === 'patient' ? 'speaking' : 'listening');
  }, [addCaption, queueFlush]);

  const handleGeminiMessage = useCallback((value: Record<string, unknown>) => {
    if (value.setupComplete || value.setup_complete) {
      geminiReadyRef.current = true;
      setConnection('connected');
      setPhase('listening');
      return;
    }
    if (value.error) {
      setError(providerTranscriptText(value.error) ?? 'The Gemini Live session reported an error.');
      setConnection('error');
      return;
    }

    const serverContent = (value.serverContent ?? value.server_content) as Record<string, unknown> | undefined;
    if (!serverContent) return;
    if (serverContent.interrupted === true) {
      interruptPlayback();
      setPhase('listening');
      return;
    }
    const inputTranscription = (serverContent.inputTranscription ?? serverContent.input_transcription) as unknown;
    const outputTranscription = (serverContent.outputTranscription ?? serverContent.output_transcription) as unknown;
    const inputText = providerTranscriptText(inputTranscription);
    const outputText = providerTranscriptText(outputTranscription);
    if (inputText) captureTranscript('candidate', inputText);
    if (outputText) {
      setPhase('speaking');
      captureTranscript('patient', outputText);
    }

    const modelTurn = (serverContent.modelTurn ?? serverContent.model_turn) as Record<string, unknown> | undefined;
    const parts = modelTurn?.parts;
    if (Array.isArray(parts)) {
      for (const part of parts) {
        const partRecord = part as Record<string, unknown>;
        const inline = (partRecord.inlineData ?? partRecord.inline_data) as Record<string, unknown> | undefined;
        const data = inline?.data;
        if (typeof data !== 'string') continue;
        const outputContext = outputContextRef.current;
        if (!outputContext) continue;
        const buffer = pcmToAudioBuffer(outputContext, data, audioRate(inline?.mimeType ?? inline?.mime_type));
        const source = outputContext.createBufferSource();
        source.buffer = buffer;
        source.connect(outputContext.destination);
        playbackSourcesRef.current.add(source);
        source.addEventListener('ended', () => playbackSourcesRef.current.delete(source), { once: true });
        const startAt = Math.max(outputContext.currentTime, nextPlaybackTimeRef.current);
        source.start(startAt);
        nextPlaybackTimeRef.current = startAt + buffer.duration;
      }
    }

    if (serverContent.turnComplete || serverContent.turn_complete) {
      void queueFlush();
      setPhase('listening');
    }
  }, [captureTranscript, interruptPlayback, queueFlush]);

  const configureMeter = useCallback((stream: MediaStream, context: AudioContext) => {
    const source = context.createMediaStreamSource(stream);
    const analyser = context.createAnalyser();
    analyser.fftSize = 256;
    source.connect(analyser);
    inputSourceRef.current = source;
    analyserRef.current = analyser;
    const values = new Uint8Array(analyser.fftSize);
    const tick = () => {
      if (!analyserRef.current) return;
      analyser.getByteTimeDomainData(values);
      let sum = 0;
      for (const value of values) {
        const normalized = (value - 128) / 128;
        sum += normalized * normalized;
      }
      setMicLevel(Math.min(1, Math.sqrt(sum / values.length) * 3));
      meterFrameRef.current = window.requestAnimationFrame(tick);
    };
    meterFrameRef.current = window.requestAnimationFrame(tick);
  }, []);

  const waitForIce = useCallback(async (peer: RTCPeerConnection) => {
    if (peer.iceGatheringState === 'complete') return;
    await new Promise<void>((resolve) => {
      const handleState = () => {
        if (peer.iceGatheringState !== 'complete') return;
        peer.removeEventListener('icegatheringstatechange', handleState);
        resolve();
      };
      peer.addEventListener('icegatheringstatechange', handleState);
      window.setTimeout(() => {
        peer.removeEventListener('icegatheringstatechange', handleState);
        resolve();
      }, 5_000);
    });
  }, []);

  const connectOpenAi = useCallback(async (stream: MediaStream) => {
    const peer = new RTCPeerConnection();
    peerRef.current = peer;
    stream.getAudioTracks().forEach((track) => peer.addTrack(track, stream));
    peer.ontrack = (event) => {
      const remoteStream = event.streams[0] ?? new MediaStream([event.track]);
      if (audioRef.current) {
        audioRef.current.srcObject = remoteStream;
        void audioRef.current.play().catch(() => undefined);
      }
    };
    peer.onconnectionstatechange = () => {
      if (peer.connectionState === 'connected') setConnection('connected');
      if (!stoppingRef.current && (peer.connectionState === 'failed' || peer.connectionState === 'disconnected')) {
        setError('The OpenAI realtime voice connection was interrupted.');
        setConnection('error');
      }
    };
    const dataChannel = peer.createDataChannel('oai-events');
    dataChannelRef.current = dataChannel;
    dataChannel.onopen = () => {
      setConnection('connected');
      setPhase('listening');
    };
    dataChannel.onmessage = (event) => {
      try {
        const value = JSON.parse(typeof event.data === 'string' ? event.data : '') as Record<string, unknown>;
        handleOpenAiEvent(value);
      } catch {
        setError('The OpenAI realtime voice stream returned an unreadable event.');
        setConnection('error');
      }
    };

    const offer = await peer.createOffer();
    await peer.setLocalDescription(offer);
    await waitForIce(peer);
    const localSdp = peer.localDescription?.sdp;
    if (!localSdp) throw new Error('The browser did not produce a WebRTC offer.');
    const answer: LiveVoiceOpenAiOfferResponse = await createOpenAiLiveOffer(sessionId, localSdp);
    providerSessionIdRef.current = answer.providerSessionId;
    await peer.setRemoteDescription({ type: 'answer', sdp: answer.answerSdp });
  }, [handleOpenAiEvent, sessionId, waitForIce]);

  const configureGeminiInput = useCallback((stream: MediaStream, context: AudioContext) => {
    const source = inputSourceRef.current ?? context.createMediaStreamSource(stream);
    const processor = context.createScriptProcessor(4096, 1, 1);
    const silentGain = context.createGain();
    silentGain.gain.value = 0;
    processor.onaudioprocess = (event) => {
      const socket = socketRef.current;
      if (!geminiReadyRef.current || !socket || socket.readyState !== WebSocket.OPEN) return;
      const input = event.inputBuffer.getChannelData(0);
      const samples = downsample(input, context.sampleRate, TARGET_SAMPLE_RATE);
      socket.send(JSON.stringify({
        realtimeInput: {
          audio: {
            data: pcm16Base64(samples),
            mimeType: `audio/pcm;rate=${TARGET_SAMPLE_RATE}`,
          },
        },
      }));
    };
    source.connect(processor);
    processor.connect(silentGain);
    silentGain.connect(context.destination);
    processorRef.current = processor;
    silentGainRef.current = silentGain;
  }, []);

  const connectGemini = useCallback(async (stream: MediaStream, context: AudioContext) => {
    const token: LiveVoiceGeminiTokenResponse = await createGeminiLiveToken(sessionId);
    providerSessionIdRef.current = token.providerSessionId;
    const socket = new WebSocket(token.webSocketUrl);
    // Gemini Live sends every server message as a binary frame.
    socket.binaryType = 'arraybuffer';
    socketRef.current = socket;
    outputContextRef.current = context;
    await new Promise<void>((resolve, reject) => {
      const timeout = window.setTimeout(() => reject(new Error('Gemini Live did not connect in time.')), 15_000);
      socket.onopen = () => {
        // The ephemeral token already locks the full setup (persona, audio
        // modality, transcription); the client may only name the model.
        socket.send(JSON.stringify({ setup: { model: token.model } }));
        configureGeminiInput(stream, context);
      };
      socket.onmessage = (event) => {
        try {
          const raw = typeof event.data === 'string' ? event.data : new TextDecoder().decode(event.data as ArrayBuffer);
          const value = JSON.parse(raw) as Record<string, unknown>;
          handleGeminiMessage(value);
          if (value.setupComplete || value.setup_complete) {
            window.clearTimeout(timeout);
            resolve();
          }
        } catch {
          window.clearTimeout(timeout);
          reject(new Error('The Gemini Live stream returned an unreadable event.'));
        }
      };
      socket.onerror = () => {
        window.clearTimeout(timeout);
        reject(new Error('The Gemini Live connection could not be established.'));
      };
      socket.onclose = () => {
        if (!stoppingRef.current) {
          setError('The Gemini Live connection was closed.');
          setConnection('error');
        }
      };
    });
  }, [configureGeminiInput, handleGeminiMessage, sessionId]);

  const start = useCallback(async () => {
    if (!sessionId) return false;
    if (!preflight) await prepare();
    const provider = providerRef.current;
    if (!provider) {
      setError('The realtime voice provider is not ready.');
      return false;
    }
    setError(null);
    setEnded(false);
    stoppingRef.current = false;
    setConnection('connecting');
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true, channelCount: 1 } });
      streamRef.current = stream;
      const context = new AudioContext();
      inputContextRef.current = context;
      await context.resume();
      configureMeter(stream, context);
      if (provider === 'openai') await connectOpenAi(stream);
      else await connectGemini(stream, context);
      setMicEnabled(true);
      setPhase('listening');
      return true;
    } catch (caught) {
      closeTransport();
      const message = caught instanceof Error ? caught.message : 'The native realtime voice agent could not start.';
      setError(message);
      setConnection('error');
      return false;
    }
  }, [closeTransport, configureGeminiInput, configureMeter, connectGemini, connectOpenAi, preflight, prepare, sessionId]);

  const stop = useCallback(async () => {
    const provider = providerRef.current;
    const providerSessionId = providerSessionIdRef.current;
    if (stoppingRef.current || !provider || !providerSessionId) return true;
    stoppingRef.current = true;
    setConnection('ending');
    setMicEnabled(false);
    setPhase('idle');
    try {
      // GPT-Live bills until the session closes and confirms final usage only
      // on session.closed; closing first also drains the last transcript deltas.
      const channel = dataChannelRef.current;
      if (provider === 'openai' && channel?.readyState === 'open') {
        await new Promise<void>((resolve) => {
          const timer = window.setTimeout(resolve, 5_000);
          openAiClosedRef.current = () => {
            window.clearTimeout(timer);
            resolve();
          };
          try {
            channel.send(JSON.stringify({ type: 'session.close' }));
          } catch {
            openAiClosedRef.current();
          }
        });
        openAiClosedRef.current = null;
      }
      await flushPromiseRef.current;
      await flushPendingTurn();
      await persistLiveVoiceTranscript(sessionId, {
        provider,
        providerSessionId,
        segments: segmentsRef.current,
      });
      closeTransport();
      setEnded(true);
      setConnection('ended');
      return true;
    } catch (caught) {
      stoppingRef.current = false;
      const message = caught instanceof Error ? caught.message : 'The completed voice transcript could not be saved.';
      setError(message);
      setConnection('error');
      return false;
    }
  }, [closeTransport, flushPendingTurn, sessionId]);

  useEffect(() => {
    return () => {
      void stop();
    };
  }, [stop]);

  useEffect(() => {
    setCaptions([]);
    setError(null);
    setPreflight(null);
    setEnded(false);
    providerRef.current = null;
    providerSessionIdRef.current = null;
    pendingCandidateRef.current = '';
    pendingPatientRef.current = '';
    pendingStartedAtRef.current = null;
    patientStartMsRef.current = null;
    segmentsRef.current = [];
    turnIndexRef.current = 0;
    stoppingRef.current = false;
    if (!sessionId) {
      setConnection('idle');
      return undefined;
    }
    void prepare();
    return () => {
      stoppingRef.current = true;
      closeTransport();
    };
  }, [closeTransport, prepare, sessionId]);

  return {
    connection,
    phase,
    preflight,
    captions,
    micLevel,
    micEnabled,
    awaitingCandidateStart,
    error,
    ended,
    audioRef,
    prepare,
    start,
    stop,
  };
}
