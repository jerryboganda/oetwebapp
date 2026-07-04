'use client';

/**
 * Video Library — Bunny collection console. Two-pane: the left lists every
 * collection in the live Bunny Stream library; selecting one shows its videos
 * on the right, each annotated "In catalog" / "Not imported". From here an admin
 * can create/rename/delete collections, move videos between them, import a
 * Bunny-native video into the learner catalog (then jump into the publish
 * wizard), or permanently delete an un-imported video from Bunny.
 *
 * Bunny is the source of truth for membership — this reads it live. Playback is
 * never touched here (no playback URL is minted or shown). A dormant Bunny
 * answers 503 `bunny_not_configured`, which renders a setup EmptyState.
 */

import { type FormEvent, useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import {
  ArrowRightLeft,
  ExternalLink,
  Layers,
  Loader2,
  Pencil,
  Plus,
  Settings,
  Trash2,
  Upload,
} from 'lucide-react';
import { AdminOperationsLayout } from '@/components/admin/layout/admin-operations-layout';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Button, buttonVariants } from '@/components/admin/ui/button';
import { Badge, type BadgeTone } from '@/components/admin/ui/badge';
import { Input } from '@/components/admin/ui/input';
import { NativeSelect } from '@/components/admin/ui/native-select';
import { EmptyState } from '@/components/admin/ui/empty-state';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/admin/ui/alert-dialog';
import { Toast } from '@/components/ui/alert';
import { Modal } from '@/components/ui/modal';
import { ApiError } from '@/lib/api';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import {
  adminBunnyDeleteCollectionVideo,
  adminCreateCollection,
  adminDeleteCollection,
  adminImportCollectionVideo,
  adminListCollections,
  adminListCollectionVideos,
  adminMoveCollectionVideo,
  adminRenameCollection,
  type AdminCollection,
  type AdminCollectionVideo,
  type VideoEncodeStatus,
} from '@/lib/api/video-library';

const ENCODE_BADGE: Record<VideoEncodeStatus, { label: string; tone: BadgeTone }> = {
  not_uploaded: { label: 'Not uploaded', tone: 'muted' },
  uploading: { label: 'Uploading', tone: 'warning' },
  queued: { label: 'Queued', tone: 'warning' },
  processing: { label: 'Processing', tone: 'info' },
  encoding: { label: 'Encoding', tone: 'info' },
  ready: { label: 'Ready', tone: 'success' },
  failed: { label: 'Failed', tone: 'danger' },
};

const SETTINGS_HREF = '/admin/settings';

function isNotConfigured(err: unknown): boolean {
  return err instanceof ApiError && (err.code === 'bunny_not_configured' || err.status === 503);
}

function errorMessage(err: unknown, fallback: string): string {
  return err instanceof Error ? err.message : fallback;
}

function formatDuration(totalSeconds: number): string {
  if (!totalSeconds || totalSeconds <= 0) return '—';
  const m = Math.floor(totalSeconds / 60);
  const s = Math.floor(totalSeconds % 60);
  return `${m}:${s.toString().padStart(2, '0')}`;
}

function formatBytes(bytes: number): string {
  if (!bytes || bytes <= 0) return '—';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let value = bytes;
  let i = 0;
  while (value >= 1024 && i < units.length - 1) {
    value /= 1024;
    i += 1;
  }
  return `${value.toFixed(i === 0 || value >= 10 ? 0 : 1)} ${units[i]}`;
}

