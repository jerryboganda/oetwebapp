'use client';

import { useState } from 'react';
import { HelpCircle } from 'lucide-react';
import { cn } from '@/lib/utils';
import { trackHelpCenterOpened } from '@/lib/onboarding/tour-events';
import { HelpCenterDrawer } from './help-center-drawer';
import type { UserRole } from '@/lib/types/auth';

/**
 * The persistent "?" Help affordance in the top nav. Carries the
 * `learner-help-launcher` tour anchor (the dashboard tour points here so users
 * learn where to replay tours). Opens the role-aware Help / replay drawer.
 */
export function TourLauncher({ workspaceRole, className }: { workspaceRole?: UserRole; className?: string }) {
  const [open, setOpen] = useState(false);

  return (
    <>
      <button
        type="button"
        data-tour="learner-help-launcher"
        onClick={() => {
          setOpen(true);
          trackHelpCenterOpened({
            role: workspaceRole,
            route: typeof window !== 'undefined' ? window.location.pathname : undefined,
          });
        }}
        className={cn(
          'touch-target pressable flex h-9 w-9 items-center justify-center rounded-full text-muted transition-colors hover:bg-primary/10 hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
          className,
        )}
        aria-label="Help and guided tours"
        aria-haspopup="dialog"
      >
        <HelpCircle className="h-5 w-5" aria-hidden="true" />
      </button>
      <HelpCenterDrawer open={open} onClose={() => setOpen(false)} workspaceRole={workspaceRole} />
    </>
  );
}
