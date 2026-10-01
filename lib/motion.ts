import type { Transition, Variant, Variants } from 'motion/react';
import { getAppRuntimeKind, type AppRuntimeKind } from './runtime-signals';

export type MotionSurface = 'route' | 'list' | 'section' | 'item' | 'overlay' | 'state' | 'skeleton';

type MotionPoint = readonly [number, number, number, number];

/**
 * Motion tokens for motion/react. The CSS side mirrors these exact values as
 * --duration-{instant,fast,normal,slow,hero} and --ease-{standard,enter,exit}
 * in app/globals.css — change both together.
 */
export const motionTokens = {
  duration: {
    instant: 0.12,
    fast: 0.16,
    base: 0.22,
    slow: 0.28,
    hero: 0.36,
    // One-shot flourishes (a bell nudge, a badge or flame pop).
    flourish: 0.64,
    // Count-ups and ring/bar draw-ins: long enough to read as counting.
    count: 0.8,
  },
  ease: {
    standard: [0.22, 1, 0.36, 1] as MotionPoint,
    subtle: [0.32, 0, 0.2, 1] as MotionPoint,
    entrance: [0.16, 1, 0.3, 1] as MotionPoint,
    exit: [0.4, 0, 1, 1] as MotionPoint,
  },
  // Material 3 Expressive spring physics (damping ratio ζ = damping / 2√(stiffness·mass)).
  // Reveals settle with a hint of life (ζ ≈ 0.8); sliding indicators stay firm
  // (ζ ≈ 0.9); `pop` (ζ ≈ 0.6, ~9% overshoot) is only for presses, badges and
  // celebrations, never for per-row reveals, which must stay cheap while scrolling.
  spring: {
    route: { type: 'spring', stiffness: 380, damping: 35, mass: 1 },
    section: { type: 'spring', stiffness: 380, damping: 31, mass: 1 },
    item: { type: 'spring', stiffness: 380, damping: 31, mass: 1 },
    overlay: { type: 'spring', stiffness: 420, damping: 33, mass: 1 },
    layout: { type: 'spring', stiffness: 700, damping: 48, mass: 1 },
    pop: { type: 'spring', stiffness: 800, damping: 34, mass: 1 },
  },
  distance: {
    route: 20,
    list: 12,
    section: 12,
    item: 10,
    overlay: 6,
  },
  scale: {
    route: 0.99,
    list: 0.995,
    section: 0.99,
    item: 0.98,
    overlay: 0.97,
  },
} as const;

type MotionRuntimeProfile = {
  distanceScale: number;
  scaleScale: number;
  durationScale: number;
  staggerStep: number;
  staggerCap: number;
};

const motionRuntimeProfiles: Record<AppRuntimeKind, MotionRuntimeProfile> = {
  web: {
    distanceScale: 1,
    scaleScale: 1,
    durationScale: 1,
    staggerStep: 0.03,
    staggerCap: 0.18,
  },
  desktop: {
    distanceScale: 0.9,
    scaleScale: 0.75,
    durationScale: 0.95,
    staggerStep: 0.027,
    staggerCap: 0.16,
  },
  'capacitor-native': {
    distanceScale: 0.8,
    scaleScale: 0.6,
    durationScale: 0.9,
    staggerStep: 0.024,
    staggerCap: 0.14,
  },
};

function getMotionRuntimeProfile(runtimeKind?: AppRuntimeKind) {
  return motionRuntimeProfiles[runtimeKind ?? getAppRuntimeKind()];
}

function scaleHiddenScale(value: number, profile: MotionRuntimeProfile) {
  return 1 - (1 - value) * profile.scaleScale;
}

function tuneVariant(variant: Variant, profile: MotionRuntimeProfile): Variant {
  if (variant === null || typeof variant !== 'object' || Array.isArray(variant)) {
    return variant;
  }

  const adjusted = { ...variant } as Record<string, unknown>;

  if (typeof adjusted.x === 'number') {
    adjusted.x *= profile.distanceScale;
  }

  if (typeof adjusted.y === 'number') {
    adjusted.y *= profile.distanceScale;
  }

  if (typeof adjusted.scale === 'number') {
    adjusted.scale = scaleHiddenScale(adjusted.scale, profile);
  }

  return adjusted as Variant;
}

function tuneTransition(transition: Transition, profile: MotionRuntimeProfile): Transition {
  if (transition === null || typeof transition !== 'object' || Array.isArray(transition)) {
    return transition;
  }

  const adjusted = { ...transition } as Record<string, unknown>;

  if (typeof adjusted.duration === 'number') {
    adjusted.duration *= profile.durationScale;
  }

  return adjusted as Transition;
}

const motionSurfaceSpecs: Record<
  MotionSurface,
  {
    hidden: Variant;
    visible: Variant;
    exit: Variant;
    transition: Transition;
    reducedTransition: Transition;
  }
