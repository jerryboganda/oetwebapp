'use client';

/**
 * Generic multi-step admin wizard shell.
 *
 * A module-agnostic generalisation of the mock-bundle `WizardShell`
 * (`components/domain/mock-wizard/WizardShell.tsx`). The proven mechanics —
 * the per-step `registerCanAdvance` / `registerStepSubmit` contract, the
 * Back / Save / Next footer, focus-on-step-change for keyboard users, and the
 * saved-at timestamp — are preserved. Three things are parameterised so any
 * content type can drive it:
 *   - `steps`         : the ordered `WizardStepDef[]` (instead of a fixed union)
 *   - `buildStepHref` : the per-step route (instead of a hardcoded mock path)
 *   - `refresh`       : how to re-fetch the entity (instead of fetchAdminMockBundle)
 *
 * The mock-bundle wizard is left untouched; this is additive so it cannot
 * regress. Mocks can later migrate onto this shell because the registration
 * contract is identical.
 *
 * All navigation (Back, Next, stepper click) persists the current step first,
 * so a data-entry operator never loses edits by moving between steps. If a save
 * throws, navigation is aborted and the step surfaces its own error.
 */

import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import { usePathname, useRouter } from 'next/navigation';
import { ChevronLeft, ChevronRight, Loader2, Save } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Stepper, type Step } from '@/components/ui/stepper';
import { AdminWizardContext, type AdminWizardContextValue } from './useAdminWizard';
import {
  getCurrentStepId,
  getStepIndex,
  getNextStepId,
  getPrevStepId,
  type WizardStepDef,
} from './wizard-config';

export interface AdminWizardProps<TEntity> {
  /** Initial server snapshot; the shell keeps it in sync via `refresh`. */
  entity: TEntity;
  steps: WizardStepDef[];
  /** Build the route for a step id (replaces the mock wizard's hardcoded path). */
  buildStepHref: (stepId: string) => string;
  /** Re-fetch the entity after a save (replaces fetchAdminMockBundle). */
  refresh: () => Promise<TEntity>;
  /** Header content (title / status badge / id) — the host supplies its own. */
  header?: ReactNode;
  canWrite: boolean;
  canPublish: boolean;
  /** The step body for the active step. */
  children: ReactNode;
}

