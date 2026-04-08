import { type ReactNode } from 'react';
import { AppShell, type AppShellProps } from './app-shell';
import { LearnerWorkspaceContainer } from './learner-workspace-container';

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
    <AppShell {...shellProps} workspaceRole="admin">
      <LearnerWorkspaceContainer className={workspaceClassName}>
        {children}
      </LearnerWorkspaceContainer>
    </AppShell>
  );
}
