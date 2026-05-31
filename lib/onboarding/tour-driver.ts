import type { DriveStep } from 'driver.js';
import type { TourDefinition, TourStep } from './tour-types';

export interface RunTourCallbacks {
  /** Disable Driver.js animation when the user prefers reduced motion. */
  reducedMotion?: boolean;
  onStepViewed?: (stepIndex: number, step: TourStep) => void;
  /** Fired when the user reaches and finishes the last step ("Finish"). */
  onComplete?: () => void;
  /** Fired when the user closes the tour early (X / overlay / Esc). */
  onSkip?: (atStepIndex: number) => void;
}

function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

function describe(step: TourStep): string {
  const body = `<p class="oet-tour-body">${escapeHtml(step.body)}</p>`;
  if (!step.why) return body;
  return `${body}<p class="oet-tour-why"><span>Why this matters</span>${escapeHtml(step.why)}</p>`;
}

function toDriveStep(step: TourStep): DriveStep {
  const popover: DriveStep['popover'] = {
    title: step.title,
    description: describe(step),
  };
  if (step.side && step.side !== 'over') popover.side = step.side;
  if (step.align) popover.align = step.align;
  return step.target
    ? { element: `[data-tour="${step.target}"]`, popover }
    : { popover };
}

/**
 * Drives a tour with Driver.js. Lazily imports the library (SSR-safe) and the
 * themed CSS is loaded by the TourProvider. Steps whose target element is not
 * currently mounted are skipped, so a tour degrades gracefully across routes.
 */
export async function runTour(def: TourDefinition, cb: RunTourCallbacks = {}): Promise<void> {
  if (typeof window === 'undefined') return;

  const keptSteps: TourStep[] = [];
  const driveSteps: DriveStep[] = [];
  for (const step of def.steps) {
    if (step.target && !document.querySelector(`[data-tour="${step.target}"]`)) continue;
    keptSteps.push(step);
    driveSteps.push(toDriveStep(step));
  }
  if (driveSteps.length === 0) return;

  const { driver } = await import('driver.js');

  let completed = false;
  let activeIndex = 0;

  const instance = driver({
    showProgress: keptSteps.length > 1,
    progressText: 'Step {{current}} of {{total}}',
    nextBtnText: 'Next',
    prevBtnText: 'Back',
    doneBtnText: 'Finish',
    animate: !cb.reducedMotion,
    smoothScroll: !cb.reducedMotion,
    allowClose: true,
    overlayOpacity: 0.6,
    stagePadding: 6,
    stageRadius: 12,
    popoverClass: 'oet-tour-popover',
    onHighlightStarted: (_element, _step, options) => {
      activeIndex = options.state.activeIndex ?? activeIndex;
      const step = keptSteps[activeIndex];
      if (step) cb.onStepViewed?.(activeIndex, step);
    },
    onDestroyStarted: () => {
      // No next step means we are on the final step → treat as completed.
      if (!instance.hasNextStep()) completed = true;
      instance.destroy();
    },
    onDestroyed: () => {
      if (completed) cb.onComplete?.();
      else cb.onSkip?.(activeIndex);
    },
    steps: driveSteps,
  });

  instance.drive();
}
