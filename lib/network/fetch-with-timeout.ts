const DEFAULT_TIMEOUT_MS = 30_000;

function createAbortError(message: string): DOMException {
  return new DOMException(message, 'AbortError');
}

export async function fetchWithTimeout(
  input: RequestInfo | URL,
  init: RequestInit = {},
  timeoutMs = DEFAULT_TIMEOUT_MS,
): Promise<Response> {
  const controller = new AbortController();
  const externalSignal = init.signal;

  const handleExternalAbort = () => {
    controller.abort(externalSignal?.reason ?? createAbortError('The request was aborted.'));
  };

  if (externalSignal) {
    if (externalSignal.aborted) {
      controller.abort(externalSignal.reason ?? createAbortError('The request was aborted.'));
    } else {
      externalSignal.addEventListener('abort', handleExternalAbort, { once: true });
    }
  }

  const timeoutId = globalThis.setTimeout(() => {
    controller.abort(createAbortError('The request timed out.'));
  }, timeoutMs);

  try {
    return await fetch(input, {
      ...init,
      signal: controller.signal,
    });
  } finally {
    globalThis.clearTimeout(timeoutId);
    if (externalSignal) {
      externalSignal.removeEventListener('abort', handleExternalAbort);
    }
  }
}