export default function AdminVideoCollectionsPage() {
  const router = useRouter();
  const { user } = useCurrentUser();
  const canWrite = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);
  const canSystemAdmin = hasPermission(user?.adminPermissions, AdminPermission.SystemAdmin);

  const [notConfigured, setNotConfigured] = useState(false);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const [collections, setCollections] = useState<AdminCollection[]>([]);
  const [loadingCollections, setLoadingCollections] = useState(true);
  const [selected, setSelected] = useState<AdminCollection | null>(null);

  const [videos, setVideos] = useState<AdminCollectionVideo[]>([]);
  const [loadingVideos, setLoadingVideos] = useState(false);

  const [createOpen, setCreateOpen] = useState(false);
  const [newName, setNewName] = useState('');
  const [creating, setCreating] = useState(false);

  const [renaming, setRenaming] = useState<AdminCollection | null>(null);
  const [renameName, setRenameName] = useState('');
  const [savingRename, setSavingRename] = useState(false);

  const [deletingCollection, setDeletingCollection] = useState<AdminCollection | null>(null);
  const [deleteBusy, setDeleteBusy] = useState(false);

  const [moving, setMoving] = useState<AdminCollectionVideo | null>(null);
  const [moveTarget, setMoveTarget] = useState('');
  const [moveBusy, setMoveBusy] = useState(false);

  const [bunnyDeleting, setBunnyDeleting] = useState<AdminCollectionVideo | null>(null);
  const [bunnyDeleteBusy, setBunnyDeleteBusy] = useState(false);

  const [importingId, setImportingId] = useState<string | null>(null);

  const loadCollections = useCallback(async () => {
    setLoadingCollections(true);
    try {
      const list = await adminListCollections({ itemsPerPage: 100 });
      setCollections(list.items);
      setNotConfigured(false);
    } catch (err) {
      if (isNotConfigured(err)) {
        setNotConfigured(true);
      } else {
        setToast({ variant: 'error', message: errorMessage(err, 'Failed to load collections.') });
      }
    } finally {
      setLoadingCollections(false);
    }
  }, []);

  useEffect(() => {
    void loadCollections();
  }, [loadCollections]);

  const loadVideos = useCallback(async (collectionId: string) => {
    setLoadingVideos(true);
    try {
      const page = await adminListCollectionVideos(collectionId, { itemsPerPage: 100 });
      setVideos(page.items);
    } catch (err) {
      if (isNotConfigured(err)) {
        setNotConfigured(true);
      } else {
        setToast({ variant: 'error', message: errorMessage(err, 'Failed to load videos.') });
      }
    } finally {
      setLoadingVideos(false);
    }
  }, []);

  function selectCollection(collection: AdminCollection) {
    setSelected(collection);
    setVideos([]);
    void loadVideos(collection.collectionId);
  }

  async function refreshSelected() {
    await loadCollections();
    if (selected) await loadVideos(selected.collectionId);
  }

  async function handleCreate(event: FormEvent) {
    event.preventDefault();
    if (!newName.trim() || creating) return;
    setCreating(true);
    try {
      await adminCreateCollection(newName.trim());
      setToast({ variant: 'success', message: 'Collection created.' });
      setNewName('');
      setCreateOpen(false);
      await loadCollections();
    } catch (err) {
      setToast({ variant: 'error', message: errorMessage(err, 'Create failed.') });
    } finally {
      setCreating(false);
    }
  }

  function openRename(collection: AdminCollection) {
    setRenaming(collection);
    setRenameName(collection.name);
  }

  async function handleRename(event: FormEvent) {
    event.preventDefault();
    if (!renaming || !renameName.trim() || savingRename) return;
    setSavingRename(true);
    try {
      await adminRenameCollection(renaming.collectionId, renameName.trim());
      setToast({ variant: 'success', message: 'Collection renamed.' });
      setRenaming(null);
      await loadCollections();
      if (selected?.collectionId === renaming.collectionId) {
        setSelected({ ...selected, name: renameName.trim() });
      }
    } catch (err) {
      setToast({ variant: 'error', message: errorMessage(err, 'Rename failed.') });
    } finally {
      setSavingRename(false);
    }
  }

  async function handleDeleteCollection() {
    if (!deletingCollection) return;
    setDeleteBusy(true);
    try {
      await adminDeleteCollection(deletingCollection.collectionId);
      setToast({ variant: 'success', message: `"${deletingCollection.name}" deleted.` });
      if (selected?.collectionId === deletingCollection.collectionId) {
        setSelected(null);
        setVideos([]);
      }
      await loadCollections();
    } catch (err) {
      setToast({ variant: 'error', message: errorMessage(err, 'Delete failed.') });
    } finally {
      setDeleteBusy(false);
      setDeletingCollection(null);
    }
  }

  async function handleImport(video: AdminCollectionVideo) {
    if (importingId) return;
    setImportingId(video.bunnyVideoId);
    try {
      const detail = await adminImportCollectionVideo(video.bunnyVideoId, {
        title: video.title || undefined,
        collectionId: selected?.collectionId ?? null,
      });
      setToast({ variant: 'success', message: 'Imported — opening the wizard…' });
      router.push(`/admin/content/videos/${detail.videoId}/details`);
    } catch (err) {
      setToast({ variant: 'error', message: errorMessage(err, 'Import failed.') });
      setImportingId(null);
    }
  }

  function openMove(video: AdminCollectionVideo) {
    setMoving(video);
    setMoveTarget('');
  }

  async function handleMove(event: FormEvent) {
    event.preventDefault();
    if (!moving || moveBusy) return;
    setMoveBusy(true);
    try {
      await adminMoveCollectionVideo(moving.bunnyVideoId, moveTarget || null);
      setToast({ variant: 'success', message: 'Video moved.' });
      setMoving(null);
      await refreshSelected();
    } catch (err) {
      setToast({ variant: 'error', message: errorMessage(err, 'Move failed.') });
    } finally {
      setMoveBusy(false);
    }
  }

  async function handleBunnyDelete() {
    if (!bunnyDeleting) return;
    setBunnyDeleteBusy(true);
    try {
      await adminBunnyDeleteCollectionVideo(bunnyDeleting.bunnyVideoId);
      setToast({ variant: 'success', message: 'Video permanently deleted from Bunny.' });
      await refreshSelected();
    } catch (err) {
      setToast({ variant: 'error', message: errorMessage(err, 'Delete failed.') });
    } finally {
      setBunnyDeleteBusy(false);
      setBunnyDeleting(null);
    }
  }

  const moveOptions = collections
    .filter((c) => c.collectionId !== selected?.collectionId)
    .map((c) => ({ value: c.collectionId, label: c.name }));

  return (
    <>
      <AdminOperationsLayout
        eyebrow="Content"
        title="Collections"
        description="Browse and manage the live Bunny Stream library: collections, the videos inside them, and import into the learner catalog — without leaving the app."
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Content', href: '/admin/content' },
          { label: 'Video Library', href: '/admin/content/videos' },
          { label: 'Collections' },
        ]}
      >
        {notConfigured ? (
          <Card>
            <CardContent className="p-5">
              <EmptyState
                icon={<Settings className="h-6 w-6" />}
                title="Bunny Stream isn't configured"
                description="Add the Bunny Stream library credentials in Admin → Settings to browse and manage collections."
                primaryAction={{ label: 'Open Settings', href: SETTINGS_HREF }}
              />
            </CardContent>
          </Card>
        ) : (
          <div className="grid gap-4 lg:grid-cols-[340px_1fr]">
            {/* ── Left: collections ── */}
            <Card>
              <CardContent className="p-4 space-y-3">
                <div className="flex items-center justify-between gap-2">
                  <h2 className="text-sm font-bold text-admin-fg-strong">Collections</h2>
                  {canWrite ? (
                    <Button size="sm" variant="primary" onClick={() => setCreateOpen(true)}>
                      <Plus className="mr-1 h-4 w-4" /> New
                    </Button>
                  ) : null}
                </div>

                {loadingCollections ? (
                  <div className="flex items-center gap-2 py-8 text-sm text-admin-fg-muted">
                    <Loader2 className="h-4 w-4 animate-spin" /> Loading collections…
                  </div>
                ) : collections.length === 0 ? (
                  <EmptyState
                    icon={<Layers className="h-6 w-6" />}
                    title="No collections"
                    description="Create a collection to start organising your Bunny videos."
                  />
                ) : (
                  <ul className="space-y-1">
                    {collections.map((collection) => {
                      const active = selected?.collectionId === collection.collectionId;
                      return (
                        <li key={collection.collectionId}>
                          <div
                            className={`group flex items-center gap-2 rounded-admin border px-3 py-2 ${
                              active
                                ? 'border-admin-primary bg-admin-primary-tint'
                                : 'border-admin-border bg-admin-bg-surface hover:bg-admin-bg-subtle'
                            }`}
                          >
                            <button
                              type="button"
                              onClick={() => selectCollection(collection)}
                              className="min-w-0 flex-1 text-left"
                            >
                              <p className="truncate text-sm font-semibold text-admin-fg-strong">{collection.name}</p>
                              <p className="mt-0.5 text-xs text-admin-fg-muted">
                                {collection.videoCount} video{collection.videoCount === 1 ? '' : 's'} · {formatBytes(collection.totalSizeBytes)}
                              </p>
                            </button>
                            {canWrite ? (
                              <button
                                type="button"
                                onClick={() => openRename(collection)}
                                className="rounded p-1 text-admin-fg-muted hover:text-admin-fg-strong"
                                aria-label={`Rename ${collection.name}`}
                              >
                                <Pencil className="h-3.5 w-3.5" />
                              </button>
                            ) : null}
                            {canSystemAdmin ? (
                              <button
                                type="button"
                                onClick={() => setDeletingCollection(collection)}
                                className="rounded p-1 text-admin-fg-muted hover:text-[var(--admin-danger)]"
                                aria-label={`Delete ${collection.name}`}
                              >
                                <Trash2 className="h-3.5 w-3.5" />
                              </button>
                            ) : null}
                          </div>
                        </li>
                      );
                    })}
                  </ul>
                )}
              </CardContent>
            </Card>

            {/* ── Right: videos in the selected collection ── */}
            <Card>
              <CardContent className="p-4">
                {!selected ? (
                  <EmptyState
                    icon={<Layers className="h-6 w-6" />}
                    title="Select a collection"
                    description="Pick a collection on the left to see the videos inside it."
                  />
                ) : loadingVideos ? (
                  <div className="flex items-center gap-2 py-10 text-sm text-admin-fg-muted">
                    <Loader2 className="h-4 w-4 animate-spin" /> Loading videos…
                  </div>
                ) : videos.length === 0 ? (
                  <EmptyState
                    icon={<Layers className="h-6 w-6" />}
                    title="No videos in this collection"
                    description="Upload to this collection from Bunny, or move existing videos here."
                  />
                ) : (
                  <>
                    <div className="mb-3 flex items-center justify-between gap-2">
                      <h2 className="truncate text-sm font-bold text-admin-fg-strong">{selected.name}</h2>
                      <span className="shrink-0 text-xs text-admin-fg-muted">{videos.length} shown</span>
                    </div>
                    <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
                      {videos.map((video) => {
                        const encode = ENCODE_BADGE[video.encodeStatus];
                        const importing = importingId === video.bunnyVideoId;
                        return (
                          <div
                            key={video.bunnyVideoId}
                            className="flex flex-col overflow-hidden rounded-admin border border-admin-border bg-admin-bg-surface"
                          >
                            <div className="relative aspect-video bg-admin-bg-subtle">
                              {video.thumbnailUrl ? (
                                // eslint-disable-next-line @next/next/no-img-element
                                <img
                                  src={video.thumbnailUrl}
                                  alt=""
                                  className="h-full w-full object-cover"
                                  loading="lazy"
                                />
                              ) : (
                                <div className="flex h-full items-center justify-center text-admin-fg-muted">
                                  <Layers className="h-6 w-6" />
                                </div>
                              )}
                              <span className="absolute bottom-1 right-1 rounded bg-black/70 px-1.5 py-0.5 text-xs font-medium text-white">
                                {formatDuration(video.durationSeconds)}
                              </span>
                            </div>

                            <div className="flex flex-1 flex-col gap-2 p-3">
                              <p className="line-clamp-2 text-sm font-semibold text-admin-fg-strong">{video.title || 'Untitled'}</p>
                              <div className="flex flex-wrap items-center gap-1.5">
                                <Badge variant={encode.tone} size="sm">{encode.label}</Badge>
                                {video.isImported ? (
                                  <Badge variant="success" size="sm">In catalog</Badge>
                                ) : (
                                  <Badge variant="muted" size="sm">Not imported</Badge>
                                )}
                                <span className="text-xs text-admin-fg-muted">{formatBytes(video.storageSizeBytes)}</span>
                              </div>

                              <div className="mt-auto flex flex-wrap items-center gap-1.5 pt-1">
                                {video.isImported && video.localVideoId ? (
                                  <Link
                                    href={`/admin/content/videos/${video.localVideoId}/details`}
                                    className={buttonVariants({ variant: 'outline', size: 'sm' })}
                                  >
                                    <ExternalLink className="mr-1 h-3.5 w-3.5" /> Open in catalog
                                  </Link>
                                ) : canWrite ? (
                                  <Button
                                    size="sm"
                                    variant="primary"
                                    disabled={importing}
                                    onClick={() => void handleImport(video)}
                                  >
                                    {importing ? (
                                      <Loader2 className="mr-1 h-3.5 w-3.5 animate-spin" />
                                    ) : (
                                      <Upload className="mr-1 h-3.5 w-3.5" />
                                    )}
                                    Import
                                  </Button>
                                ) : null}

                                {canWrite ? (
                                  <Button size="sm" variant="ghost" onClick={() => openMove(video)}>
                                    <ArrowRightLeft className="mr-1 h-3.5 w-3.5" /> Move
                                  </Button>
                                ) : null}

                                {!video.isImported && canSystemAdmin ? (
                                  <Button
                                    size="sm"
                                    variant="ghost"
                                    onClick={() => setBunnyDeleting(video)}
                                    aria-label={`Delete ${video.title} from Bunny`}
                                  >
                                    <Trash2 className="h-3.5 w-3.5 text-[var(--admin-danger)]" />
                                  </Button>
                                ) : null}
                              </div>
                            </div>
                          </div>
                        );
                      })}
                    </div>
                  </>
                )}
              </CardContent>
            </Card>
          </div>
        )}
      </AdminOperationsLayout>

      {/* ── New collection ── */}
      <Modal open={createOpen} onClose={() => setCreateOpen(false)} title="New collection" size="sm">
        <form className="space-y-4" onSubmit={handleCreate}>
          <Input
            label="Collection name"
            value={newName}
            onChange={(e) => setNewName(e.target.value)}
            placeholder="e.g. Speaking role-plays"
            autoFocus
          />
          <div className="flex flex-wrap justify-end gap-2 border-t border-border pt-4">
            <Button type="button" variant="ghost" onClick={() => setCreateOpen(false)}>
              Cancel
            </Button>
            <Button type="submit" variant="primary" disabled={!newName.trim() || creating}>
              Create collection
            </Button>
          </div>
        </form>
      </Modal>

      {/* ── Rename collection ── */}
      <Modal open={Boolean(renaming)} onClose={() => setRenaming(null)} title="Rename collection" size="sm">
        {renaming ? (
          <form className="space-y-4" onSubmit={handleRename}>
            <Input label="Name" value={renameName} onChange={(e) => setRenameName(e.target.value)} autoFocus />
            <div className="flex flex-wrap justify-end gap-2 border-t border-border pt-4">
              <Button type="button" variant="ghost" onClick={() => setRenaming(null)}>
                Cancel
              </Button>
              <Button type="submit" variant="primary" disabled={!renameName.trim() || savingRename}>
                Save
              </Button>
            </div>
          </form>
        ) : null}
      </Modal>

      {/* ── Move video ── */}
      <Modal open={Boolean(moving)} onClose={() => setMoving(null)} title="Move video" size="sm">
        {moving ? (
          <form className="space-y-4" onSubmit={handleMove}>
            <p className="text-sm text-admin-fg-muted line-clamp-2">{moving.title || 'Untitled'}</p>
            <NativeSelect
              label="Destination collection"
              value={moveTarget}
              onChange={(e) => setMoveTarget(e.target.value)}
              placeholder="No collection (ungrouped)"
              options={moveOptions}
            />
            <div className="flex flex-wrap justify-end gap-2 border-t border-border pt-4">
              <Button type="button" variant="ghost" onClick={() => setMoving(null)}>
                Cancel
              </Button>
              <Button type="submit" variant="primary" disabled={moveBusy}>
                Move video
              </Button>
            </div>
          </form>
        ) : null}
      </Modal>

      {/* ── Delete collection ── */}
      <AlertDialog
        open={Boolean(deletingCollection)}
        onOpenChange={(open) => !open && setDeletingCollection(null)}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Delete this collection?</AlertDialogTitle>
            <AlertDialogDescription>
              {deletingCollection && deletingCollection.videoCount > 0
                ? `"${deletingCollection.name}" holds ${deletingCollection.videoCount} video${deletingCollection.videoCount === 1 ? '' : 's'}. Deleting the collection removes the folder on Bunny; the videos themselves are not deleted. This cannot be undone.`
                : `"${deletingCollection?.name}" will be removed from Bunny. This cannot be undone.`}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={deleteBusy}>Cancel</AlertDialogCancel>
            <AlertDialogAction
              className={buttonVariants({ variant: 'destructive' })}
              disabled={deleteBusy}
              onClick={(e) => {
                e.preventDefault();
                void handleDeleteCollection();
              }}
            >
              Delete collection
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {/* ── Permanently delete a Bunny video ── */}
      <AlertDialog open={Boolean(bunnyDeleting)} onOpenChange={(open) => !open && setBunnyDeleting(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Delete this video from Bunny?</AlertDialogTitle>
            <AlertDialogDescription>
              {`"${bunnyDeleting?.title || 'Untitled'}" will be permanently deleted from Bunny Stream. This cannot be undone. (Videos already imported into the catalog must be removed from the Video Library instead.)`}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={bunnyDeleteBusy}>Cancel</AlertDialogCancel>
            <AlertDialogAction
              className={buttonVariants({ variant: 'destructive' })}
              disabled={bunnyDeleteBusy}
              onClick={(e) => {
                e.preventDefault();
                void handleBunnyDelete();
              }}
            >
              Delete permanently
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {toast ? (
        <Toast
          variant={toast.variant}
          message={toast.message}
          duration={toast.variant === 'error' ? 12000 : 5000}
          onClose={() => setToast(null)}
        />
      ) : null}
    </>
  );
}
