'use client';

import type { ReactNode } from 'react';
import { AuthProvider } from '@/contexts/auth-context';
import { MobileRuntimeBridge } from '@/components/mobile/mobile-runtime-bridge';
import { RuntimeLifecycleBridge } from '@/components/runtime/runtime-lifecycle-bridge';

export function AppProviders({ children }: { children: ReactNode }) {
  return (
    <AuthProvider>
      <RuntimeLifecycleBridge />
      <MobileRuntimeBridge />
      {children}
    </AuthProvider>
  );
}
