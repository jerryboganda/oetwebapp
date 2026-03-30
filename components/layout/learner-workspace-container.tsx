import { type HTMLAttributes } from 'react';
import { cn } from '@/lib/utils';

export function LearnerWorkspaceContainer({ className, children, ...props }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      data-testid="learner-workspace-container"
      className={cn('w-full max-w-[1200px] mx-auto px-4 sm:px-6 lg:px-8 py-6', className)}
      {...props}
    >
      {children}
    </div>
  );
}
