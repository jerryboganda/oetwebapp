'use client';

/**
 * Context + hook for the generic `<AdminWizard>` shell.
 *
 * The context is intentionally entity-agnostic: it carries an opaque `entity`
 * snapshot plus a `refresh()` callback the host supplies, so the same shell
 * drives Speaking cards, Speaking mock sets, and (later) other content types.
 * Step components register their per-step "can advance" gate and "submit"
 * handler through here; the shell reads them to drive the Back/Save/Next footer.
 */

import { createContext, useContext } from 'react';
import type { WizardStepDef } from './wizard-config';

export interface AdminWizardContextValue<TEntity = unknown> {
  /** Latest server snapshot of the entity being edited. */
  entity: TEntity;
  /** Re-fetch the entity from the server and refresh the shared snapshot. */
  refresh: () => Promise<void>;
  steps: WizardStepDef[];
  currentStepId: string;
  currentStepIndex: number;
  /** Navigate to a step, running the current step's submit first. */
  goToStep: (stepId: string) => void;
  /** Lets a step toggle the shell's "Saving…" affordance for its own async. */
  setSaving: (saving: boolean) => void;
  isSaving: boolean;
  savedAt: number | null;
  registerCanAdvance: (stepId: string, canAdvance: boolean) => void;
  registerStepSubmit: (stepId: string, submit: (() => Promise<void>) | null) => void;
  /** Resolved once at the shell so steps don't re-derive permissions. */
  canWrite: boolean;
  canPublish: boolean;
}

export const AdminWizardContext = createContext<AdminWizardContextValue | null>(null);

export function useAdminWizard<TEntity = unknown>(): AdminWizardContextValue<TEntity> {
  const ctx = useContext(AdminWizardContext);
  if (!ctx) throw new Error('useAdminWizard must be used inside <AdminWizard>');
  return ctx as AdminWizardContextValue<TEntity>;
}
