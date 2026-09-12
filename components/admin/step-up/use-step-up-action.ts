'use client';

import { useCallback, useState } from 'react';

import { isStepUpRequiredError, requestStepUp } from '@/lib/api/step-up';

type StepUpAction<T> = () => Promise<T>;

/**
 * Wraps a step-up protected admin action so a `step_up_required` rejection prompts
 * for an authenticator code and then replays the action with `onSuccess`.
 *
 * The action must already carry its scope (the domain helpers in
 * `lib/api/billing-expansion.ts` / `lib/api/billing-products.ts` attach it), so the
 * replay picks up the freshly cached proof automatically.
 */
export function useStepUpAction(scope: string) {
  const [promptOpen, setPromptOpen] = useState(false);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [retry, setRetry] = useState<StepUpAction<unknown> | null>(null);

  const run = useCallback(
    async <T,>(action: StepUpAction<T>, onSuccess: (result: T) => void): Promise<void> => {
      setPending(true);
      setError(null);
      try {
        const result = await action();
        onSuccess(result);
        setRetry(null);
      } catch (cause) {
        if (isStepUpRequiredError(cause)) {
          setRetry(() => async () => onSuccess(await action()));
          setPromptOpen(true);
        } else {
          throw cause;
        }
      } finally {
        setPending(false);
      }
    },
    [],
  );

  const submitCode = useCallback(
    async (code: string) => {
      setError(null);
      setPending(true);
      try {
        await requestStepUp(scope, code);
        setPromptOpen(false);
        const replay = retry;
        setRetry(null);
        await replay?.();
      } catch (cause) {
        setError(cause instanceof Error ? cause.message : 'Verification failed. Please try again.');
      } finally {
        setPending(false);
      }
    },
    [retry, scope],
  );

  const cancel = useCallback(() => {
    setPromptOpen(false);
    setRetry(null);
    setError(null);
  }, []);

  return { run, promptOpen, pending, error, submitCode, cancel };
}
