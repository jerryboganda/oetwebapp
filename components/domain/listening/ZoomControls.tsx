'use client';

import { Minus, Plus, RotateCcw } from 'lucide-react';
import { Button } from '@/components/ui/button';

export const LISTENING_ZOOM_MIN = 90;
export const LISTENING_ZOOM_MAX = 130;
export const LISTENING_ZOOM_STEP = 10;

export interface ZoomControlsProps {
  value: number;
  onChange: (value: number) => void;
}

function clampZoom(value: number) {
  return Math.min(LISTENING_ZOOM_MAX, Math.max(LISTENING_ZOOM_MIN, value));
}

export function ZoomControls({ value, onChange }: ZoomControlsProps) {
  const zoom = clampZoom(value);

  const changeZoom = (nextValue: number) => {
    onChange(clampZoom(nextValue));
  };

  return (
    <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-border bg-surface px-3 py-2 shadow-sm">
      <div>
        <p className="text-xs font-black uppercase tracking-widest text-muted">Question zoom</p>
        <p className="text-xs text-muted" aria-live="polite">Current zoom {zoom}%</p>
      </div>
      <div className="flex items-center gap-2" aria-label="Question zoom controls">
        <Button
          type="button"
          variant="outline"
          size="sm"
          aria-label="Decrease question zoom"
          onClick={() => changeZoom(zoom - LISTENING_ZOOM_STEP)}
          disabled={zoom <= LISTENING_ZOOM_MIN}
        >
          <Minus className="h-4 w-4" aria-hidden="true" />
        </Button>
        <span className="min-w-14 text-center font-mono text-sm font-black text-navy" aria-hidden="true">
          {zoom}%
        </span>
        <Button
          type="button"
          variant="outline"
          size="sm"
          aria-label="Increase question zoom"
          onClick={() => changeZoom(zoom + LISTENING_ZOOM_STEP)}
          disabled={zoom >= LISTENING_ZOOM_MAX}
        >
          <Plus className="h-4 w-4" aria-hidden="true" />
        </Button>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          aria-label="Reset question zoom"
          onClick={() => changeZoom(100)}
          disabled={zoom === 100}
        >
          <RotateCcw className="h-4 w-4" aria-hidden="true" />
        </Button>
      </div>
    </div>
  );
}