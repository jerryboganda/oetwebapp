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
import { ArrowLeft } from 'lucide-react';

import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Skeleton } from '@/components/admin/ui/skeleton';

import { Toast } from '@/components/ui/alert';
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

const BREADCRUMBS_BASE = [
  { label: 'Admin', href: '/admin' },
  { label: 'Content', href: '/admin/content' },
  { label: 'Speaking', href: '/admin/content/speaking' },
  { label: 'Role-play cards', href: '/admin/content/speaking/role-play-cards' },
];

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

  const breadcrumbs = [
    ...BREADCRUMBS_BASE,
    { label: card?.scenarioTitle ?? 'Card', href: `/admin/content/speaking/role-play-cards/${encodeURIComponent(cardId)}` },
    { label: 'Interlocutor script' },
  ];

  return (
    <AdminCatalogLayout
      title={card ? `Interlocutor script: ${card.scenarioTitle}` : 'Interlocutor script'}
      description="Hidden card never shown to learners. Drives the AI patient persona and the tutor cue panel."
      breadcrumbs={breadcrumbs}
      eyebrow="CMS · Hidden"
      backHref={`/admin/content/speaking/role-play-cards/${encodeURIComponent(cardId)}`}
      hideViewModeToggle
      actions={
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
      }
    >
      {loading ? (
        <div className="col-span-full space-y-3">
          <Skeleton className="h-16" />
          <Skeleton className="h-64" />
        </div>
      ) : (
        <Card className="col-span-full">
          <CardContent className="p-6">
            <InterlocutorScriptEditor
              cardId={cardId}
              value={script}
              mode={script ? 'edit' : 'create'}
              submitting={submitting}
              onSubmit={handleSubmit}
            />
          </CardContent>
        </Card>
      )}

      {toast ? (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      ) : null}
    </AdminCatalogLayout>
  );
}
