'use client';

import { AnimatePresence, motion, useReducedMotion, type HTMLMotionProps } from 'motion/react';
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

function MotionReveal({
  surface = 'section',
  delayIndex = 0,
  delay = 0,
  className,
  layout = 'position',
  transition,
  ...props
}: MotionRevealProps) {
  const hasHydrated = useHasHydrated();
  const reducedMotionPreference = prefersReducedMotion(useReducedMotion());
  // The server cannot read the client's prefers-reduced-motion, so SSR always
  // emits the full (non-reduced) variant — including its transform. If the
  // client's first render adopted a different reduced-motion value, the variant
  // markup would diverge from the server HTML and React would report a hydration
  // mismatch. Gate the preference behind hydration (mirroring runtimeKind) so
  // the server and first client render agree, then honor the real preference on
  // the post-hydration render.
  const reducedMotion = hasHydrated ? reducedMotionPreference : false;
  const runtimeKind = hasHydrated ? undefined : 'web';
  const motionProps = getSurfaceMotion(surface, reducedMotion, runtimeKind);
  const baseTransition = {
    ...getSurfaceTransition(surface, reducedMotion, runtimeKind),
    delay: getMotionDelay(delayIndex, reducedMotion, delay, runtimeKind),
  };

  return (
    <motion.div
      layout={layout}
      className={cn(className)}
      {...motionProps}
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
  const reducedMotion = prefersReducedMotion(useReducedMotion());
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
  const reducedMotion = prefersReducedMotion(useReducedMotion());
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
  const reducedMotion = prefersReducedMotion(useReducedMotion());
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
