import { type ReactNode } from 'react';
import { AppShell, type AppShellProps } from './app-shell';
import { cn } from '@/lib/utils';

export interface AdminDashboardShellProps extends AppShellProps {
  workspaceClassName?: string;
  children: ReactNode;
}

export function AdminDashboardShell({
  children,
  workspaceClassName,
  ...shellProps
}: AdminDashboardShellProps) {
  return (
    <AppShell requiredRole="admin" className="!p-0" {...shellProps} workspaceRole="admin">
      <div className={cn('w-full px-3 py-3 sm:px-4 sm:py-4', workspaceClassName)}>
        {children}
      </div>
    </AppShell>
  );
}
