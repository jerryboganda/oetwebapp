'use client';

import type { ReactNode } from 'react';
import { AuthProvider } from '@/contexts/auth-context';
import { MobileRuntimeBridge } from '@/components/mobile/mobile-runtime-bridge';

export function AppProviders({ children }: { children: ReactNode }) {
  return (
    <AuthProvider>
      <MobileRuntimeBridge />
      {children}
    </AuthProvider>
  );
}
