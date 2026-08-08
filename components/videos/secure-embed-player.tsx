'use client';

import {
  forwardRef,
  useEffect,
  useImperativeHandle,
  useRef,
} from 'react';
import { BunnyEmbedController } from '@/lib/video/bunny-embed-controller';
import { cn } from '@/lib/utils';

export interface SecureEmbedPlayerHandle {
  pause(): void;
  play(): void;
  mute(): void;
  seekTo(seconds: number): void;
}

interface SecureEmbedPlayerProps {
  src: string;
  title: string;
  initialPositionSeconds: number;
  fit?: 'contain' | 'cover';
  onReady?: () => void;
  onPlay?: () => void;
  onPause?: () => void;
  onTimeUpdate?: (seconds: number, duration: number) => void;
  onEnded?: () => void;
  onError?: () => void;
}

type TimeUpdatePayload = {
  seconds?: unknown;
  duration?: unknown;
};

export const SecureEmbedPlayer = forwardRef<SecureEmbedPlayerHandle, SecureEmbedPlayerProps>(
  function SecureEmbedPlayer(
    {
      src,
      title,
      initialPositionSeconds,
      fit = 'contain',
      onReady,
      onPlay,
      onPause,
      onTimeUpdate,
      onEnded,
      onError,
    },
    ref,
  ) {
    const iframeRef = useRef<HTMLIFrameElement | null>(null);
    const controllerRef = useRef<BunnyEmbedController | null>(null);

    useImperativeHandle(ref, () => ({
      pause: () => controllerRef.current?.pause(),
      play: () => controllerRef.current?.play(),
      mute: () => controllerRef.current?.mute(),
      seekTo: (seconds: number) => controllerRef.current?.setCurrentTime(seconds),
    }));

    useEffect(() => {
      const iframe = iframeRef.current;
      if (!iframe) return;

      const controller = new BunnyEmbedController(iframe);
      controllerRef.current = controller;
      controller.on('ready', () => {
        if (initialPositionSeconds > 5) {
          controller.setCurrentTime(initialPositionSeconds);
        }
        onReady?.();
      });
      controller.on('play', () => onPlay?.());
      controller.on('pause', () => onPause?.());
      controller.on('ended', () => onEnded?.());
      controller.on('error', () => onError?.());
      controller.on('timeupdate', (value) => {
        const payload = (value ?? {}) as TimeUpdatePayload;
        const seconds = typeof payload.seconds === 'number' ? payload.seconds : Number(payload.seconds);
        const duration = typeof payload.duration === 'number' ? payload.duration : Number(payload.duration);
        if (Number.isFinite(seconds)) {
          onTimeUpdate?.(seconds, Number.isFinite(duration) ? duration : 0);
        }
      });

      return () => {
        controller.destroy();
        controllerRef.current = null;
      };
    }, [
      initialPositionSeconds,
      onEnded,
      onError,
      onPause,
      onPlay,
      onReady,
      onTimeUpdate,
      src,
    ]);

    return (
      <iframe
        ref={iframeRef}
        src={src}
        title={title}
        className={cn('h-full w-full border-0', fit === 'cover' ? 'object-cover' : 'object-contain')}
        allow="autoplay; encrypted-media; picture-in-picture; fullscreen"
        allowFullScreen
        referrerPolicy="strict-origin-when-cross-origin"
        sandbox="allow-scripts allow-same-origin allow-presentation"
      />
    );
  },
);