export function AdminWizard<TEntity>({
  entity: initialEntity,
  steps,
  buildStepHref,
  refresh,
  header,
  canWrite,
  canPublish,
  children,
}: AdminWizardProps<TEntity>) {
  const router = useRouter();
  const pathname = usePathname();
  const currentStepId = getCurrentStepId(pathname, steps);
  const currentStepIndex = getStepIndex(currentStepId, steps);
  const currentStep = currentStepIndex >= 0 ? steps[currentStepIndex] : undefined;

  const [entity, setEntity] = useState<TEntity>(initialEntity);
  const [saving, setSaving] = useState(false);
  const [navigating, setNavigating] = useState(false);
  const [savedAt, setSavedAt] = useState<number | null>(null);
  // canAdvance + submit-presence live in state (not only refs) so the footer
  // buttons reflect gate/registration changes live as the operator types.
  const [canAdvanceByStep, setCanAdvanceByStep] = useState<Record<string, boolean>>({});
  const [hasSubmitByStep, setHasSubmitByStep] = useState<Record<string, boolean>>({});

  const submitMap = useRef<Record<string, (() => Promise<void>) | null>>({});
  const focusRef = useRef<HTMLDivElement | null>(null);

  // Keep the shared snapshot aligned if the host swaps in a new entity
  // (e.g. routing from the create flow into a freshly-created card id).
  useEffect(() => {
    setEntity(initialEntity);
  }, [initialEntity]);

  // Move focus to the step container on step change for keyboard users.
  useEffect(() => {
    focusRef.current?.focus();
  }, [currentStepId]);

  const doRefresh = useCallback(async () => {
    const next = await refresh();
    setEntity(next);
    setSavedAt(Date.now());
  }, [refresh]);

  const registerCanAdvance = useCallback((stepId: string, canAdvance: boolean) => {
    setCanAdvanceByStep((prev) => (prev[stepId] === canAdvance ? prev : { ...prev, [stepId]: canAdvance }));
  }, []);

  const registerStepSubmit = useCallback(
    (stepId: string, submit: (() => Promise<void>) | null) => {
      submitMap.current[stepId] = submit;
      const has = Boolean(submit);
      setHasSubmitByStep((prev) => (prev[stepId] === has ? prev : { ...prev, [stepId]: has }));
    },
    [],
  );

  const goToStep = useCallback(
    (stepId: string) => {
      router.push(buildStepHref(stepId));
    },
    [router, buildStepHref],
  );

  // Persist the current step, then navigate. Used by Back, Next and the
  // stepper so no edits are silently lost between steps.
  const navigateWithSave = useCallback(
    async (stepId: string) => {
      if (saving || navigating) return;
      const submit = submitMap.current[currentStepId];
      setNavigating(true);
      try {
        if (submit) await submit();
        setSavedAt(Date.now());
        goToStep(stepId);
      } finally {
        setNavigating(false);
      }
    },
    [currentStepId, goToStep, navigating, saving],
  );

  const handleSave = useCallback(async () => {
    if (saving || navigating) return;
    const submit = submitMap.current[currentStepId];
    if (!submit) return;
    setNavigating(true);
    try {
      await submit();
      setSavedAt(Date.now());
    } finally {
      setNavigating(false);
    }
  }, [currentStepId, navigating, saving]);

  const value = useMemo<AdminWizardContextValue<TEntity>>(
    () => ({
      entity,
      refresh: doRefresh,
      steps,
      currentStepId,
      currentStepIndex,
      goToStep: (stepId: string) => void navigateWithSave(stepId),
      setSaving,
      isSaving: saving,
      savedAt,
      registerCanAdvance,
      registerStepSubmit,
      canWrite,
      canPublish,
    }),
    [
      entity,
      doRefresh,
      steps,
      currentStepId,
      currentStepIndex,
      navigateWithSave,
      saving,
      savedAt,
      registerCanAdvance,
      registerStepSubmit,
      canWrite,
      canPublish,
    ],
  );

  const nextStepId = getNextStepId(currentStepId, steps);
  const prevStepId = getPrevStepId(currentStepId, steps);
  const nextStepLabel = nextStepId ? steps[currentStepIndex + 1]?.label : null;

  const stepperSteps: Step[] = steps.map((s) => ({
    id: s.id,
    label: s.label,
    description: s.description,
    icon: s.icon,
  }));

  const busy = saving || navigating;
  const nextDisabled =
    busy || !nextStepId || (canAdvanceByStep[currentStepId] === false && !currentStep?.optional);

  return (
    <AdminWizardContext.Provider value={value as AdminWizardContextValue}>
      <div className="space-y-6">
        {/* Stepper */}
        <nav aria-label="Wizard steps" className="rounded-2xl border border-border bg-surface p-4">
          <Stepper
            steps={stepperSteps}
            currentStep={currentStepIndex < 0 ? 0 : currentStepIndex}
            onStepClick={(idx) => void navigateWithSave(steps[idx].id)}
          />
        </nav>

        {/* Entity header */}
        {header ? (
          <div className="flex flex-wrap items-center justify-between gap-2 rounded-2xl border border-border bg-background-light px-4 py-3">
            <div className="text-sm">{header}</div>
            <div className="flex items-center gap-2">
              {savedAt ? (
                <span className="text-xs text-muted">Saved {new Date(savedAt).toLocaleTimeString()}</span>
              ) : null}
              {busy ? (
                <span className="inline-flex items-center gap-1 text-xs text-muted">
                  <Loader2 className="h-3 w-3 animate-spin" /> Saving…
                </span>
              ) : null}
            </div>
          </div>
        ) : null}

        {/* Step body */}
        <div
          ref={focusRef}
          tabIndex={-1}
          aria-label={`Wizard step: ${currentStep?.label ?? ''}`}
          className="outline-none"
        >
          {children}
        </div>

        {/* Footer nav */}
        <div className="flex items-center justify-between gap-2 rounded-2xl border border-border bg-surface p-3">
          <Button
            variant="outline"
            onClick={() => prevStepId && void navigateWithSave(prevStepId)}
            disabled={!prevStepId || busy}
          >
            <ChevronLeft className="mr-1 h-4 w-4" /> Back
          </Button>
          <div className="flex items-center gap-2">
            <Button
              variant="secondary"
              onClick={() => void handleSave()}
              disabled={busy || !canWrite || !hasSubmitByStep[currentStepId]}
            >
              <Save className="mr-1 h-4 w-4" /> Save
            </Button>
            <Button
              variant="primary"
              onClick={() => nextStepId && void navigateWithSave(nextStepId)}
              disabled={nextDisabled}
            >
              {busy ? <Loader2 className="mr-1 h-4 w-4 animate-spin" /> : null}
              {nextStepId ? `Next: ${nextStepLabel ?? ''}` : 'Final step'}
              {nextStepId ? <ChevronRight className="ml-1 h-4 w-4" /> : null}
            </Button>
          </div>
        </div>
      </div>
    </AdminWizardContext.Provider>
  );
}
