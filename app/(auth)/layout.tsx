'use client';

import type { ReactNode } from 'react';
import { MotionPage } from '@/components/ui/motion-primitives';

export default function AuthLayout({ children }: { children: ReactNode }) {
  return <MotionPage>{children}</MotionPage>;
}
