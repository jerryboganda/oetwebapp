import { type ComponentPropsWithoutRef } from 'react';
import { motion, useReducedMotion } from 'motion/react';
import { cn } from '@/lib/utils';
import { getSurfaceMotion, prefersReducedMotion } from '@/lib/motion';

export type LearnerWorkspaceWidth = 'learner' | 'admin' | 'full';

const widthClasses: Record<LearnerWorkspaceWidth, string> = {
  learner: 'max-w-[1200px]',
  admin: 'max-w-[1440px]',
  full: 'max-w-none',
};

type LearnerWorkspaceContainerProps = ComponentPropsWithoutRef<typeof motion.div> & {
  maxWidth?: LearnerWorkspaceWidth;
};

export function LearnerWorkspaceContainer({
  className,
  children,
  maxWidth = 'learner',
  ...props
}: LearnerWorkspaceContainerProps) {
  const reducedMotion = prefersReducedMotion(useReducedMotion());
  const workspaceMotion = getSurfaceMotion('section', reducedMotion);

  return (
    <motion.div
      data-testid="learner-workspace-container"
      data-workspace-width={maxWidth}
      className={cn(
        'w-full mx-auto px-4 sm:px-6 lg:px-8 py-2 sm:py-4 lg:py-6',
        widthClasses[maxWidth],
        className,
      )}
      layout={!reducedMotion}
      {...workspaceMotion}
      {...props}
    >
      {children}
    </motion.div>
  );
}
