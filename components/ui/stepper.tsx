'use client';

import { cn } from '@/lib/utils';
import { Check } from 'lucide-react';
import { type ReactNode } from 'react';
import { motion, useReducedMotion, AnimatePresence } from 'motion/react';
import { motionTokens, prefersReducedMotion } from '@/lib/motion';

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
  const reducedMotion = prefersReducedMotion(useReducedMotion());
  const springTransition = reducedMotion
    ? { duration: motionTokens.duration.instant }
    : { type: 'spring' as const, stiffness: 500, damping: 35, mass: 0.7 };

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
              <motion.div
                layout
                transition={springTransition}
                className={cn(
                  'w-10 h-10 rounded-full flex items-center justify-center text-sm font-bold shrink-0',
                  isComplete && 'bg-primary text-white',
                  isCurrent && 'bg-primary/10 text-primary border-2 border-primary',
                  isUpcoming && 'bg-gray-100 text-muted border border-gray-200',
                )}
                aria-current={isCurrent ? 'step' : undefined}
              >
                <AnimatePresence mode="wait" initial={false}>
                  {isComplete ? (
                    <motion.span
                      key="check"
                      initial={{ opacity: 0, scale: 0.5 }}
                      animate={{ opacity: 1, scale: 1 }}
                      exit={{ opacity: 0, scale: 0.5 }}
                      transition={springTransition}
                    >
                      <Check className="w-4 h-4" />
                    </motion.span>
                  ) : (
                    <motion.span
                      key={`step-${idx}`}
                      initial={{ opacity: 0 }}
                      animate={{ opacity: 1 }}
                      exit={{ opacity: 0 }}
                      transition={{ duration: motionTokens.duration.fast }}
                    >
                      {step.icon || idx + 1}
                    </motion.span>
                  )}
                </AnimatePresence>
              </motion.div>
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
                <motion.div
                  className="h-full bg-primary rounded"
                  initial={{ width: '0%' }}
                  animate={{ width: isComplete ? '100%' : '0%' }}
                  transition={springTransition}
                />
              </div>
            )}
          </div>
        );
      })}
    </nav>
  );
}
