'use client';

/**
 * Speaking card wizard shell. Loads the card once (client-side, using the
 * browser-auth API client), resolves write/publish permissions, and mounts the
 * generic <AdminWizard> so the per-step pages render inside it.
 */

import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { useParams } from 'next/navigation';
import { Loader2 } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { AdminRoutePanel, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AdminWizard } from '@/components/domain/wizard/AdminWizard';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { CARD_WIZARD_STEPS, buildCardStepHref } from '@/components/domain/speaking/wizard/card-wizard-config';
import { adminGetRolePlayCard, type RolePlayCardDetail } from '@/lib/api/speaking-role-play-cards';

export default function SpeakingCardWizardLayout({ children }: { children: ReactNode }) {
  const params = useParams<{ id: string }>();
  const cardId = params?.id ?? '';
  const { user } = useCurrentUser();
  const perms = user?.adminPermissions;

  const [card, setCard] = useState<RolePlayCardDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    setLoading(true);
    adminGetRolePlayCard(cardId)
      .then((c) => {
        if (active) setCard(c);
      })
      .catch((e) => {
        if (active) setError(e instanceof Error ? e.message : 'Card not found.');
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [cardId]);

  const refresh = useCallback(() => adminGetRolePlayCard(cardId), [cardId]);

  if (loading) {
    return (
      <AdminRouteWorkspace>
        <AdminRoutePanel>
          <p className="inline-flex items-center gap-2 text-sm text-admin-text-muted">
            <Loader2 className="h-4 w-4 animate-spin" /> Loading role-play card…
          </p>
        </AdminRoutePanel>
      </AdminRouteWorkspace>
    );
  }

  if (error || !card) {
    return (
      <AdminRouteWorkspace>
        <AdminRoutePanel>
          <p className="text-sm text-admin-text">{error ?? 'Role-play card not found.'}</p>
        </AdminRoutePanel>
      </AdminRouteWorkspace>
    );
  }

  const canWrite = hasPermission(perms, AdminPermission.ContentWrite);
  const canPublish = hasPermission(perms, AdminPermission.ContentPublish);

  return (
    <AdminRouteWorkspace>
      <AdminRoutePanel>
        <AdminWizard<RolePlayCardDetail>
          entity={card}
          steps={CARD_WIZARD_STEPS}
          buildStepHref={(stepId) => buildCardStepHref(cardId, stepId)}
          refresh={refresh}
          canWrite={canWrite}
          canPublish={canPublish}
          header={
            <div className="flex items-center gap-2">
              <span className="font-bold text-navy">{card.scenarioTitle}</span>
              <Badge variant={(card.status ?? '').toLowerCase() === 'published' ? 'success' : 'muted'}>{card.status}</Badge>
            </div>
          }
        >
          {children}
        </AdminWizard>
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
