'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, BookOpen, Headphones, Megaphone, Plus, Trash2 } from 'lucide-react';
import { AdminRouteSectionHeader, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { NoBillingPermission } from '@/components/admin/billing/no-billing-permission';
import {
  adminListTutorBookUpdates,
  adminUpsertTutorBookUpdate,
  adminDeleteTutorBookUpdate,
  adminListTutorBookAudioScripts,
  adminUpsertTutorBookAudioScript,
  adminDeleteTutorBookAudioScript,
  type AdminTutorBookUpdate,
  type AdminTutorBookAudioScript,
} from '@/lib/api';
import { useAuth } from '@/contexts/auth-context';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';

type LoadState = 'loading' | 'success' | 'error';

export default function AdminTutorBookPage() {
  const { user } = useAuth();
  const canWriteCatalog = hasPermission(
    user?.adminPermissions,
    AdminPermission.BillingWrite,
    AdminPermission.BillingCatalogWrite,
  );

  const [updates, setUpdates] = useState<AdminTutorBookUpdate[]>([]);
  const [scripts, setScripts] = useState<AdminTutorBookAudioScript[]>([]);
  const [status, setStatus] = useState<LoadState>('loading');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [saveMessage, setSaveMessage] = useState<string | null>(null);

  // Update editor state
  const [updateDraft, setUpdateDraft] = useState({
    id: '' as string,
    title: '',
    bodyMarkdown: '',
    audience: 'all',
    isPublished: true,
  });

  // Audio script editor state
  const [scriptDraft, setScriptDraft] = useState({
    id: '' as string,
    chapter: '',
    title: '',
    audioUrl: '',
    transcriptUrl: '',
    displayOrder: 0,
    isPublished: true,
  });

  const reload = useCallback(async () => {
    if (!canWriteCatalog) return;
    setStatus('loading');
    try {
      const [u, s] = await Promise.all([adminListTutorBookUpdates(), adminListTutorBookAudioScripts()]);
      setUpdates(u);
      setScripts(s);
      setStatus('success');
      setErrorMessage(null);
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : 'Failed to load.');
      setStatus('error');
    }
  }, [canWriteCatalog]);

  useEffect(() => {
    void reload();
  }, [reload]);

  if (!user) return null;
  if (!canWriteCatalog) return <NoBillingPermission />;

  const handleSaveUpdate = async () => {
    setSaveMessage(null);
    try {
      await adminUpsertTutorBookUpdate({
        id: updateDraft.id || undefined,
        title: updateDraft.title,
        bodyMarkdown: updateDraft.bodyMarkdown,
        audience: updateDraft.audience,
        isPublished: updateDraft.isPublished,
      });
      setSaveMessage('Update saved.');
      setUpdateDraft({ id: '', title: '', bodyMarkdown: '', audience: 'all', isPublished: true });
      await reload();
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : 'Save failed.');
    }
  };

  const handleSaveScript = async () => {
    setSaveMessage(null);
    try {
      await adminUpsertTutorBookAudioScript({
        id: scriptDraft.id || undefined,
        chapter: scriptDraft.chapter,
        title: scriptDraft.title,
        audioUrl: scriptDraft.audioUrl,
        transcriptUrl: scriptDraft.transcriptUrl || null,
        displayOrder: scriptDraft.displayOrder,
        isPublished: scriptDraft.isPublished,
      });
      setSaveMessage('Audio script saved.');
      setScriptDraft({ id: '', chapter: '', title: '', audioUrl: '', transcriptUrl: '', displayOrder: 0, isPublished: true });
      await reload();
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : 'Save failed.');
    }
  };

  const handleEditUpdate = (row: AdminTutorBookUpdate) => {
    setUpdateDraft({ id: row.id, title: row.title, bodyMarkdown: row.bodyMarkdown, audience: row.audience, isPublished: row.isPublished });
  };

  const handleEditScript = (row: AdminTutorBookAudioScript) => {
    setScriptDraft({
      id: row.id,
      chapter: row.chapter,
      title: row.title,
      audioUrl: row.audioUrl,
      transcriptUrl: row.transcriptUrl ?? '',
      displayOrder: row.displayOrder,
      isPublished: row.isPublished,
    });
  };

  const handleDeleteUpdate = async (id: string) => {
    if (!confirm('Delete this Tutor Book update? This cannot be undone.')) return;
    try {
      await adminDeleteTutorBookUpdate(id);
      await reload();
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : 'Delete failed.');
    }
  };

  const handleDeleteScript = async (id: string) => {
    if (!confirm('Delete this audio script? This cannot be undone.')) return;
    try {
      await adminDeleteTutorBookAudioScript(id);
      await reload();
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : 'Delete failed.');
    }
  };

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        eyebrow="Content"
        title="The Tutor Book — Admin"
        description="Manage recall updates + audio scripts surfaced inside the /learner/tutor-book reader. Telegram invite URL is configured via TutorBook:TelegramInviteUrl in appsettings."
        icon={<BookOpen aria-hidden className="h-5 w-5" />}
        actions={
          <Button asChild variant="ghost" size="sm">
            <Link href="/admin">
              <ArrowLeft className="mr-1.5 h-4 w-4" /> Back to Admin
            </Link>
          </Button>
        }
      />

      {errorMessage && <InlineAlert variant="error">{errorMessage}</InlineAlert>}
      {saveMessage && <InlineAlert variant="success">{saveMessage}</InlineAlert>}

      <section className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm dark:border-slate-800 dark:bg-slate-950">
        <header className="flex items-center gap-2">
          <Megaphone className="h-4 w-4 text-[#996F1F]" />
          <h3 className="text-base font-bold">Updates feed</h3>
        </header>
        <p className="mt-1 text-sm text-slate-600 dark:text-slate-300">
          Recall amendments, errata, new card additions. Audience = <code>all</code>, <code>medicine</code>, <code>nursing</code>, <code>pharmacy</code>.
        </p>

        <div className="mt-4 grid gap-4 md:grid-cols-2">
          <input className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm" placeholder="Title" value={updateDraft.title} onChange={(e) => setUpdateDraft({ ...updateDraft, title: e.target.value })} />
          <select className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm" value={updateDraft.audience} onChange={(e) => setUpdateDraft({ ...updateDraft, audience: e.target.value })}>
            <option value="all">All disciplines</option>
            <option value="medicine">Medicine</option>
            <option value="nursing">Nursing</option>
            <option value="pharmacy">Pharmacy</option>
          </select>
        </div>
        <textarea className="mt-3 w-full rounded-md border border-slate-300 px-3 py-2 text-sm font-mono" rows={5} placeholder="Body (Markdown)" value={updateDraft.bodyMarkdown} onChange={(e) => setUpdateDraft({ ...updateDraft, bodyMarkdown: e.target.value })} />
        <label className="mt-3 flex items-center gap-2 text-sm">
          <input type="checkbox" checked={updateDraft.isPublished} onChange={(e) => setUpdateDraft({ ...updateDraft, isPublished: e.target.checked })} /> Published
        </label>
        <div className="mt-4 flex gap-2">
          <Button onClick={handleSaveUpdate}>
            <Plus className="mr-1 h-4 w-4" /> {updateDraft.id ? 'Update' : 'Create'}
          </Button>
          {updateDraft.id && (
            <Button variant="outline" onClick={() => setUpdateDraft({ id: '', title: '', bodyMarkdown: '', audience: 'all', isPublished: true })}>
              Cancel edit
            </Button>
          )}
        </div>

        {status === 'loading' ? (
          <Skeleton className="mt-6 h-32 w-full" />
        ) : (
          <ul className="mt-6 divide-y divide-slate-200 dark:divide-slate-800">
            {updates.length === 0 ? (
              <li className="py-6 text-center text-sm text-slate-500">No updates yet.</li>
            ) : (
              updates.map((row) => (
                <li key={row.id} className="flex items-start justify-between gap-3 py-3">
                  <div>
                    <div className="font-semibold">{row.title}</div>
                    <div className="text-xs text-slate-500">{new Date(row.publishedAt).toLocaleString()} · {row.audience} · {row.isPublished ? 'published' : 'draft'}</div>
                    <p className="mt-1 text-sm text-slate-700 line-clamp-2 dark:text-slate-300">{row.bodyMarkdown}</p>
                  </div>
                  <div className="flex flex-none gap-2">
                    <Button size="sm" variant="outline" onClick={() => handleEditUpdate(row)}>Edit</Button>
                    <Button size="sm" variant="outline" onClick={() => handleDeleteUpdate(row.id)}>
                      <Trash2 className="h-3 w-3" />
                    </Button>
                  </div>
                </li>
              ))
            )}
          </ul>
        )}
      </section>

      <section className="mt-8 rounded-2xl border border-slate-200 bg-white p-6 shadow-sm dark:border-slate-800 dark:bg-slate-950">
        <header className="flex items-center gap-2">
          <Headphones className="h-4 w-4 text-[#996F1F]" />
          <h3 className="text-base font-bold">Audio scripts</h3>
        </header>
        <p className="mt-1 text-sm text-slate-600 dark:text-slate-300">
          Per-chapter MP3 + optional transcript URLs. Surfaced inside the reader's Audio Scripts tab.
        </p>

        <div className="mt-4 grid gap-3 md:grid-cols-2">
          <input className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm" placeholder="Chapter (e.g. Listening 1)" value={scriptDraft.chapter} onChange={(e) => setScriptDraft({ ...scriptDraft, chapter: e.target.value })} />
          <input className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm" placeholder="Title" value={scriptDraft.title} onChange={(e) => setScriptDraft({ ...scriptDraft, title: e.target.value })} />
          <input className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm" placeholder="Audio URL" value={scriptDraft.audioUrl} onChange={(e) => setScriptDraft({ ...scriptDraft, audioUrl: e.target.value })} />
          <input className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm" placeholder="Transcript URL (optional)" value={scriptDraft.transcriptUrl} onChange={(e) => setScriptDraft({ ...scriptDraft, transcriptUrl: e.target.value })} />
          <input type="number" min={0} className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm" placeholder="Display order" value={scriptDraft.displayOrder} onChange={(e) => setScriptDraft({ ...scriptDraft, displayOrder: Number(e.target.value) || 0 })} />
        </div>
        <label className="mt-3 flex items-center gap-2 text-sm">
          <input type="checkbox" checked={scriptDraft.isPublished} onChange={(e) => setScriptDraft({ ...scriptDraft, isPublished: e.target.checked })} /> Published
        </label>
        <div className="mt-4 flex gap-2">
          <Button onClick={handleSaveScript}>
            <Plus className="mr-1 h-4 w-4" /> {scriptDraft.id ? 'Update' : 'Create'}
          </Button>
          {scriptDraft.id && (
            <Button variant="outline" onClick={() => setScriptDraft({ id: '', chapter: '', title: '', audioUrl: '', transcriptUrl: '', displayOrder: 0, isPublished: true })}>
              Cancel edit
            </Button>
          )}
        </div>

        {status === 'loading' ? (
          <Skeleton className="mt-6 h-32 w-full" />
        ) : (
          <ul className="mt-6 divide-y divide-slate-200 dark:divide-slate-800">
            {scripts.length === 0 ? (
              <li className="py-6 text-center text-sm text-slate-500">No audio scripts yet.</li>
            ) : (
              scripts.map((row) => (
                <li key={row.id} className="flex items-start justify-between gap-3 py-3">
                  <div className="min-w-0 flex-1">
                    <div className="font-semibold">{row.chapter} — {row.title}</div>
                    <div className="truncate text-xs text-slate-500">{row.audioUrl}</div>
                    <div className="text-xs text-slate-500">order #{row.displayOrder} · {row.isPublished ? 'published' : 'draft'}</div>
                  </div>
                  <div className="flex flex-none gap-2">
                    <Button size="sm" variant="outline" onClick={() => handleEditScript(row)}>Edit</Button>
                    <Button size="sm" variant="outline" onClick={() => handleDeleteScript(row.id)}>
                      <Trash2 className="h-3 w-3" />
                    </Button>
                  </div>
                </li>
              ))
            )}
          </ul>
        )}
      </section>
    </AdminRouteWorkspace>
  );
}
