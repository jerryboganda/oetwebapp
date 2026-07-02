import type { ReactNode } from 'react';
import {
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { WizardShellLoader } from '@/components/domain/mock-wizard/WizardShellLoader';

// The bundle fetch must happen client-side: the admin API client resolves its
// bearer token from web storage, which is unavailable during server render.
export default async function MockWizardLayout({
  children,
  params,
}: {
  children: ReactNode;
  params: Promise<{ bundleId: string }>;
}) {
  const { bundleId } = await params;

  return (
    <AdminRouteWorkspace>
      <AdminRoutePanel>
        <WizardShellLoader bundleId={bundleId}>{children}</WizardShellLoader>
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