> = {
  route: {
    hidden: { opacity: 0, y: motionTokens.distance.route, scale: motionTokens.scale.route },
    visible: { opacity: 1, y: 0, scale: 1 },
    exit: { opacity: 0, y: -10, scale: 0.996 },
    transition: motionTokens.spring.route,
    reducedTransition: {
      duration: motionTokens.duration.base,
      ease: motionTokens.ease.standard,
    },
  },
  list: {
    hidden: { opacity: 0, y: motionTokens.distance.list },
    visible: { opacity: 1, y: 0 },
    exit: { opacity: 0, y: -6 },
    transition: motionTokens.spring.section,
    reducedTransition: {
      duration: motionTokens.duration.fast,
      ease: motionTokens.ease.standard,
    },
  },
  section: {
    hidden: { opacity: 0, y: motionTokens.distance.section },
    visible: { opacity: 1, y: 0 },
    exit: { opacity: 0, y: -4 },
    transition: motionTokens.spring.section,
    reducedTransition: {
      duration: motionTokens.duration.fast,
      ease: motionTokens.ease.standard,
    },
  },
  item: {
    hidden: { opacity: 0, y: motionTokens.distance.item, scale: motionTokens.scale.item },
    visible: { opacity: 1, y: 0, scale: 1 },
    exit: { opacity: 0, y: -4, scale: 0.99 },
    transition: motionTokens.spring.item,
    reducedTransition: {
      duration: motionTokens.duration.fast,
      ease: motionTokens.ease.standard,
    },
  },
  overlay: {
    hidden: { opacity: 0, y: motionTokens.distance.overlay, scale: motionTokens.scale.overlay },
    visible: { opacity: 1, y: 0, scale: 1 },
    exit: { opacity: 0, y: 4, scale: 0.99 },
    transition: motionTokens.spring.overlay,
    reducedTransition: {
      duration: motionTokens.duration.fast,
      ease: motionTokens.ease.standard,
    },
  },
  state: {
    hidden: { opacity: 0, y: 4 },
    visible: { opacity: 1, y: 0 },
    exit: { opacity: 0, y: 0 },
    transition: {
      duration: motionTokens.duration.base,
      ease: motionTokens.ease.entrance,
    },
    reducedTransition: {
      duration: motionTokens.duration.instant,
      ease: motionTokens.ease.standard,
    },
  },
  skeleton: {
    hidden: { opacity: 0.6 },
    visible: { opacity: 1 },
    exit: { opacity: 0.5 },
    transition: {
      duration: motionTokens.duration.fast,
      ease: motionTokens.ease.subtle,
    },
    reducedTransition: {
      duration: motionTokens.duration.instant,
      ease: motionTokens.ease.standard,
    },
  },
};

export function prefersReducedMotion(value: boolean | null | undefined) {
  return value ?? false;
}

export function getMotionPresenceMode(reducedMotion: boolean) {
  return reducedMotion ? 'sync' : 'wait';
}

export function getMotionDelay(index: number, reducedMotion: boolean, baseDelay = 0, runtimeKind?: AppRuntimeKind) {
  if (reducedMotion) {
    return baseDelay;
  }

  const profile = getMotionRuntimeProfile(runtimeKind);

  return baseDelay + Math.min(index * profile.staggerStep, profile.staggerCap);
}

export function getSurfaceTransition(
  surface: MotionSurface,
  reducedMotion = false,
  runtimeKind?: AppRuntimeKind,
): Transition {
  const profile = getMotionRuntimeProfile(runtimeKind);
  const transition = reducedMotion ? motionSurfaceSpecs[surface].reducedTransition : motionSurfaceSpecs[surface].transition;

  return tuneTransition(transition, profile);
}

export function getSurfaceVariants(
  surface: MotionSurface,
  reducedMotion = false,
  runtimeKind?: AppRuntimeKind,
): Variants {
  const spec = motionSurfaceSpecs[surface];
  const profile = getMotionRuntimeProfile(runtimeKind);

  return {
    hidden: reducedMotion ? { opacity: 0 } : tuneVariant(spec.hidden, profile),
    visible: reducedMotion ? { opacity: 1 } : spec.visible,
    exit: reducedMotion ? { opacity: 0 } : tuneVariant(spec.exit, profile),
  };
}

export function getSurfaceMotion(surface: MotionSurface, reducedMotion = false, runtimeKind?: AppRuntimeKind) {
  return {
    initial: 'hidden' as const,
    animate: 'visible' as const,
    exit: 'exit' as const,
    variants: getSurfaceVariants(surface, reducedMotion, runtimeKind),
    transition: getSurfaceTransition(surface, reducedMotion, runtimeKind),
  };
}

export function getSharedLayoutId(namespace: string, value: string | number) {
  return `${namespace}:${value}`;
}

/* ─── Microinteraction Presets ─── */

/**
 * Hover / tap / focus presets for interactive controls.
 * All presets collapse to no-ops when reduced motion is preferred.
 */

