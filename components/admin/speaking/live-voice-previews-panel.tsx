'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { Loader2, Play, Square, Volume2 } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  adminCreateLiveVoicePreviewOffer,
  adminGetLiveVoicePreviewList,
  type LiveVoicePreviewList,
} from '@/lib/api/speaking-live-voice-admin';

type PlayState = { cell: string; phase: 'connecting' | 'playing' } | null;

/** A silent microphone track: the provider session expects audio in both directions, the preview never sends any. */
function silentTrack(): { stream: MediaStream; close: () => void } {
  const context = new AudioContext();
  const destination = context.createMediaStreamDestination();
  return { stream: destination.stream, close: () => void context.close().catch(() => undefined) };
}

async function waitForIce(peer: RTCPeerConnection): Promise<void> {
  if (peer.iceGatheringState === 'complete') return;
  await new Promise<void>((resolve) => {
    const done = () => {
      window.clearTimeout(timer);
      peer.removeEventListener('icegatheringstatechange', onState);
      resolve();
    };
    const onState = () => {
      if (peer.iceGatheringState === 'complete') done();
    };
    const timer = window.setTimeout(done, 5_000);
    peer.addEventListener('icegatheringstatechange', onState);
  });
}

/**
 * The owner's listening check for the live AI-patient voices: one short GPT-Live clip per voice, played in the browser.
 * OpenAI is the primary provider and Gemini the fallback; only the OpenAI pool is previewed here.
 */
