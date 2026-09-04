import { navigateAuthOnce, resetAuthNavigationForTests } from '@/lib/navigation/auth-redirect';

describe('navigateAuthOnce', () => {
  const realLocation = window.location;
  const replaceSpy = vi.fn();
  const assignSpy = vi.fn();

  beforeEach(() => {
    resetAuthNavigationForTests();
    replaceSpy.mockReset();
    assignSpy.mockReset();
    Object.defineProperty(window, 'location', {
      value: { ...realLocation, replace: replaceSpy, assign: assignSpy },
      writable: true,
      configurable: true,
    });
  });

  afterEach(() => {
    Object.defineProperty(window, 'location', {
      value: realLocation,
      writable: true,
      configurable: true,
    });
    resetAuthNavigationForTests();
  });

  it('performs the first hard navigation with replace', () => {
    expect(navigateAuthOnce('/sign-in?next=%2Fdashboard', true)).toBe(true);
    expect(replaceSpy).toHaveBeenCalledTimes(1);
    expect(replaceSpy).toHaveBeenCalledWith('/sign-in?next=%2Fdashboard');
    expect(assignSpy).not.toHaveBeenCalled();
  });

  it('performs the first hard navigation with assign when replace is false', () => {
    expect(navigateAuthOnce('/verify-email?email=x', false)).toBe(true);
    expect(assignSpy).toHaveBeenCalledTimes(1);
    expect(assignSpy).toHaveBeenCalledWith('/verify-email?email=x');
    expect(replaceSpy).not.toHaveBeenCalled();
  });

  it('collapses concurrent duplicate navigations into a single document load', () => {
    // Ten dashboard queries failing at once used to produce ten full WebView
    // document loads; now only the first navigates.
    const results = Array.from({ length: 10 }, () =>
      navigateAuthOnce('/sign-in?next=%2Fdashboard', true),
    );

    expect(results).toEqual([true, false, false, false, false, false, false, false, false, false]);
    expect(replaceSpy).toHaveBeenCalledTimes(1);
  });

  it('collapses mixed-target navigations (session loss + email gate racing)', () => {
    expect(navigateAuthOnce('/sign-in?next=%2F', true)).toBe(true);
    expect(navigateAuthOnce('/verify-email', false)).toBe(false);
    expect(replaceSpy).toHaveBeenCalledTimes(1);
    expect(assignSpy).not.toHaveBeenCalled();
  });
});
