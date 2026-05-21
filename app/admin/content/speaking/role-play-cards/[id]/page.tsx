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
import { ArrowLeft, ClipboardList, Copy, Eye, ShieldAlert } from 'lucide-react';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { RolePlayCardEditor, type RolePlayCardEditorValue } from '@/components/domain/speaking/RolePlayCardEditor';
import {
  adminArchiveRolePlayCard,
  adminDuplicateRolePlayCard,
  adminGetRolePlayCard,
  adminPatchRolePlayCard,
  adminPublishRolePlayCard,
  type RolePlayCardDetail,
} from '@/lib/api/speaking-role-play-cards';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

export default function EditSpeakingRolePlayCardPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const cardId = params?.id ?? '';

  const [card, setCard] = useState<RolePlayCardDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

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

  return (
    <AdminRouteWorkspace role="main" aria-label="Edit speaking role-play card">
      <AdminRouteHero
        eyebrow="CMS"
        icon={ClipboardList}
        accent="navy"
        title={card?.scenarioTitle ?? 'Edit role-play card'}
        description="Edit the candidate-facing card. The hidden interlocutor script lives on its own side page."
        aside={
          <div className="flex flex-col gap-2 rounded-2xl border border-border bg-background-light p-4 shadow-sm">
            <Button
              variant="outline"
              onClick={() => router.push('/admin/content/speaking/role-play-cards')}
            >
              <ArrowLeft className="mr-1 h-4 w-4" /> Back to list
            </Button>
            {card ? (
              <>
                <Link
                  href={`/admin/content/speaking/role-play-cards/${encodeURIComponent(card.cardId)}/interlocutor`}
                  className="inline-flex items-center justify-center rounded-2xl border border-amber-300 bg-amber-50 px-3 py-2 text-xs font-bold uppercase tracking-wider text-amber-800 hover:bg-amber-100 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-100"
                >
                  <ShieldAlert className="mr-1 h-3.5 w-3.5" />
                  {card.hasInterlocutorScript
                    ? 'Edit interlocutor script'
                    : 'Add interlocutor script'}
                </Link>
                <Link
                  href={`/admin/content/speaking/role-play-cards/${encodeURIComponent(card.cardId)}/preview`}
                  className="inline-flex items-center justify-center rounded-2xl border border-border bg-surface px-3 py-2 text-xs font-bold uppercase tracking-wider hover:border-primary"
                >
                  <Eye className="mr-1 h-3.5 w-3.5" /> Preview
                </Link>
              </>
            ) : null}
          </div>
        }
      />

      {loading ? (
        <div className="space-y-3">
          <Skeleton className="h-12" />
          <Skeleton className="h-64" />
        </div>
      ) : card ? (
        <>
          <AdminRoutePanel>
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div className="flex flex-wrap items-center gap-2 text-sm">
                <Badge
                  variant={
                    isPublished ? 'success' : isArchived ? 'outline' : 'muted'
                  }
                >
                  {card.status}
                </Badge>
                <span className="text-admin-text-muted">
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
          </AdminRoutePanel>

          <Card className="p-6">
            <RolePlayCardEditor
              mode="edit"
              initial={card}
              submitting={busy}
              onSubmit={handleSubmit}
            />
          </Card>
        </>
      ) : (
        <Card className="p-8 text-center text-muted">Card not found.</Card>
      )}

      {toast ? (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      ) : null}
    </AdminRouteWorkspace>
  );
}
