import { type ReactNode } from 'react';
import { AppShell, type AppShellProps } from './app-shell';
import { LearnerWorkspaceContainer } from './learner-workspace-container';
import { learnNavItems, mainNavItems } from './sidebar';

export interface LearnerDashboardShellProps extends AppShellProps {
  workspaceClassName?: string;
  children: ReactNode;
}

export function LearnerDashboardShell({
  children,
  workspaceClassName,
  mobileMenuSections,
  navItems,
  ...shellProps
}: LearnerDashboardShellProps) {
  const learnerNavItems = navItems ?? mainNavItems;
  const learnerMobileMenuSections = mobileMenuSections ?? [
    { label: 'Practice', items: learnerNavItems },
    { label: 'Learn', items: learnNavItems },
  ];

  return (
    <AppShell
      {...shellProps}
      navItems={learnerNavItems}
      mobileMenuSections={learnerMobileMenuSections}
      workspaceRole="learner"
    >
      <LearnerWorkspaceContainer className={workspaceClassName}>
        {children}
      </LearnerWorkspaceContainer>
    </AppShell>
  );
}