export function LiveVoicePreviewsPanel() {
  const [list, setList] = useState<LiveVoicePreviewList | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [playing, setPlaying] = useState<PlayState>(null);
  const [error, setError] = useState<string | null>(null);
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const cleanupRef = useRef<(() => void) | null>(null);

  useEffect(() => {
    let active = true;
    adminGetLiveVoicePreviewList()
      .then((value) => active && setList(value))
      .catch((err: unknown) => active && setLoadError(err instanceof Error ? err.message : 'Could not load the live voices.'));
    return () => {
      active = false;
      cleanupRef.current?.();
    };
  }, []);

  const stop = useCallback(() => {
    cleanupRef.current?.();
    cleanupRef.current = null;
    setPlaying(null);
  }, []);

  const play = useCallback(async (cell: string) => {
    stop();
    setError(null);
    if (typeof RTCPeerConnection === 'undefined') {
      setError('WebRTC is not available in this browser.');
      return;
    }

    setPlaying({ cell, phase: 'connecting' });
    const silent = silentTrack();
    const peer = new RTCPeerConnection();
    let timer: number | undefined;
    let closed = false;
    const close = () => {
      if (closed) return;
      closed = true;
      window.clearTimeout(timer);
      peer.close();
      silent.close();
      if (audioRef.current) audioRef.current.srcObject = null;
    };
    cleanupRef.current = close;

    try {
      silent.stream.getAudioTracks().forEach((track) => peer.addTrack(track, silent.stream));
      peer.ontrack = (event) => {
        if (audioRef.current) {
          audioRef.current.srcObject = event.streams[0] ?? new MediaStream([event.track]);
          void audioRef.current.play().catch(() => undefined);
        }
        setPlaying({ cell, phase: 'playing' });
      };
      const channel = peer.createDataChannel('oai-events');
      // The session is told to speak the sample as soon as it starts; this asks again in case it waits for a turn.
      channel.onopen = () => channel.send(JSON.stringify({ type: 'response.create' }));

      await peer.setLocalDescription(await peer.createOffer());
      await waitForIce(peer);
      const sdp = peer.localDescription?.sdp;
      if (!sdp) throw new Error('The browser did not produce a WebRTC offer.');
      const answer = await adminCreateLiveVoicePreviewOffer(cell, sdp);
      await peer.setRemoteDescription({ type: 'answer', sdp: answer.answerSdp });
      timer = window.setTimeout(() => {
        close();
        setPlaying(null);
      }, Math.max(5, answer.maxSeconds) * 1000);
    } catch (err) {
      close();
      setPlaying(null);
      setError(err instanceof Error ? err.message : 'Could not start the preview.');
    }
  }, [stop]);

  return (
    <section aria-labelledby="live-patient-voices-heading" className="space-y-3" data-testid="live-voice-previews">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="live-patient-voices-heading" className="flex items-center gap-2 text-sm font-semibold text-admin-fg-strong">
          <Volume2 className="h-4 w-4" aria-hidden="true" /> Live patient voices (Speaking)
        </h2>
        {list ? (
          <div className="flex items-center gap-2 text-xs text-admin-fg-muted" data-testid="live-voice-order">
            {list.candidateOrder.length ? (
              list.candidateOrder.map((provider, index) => (
                <Badge key={provider} variant={index === 0 ? 'success' : 'muted'} size="sm">
                  {index === 0 ? 'Primary' : 'Fallback'}: {provider === 'openai' ? 'OpenAI' : 'Gemini'}
                </Badge>
              ))
            ) : (
              <Badge variant="warning" size="sm">No live voice provider is available</Badge>
            )}
          </div>
        ) : null}
      </div>

      <p className="text-xs text-admin-fg-muted">
        OpenAI is the primary live-patient provider and Gemini the fallback (its voices stay configured but are not previewed
        here). Each button plays one short clip, about {list?.maxSeconds ?? 20} seconds, in this browser. Nothing is recorded.
      </p>
      {list && list.primaryProvider !== 'openai' ? (
        <p role="alert" className="text-xs font-medium text-admin-danger" data-testid="live-voice-primary-warning">
          The server currently orders {list.primaryProvider} first. The owner decision is OpenAI first: the
          LIVEVOICE__PRIMARYPROVIDER setting in the production environment must be removed or set to openai.
        </p>
      ) : null}

      {loadError ? <p role="alert" className="text-sm text-admin-danger">{loadError}</p> : null}
      {error ? <p role="alert" className="text-sm text-admin-danger" data-testid="live-voice-error">{error}</p> : null}

      {list ? (
        <>
          <p className="text-xs italic text-admin-fg-muted">&ldquo;{list.sampleText}&rdquo;</p>
          <div className="overflow-x-auto rounded-admin-lg border border-admin-border">
            <table className="w-full text-sm">
              <thead className="bg-admin-bg-subtle text-left text-xs uppercase tracking-wide text-admin-fg-muted">
                <tr>
                  <th scope="col" className="p-3">Patient</th>
                  <th scope="col" className="p-3">OpenAI voice</th>
                  <th scope="col" className="p-3">Gemini fallback</th>
                  <th scope="col" className="p-3"><span className="sr-only">Listen</span></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-admin-border">
                {list.cells.map((cell) => {
                  const active = playing?.cell === cell.key;
                  return (
                    <tr key={cell.key} data-testid="live-voice-row">
                      <td className="p-3 text-admin-fg-strong">{cell.gender}, {cell.ageBand}</td>
                      <td className="p-3">
                        <span className="font-medium capitalize">{cell.openAiVoice || 'provider default'}</span>
                        {cell.accentNote ? <span className="ml-2 text-xs text-admin-fg-muted">{cell.accentNote}</span> : null}
                      </td>
                      <td className="p-3 text-admin-fg-muted">{cell.geminiVoice || 'provider default'}</td>
                      <td className="p-3 text-right">
                        {active ? (
                          <Button size="sm" variant="outline" onClick={stop} aria-label={`Stop ${cell.openAiVoice}`}>
                            {playing?.phase === 'connecting' ? <Loader2 className="mr-1 h-4 w-4 animate-spin" aria-hidden="true" /> : <Square className="mr-1 h-4 w-4" aria-hidden="true" />}
                            {playing?.phase === 'connecting' ? 'Connecting' : 'Stop'}
                          </Button>
                        ) : (
                          <Button
                            size="sm"
                            variant="outline"
                            disabled={playing !== null}
                            onClick={() => void play(cell.key)}
                            aria-label={`Play ${cell.openAiVoice}`}
                          >
                            <Play className="mr-1 h-4 w-4" aria-hidden="true" /> Play
                          </Button>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </>
      ) : !loadError ? (
        <p className="text-sm text-admin-fg-muted">Loading the live voices...</p>
      ) : null}

      {/* The provider's audio plays here. */}
      <audio ref={audioRef} autoPlay className="hidden" />
    </section>
  );
}
