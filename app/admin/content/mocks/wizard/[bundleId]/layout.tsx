import { notFound } from 'next/navigation';
import type { ReactNode } from 'react';
import {
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { fetchAdminMockBundle } from '@/lib/api';
import { WizardShell, type WizardMockBundle } from '@/components/domain/mock-wizard/WizardShell';

export default async function MockWizardLayout({
  children,
  params,
}: {
  children: ReactNode;
  params: Promise<{ bundleId: string }>;
}) {
  const { bundleId } = await params;
  let bundle: WizardMockBundle;
  try {
    bundle = (await fetchAdminMockBundle(bundleId)) as WizardMockBundle;
  } catch {
    notFound();
  }
  if (!bundle?.id) notFound();

  return (
    <AdminRouteWorkspace>
      <AdminRoutePanel>
        <WizardShell bundle={bundle}>{children}</WizardShell>
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
