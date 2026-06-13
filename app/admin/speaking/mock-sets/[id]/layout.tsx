'use client';

/** Speaking mock-set wizard shell. */

import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { useParams } from 'next/navigation';
import { Loader2 } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { AdminRoutePanel, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AdminWizard } from '@/components/domain/wizard/AdminWizard';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { MOCK_SET_WIZARD_STEPS, buildMockSetStepHref } from '@/components/domain/speaking/mock-set-wizard/mock-set-wizard-config';
import { fetchAdminSpeakingMockSet, type AdminSpeakingMockSetRow } from '@/lib/api';

export default function SpeakingMockSetWizardLayout({ children }: { children: ReactNode }) {
  const params = useParams<{ id: string }>();
  const mockSetId = params?.id ?? '';
  const { user } = useCurrentUser();
  const perms = user?.adminPermissions;

  const [row, setRow] = useState<AdminSpeakingMockSetRow | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    setLoading(true);
    fetchAdminSpeakingMockSet(mockSetId)
      .then((r) => {
        if (active) setRow(r);
      })
      .catch((e) => {
        if (active) setError(e instanceof Error ? e.message : 'Mock set not found.');
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [mockSetId]);

  const refresh = useCallback(() => fetchAdminSpeakingMockSet(mockSetId), [mockSetId]);

  if (loading) {
    return (
      <AdminRouteWorkspace>
        <AdminRoutePanel>
          <p className="inline-flex items-center gap-2 text-sm text-admin-text-muted">
            <Loader2 className="h-4 w-4 animate-spin" /> Loading mock set…
          </p>
        </AdminRoutePanel>
      </AdminRouteWorkspace>
    );
  }

  if (error || !row) {
    return (
      <AdminRouteWorkspace>
        <AdminRoutePanel>
          <p className="text-sm text-admin-text">{error ?? 'Mock set not found.'}</p>
        </AdminRoutePanel>
      </AdminRouteWorkspace>
    );
  }

  const canWrite = hasPermission(perms, AdminPermission.ContentWrite);
  const canPublish = hasPermission(perms, AdminPermission.ContentPublish);

  return (
    <AdminRouteWorkspace>
      <AdminRoutePanel>
        <AdminWizard<AdminSpeakingMockSetRow>
          entity={row}
          steps={MOCK_SET_WIZARD_STEPS}
          buildStepHref={(stepId) => buildMockSetStepHref(mockSetId, stepId)}
          refresh={refresh}
          canWrite={canWrite}
          canPublish={canPublish}
          header={
            <div className="flex items-center gap-2">
              <span className="font-bold text-navy">{row.title}</span>
              <Badge variant={row.status === 'published' ? 'success' : 'muted'}>{row.status}</Badge>
            </div>
          }
        >
          {children}
        </AdminWizard>
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
