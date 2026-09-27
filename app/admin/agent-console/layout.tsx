import type { ReactNode } from 'react';
import { OwnerAgentConsoleShell } from '@/components/admin/agent-console/OwnerAgentConsoleShell';

/**
 * Owner Agent Console segment. The admin shell (app/admin/layout.tsx) already
 * requires system_admin for these routes; the console shell adds the owner
 * check (/v1/owner-agent/me), the password + TOTP unlock (1 hour, HttpOnly
 * cookie) and the Sessions / History / Settings tabs.
 * proxy.ts serves this segment with its own strict CSP.
 */
export default function AgentConsoleLayout({ children }: { children: ReactNode }) {
  return <OwnerAgentConsoleShell>{children}</OwnerAgentConsoleShell>;
}
