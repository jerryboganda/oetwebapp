'use client';

/**
 * Phase 1 (C.2) of the OET Speaking module roadmap.
 *
 * Admin edit view for a single role-play card. Renders the candidate
 * card via `RolePlayCardEditor`, shows the current lifecycle status
 * with publish/archive/duplicate buttons, and links across to the
 * interlocutor editor + preview pages.
 */

import { useCallback, useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, Copy, Eye, ShieldAlert } from 'lucide-react';

import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Skeleton } from '@/components/admin/ui/skeleton';

import { Toast } from '@/components/ui/alert';
import { RolePlayCardEditor, type RolePlayCardEditorValue } from '@/components/domain/speaking/RolePlayCardEditor';
import {
  adminArchiveRolePlayCard,
  adminDuplicateRolePlayCard,
  adminGetRolePlayCard,
  adminPatchRolePlayCard,
  adminPublishRolePlayCard,
  adminListSpeakingCardTypes,
  type RolePlayCardDetail,
  type SpeakingCardTypeDetail,
} from '@/lib/api/speaking-role-play-cards';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

const BREADCRUMBS_BASE = [
  { label: 'Admin', href: '/admin' },
  { label: 'Content', href: '/admin/content' },
  { label: 'Speaking', href: '/admin/content/speaking' },
  { label: 'Role-play cards', href: '/admin/content/speaking/role-play-cards' },
];

export default function EditSpeakingRolePlayCardPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const cardId = params?.id ?? '';

  const [card, setCard] = useState<RolePlayCardDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);
  const [cardTypes, setCardTypes] = useState<SpeakingCardTypeDetail[]>([]);

  useEffect(() => {
    adminListSpeakingCardTypes(true)
      .then(setCardTypes)
      .catch(() => setCardTypes([]));
  }, []);

  const reload = useCallback(async () => {
    if (!cardId) return;
    setLoading(true);
    try {
      const detail = await adminGetRolePlayCard(cardId);
      setCard(detail);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setLoading(false);
    }
  }, [cardId]);

  useEffect(() => {
    void reload();
  }, [reload]);

  async function handleSubmit(value: RolePlayCardEditorValue) {
    if (!cardId) return;
    setBusy(true);
    try {
      const updated = await adminPatchRolePlayCard(cardId, value);
      setCard(updated);
      setToast({ variant: 'success', message: 'Card updated.' });
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setBusy(false);
    }
  }

  async function handlePublish() {
    if (!card) return;
    setBusy(true);
    try {
      const updated = await adminPublishRolePlayCard(card.cardId);
      setCard(updated);
      setToast({ variant: 'success', message: `Published "${updated.scenarioTitle}".` });
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setBusy(false);
    }
  }

  async function handleArchive() {
    if (!card) return;
    if (!confirm('Archive this card? Archived cards are read-only.')) return;
    setBusy(true);
    try {
      const updated = await adminArchiveRolePlayCard(card.cardId);
      setCard(updated);
      setToast({ variant: 'success', message: 'Card archived.' });
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setBusy(false);
    }
  }

  async function handleDuplicate() {
    if (!card) return;
    setBusy(true);
    try {
      const dup = await adminDuplicateRolePlayCard(card.cardId);
      setToast({ variant: 'success', message: `Duplicated as "${dup.scenarioTitle}".` });
      router.push(`/admin/content/speaking/role-play-cards/${encodeURIComponent(dup.cardId)}`);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setBusy(false);
    }
  }

  const isArchived = card?.status?.toLowerCase() === 'archived';
  const isPublished = card?.status?.toLowerCase() === 'published';

  const breadcrumbs = [
    ...BREADCRUMBS_BASE,
    { label: card?.scenarioTitle ?? 'Edit role-play card' },
  ];

  return (
    <AdminCatalogLayout
      title={card?.scenarioTitle ?? 'Edit role-play card'}
      description="Edit the candidate-facing card. The hidden interlocutor script lives on its own side page."
      breadcrumbs={breadcrumbs}
      eyebrow="CMS"
      backHref="/admin/content/speaking/role-play-cards"
      hideViewModeToggle
      actions={
        <div className="flex flex-wrap gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => router.push('/admin/content/speaking/role-play-cards')}
          >
            <ArrowLeft className="mr-1 h-4 w-4" /> Back to list
          </Button>
          {card ? (
            <>
              <Link
                href={`/admin/content/speaking/role-play-cards/${encodeURIComponent(card.cardId)}/interlocutor`}
                className="inline-flex h-8 items-center justify-center rounded-admin border border-admin-warning bg-admin-bg-surface px-3 text-xs font-semibold text-admin-warning hover:bg-admin-state-hover"
              >
                <ShieldAlert className="mr-1 h-3.5 w-3.5" />
                {card.hasInterlocutorScript
                  ? 'Edit interlocutor script'
                  : 'Add interlocutor script'}
              </Link>
              <Link
                href={`/admin/content/speaking/role-play-cards/${encodeURIComponent(card.cardId)}/preview`}
                className="inline-flex h-8 items-center justify-center rounded-admin border border-admin-border bg-admin-bg-surface px-3 text-xs font-semibold text-admin-fg-default hover:border-admin-primary"
              >
                <Eye className="mr-1 h-3.5 w-3.5" /> Preview
              </Link>
            </>
          ) : null}
        </div>
      }
    >
      {loading ? (
        <div className="col-span-full space-y-3">
          <Skeleton className="h-12" />
          <Skeleton className="h-64" />
        </div>
      ) : card ? (
        <>
          <Card className="col-span-full">
            <CardContent className="p-4">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <div className="flex flex-wrap items-center gap-2 text-sm">
                  <Badge
                    variant={isPublished ? 'success' : isArchived ? 'warning' : 'default'}
                    intensity="tinted"
                  >
                    {card.status}
                  </Badge>
                  <span className="text-admin-fg-muted">
                    Created {new Date(card.createdAt).toLocaleDateString()} - Updated{' '}
                    {new Date(card.updatedAt).toLocaleDateString()}
                    {card.publishedAt
                      ? ` - Published ${new Date(card.publishedAt).toLocaleDateString()}`
                      : ''}
                  </span>
                </div>
                <div className="flex flex-wrap items-center gap-2">
                  {!isPublished && !isArchived ? (
                    <Button
                      onClick={() => void handlePublish()}
                      disabled={busy || !card.hasInterlocutorScript}
                      title={
                        card.hasInterlocutorScript
                          ? 'Publish card'
                          : 'Add an interlocutor script before publishing'
                      }
                    >
                      Publish
                    </Button>
                  ) : null}
                  {isPublished ? (
                    <Button variant="outline" onClick={() => void handleArchive()} disabled={busy}>
                      Archive
                    </Button>
                  ) : null}
                  <Button variant="ghost" onClick={() => void handleDuplicate()} disabled={busy}>
                    <Copy className="mr-1 h-4 w-4" /> Duplicate
                  </Button>
                </div>
              </div>
            </CardContent>
          </Card>

          <Card className="col-span-full">
            <CardContent className="p-6">
              <RolePlayCardEditor
                mode="edit"
                initial={card}
                submitting={busy}
                onSubmit={handleSubmit}
                cardTypes={cardTypes}
              />
            </CardContent>
          </Card>
        </>
      ) : (
        <Card className="col-span-full">
          <CardContent className="p-8 text-center text-admin-fg-muted">Card not found.</CardContent>
        </Card>
      )}

      {toast ? (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      ) : null}
    </AdminCatalogLayout>
  );
}
