/**
 * Generic, module-agnostic wizard step model + URL helpers.
 *
 * Extracted from the mock-bundle wizard (`lib/mock-wizard/state.ts`) so that
 * any admin content type (Speaking cards, Speaking mock sets, and later
 * Listening / Writing) can drive the shared `<AdminWizard>` shell with its own
 * ordered list of steps. The shell derives the active step from the last URL
 * segment, validated against the step ids supplied here.
 *
 * This file is pure data/logic — no React, no client directive — so it can be
 * imported from both server and client modules.
 */

import type { ReactNode } from 'react';

export interface WizardStepDef {
  /** URL segment for the step, e.g. `classification`. */
  id: string;
  /** Short label shown in the stepper. */
  label: string;
  /** Optional one-line subtitle (shown in the vertical stepper). */
  description?: string;
  /** Optional leading icon for the stepper indicator. */
  icon?: ReactNode;
  /**
   * When true, the footer "Next" stays enabled even if the step has not
   * registered a passing `canAdvance` gate. Use for deferrable steps such as
   * the hidden interlocutor script or exam-level assets.
   */
  optional?: boolean;
}

/** Resolve the active step id from a pathname, falling back to the first step. */
export function getCurrentStepId(
  pathname: string | null,
  steps: WizardStepDef[],
  fallback?: string,
): string {
  const ids = steps.map((s) => s.id);
  const fallbackId = fallback ?? ids[0] ?? '';
  if (!pathname) return fallbackId;
  const parts = pathname.split('/').filter(Boolean);
  const last = parts[parts.length - 1];
  return ids.includes(last) ? last : fallbackId;
}

export function getStepIndex(stepId: string, steps: WizardStepDef[]): number {
  return steps.findIndex((s) => s.id === stepId);
}

export function getNextStepId(stepId: string, steps: WizardStepDef[]): string | null {
  const idx = getStepIndex(stepId, steps);
  if (idx < 0 || idx >= steps.length - 1) return null;
  return steps[idx + 1].id;
}

export function getPrevStepId(stepId: string, steps: WizardStepDef[]): string | null {
  const idx = getStepIndex(stepId, steps);
  if (idx <= 0) return null;
  return steps[idx - 1].id;
}