export function getMicroHover(reducedMotion = false) {
  if (reducedMotion) return {};
  const profile = getMotionRuntimeProfile();
  const s = 1 + 0.02 * profile.scaleScale;
  return { scale: s, transition: motionTokens.spring.layout };
}

export function getMicroTap(reducedMotion = false) {
  if (reducedMotion) return {};
  const profile = getMotionRuntimeProfile();
  const s = 1 - 0.03 * profile.scaleScale;
  return { scale: s, transition: motionTokens.spring.pop };
}

export function getMicroFocus(reducedMotion = false) {
  if (reducedMotion) return {};
  return {
    scale: 1.005,
    transition: { duration: motionTokens.duration.fast, ease: motionTokens.ease.standard },
  };
}

/** Celebratory pop for success confirmations (badges, toasts, completions). */
export function getCelebrateMotion(reducedMotion = false) {
  const profile = getMotionRuntimeProfile();
  if (reducedMotion) {
    return {
      initial: { opacity: 0 },
      animate: { opacity: 1 },
      exit: { opacity: 0 },
      transition: { duration: motionTokens.duration.fast, ease: motionTokens.ease.standard },
    };
  }
  return {
    initial: { opacity: 0, scale: 1 - 0.06 * profile.scaleScale, y: 8 * profile.distanceScale },
    animate: { opacity: 1, scale: 1, y: 0 },
    exit: { opacity: 0, scale: 1 - 0.03 * profile.scaleScale, y: -4 * profile.distanceScale },
    transition: motionTokens.spring.pop,
  };
}

/** Collapse transition specs for animated height. */
export function getCollapseTransition(reducedMotion = false): Transition {
  if (reducedMotion) {
    return { duration: motionTokens.duration.instant, ease: motionTokens.ease.standard };
  }
  const profile = getMotionRuntimeProfile();
  return tuneTransition(
    { type: 'spring' as const, stiffness: 500, damping: 40, mass: 0.8 },
    profile,
  );
}

/** Fade-switch transition for mutually exclusive content (tab panels, form steps). */
export function getFadeSwitchTransition(reducedMotion = false): Transition {
  if (reducedMotion) {
    return { duration: motionTokens.duration.instant, ease: motionTokens.ease.standard };
  }
  const profile = getMotionRuntimeProfile();
  return tuneTransition(
    { duration: motionTokens.duration.base, ease: motionTokens.ease.entrance },
    profile,
  );
}

export function getFadeSwitchVariants(reducedMotion = false, direction: 1 | -1 = 1): Variants {
  if (reducedMotion) {
    return { hidden: { opacity: 0 }, visible: { opacity: 1 }, exit: { opacity: 0 } };
  }
  const profile = getMotionRuntimeProfile();
  const y = 6 * profile.distanceScale * direction;
  return {
    hidden: { opacity: 0, y },
    visible: { opacity: 1, y: 0 },
    exit: { opacity: 0, y: -y * 0.5 },
  };
}

/** Progress / meter fill transition for width/height value changes. */
export function getProgressFillTransition(reducedMotion = false): Transition {
  if (reducedMotion) {
    return { duration: motionTokens.duration.instant, ease: motionTokens.ease.standard };
  }

  const profile = getMotionRuntimeProfile();
  return tuneTransition(
    { duration: motionTokens.duration.slow, ease: motionTokens.ease.entrance },
    profile,
  );
}

/** Fast value transition for near-realtime meters such as audio levels. */
export function getRealtimeValueTransition(reducedMotion = false): Transition {
  if (reducedMotion) {
    return { duration: motionTokens.duration.instant, ease: motionTokens.ease.standard };
  }

  const profile = getMotionRuntimeProfile();
  return tuneTransition(
    { duration: motionTokens.duration.instant, ease: motionTokens.ease.standard },
    profile,
  );
}

/** Slow looping pulse for active recording/listening indicators. */
export function getRecordingPulseTransition(reducedMotion = false): Transition {
  if (reducedMotion) {
    return { duration: motionTokens.duration.instant, ease: motionTokens.ease.standard };
  }

  const profile = getMotionRuntimeProfile();
  return tuneTransition(
    { duration: 2, repeat: Infinity, ease: motionTokens.ease.subtle },
    profile,
  );
}

/* ─── Desktop lifecycle transitions ─── */

/** CSS transition spec for desktop window focus/blur visual feedback. */
export function getDesktopFocusTransitionCSS() {
  return {
    property: 'opacity, filter',
    duration: `${motionTokens.duration.base}s`,
    easing: `cubic-bezier(${motionTokens.ease.standard.join(',')})`,
  } as const;
}

/* ─── Mobile resume transition ─── */

/** Subtle fade-in for Capacitor mobile app resume. */
export function getMobileResumeMotion(reducedMotion = false) {
  if (reducedMotion) {
    return { initial: { opacity: 1 }, animate: { opacity: 1 } };
  }
  return {
    initial: { opacity: 0.92 },
    animate: { opacity: 1 },
    transition: { duration: motionTokens.duration.fast, ease: motionTokens.ease.entrance },
  };
}
