import type { ReactNode } from 'react';
import { AuthProvider } from '@/contexts/auth-context';

export default function AuthLayout({ children }: { children: ReactNode }) {
  return <AuthProvider>{children}</AuthProvider>;
}
