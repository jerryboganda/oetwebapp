'use client';

/**
 * Registers a wizard step's "can advance" gate and "submit" handler with the
 * enclosing `<AdminWizard>`. Wraps the two registration effects so each step
 * is a one-liner. Pass a `useCallback`-memoised `submit` to avoid re-registering
 * on every render.
 */

import { useEffect } from 'react';
import { useAdminWizard } from '@/components/domain/wizard/useAdminWizard';

export function useStepRegistration(
  stepId: string,
  opts: { canAdvance: boolean; submit: (() => Promise<void>) | null },
): void {
  const { registerCanAdvance, registerStepSubmit } = useAdminWizard();

  useEffect(() => {
    registerCanAdvance(stepId, opts.canAdvance);
  }, [stepId, opts.canAdvance, registerCanAdvance]);

  useEffect(() => {
    registerStepSubmit(stepId, opts.submit);
    return () => registerStepSubmit(stepId, null);
  }, [stepId, opts.submit, registerStepSubmit]);
}
