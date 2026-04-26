'use client';

import { cn } from '@/lib/utils';
import { Check } from 'lucide-react';
import { type ReactNode } from 'react';

export interface Step {
  id: string;
  label: string;
  description?: string;
  icon?: ReactNode;
}

interface StepperProps {
  steps: Step[];
  currentStep: number; // 0-indexed
  className?: string;
  orientation?: 'horizontal' | 'vertical';
}

export function Stepper({ steps, currentStep, className, orientation = 'horizontal' }: StepperProps) {
  const isVertical = orientation === 'vertical';

  return (
    <nav aria-label="Progress" className={cn(isVertical ? 'flex flex-col gap-4' : 'flex items-center gap-2', className)}>
      {steps.map((step, idx) => {
        const isComplete = idx < currentStep;
        const isCurrent = idx === currentStep;
        const isUpcoming = idx > currentStep;

        return (
          <div
            key={step.id}
            className={cn(
              isVertical ? 'flex items-start gap-3' : 'flex items-center gap-2',
              !isVertical && idx < steps.length - 1 && 'flex-1',
            )}
          >
            {/* Step indicator */}
            <div className="flex items-center gap-2">
              <div
                className={cn(
                  'w-10 h-10 rounded-full flex items-center justify-center text-sm font-bold shrink-0',
                  'motion-safe:transition-colors motion-safe:duration-300 motion-safe:ease-out',
                  isComplete && 'bg-primary text-white',
                  isCurrent && 'bg-primary/10 text-primary border-2 border-primary',
                  isUpcoming && 'bg-gray-100 text-muted border border-gray-200',
                )}
                aria-current={isCurrent ? 'step' : undefined}
              >
                {isComplete ? (
                  <span className="inline-flex">
                    <Check className="w-4 h-4" />
                  </span>
                ) : (
                  <span className="inline-flex">
                    {step.icon || idx + 1}
                  </span>
                )}
              </div>
              <div className={cn(!isVertical && 'hidden sm:block')}>
                <p className={cn('text-xs font-semibold', isCurrent ? 'text-primary' : isComplete ? 'text-navy' : 'text-muted')}>
                  {step.label}
                </p>
                {step.description && isVertical && (
                  <p className="text-xs text-muted mt-0.5">{step.description}</p>
                )}
              </div>
            </div>
            {/* Connector line */}
            {!isVertical && idx < steps.length - 1 && (
              <div className="flex-1 h-0.5 rounded bg-gray-200 overflow-hidden">
                <div
                  data-state={isComplete ? 'complete' : 'incomplete'}
                  className={cn(
                    'h-full bg-primary rounded origin-left',
                    'motion-safe:transition-transform motion-safe:duration-500 motion-safe:ease-out',
                    'data-[state=complete]:scale-x-100 data-[state=incomplete]:scale-x-0',
                  )}
                />
              </div>
            )}
          </div>
        );
      })}
    </nav>
  );
}
