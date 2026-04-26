'use client';

import { cn } from '@/lib/utils';
import { Mic, Volume2, CheckCircle2, AlertCircle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { useState } from 'react';

type CheckStep = 'permission' | 'record' | 'playback' | 'noise';
type StepStatus = 'pending' | 'active' | 'passed' | 'failed';

interface MicCheckPanelProps {
  onComplete?: () => void;
  className?: string;
}

export function MicCheckPanel({ onComplete, className }: MicCheckPanelProps) {
  const [steps, setSteps] = useState<Record<CheckStep, StepStatus>>({
    permission: 'active',
    record: 'pending',
    playback: 'pending',
    noise: 'pending',
  });
  const [error] = useState<string>();

  const stepLabels: Record<CheckStep, { label: string; icon: typeof Mic }> = {
    permission: { label: 'Microphone Permission', icon: Mic },
    record: { label: 'Recording Test', icon: Mic },
    playback: { label: 'Playback Verification', icon: Volume2 },
    noise: { label: 'Background Noise Check', icon: Volume2 },
  };

  const advanceStep = (current: CheckStep, next: CheckStep | null) => {
    setSteps((prev) => ({
      ...prev,
      [current]: 'passed',
      ...(next ? { [next]: 'active' } : {}),
    }));
    if (!next) onComplete?.();
  };

  const stepOrder: CheckStep[] = ['permission', 'record', 'playback', 'noise'];

  return (
    <div className={cn('flex flex-col gap-4', className)}>
      <InlineAlert variant="info">
        We need to check your microphone and environment before you start the speaking task. This ensures your recording will be clear.
      </InlineAlert>

      <div className="flex flex-col gap-3">
        {stepOrder.map((step, idx) => {
          const s = steps[step];
          const config = stepLabels[step];
          const Icon = config.icon;
          const nextStep = idx < stepOrder.length - 1 ? stepOrder[idx + 1] : null;

          return (
            <div key={step} className={cn(
              'flex items-center gap-3 p-4 rounded border transition-colors',
              s === 'active' && 'border-primary bg-primary/5',
              s === 'passed' && 'border-emerald-200 bg-emerald-50/50',
              s === 'failed' && 'border-red-200 bg-red-50/50',
              s === 'pending' && 'border-border bg-background-light opacity-50',
            )}>
              <div className={cn(
                'w-10 h-10 rounded-full flex items-center justify-center shrink-0',
                s === 'passed' && 'bg-emerald-100 text-emerald-600',
                s === 'failed' && 'bg-red-100 text-red-600',
                s === 'active' && 'bg-primary/10 text-primary',
                s === 'pending' && 'bg-background-light text-muted',
              )}>
                {s === 'passed' ? <CheckCircle2 className="w-5 h-5" /> :
                 s === 'failed' ? <AlertCircle className="w-5 h-5" /> :
                 <Icon className="w-5 h-5" />}
              </div>
              <div className="flex-1">
                <p className="text-sm font-semibold text-navy">{config.label}</p>
                {s === 'passed' && <p className="text-xs text-emerald-600">Passed</p>}
                {s === 'failed' && <p className="text-xs text-red-600">Failed — please try again</p>}
              </div>
              {s === 'active' && (
                <Button size="sm" onClick={() => advanceStep(step, nextStep)}>
                  {step === 'permission' ? 'Allow Access' : step === 'record' ? 'Record' : step === 'playback' ? 'Play Back' : 'Check'}
                </Button>
              )}
              {s === 'failed' && (
                <Button size="sm" variant="outline" onClick={() => setSteps(prev => ({ ...prev, [step]: 'active' }))}>
                  Retry
                </Button>
              )}
            </div>
          );
        })}
      </div>

      {error && (
        <InlineAlert variant="error" dismissible>
          {error}
        </InlineAlert>
      )}
    </div>
  );
}
