'use client';

import type { ReactNode } from 'react';
import { TourProvider } from '@/components/onboarding/tour-provider';
import { LearnerPasteGuard } from '@/components/system/LearnerPasteGuard';
import { NotificationCenterProvider } from '@/contexts/notification-center-context';

/**
 * Provider tree needed only after an authenticated workspace is active.
 * Keeping it in its own client chunk prevents auth routes from downloading
 * notification/SignalR and product-tour code or styles.
 */
export function AuthenticatedWorkspaceProviders({ children }: { children: ReactNode }) {
  return (
    <NotificationCenterProvider>
      <LearnerPasteGuard />
      <TourProvider>{children}</TourProvider>
    </NotificationCenterProvider>
  );
}
