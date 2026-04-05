import { expect, type Page, type Request, type TestInfo } from '@playwright/test';

interface PageDiagnostics {
  consoleErrors: string[];
  pageErrors: string[];
  requestFailures: string[];
  clientErrorResponses: string[];
  responseFailures: string[];
  detach: () => void;
}

type DiagnosticExpectationOptions = {
  allowAuthRedirectNoise?: boolean;
  allowNotificationReconnectNoise?: boolean;
};

function isIgnorableFailure(url: string) {
  return url.includes('/_next/static/') || url.endsWith('/favicon.ico');
}

export function isIgnorableRequestFailureDetails(url: string, errorText: string) {
  if (isIgnorableFailure(url)) {
    return true;
  }

  // Browsers will cancel in-flight fetches during reloads, route transitions, prefetch replacement,
  // and redirect hand-offs. Those cancellations are noisy but not correctness defects.
  if (errorText.includes('ERR_ABORTED') || errorText.includes('NS_BINDING_ABORTED')) {
    return true;
  }

  return false;
}

function isIgnorableRequestFailure(request: Request) {
  const url = request.url();
  const errorText = request.failure()?.errorText ?? '';
  return isIgnorableRequestFailureDetails(url, errorText);
}

export function observePage(page: Page): PageDiagnostics {
  const consoleErrors: string[] = [];
  const pageErrors: string[] = [];
  const requestFailures: string[] = [];
  const clientErrorResponses: string[] = [];
  const responseFailures: string[] = [];

  const onConsole = (message: { type(): string; text(): string }) => {
    if (message.type() === 'error') {
      const text = message.text();
      if (text.includes('Failed to start the connection: Error: The connection was stopped during negotiation.')) {
        return;
      }

      if (text.includes("Connection disconnected with error 'TypeError: Failed to fetch'.")) {
        return;
      }

      consoleErrors.push(text);
    }
  };

  const onPageError = (error: Error) => {
    pageErrors.push(error.message);
  };

  const onRequestFailed = (request: Request) => {
    const url = request.url();
    if (!isIgnorableRequestFailure(request)) {
      requestFailures.push(`${url} :: ${request.failure()?.errorText ?? 'unknown request failure'}`);
    }
  };

  const onResponse = (response: { status(): number; url(): string }) => {
    const url = response.url();
    if (response.status() >= 400 && response.status() < 500 && !isIgnorableFailure(url)) {
      clientErrorResponses.push(`${response.status()} :: ${url}`);
    }
    if (response.status() >= 500 && !isIgnorableFailure(url)) {
      responseFailures.push(`${response.status()} :: ${url}`);
    }
  };

  page.on('console', onConsole);
  page.on('pageerror', onPageError);
  page.on('requestfailed', onRequestFailed);
  page.on('response', onResponse);

  return {
    consoleErrors,
    pageErrors,
    requestFailures,
    clientErrorResponses,
    responseFailures,
    detach() {
      page.off('console', onConsole);
      page.off('pageerror', onPageError);
      page.off('requestfailed', onRequestFailed);
      page.off('response', onResponse);
    },
  };
}

export async function attachDiagnostics(testInfo: TestInfo, diagnostics: PageDiagnostics) {
  await testInfo.attach('console-errors', {
    body: diagnostics.consoleErrors.join('\n') || 'none',
    contentType: 'text/plain',
  });
  await testInfo.attach('page-errors', {
    body: diagnostics.pageErrors.join('\n') || 'none',
    contentType: 'text/plain',
  });
  await testInfo.attach('request-failures', {
    body: diagnostics.requestFailures.join('\n') || 'none',
    contentType: 'text/plain',
  });
  await testInfo.attach('client-error-responses', {
    body: diagnostics.clientErrorResponses.join('\n') || 'none',
    contentType: 'text/plain',
  });
  await testInfo.attach('response-failures', {
    body: diagnostics.responseFailures.join('\n') || 'none',
    contentType: 'text/plain',
  });
}

function shouldIgnoreClientErrorResponseText(text: string, options: DiagnosticExpectationOptions) {
  if (
    options.allowNotificationReconnectNoise
    && text.startsWith('404 :: ')
    && text.includes('/api/backend/v1/notifications/hub?id=')
  ) {
    return true;
  }

  return false;
}

function shouldIgnoreConsoleError(
  text: string,
  options: DiagnosticExpectationOptions,
  diagnostics: PageDiagnostics,
) {
  if (
    options.allowAuthRedirectNoise
    && (text.startsWith('[API] No auth token available for production request to ')
      || text === 'Failed to load resource: the server responded with a status of 401 (Unauthorized)')
  ) {
    return true;
  }

  if (
    options.allowNotificationReconnectNoise
    && (text === 'TypeError: Failed to fetch'
      || text.includes("Connection disconnected with error 'TypeError: Load failed'.")
      || text.includes('Failed to complete negotiation with the server: TypeError: Failed to fetch')
      || text.includes('Failed to complete negotiation with the server: TypeError: Load failed')
      || text.includes('Failed to start the connection: Error: Failed to complete negotiation with the server: TypeError: Failed to fetch')
      || text.includes('Failed to start the connection: Error: Failed to complete negotiation with the server: TypeError: Load failed')
      || (
        text === 'Failed to load resource: the server responded with a status of 404 (Not Found)'
        && diagnostics.clientErrorResponses.some((entry) => shouldIgnoreClientErrorResponseText(entry, options))
      )
      || text.includes("Error: Failed to start the transport 'LongPolling': TypeError: Failed to fetch")
      || text.includes("Error: Failed to start the connection: Error: Unable to connect to the server with any of the available transports. WebSockets failed: Error: 'WebSockets' is disabled by the client. ServerSentEvents failed: Error: 'ServerSentEvents' is disabled by the client. Error: LongPolling failed: TypeError: Failed to fetch"))
  ) {
    return true;
  }

  return false;
}

function shouldIgnorePageError(text: string, options: DiagnosticExpectationOptions) {
  if (
    options.allowNotificationReconnectNoise
    && text.includes('/api/backend/v1/notifications/hub/negotiate?negotiateVersion=1 due to access control checks.')
  ) {
    return true;
  }

  return false;
}

function shouldIgnoreRequestFailureText(text: string, options: DiagnosticExpectationOptions) {
  if (
    options.allowNotificationReconnectNoise
    && text.includes('/api/backend/v1/notifications/hub?id=')
    && text.includes('Load request cancelled')
  ) {
    return true;
  }

  return false;
}

export function expectNoSevereClientIssues(
  diagnostics: PageDiagnostics,
  options: DiagnosticExpectationOptions = {},
) {
  expect.soft(
    diagnostics.pageErrors.filter((entry) => !shouldIgnorePageError(entry, options)),
    'Unhandled page errors should remain empty',
  ).toEqual([]);
  expect.soft(
    diagnostics.consoleErrors.filter((entry) => !shouldIgnoreConsoleError(entry, options, diagnostics)),
    'Console errors should remain empty',
  ).toEqual([]);
  expect.soft(
    diagnostics.requestFailures.filter((entry) => !shouldIgnoreRequestFailureText(entry, options)),
    'Failed browser requests should remain empty',
  ).toEqual([]);
  expect.soft(
    diagnostics.clientErrorResponses.filter((entry) => !shouldIgnoreClientErrorResponseText(entry, options)),
    'Client 4xx responses should remain empty',
  ).toEqual([]);
  expect.soft(diagnostics.responseFailures, 'Server 5xx responses should remain empty').toEqual([]);
}
