import { type ReactNode } from 'react';
import { AppShell, type AppShellProps } from './app-shell';
import { LearnerWorkspaceContainer } from './learner-workspace-container';

export interface ExpertDashboardShellProps extends AppShellProps {
  workspaceClassName?: string;
  children: ReactNode;
}

export function ExpertDashboardShell({
  children,
  workspaceClassName,
  ...shellProps
}: ExpertDashboardShellProps) {
  return (
    <AppShell {...shellProps} workspaceRole="expert">
      <LearnerWorkspaceContainer className={workspaceClassName}>
        {children}
      </LearnerWorkspaceContainer>
    </AppShell>
  );
}
