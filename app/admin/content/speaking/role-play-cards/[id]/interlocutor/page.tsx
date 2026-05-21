'use client';

/**
 * Phase 1 (C.2) of the OET Speaking module roadmap.
 *
 * Admin edit view for the hidden interlocutor script on a specific
 * role-play card. The whole page is gated by the "EYES-ONLY: TUTOR +
 * ADMIN" banner inside `InterlocutorScriptEditor` so admins can't ship
 * this to learners by accident.
 */

import { useCallback, useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { ArrowLeft, ShieldAlert } from 'lucide-react';
import {
  AdminRouteHero,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { InterlocutorScriptEditor } from '@/components/domain/speaking/InterlocutorScriptEditor';
import {
  adminGetInterlocutorScript,
  adminGetRolePlayCard,
  adminUpsertInterlocutorScript,
  type InterlocutorScriptDetail,
  type RolePlayCardDetail,
  type UpsertInterlocutorScriptInput,
} from '@/lib/api/speaking-role-play-cards';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

export default function EditInterlocutorScriptPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const cardId = params?.id ?? '';

  const [card, setCard] = useState<RolePlayCardDetail | null>(null);
  const [script, setScript] = useState<InterlocutorScriptDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  const reload = useCallback(async () => {
    if (!cardId) return;
    setLoading(true);
    try {
      const [detail, existing] = await Promise.all([
        adminGetRolePlayCard(cardId),
        adminGetInterlocutorScript(cardId),
      ]);
      setCard(detail);
      setScript(existing);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setLoading(false);
    }
  }, [cardId]);

  useEffect(() => {
    void reload();
  }, [reload]);

  async function handleSubmit(targetCardId: string, value: UpsertInterlocutorScriptInput) {
    setSubmitting(true);
    try {
      const saved = await adminUpsertInterlocutorScript(targetCardId, value);
      setScript(saved);
      setToast({ variant: 'success', message: 'Interlocutor script saved.' });
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="Edit interlocutor script">
      <AdminRouteHero
        eyebrow="CMS"
        icon={ShieldAlert}
        accent="amber"
        title={
          card ? `Interlocutor script — ${card.scenarioTitle}` : 'Interlocutor script'
        }
        description="Hidden card never shown to learners. Drives the AI patient persona and the tutor cue panel."
        aside={
          <div className="rounded-2xl border border-border bg-background-light p-4 shadow-sm">
            <Button
              variant="outline"
              onClick={() =>
                router.push(
                  `/admin/content/speaking/role-play-cards/${encodeURIComponent(cardId)}`,
                )
              }
            >
              <ArrowLeft className="mr-1 h-4 w-4" /> Back to card
            </Button>
          </div>
        }
      />

      {loading ? (
        <div className="space-y-3">
          <Skeleton className="h-16" />
          <Skeleton className="h-64" />
        </div>
      ) : (
        <Card className="p-6">
          <InterlocutorScriptEditor
            cardId={cardId}
            value={script}
            mode={script ? 'edit' : 'create'}
            submitting={submitting}
            onSubmit={handleSubmit}
          />
        </Card>
      )}

      {toast ? (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      ) : null}
    </AdminRouteWorkspace>
  );
}
