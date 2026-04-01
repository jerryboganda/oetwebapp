import { expect, type Page, type Request, type TestInfo } from '@playwright/test';

interface PageDiagnostics {
  consoleErrors: string[];
  pageErrors: string[];
  requestFailures: string[];
  responseFailures: string[];
  detach: () => void;
}

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
  const responseFailures: string[] = [];

  const onConsole = (message: { type(): string; text(): string }) => {
    if (message.type() === 'error') {
      const text = message.text();
      if (text.includes('Failed to start the connection: Error: The connection was stopped during negotiation.')) {
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
  await testInfo.attach('response-failures', {
    body: diagnostics.responseFailures.join('\n') || 'none',
    contentType: 'text/plain',
  });
}

export function expectNoSevereClientIssues(diagnostics: PageDiagnostics) {
  expect.soft(diagnostics.pageErrors, 'Unhandled page errors should remain empty').toEqual([]);
  expect.soft(diagnostics.consoleErrors, 'Console errors should remain empty').toEqual([]);
  expect.soft(diagnostics.requestFailures, 'Failed browser requests should remain empty').toEqual([]);
  expect.soft(diagnostics.responseFailures, 'Server 5xx responses should remain empty').toEqual([]);
}
