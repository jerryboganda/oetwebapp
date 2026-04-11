import {
  getCelebrateMotion,
  getCollapseTransition,
  getDesktopFocusTransitionCSS,
  getFadeSwitchTransition,
  getFadeSwitchVariants,
  getMicroFocus,
  getMicroHover,
  getMicroTap,
  getMobileResumeMotion,
  getMotionDelay,
  getMotionPresenceMode,
  getSharedLayoutId,
  getSurfaceMotion,
  getSurfaceTransition,
  motionTokens,
  prefersReducedMotion,
} from './motion';

describe('motion helpers', () => {
  afterEach(() => {
    delete document.documentElement.dataset.runtimeKind;
    delete document.documentElement.dataset.desktopNative;
    delete document.documentElement.dataset.desktopPlatform;
    delete document.documentElement.dataset.capacitorNative;
    delete document.documentElement.dataset.capacitorPlatform;
  });

  it('defaults reduced-motion checks to false when the browser value is unavailable', () => {
    expect(prefersReducedMotion(null)).toBe(false);
    expect(prefersReducedMotion(undefined)).toBe(false);
    expect(prefersReducedMotion(true)).toBe(true);
  });

  it('uses wait mode for full-motion and sync mode when motion should be reduced', () => {
    expect(getMotionPresenceMode(false)).toBe('wait');
    expect(getMotionPresenceMode(true)).toBe('sync');
  });

  it('keeps route transitions premium but subtle', () => {
    const transition = getSurfaceTransition('route', false);

    expect(transition).toMatchObject({
      type: 'spring',
      stiffness: motionTokens.spring.route.stiffness,
      damping: motionTokens.spring.route.damping,
    });
  });

  it('collapses spatial movement for reduced-motion states', () => {
    const motion = getSurfaceMotion('item', true);

    expect(motion.variants.hidden).toEqual({ opacity: 0 });
    expect(motion.variants.visible).toEqual({ opacity: 1 });
    expect(motion.variants.exit).toEqual({ opacity: 0 });
    expect(motion.transition).toMatchObject({
      duration: motionTokens.duration.fast,
    });
  });

  it('keeps stagger timing restrained and capped', () => {
    expect(getMotionDelay(0, false)).toBe(0);
    expect(getMotionDelay(4, false)).toBeCloseTo(0.16, 2);
    expect(getMotionDelay(20, false)).toBeLessThanOrEqual(0.18);
    expect(getMotionDelay(4, true, 0.05)).toBe(0.05);
  });

  it('tunes motion more tightly on desktop and mobile runtimes', () => {
    document.documentElement.dataset.runtimeKind = 'desktop';

    const desktopRouteMotion = getSurfaceMotion('route', false);
    const desktopHidden = desktopRouteMotion.variants.hidden as { y?: number; scale?: number };

    expect(getMotionDelay(4, false)).toBeCloseTo(0.144, 3);
    expect(desktopHidden.y).toBeCloseTo(14.4, 1);
    expect(desktopHidden.scale).toBeCloseTo(0.994, 3);
    expect(getSurfaceTransition('state', true).duration).toBeCloseTo(0.114, 3);

    document.documentElement.dataset.runtimeKind = 'capacitor-native';

    const mobileRouteMotion = getSurfaceMotion('route', false);
    const mobileHidden = mobileRouteMotion.variants.hidden as { y?: number; scale?: number };

    expect(getMotionDelay(4, false)).toBeCloseTo(0.128, 3);
    expect(mobileHidden.y).toBeCloseTo(12.8, 1);
    expect(mobileHidden.scale).toBeCloseTo(0.995, 3);
    expect(getSurfaceTransition('state', true).duration).toBeCloseTo(0.108, 3);
  });

  it('builds stable shared layout ids for motion continuity', () => {
    expect(getSharedLayoutId('tabs', 'overview')).toBe('tabs:overview');
    expect(getSharedLayoutId('queue-card', 42)).toBe('queue-card:42');
  });

  /* ─── Microinteraction Presets ─── */

  it('returns scale-up hover and scale-down tap presets for full motion', () => {
    const hover = getMicroHover(false);
    const tap = getMicroTap(false);

    expect(hover.scale).toBeGreaterThan(1);
    expect(tap.scale).toBeLessThan(1);
    expect(hover.transition).toBeDefined();
    expect(tap.transition).toBeDefined();
  });

  it('returns empty objects for hover/tap/focus when reduced motion is on', () => {
    expect(getMicroHover(true)).toEqual({});
    expect(getMicroTap(true)).toEqual({});
    expect(getMicroFocus(true)).toEqual({});
  });

  it('provides a focus preset with subtle scale for full motion', () => {
    const focus = getMicroFocus(false);
    expect(focus.scale).toBeCloseTo(1.005, 3);
    expect(focus.transition).toBeDefined();
  });

  /* ─── Celebrate Motion ─── */

  it('returns celebratory pop motion with scale and y offset', () => {
    const celebrate = getCelebrateMotion(false);

    expect(celebrate.initial).toHaveProperty('scale');
    expect(celebrate.initial).toHaveProperty('y');
    expect(celebrate.animate).toEqual({ opacity: 1, scale: 1, y: 0 });
    expect(celebrate.transition).toMatchObject({ type: 'spring' });
  });

  it('collapses celebrate motion to opacity-only for reduced motion', () => {
    const celebrate = getCelebrateMotion(true);

    expect(celebrate.initial).toEqual({ opacity: 0 });
    expect(celebrate.animate).toEqual({ opacity: 1 });
    expect(celebrate.exit).toEqual({ opacity: 0 });
    expect(celebrate.transition).toMatchObject({ duration: motionTokens.duration.fast });
  });

  /* ─── Collapse Transition ─── */

  it('returns spring-based collapse transition for full motion', () => {
    const transition = getCollapseTransition(false);
    expect(transition).toMatchObject({ type: 'spring' });
  });

  it('returns instant duration collapse for reduced motion', () => {
    const transition = getCollapseTransition(true);
    expect(transition).toMatchObject({ duration: motionTokens.duration.instant });
  });

  /* ─── Fade-Switch ─── */

  it('returns fade-switch variants with y offset for full motion', () => {
    const variants = getFadeSwitchVariants(false, 1);
    const hidden = variants.hidden as Record<string, number>;
    expect(hidden.y).toBeGreaterThan(0);
    expect(hidden.opacity).toBe(0);
  });

  it('collapses fade-switch variants to opacity-only for reduced motion', () => {
    const variants = getFadeSwitchVariants(true);
    expect(variants.hidden).toEqual({ opacity: 0 });
    expect(variants.visible).toEqual({ opacity: 1 });
    expect(variants.exit).toEqual({ opacity: 0 });
  });

  it('returns entrance-eased transition for fade-switch', () => {
    const transition = getFadeSwitchTransition(false);
    expect(transition).toMatchObject({
      duration: motionTokens.duration.base,
      ease: motionTokens.ease.entrance,
    });
  });

  it('returns instant fade-switch for reduced motion', () => {
    const transition = getFadeSwitchTransition(true);
    expect(transition).toMatchObject({ duration: motionTokens.duration.instant });
  });

  /* ─── Platform-adaptive microinteractions ─── */

  it('scales micro-hover and micro-tap for desktop runtime', () => {
    document.documentElement.dataset.runtimeKind = 'desktop';

    const hover = getMicroHover(false);
    const tap = getMicroTap(false);

    // Desktop has scaleScale 0.75, so hover should be less than web's 1.02
    expect(hover.scale).toBeLessThan(1.02);
    expect(hover.scale).toBeGreaterThan(1);
    expect(tap.scale).toBeGreaterThan(0.97);
    expect(tap.scale).toBeLessThan(1);
  });

  /* ─── Desktop lifecycle transition ─── */

  it('returns desktop focus transition CSS specs', () => {
    const css = getDesktopFocusTransitionCSS();
    expect(css.property).toBe('opacity, filter');
    expect(css.duration).toMatch(/^\d+\.\d+s$/);
    expect(css.easing).toContain('cubic-bezier');
  });

  /* ─── Mobile resume motion ─── */

  it('returns subtle opacity fade-in for mobile resume', () => {
    const motion = getMobileResumeMotion(false);
    expect(motion.initial).toEqual({ opacity: 0.92 });
    expect(motion.animate).toEqual({ opacity: 1 });
    expect(motion.transition).toBeDefined();
  });

  it('collapses mobile resume to no-op for reduced motion', () => {
    const motion = getMobileResumeMotion(true);
    expect(motion.initial).toEqual({ opacity: 1 });
    expect(motion.animate).toEqual({ opacity: 1 });
    expect(motion.transition).toBeUndefined();
  });

  /* ─── Cross-platform consistency verification ─── */

  it('maintains consistent motion language across all three runtimes', () => {
    // Web baseline
    const webRoute = getSurfaceMotion('route', false);
    const webHidden = webRoute.variants.hidden as { y?: number };

    // Desktop
    document.documentElement.dataset.runtimeKind = 'desktop';
    const desktopRoute = getSurfaceMotion('route', false);
    const desktopHidden = desktopRoute.variants.hidden as { y?: number };

    // Native
    document.documentElement.dataset.runtimeKind = 'capacitor-native';
    const nativeRoute = getSurfaceMotion('route', false);
    const nativeHidden = nativeRoute.variants.hidden as { y?: number };

    // All three share the same structure
    expect(webHidden.y).toBeDefined();
    expect(desktopHidden.y).toBeDefined();
    expect(nativeHidden.y).toBeDefined();

    // Desktop < web, native < desktop (progressively tighter)
    expect(desktopHidden.y!).toBeLessThan(webHidden.y!);
    expect(nativeHidden.y!).toBeLessThan(desktopHidden.y!);
  });

  it('stagger caps are progressively tighter across platforms', () => {
    const delays: number[] = [];

    delete document.documentElement.dataset.runtimeKind;
    delays.push(getMotionDelay(20, false)); // web

    document.documentElement.dataset.runtimeKind = 'desktop';
    delays.push(getMotionDelay(20, false)); // desktop

    document.documentElement.dataset.runtimeKind = 'capacitor-native';
    delays.push(getMotionDelay(20, false)); // native

    expect(delays[0]).toBeGreaterThan(delays[1]); // web > desktop
    expect(delays[1]).toBeGreaterThan(delays[2]); // desktop > native
  });
});
