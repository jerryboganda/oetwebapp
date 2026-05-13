'use client';

// Listening V2 — forward-only section stepper. Sections to the left of
// the active index are permanently locked; the active section shows
// either `active` or `reviewing` depending on FSM phase.

import { Lock, Timer } from 'lucide-react';
import {
  LISTENING_SECTION_SHORT_LABEL,
  type ListeningSectionCode,
} from '@/lib/listening-sections';

export interface ListeningSectionStepperProps {
  sections: ListeningSectionCode[];
  currentIndex: number;
  isReviewing: boolean;
}

export function ListeningSectionStepper({
  sections,
  currentIndex,
  isReviewing,
}: ListeningSectionStepperProps) {
  return (
    <div
      data-testid="listening-section-stepper"
      role="list"
      className="flex flex-wrap items-center gap-2 rounded-2xl border border-border bg-surface p-3 text-xs font-black uppercase tracking-widest"
    >
      {sections.map((code, idx) => {
        const state =
          idx < currentIndex
            ? 'locked'
            : idx === currentIndex
              ? isReviewing
                ? 'reviewing'
                : 'active'
              : 'pending';
        const stateLabel = state === 'locked'
          ? 'locked'
          : state === 'active'
            ? 'active section'
            : state === 'reviewing'
              ? 'review window'
              : 'pending';
        const label = LISTENING_SECTION_SHORT_LABEL[code];
        return (
          <span
            key={code}
            role="listitem"
            data-state={state}
            aria-current={idx === currentIndex ? 'step' : undefined}
            aria-label={`${label}, ${stateLabel}`}
            className={`inline-flex items-center gap-1 rounded-full px-3 py-1.5 ${
              state === 'locked'
                ? 'bg-background-light text-muted/60'
                : state === 'active'
                  ? 'bg-primary text-white'
                  : state === 'reviewing'
                    ? 'bg-warning/10 text-warning'
                    : 'bg-background-light text-muted'
            }`}
          >
            {state === 'locked' ? (
              <Lock className="h-3 w-3" aria-hidden="true" />
            ) : state === 'reviewing' ? (
              <Timer className="h-3 w-3" aria-hidden="true" />
            ) : null}
            {label}
          </span>
        );
      })}
      <span className="ml-auto hidden text-[10px] normal-case tracking-normal text-muted sm:inline">
        Forward-only — completed sections cannot be revisited.
      </span>
    </div>
  );
}
