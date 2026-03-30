import { type ReactNode } from 'react';
import { AppShell, type AppShellProps } from './app-shell';
import { LearnerWorkspaceContainer } from './learner-workspace-container';

export interface LearnerDashboardShellProps extends AppShellProps {
  workspaceClassName?: string;
  children: ReactNode;
}

export function LearnerDashboardShell({
  children,
  workspaceClassName,
  ...shellProps
}: LearnerDashboardShellProps) {
  return (
    <AppShell {...shellProps}>
      <LearnerWorkspaceContainer className={workspaceClassName}>
        {children}
      </LearnerWorkspaceContainer>
    </AppShell>
  );
}
