import { type ReactNode } from 'react';
import { LearnerBreadcrumbs } from '@/components/domain/learner-breadcrumbs';
import { AppShell, type AppShellProps } from './app-shell';
import { LearnerWorkspaceContainer } from './learner-workspace-container';
import { learnerMainNavItems, learnerMobileNavItems } from './sidebar';

export interface LearnerDashboardShellProps extends AppShellProps {
  workspaceClassName?: string;
  children: ReactNode;
}

export function LearnerDashboardShell({
  children,
  workspaceClassName,
  distractionFree,
  mobileMenuSections,
  mobileNavItems,
  navItems,
  ...shellProps
}: LearnerDashboardShellProps) {
  // Learner sidebar: Dashboard | Listening | Reading | Writing | Mocks |
  // Recalls | Progress | Billing. The legacy Learn group (Grammar/Classes/
  // Lessons/Strategies/Conversation) is not surfaced in the candidate workspace.
  const learnerNavItems = navItems ?? learnerMainNavItems;
  const learnerMobileMenuSections = mobileMenuSections ?? [
    { label: 'Practice', items: learnerNavItems },
  ];
  const learnerBottomNavItems = mobileNavItems ?? learnerMobileNavItems;

  return (
    <AppShell
      requiredRole="learner"
      distractionFree={distractionFree}
      {...shellProps}
      navItems={learnerNavItems}
      mobileNavItems={learnerBottomNavItems}
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
