"use client";

import { createContext, type ReactNode, useContext } from 'react';
import { AppShell, type AppShellProps } from './app-shell';
import { LearnerWorkspaceContainer } from './learner-workspace-container';

const AdminDashboardShellContext = createContext(false);

export interface AdminDashboardShellProps extends AppShellProps {
  workspaceClassName?: string;
  children: ReactNode;
}

export function AdminDashboardShell({
  children,
  workspaceClassName,
  ...shellProps
}: AdminDashboardShellProps) {
  const isNestedShell = useContext(AdminDashboardShellContext);

  if (isNestedShell) {
    return <>{children}</>;
  }

  return (
    <AdminDashboardShellContext.Provider value={true}>
      <AppShell requiredRole="admin" {...shellProps} workspaceRole="admin">
        <LearnerWorkspaceContainer className={workspaceClassName}>
          {children}
        </LearnerWorkspaceContainer>
      </AppShell>
    </AdminDashboardShellContext.Provider>
  );
}
