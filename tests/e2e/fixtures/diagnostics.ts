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
  allowNextDevNoise?: boolean;
  allowMobileWebKitReloadNoise?: boolean;
};

function isIgnorableFailure(url: string) {
  return url.includes('/_next/static/') || url.endsWith('/favicon.ico');
}

export function isIgnorableRequestFailureDetails(url: string, errorText: string) {
  if (isIgnorableFailure(url)) {
    return true;
  }

  if (
    url.includes('/api/backend/v1/analytics/events')
    && errorText.includes('Load request cancelled')
  ) {
    return true;
  }

  if (
    url.includes('/__nextjs_original-stack-frames')
    && errorText.includes('Load request cancelled')
  ) {
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

  if (
    options.allowNextDevNoise
    && text.startsWith('500 :: ')
    && text.includes('http://localhost:3000/')
  ) {
    return true;
  }

  if (
    options.allowMobileWebKitReloadNoise
    && text.startsWith('500 :: ')
    && text.includes('http://localhost:3000/')
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

  if (options.allowNotificationReconnectNoise) {
    const isReconnectNoise =
      text === 'TypeError: Failed to fetch'
      || text.includes('TypeError: NetworkError when attempting to fetch resource.')
      || text.includes("Connection disconnected with error 'TypeError: Load failed'.")
      || text.includes("Connection disconnected with error 'TypeError: NetworkError when attempting to fetch resource.'.")
      || text.includes('Failed to complete negotiation with the server: TypeError: Failed to fetch')
      || text.includes('Failed to complete negotiation with the server: TypeError: NetworkError when attempting to fetch resource.')
      || text.includes('Failed to complete negotiation with the server: TypeError: Load failed')
      || text.includes('Failed to start the connection: Error: Failed to complete negotiation with the server: TypeError: Failed to fetch')
      || text.includes('Failed to start the connection: Error: Failed to complete negotiation with the server: TypeError: NetworkError when attempting to fetch resource.')
      || text.includes('Failed to start the connection: Error: Failed to complete negotiation with the server: TypeError: Load failed')
      || text.includes("Failed to start the transport 'LongPolling': TypeError: Load failed")
      || text.includes("Error: Failed to start the transport 'LongPolling': TypeError: Failed to fetch")
      || text.includes("Error: Failed to start the transport 'LongPolling': TypeError: NetworkError when attempting to fetch resource.")
      || text.includes("Error: Failed to start the connection: Error: Unable to connect to the server with any of the available transports. WebSockets failed: Error: 'WebSockets' is disabled by the client. ServerSentEvents failed: Error: 'ServerSentEvents' is disabled by the client. Error: LongPolling failed: TypeError: Failed to fetch")
      || text.includes("Error: Failed to start the connection: Error: Unable to connect to the server with any of the available transports. WebSockets failed: Error: 'WebSockets' is disabled by the client. ServerSentEvents failed: Error: 'ServerSentEvents' is disabled by the client. Error: LongPolling failed: TypeError: Load failed")
      || (
        text === 'Failed to load resource: the server responded with a status of 404 (Not Found)'
        && diagnostics.clientErrorResponses.some((entry) => shouldIgnoreClientErrorResponseText(entry, options))
      );

    if (isReconnectNoise) {
      return true;
    }
  }

  if (
    options.allowNextDevNoise
    && (
      text.includes('Failed to fetch RSC payload for ')
      || text.includes('Falling back to browser navigation. TypeError: Load failed')
      || text.includes('Failed to load resource: the server responded with a status of 500 (Internal Server Error)')
    )
  ) {
    return true;
  }

  if (
    options.allowMobileWebKitReloadNoise
    && (
      text.includes('Invariant: Expected clientReferenceManifest to be defined. This is a bug in Next.js.')
      || text === 'Failed to load resource: the server responded with a status of 404 (Not Found)'
    )
  ) {
    return true;
  }

  return false;
}

function shouldIgnorePageError(text: string, options: DiagnosticExpectationOptions) {
  if (
    options.allowNotificationReconnectNoise
    && text.includes('ResizeObserver loop completed with undelivered notifications.')
  ) {
    return true;
  }

  if (
    options.allowNotificationReconnectNoise
    && text.includes('/api/backend/v1/notifications/hub')
    && text.includes('due to access control checks.')
  ) {
    return true;
  }

  if (
    options.allowNotificationReconnectNoise
    && text === 'Invalid or unexpected token'
  ) {
    return true;
  }

  if (
    options.allowNextDevNoise
    && text.includes('due to access control checks.')
    && (
      text.includes('/__nextjs_original-stack-frames')
      || text.includes('/?_rsc=')
      || text.includes('/_next/static/webpack/')
    )
  ) {
    return true;
  }

  if (
    options.allowNextDevNoise
    && text.includes('Unexpected end of JSON input')
  ) {
    return true;
  }

  if (
    options.allowMobileWebKitReloadNoise
    && text.includes('Invariant: Expected clientReferenceManifest to be defined. This is a bug in Next.js.')
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

  if (
    options.allowNextDevNoise
    && text.includes('Load request cancelled')
    && (
      text.includes('/api/backend/v1/analytics/events')
      || text.includes('/__nextjs_original-stack-frames')
      || text.includes('/?_rsc=')
      || text.endsWith('http://localhost:3000/ :: Load request cancelled')
    )
  ) {
    return true;
  }

  return false;
}

function shouldIgnoreResponseFailureText(text: string, options: DiagnosticExpectationOptions) {
  if (
    options.allowNotificationReconnectNoise
    && text.startsWith('500 :: ')
    && text.includes('/api/backend/v1/notifications/hub?id=')
  ) {
    return true;
  }

  if (
    options.allowNextDevNoise
    && text.startsWith('500 :: ')
    && text.includes('http://localhost:3000/')
  ) {
    return true;
  }

  if (
    options.allowMobileWebKitReloadNoise
    && text.startsWith('500 :: ')
    && text.includes('http://localhost:3000/')
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
  expect.soft(
    diagnostics.responseFailures.filter((entry) => !shouldIgnoreResponseFailureText(entry, options)),
    'Server 5xx responses should remain empty',
  ).toEqual([]);
}
