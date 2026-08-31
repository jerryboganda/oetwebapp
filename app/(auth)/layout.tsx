'use client';

import type { ReactNode } from 'react';
import { MotionPage } from '@/components/ui/motion-primitives';
import { StaleBuildGuard } from '@/components/shell/StaleBuildGuard';

export default function AuthLayout({ children }: { children: ReactNode }) {
  // Auth pages must be paintable before client hydration. The entrance motion
  // remains available for later route changes, but hiding the initial shell
  // makes first-contentful-paint/LCP depend on JavaScript execution.
  return (
    <MotionPage initial={false}>
      <StaleBuildGuard />
      {children}
    </MotionPage>
  );
}
