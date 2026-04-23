import { type ReactNode } from 'react';
import { AppShell, type AppShellProps } from './app-shell';
import { LearnerWorkspaceContainer } from './learner-workspace-container';

export interface SponsorDashboardShellProps extends AppShellProps {
  workspaceClassName?: string;
  children: ReactNode;
}

export function SponsorDashboardShell({
  children,
  workspaceClassName,
  ...shellProps
}: SponsorDashboardShellProps) {
  return (
    <AppShell requiredRole="sponsor" {...shellProps} workspaceRole="sponsor">
      <LearnerWorkspaceContainer className={workspaceClassName}>
        {children}
      </LearnerWorkspaceContainer>
    </AppShell>
  );
}
