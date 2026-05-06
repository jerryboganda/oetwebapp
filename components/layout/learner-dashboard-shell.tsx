import { type ReactNode } from 'react';
import { LearnerBreadcrumbs } from '@/components/domain/learner-breadcrumbs';
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
  distractionFree,
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
      requiredRole="learner"
      distractionFree={distractionFree}
      {...shellProps}
      navItems={learnerNavItems}
      mobileMenuSections={learnerMobileMenuSections}
      workspaceRole="learner"
    >
      <LearnerWorkspaceContainer className={workspaceClassName}>
        {!distractionFree ? <LearnerBreadcrumbs /> : null}
        {children}
      </LearnerWorkspaceContainer>
    </AppShell>
  );
}
