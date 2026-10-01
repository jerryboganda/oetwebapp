'use client';

import { type ComponentPropsWithoutRef } from 'react';
import { motion, useReducedMotionConfig } from 'motion/react';
import { cn } from '@/lib/utils';
import { getSurfaceMotion, prefersReducedMotion } from '@/lib/motion';

type LearnerWorkspaceContainerProps = ComponentPropsWithoutRef<typeof motion.div>;

export function LearnerWorkspaceContainer({ className, children, initial = false, ...props }: LearnerWorkspaceContainerProps) {
  const reducedMotion = prefersReducedMotion(useReducedMotionConfig());
  const workspaceMotion = getSurfaceMotion('section', reducedMotion);

  return (
    <motion.div
      data-testid="learner-workspace-container"
      // No vertical padding: every shell's <main> already pads top and bottom.
      className={cn('w-full max-w-[1200px] mx-auto px-4 sm:px-6 lg:px-8', className)}
      // Layout projection during the loading-skeleton -> dashboard swap can
      // apply large transforms on WebKit and delay the real LCP candidate.
      // Route/state entrance motion remains active through workspaceMotion.
      layout={initial !== false && !reducedMotion}
      {...workspaceMotion}
      initial={initial}
      {...props}
    >
      {children}
    </motion.div>
  );
}
