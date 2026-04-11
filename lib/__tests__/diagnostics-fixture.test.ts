import { isIgnorableRequestFailureDetails } from '@/tests/e2e/fixtures/diagnostics';

describe('isIgnorableRequestFailureDetails', () => {
  it('ignores static asset failures', () => {
    expect(isIgnorableRequestFailureDetails('http://localhost:3000/_next/static/chunk.js', 'network failure')).toBe(true);
  });

  it('ignores favicon failures', () => {
    expect(isIgnorableRequestFailureDetails('http://localhost:3000/favicon.ico', 'network failure')).toBe(true);
  });

  it('ignores chromium abort noise', () => {
    expect(isIgnorableRequestFailureDetails('http://localhost:3000/submissions?_rsc=abc', 'net::ERR_ABORTED')).toBe(true);
  });

  it('ignores firefox abort noise', () => {
    expect(isIgnorableRequestFailureDetails('http://localhost:3000/settings?_rsc=abc', 'NS_BINDING_ABORTED')).toBe(true);
  });

  it('does not ignore real request failures', () => {
    expect(isIgnorableRequestFailureDetails('http://localhost:3000/api/problem', 'ECONNRESET')).toBe(false);
  });
});
