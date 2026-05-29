'use client';

/**
 * Phase 1 (C.2) of the OET Speaking module roadmap.
 *
 * Admin list view for role-play cards. Mirrors the structure of
 * `app/admin/content/speaking/shared-resources/page.tsx`:
 *   - AdminCatalogLayout (filters + actions)
 *   - Filter row (profession / difficulty / status)
 *   - Card list with badges, has-interlocutor pill, published-at column,
 *     and action buttons.
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { Copy, Plus, RotateCcw, Sparkles } from 'lucide-react';

import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { EmptyState } from '@/components/admin/ui/empty-state';

import { Select } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import {
  DIFFICULTY_OPTIONS,
  PROFESSION_OPTIONS,
  adminArchiveRolePlayCard,
  adminDuplicateRolePlayCard,
  adminListRolePlayCards,
  adminPublishRolePlayCard,
  type RolePlayCardSummary,
} from '@/lib/api/speaking-role-play-cards';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

const STATUS_OPTIONS: { value: string; label: string }[] = [
  { value: '', label: 'All statuses' },
  { value: 'Draft', label: 'Draft' },
  { value: 'Published', label: 'Published' },
  { value: 'Archived', label: 'Archived' },
];

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Content', href: '/admin/content' },
  { label: 'Speaking', href: '/admin/content/speaking' },
  { label: 'Role-play cards' },
];

export default function AdminSpeakingRolePlayCardsPage() {
  const router = useRouter();
  const [rows, setRows] = useState<RolePlayCardSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastState>(null);

  const [profession, setProfession] = useState('');
  const [difficulty, setDifficulty] = useState('');
  const [status, setStatus] = useState('');

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const data = await adminListRolePlayCards({
        professionId: profession || undefined,
        difficulty: difficulty || undefined,
        status: status || undefined,
      });
      // Some backends return { rolePlayCards: [...] }, others return [...].
      const items = Array.isArray(data)
        ? (data as RolePlayCardSummary[])
        : ((data as unknown as { rolePlayCards?: RolePlayCardSummary[] })
            .rolePlayCards ?? []);
      setRows(items);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setLoading(false);
    }
  }, [profession, difficulty, status]);

  useEffect(() => {
    void reload();
  }, [reload]);

  async function handlePublish(row: RolePlayCardSummary) {
    setBusyId(row.cardId);
    try {
      await adminPublishRolePlayCard(row.cardId);
      setToast({ variant: 'success', message: `Published "${row.title}".` });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setBusyId(null);
    }
  }

  async function handleArchive(row: RolePlayCardSummary) {
    if (!confirm(`Archive "${row.title}"? Archived cards are read-only.`)) return;
    setBusyId(row.cardId);
    try {
      await adminArchiveRolePlayCard(row.cardId);
      setToast({ variant: 'success', message: `Archived "${row.title}".` });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setBusyId(null);
    }
  }

  async function handleDuplicate(row: RolePlayCardSummary) {
    setBusyId(row.cardId);
    try {
      const created = await adminDuplicateRolePlayCard(row.cardId);
      setToast({ variant: 'success', message: `Duplicated as "${created.scenarioTitle}".` });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setBusyId(null);
    }
  }

  const filterCount = useMemo(
    () => [profession, difficulty, status].filter(Boolean).length,
    [profession, difficulty, status],
  );

  const filtersNode = (
    <div className="grid grid-cols-1 gap-3 sm:grid-cols-4">
      <Select
        label="Profession"
        value={profession}
        onChange={(e) => setProfession(e.target.value)}
        options={[{ value: '', label: 'All professions' }, ...PROFESSION_OPTIONS]}
      />
      <Select
        label="Difficulty"
        value={difficulty}
        onChange={(e) => setDifficulty(e.target.value)}
        options={[{ value: '', label: 'All difficulties' }, ...DIFFICULTY_OPTIONS]}
      />
      <Select
        label="Status"
        value={status}
        onChange={(e) => setStatus(e.target.value)}
        options={STATUS_OPTIONS}
      />
      <div className="flex items-end">
        <Button variant="outline" onClick={() => void reload()}>
          <RotateCcw className="mr-1 h-4 w-4" /> Refresh
        </Button>
      </div>
    </div>
  );

  return (
    <AdminCatalogLayout
      title="Speaking role-play cards"
      description="Author the two-card OET Speaking scenarios: candidate card plus hidden interlocutor script. Publish only after both sides are written."
      breadcrumbs={BREADCRUMBS}
      eyebrow="CMS"
      hideViewModeToggle
      filters={filtersNode}
      actions={
        <Button onClick={() => router.push('/admin/content/speaking/role-play-cards/new')}>
          <Plus className="mr-1 h-4 w-4" /> New role-play card
        </Button>
      }
    >
      <p className="col-span-full text-xs text-admin-fg-muted">
        {rows.length} card(s){filterCount > 0 ? ` matching ${filterCount} filter(s)` : ''}.
      </p>

      {loading ? (
        <div className="col-span-full space-y-3">
          {[1, 2, 3].map((i) => (
            <Skeleton key={i} className="h-24" />
          ))}
        </div>
      ) : rows.length === 0 ? (
        <div className="col-span-full">
          <EmptyState
            illustration={<Sparkles />}
            title="No role-play cards yet"
            description="Click 'New role-play card' to author the first one."
          />
        </div>
      ) : (
        <div className="col-span-full space-y-3">
          {rows.map((row) => (
            <Card key={row.cardId}>
              <CardContent className="p-4">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div className="min-w-0 flex-1">
                    <div className="mb-1 flex flex-wrap items-center gap-2">
                      <h3 className="truncate font-semibold text-admin-fg-strong">{row.title}</h3>
                      <Badge variant={statusVariant(row.status)} intensity="tinted">
                        {row.status}
                      </Badge>
                      <Badge variant="default" intensity="tinted">{row.professionId}</Badge>
                      <Badge variant="default" intensity="tinted">{row.difficulty}</Badge>
                      {row.hasInterlocutorScript ? (
                        <Badge variant="success" intensity="tinted">interlocutor ready</Badge>
                      ) : (
                        <Badge variant="warning" intensity="tinted">no interlocutor</Badge>
                      )}
                      {row.isLiveTutorEligible ? (
                        <Badge variant="info" intensity="tinted">live-tutor</Badge>
                      ) : null}
                    </div>
                    <p className="mt-1 text-xs text-admin-fg-muted">
                      {row.setting} - {row.clinicalTopic}
                      {' - '}
                      Published:{' '}
                      {row.publishedAt ? new Date(row.publishedAt).toLocaleDateString() : '—'}
                    </p>
                  </div>
                  <div className="flex flex-wrap items-center gap-2">
                    <Link
                      href={`/admin/content/speaking/role-play-cards/${encodeURIComponent(row.cardId)}`}
                      className="text-sm font-semibold text-admin-primary hover:underline"
                    >
                      Edit
                    </Link>
                    <Link
                      href={`/admin/content/speaking/role-play-cards/${encodeURIComponent(row.cardId)}/interlocutor`}
                      className="text-sm font-semibold text-admin-primary hover:underline"
                    >
                      Interlocutor
                    </Link>
                    <Link
                      href={`/admin/content/speaking/role-play-cards/${encodeURIComponent(row.cardId)}/preview`}
                      className="text-sm font-semibold text-admin-primary hover:underline"
                    >
                      Preview
                    </Link>
                    {row.status !== 'Published' && row.status !== 'Archived' ? (
                      <Button
                        size="sm"
                        onClick={() => void handlePublish(row)}
                        disabled={busyId === row.cardId || !row.hasInterlocutorScript}
                        title={
                          row.hasInterlocutorScript
                            ? 'Publish card'
                            : 'Add an interlocutor script before publishing'
                        }
                      >
                        Publish
                      </Button>
                    ) : null}
                    {row.status === 'Published' ? (
                      <Button
                        size="sm"
                        variant="outline"
                        onClick={() => void handleArchive(row)}
                        disabled={busyId === row.cardId}
                      >
                        Archive
                      </Button>
                    ) : null}
                    <Button
                      size="sm"
                      variant="ghost"
                      onClick={() => void handleDuplicate(row)}
                      disabled={busyId === row.cardId}
                      title="Duplicate as draft"
                    >
                      <Copy className="h-4 w-4" />
                    </Button>
                  </div>
                </div>
              </CardContent>
            </Card>
          ))}
        </div>
      )}

      {toast ? (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      ) : null}
    </AdminCatalogLayout>
  );
}

function statusVariant(status: string): 'success' | 'default' | 'warning' {
  switch ((status ?? '').toLowerCase()) {
    case 'published':
      return 'success';
    case 'draft':
      return 'default';
    case 'archived':
      return 'warning';
    default:
      return 'default';
  }
}
