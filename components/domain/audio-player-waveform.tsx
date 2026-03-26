'use client';

import { useEffect, useRef, useState } from 'react';
import WaveSurfer from 'wavesurfer.js';
import { Button } from '@/components/ui/button';
import { Play, Pause, FastForward } from 'lucide-react';
import { cn } from '@/lib/utils';
import { Select } from '@/components/ui/form-controls';

interface AudioPlayerWaveformProps {
  audioUrl: string;
  onTimeUpdate?: (time: number) => void;
  seekToTime?: number | null;
  className?: string;
}

export function AudioPlayerWaveform({ audioUrl, onTimeUpdate, seekToTime, className }: AudioPlayerWaveformProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const waveSurferRef = useRef<WaveSurfer | null>(null);
  const [isPlaying, setIsPlaying] = useState(false);
  const [playbackRate, setPlaybackRate] = useState(1);
  const [isReady, setIsReady] = useState(false);

  useEffect(() => {
    if (!containerRef.current) return;

    const ws = WaveSurfer.create({
      container: containerRef.current,
      waveColor: '#d1d5db',
      progressColor: '#3b82f6',
      cursorColor: '#1d4ed8',
      barWidth: 2,
      barGap: 1,
      barRadius: 2,
      height: 60,
      normalize: true,
    });

    waveSurferRef.current = ws;

    ws.load(audioUrl);

    ws.on('ready', () => {
      setIsReady(true);
    });

    ws.on('play', () => setIsPlaying(true));
    ws.on('pause', () => setIsPlaying(false));
    ws.on('timeupdate', (currentTime) => {
      if (onTimeUpdate) onTimeUpdate(currentTime);
    });

    return () => {
      ws.destroy();
      if (audioUrl.startsWith('blob:')) {
        URL.revokeObjectURL(audioUrl);
      }
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [audioUrl]);

  // Handle external seek request
  useEffect(() => {
    if (seekToTime !== null && seekToTime !== undefined && waveSurferRef.current && isReady) {
      const duration = waveSurferRef.current.getDuration();
      if (duration > 0) {
        waveSurferRef.current.seekTo(seekToTime / duration);
      }
    }
  }, [seekToTime, isReady]);

  const togglePlay = () => {
    if (waveSurferRef.current) {
      waveSurferRef.current.playPause();
    }
  };

  const handleRateChange = (rate: string) => {
    const numRate = parseFloat(rate);
    setPlaybackRate(numRate);
    if (waveSurferRef.current) {
      waveSurferRef.current.setPlaybackRate(numRate);
    }
  };

  return (
    <div className={cn('p-4 bg-white border border-gray-200 rounded-lg flex flex-col gap-3', className)} role="region" aria-label="Audio player">
      <div ref={containerRef} className="w-full" aria-hidden="true" />
      
      <div className="flex items-center justify-between">
        <Button 
          variant="outline" 
          size="sm" 
          onClick={togglePlay} 
          disabled={!isReady}
          className="w-10 h-10 p-0"
          aria-label={isPlaying ? 'Pause audio' : 'Play audio'}
        >
          {isPlaying ? <Pause className="w-5 h-5" /> : <Play className="w-5 h-5 ml-1" />}
        </Button>

        <div className="flex items-center gap-2">
          <FastForward className="w-4 h-4 text-muted" />
          <Select 
            value={playbackRate.toString()}
            onChange={(e) => handleRateChange(e.target.value)}
            options={[
              { value: '0.75', label: '0.75x' },
              { value: '1', label: '1x (Normal)' },
              { value: '1.25', label: '1.25x' },
              { value: '1.5', label: '1.5x' },
              { value: '2', label: '2x' },
            ]}
            className="h-8 py-1 text-xs min-w-[100px]"
          />
        </div>
      </div>
    </div>
  );
}
