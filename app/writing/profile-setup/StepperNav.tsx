'use client';

import Link from 'next/link';
import { Check } from 'lucide-react';
import { cn } from '@/lib/utils';
import { WIZARD_STEPS } from './wizard-state';

export interface StepperNavProps {
  currentStep: 'profession' | 'goals' | 'focus' | 'confirm';
}

export function StepperNav({ currentStep }: StepperNavProps) {
  const currentIndex = WIZARD_STEPS.find((s) => s.code === currentStep)?.index ?? 1;

  return (
    <nav aria-label="Profile setup steps" className="flex items-center justify-center gap-2">
      <ol className="flex flex-wrap items-center gap-3 sm:gap-4">
        {WIZARD_STEPS.map((step) => {
          const isComplete = step.index < currentIndex;
          const isCurrent = step.code === currentStep;
          const isReachable = step.index <= currentIndex;
          const stateLabel = isComplete ? 'completed' : isCurrent ? 'current' : 'upcoming';
          return (
            <li key={step.code} className="flex items-center gap-2">
              {isReachable ? (
                <Link
                  href={`/writing/profile-setup/${step.code}`}
                  className={cn(
                    'flex items-center gap-2 rounded-full border px-3 py-1.5 text-xs font-semibold transition-colors',
                    'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2',
                    isCurrent
                      ? 'border-primary bg-primary/10 text-primary'
                      : isComplete
                        ? 'border-emerald-300 bg-emerald-50 text-emerald-700'
                        : 'border-border bg-background text-navy hover:border-primary/40',
                  )}
                  aria-current={isCurrent ? 'step' : undefined}
                  aria-label={`Step ${step.index} of ${WIZARD_STEPS.length}: ${step.label}, ${stateLabel}`}
                >
                  <span
                    className={cn(
                      'inline-flex h-5 w-5 items-center justify-center rounded-full text-[10px] font-bold',
                      isCurrent
                        ? 'bg-primary text-white dark:bg-violet-700'
                        : isComplete
                          ? 'bg-emerald-500 text-white'
                          : 'bg-background-light text-muted',
                    )}
                  >
                    {isComplete ? <Check className="h-3 w-3" aria-hidden="true" /> : step.index}
                  </span>
                  {step.label}
                </Link>
              ) : (
                <span
                  className="flex items-center gap-2 rounded-full border border-dashed border-border bg-background px-3 py-1.5 text-xs font-semibold text-muted"
                  aria-label={`Step ${step.index} of ${WIZARD_STEPS.length}: ${step.label}, locked`}
                >
                  <span className="inline-flex h-5 w-5 items-center justify-center rounded-full bg-background-light text-[10px] font-bold text-muted">
                    {step.index}
                  </span>
                  {step.label}
                </span>
              )}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}
