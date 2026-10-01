'use client';

import { AnimatePresence, motion, useReducedMotionConfig, type HTMLMotionProps } from 'motion/react';
import {
  getCollapseTransition,
  getFadeSwitchTransition,
  getFadeSwitchVariants,
  getMotionDelay,
  getMotionPresenceMode,
  getSurfaceMotion,
  getSurfaceTransition,
  prefersReducedMotion,
  type MotionSurface,
} from '@/lib/motion';
import { cn } from '@/lib/utils';
import { useSyncExternalStore, type ReactNode } from 'react';

type MotionRevealProps = HTMLMotionProps<'div'> & {
  surface?: Exclude<MotionSurface, 'skeleton'>;
  delayIndex?: number;
  delay?: number;
};

const subscribeToHydrationSnapshot = () => () => undefined;
const getHydratedSnapshot = () => true;
const getServerHydrationSnapshot = () => false;

function useHasHydrated(): boolean {
  return useSyncExternalStore(
    subscribeToHydrationSnapshot,
    getHydratedSnapshot,
    getServerHydrationSnapshot,
  );
}

/**
 * Reveals when scrolled into view, once, so below-the-fold content animates
 * where the learner can see it instead of finishing off-screen at mount.
 * `viewport` keeps its defaults on purpose: any visible pixel triggers, because
 * a wrapper taller than viewport ÷ amount could never reach a larger threshold
 * and would stay hidden. Layout animation is opt-in (`layout`), not a default
 * cost on every reveal.
 */
function MotionReveal({
  surface = 'section',
  delayIndex = 0,
  delay = 0,
  className,
  layout = false,
  transition,
  ...props
}: MotionRevealProps) {
  const hasHydrated = useHasHydrated();
  const reducedMotionPreference = prefersReducedMotion(useReducedMotionConfig());
  // The server cannot read the client's prefers-reduced-motion, so SSR always
  // emits the full (non-reduced) variant — including its transform. If the
  // client's first render adopted a different reduced-motion value, the variant
  // markup would diverge from the server HTML and React would report a hydration
  // mismatch. Gate the preference behind hydration (mirroring runtimeKind) so
  // the server and first client render agree, then honor the real preference on
  // the post-hydration render.
  const reducedMotion = hasHydrated ? reducedMotionPreference : false;
  const runtimeKind = hasHydrated ? undefined : 'web';
  // `whileInView` replaces the mount-time `animate`; overlays that also use
  // getSurfaceMotion keep animating on mount.
  const { initial, exit, variants } = getSurfaceMotion(surface, reducedMotion, runtimeKind);
  const baseTransition = {
    ...getSurfaceTransition(surface, reducedMotion, runtimeKind),
    delay: getMotionDelay(delayIndex, reducedMotion, delay, runtimeKind),
  };

  return (
    <motion.div
      layout={layout}
      className={cn(className)}
      initial={initial}
      exit={exit}
      variants={variants}
      whileInView="visible"
      viewport={{ once: true }}
      transition={typeof transition === 'object' && transition ? { ...baseTransition, ...transition } : baseTransition}
      {...props}
    />
  );
}

export function MotionPage(props: Omit<MotionRevealProps, 'surface'>) {
  return <MotionReveal surface="route" {...props} />;
}

export function MotionSection(props: Omit<MotionRevealProps, 'surface'>) {
  return <MotionReveal surface="section" {...props} />;
}

export function MotionList(props: Omit<MotionRevealProps, 'surface'>) {
  return <MotionReveal surface="list" {...props} />;
}

export function MotionItem(props: Omit<MotionRevealProps, 'surface'>) {
  return <MotionReveal surface="item" {...props} />;
}

/* ─── MotionPresence ─── */

interface MotionPresenceProps {
  children: ReactNode;
  /** Override AnimatePresence mode; defaults to reduced-motion–aware 'wait' or 'sync'. */
  mode?: 'wait' | 'sync' | 'popLayout';
}

/** Thin AnimatePresence wrapper that auto-selects presence mode based on reduced-motion. */
export function MotionPresence({ children, mode }: MotionPresenceProps) {
  const reducedMotion = prefersReducedMotion(useReducedMotionConfig());
  const resolvedMode = mode ?? getMotionPresenceMode(reducedMotion);
  return <AnimatePresence mode={resolvedMode}>{children}</AnimatePresence>;
}

/* ─── MotionCollapse ─── */

interface MotionCollapseProps {
  open: boolean;
  children: ReactNode;
  className?: string;
  id?: string;
  role?: string;
  'aria-labelledby'?: string;
}

/** Animated height expand/collapse using motion layout and overflow clipping. */
export function MotionCollapse({ open, children, className, ...accessibilityProps }: MotionCollapseProps) {
  const reducedMotion = prefersReducedMotion(useReducedMotionConfig());
  const transition = getCollapseTransition(reducedMotion);

  return (
    <AnimatePresence initial={false}>
      {open && (
        <motion.div
          initial={{ height: 0, opacity: 0 }}
          animate={{ height: 'auto', opacity: 1 }}
          exit={{ height: 0, opacity: 0 }}
          transition={transition}
          className={cn('overflow-hidden', className)}
          {...accessibilityProps}
        >
          {children}
        </motion.div>
      )}
    </AnimatePresence>
  );
}

/* ─── MotionFadeSwitch ─── */

interface MotionFadeSwitchProps {
  /** Unique key identifying the current content; change triggers animation. */
  activeKey: string;
  children: ReactNode;
  className?: string;
  /** Direction hint: 1 = forward, -1 = backward. */
  direction?: 1 | -1;
}

/** AnimatePresence mode="wait" wrapper for mutually exclusive content (steps, tab panels). */
export function MotionFadeSwitch({ activeKey, children, className, direction = 1 }: MotionFadeSwitchProps) {
  const reducedMotion = prefersReducedMotion(useReducedMotionConfig());
  const variants = getFadeSwitchVariants(reducedMotion, direction);
  const transition = getFadeSwitchTransition(reducedMotion);

  return (
    <AnimatePresence mode={getMotionPresenceMode(reducedMotion)} initial={false}>
      <motion.div
        key={activeKey}
        variants={variants}
        initial="hidden"
        animate="visible"
        exit="exit"
        transition={transition}
        className={className}
      >
        {children}
      </motion.div>
    </AnimatePresence>
  );
}
