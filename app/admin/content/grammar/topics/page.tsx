'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { Plus, Trash2 } from 'lucide-react';
import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import {
  adminListGrammarTopics,
  adminCreateGrammarTopic,
  adminUpdateGrammarTopic,
  adminArchiveGrammarTopic,
} from '@/lib/api';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { Badge, statusToTone } from '@/components/admin/ui/badge';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import type { AdminGrammarTopic } from '@/lib/grammar/types';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

const EXAMS = [
  { value: 'oet', label: 'OET' },
  { value: 'ielts', label: 'IELTS' },
  { value: 'pte', label: 'PTE' },
];
const LEVELS = [
  { value: 'all', label: 'All levels' },
  { value: 'beginner', label: 'Beginner' },
  { value: 'intermediate', label: 'Intermediate' },
  { value: 'advanced', label: 'Advanced' },
];

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Content', href: '/admin/content' },
  { label: 'Grammar', href: '/admin/content/grammar' },
  { label: 'Topics' },
];

export default function AdminGrammarTopicsPage() {
  const { isAuthenticated, isLoading, role } = useAdminAuth();
  const { user } = useCurrentUser();
  const canWriteContent = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);
  const [topics, setTopics] = useState<AdminGrammarTopic[]>([]);
  const [loading, setLoading] = useState(true);
  const [examFilter, setExamFilter] = useState('oet');
  const [toast, setToast] = useState<ToastState>(null);

  const [newExam, setNewExam] = useState('oet');
  const [newSlug, setNewSlug] = useState('');
  const [newName, setNewName] = useState('');
  const [newDesc, setNewDesc] = useState('');
  const [newIcon, setNewIcon] = useState('\u{1F4D8}');
  const [newLevel, setNewLevel] = useState('all');
  const [newOrder, setNewOrder] = useState<number>(0);

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const data = (await adminListGrammarTopics({ examTypeCode: examFilter })) as AdminGrammarTopic[];
      setTopics(data);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message || 'Load failed.' });
    } finally {
      setLoading(false);
    }
  }, [examFilter]);

  useEffect(() => {
    if (!canWriteContent) return;
    queueMicrotask(() => void reload());
  }, [canWriteContent, reload]);

  async function create() {
    if (!canWriteContent) return;
    if (!newName.trim() || !newSlug.trim()) {
      setToast({ variant: 'error', message: 'Slug and name are required.' });
      return;
    }
    try {
      await adminCreateGrammarTopic({
        examTypeCode: newExam,
        slug: newSlug.trim(),
        name: newName.trim(),
        description: newDesc.trim() || null,
        iconEmoji: newIcon || null,
        levelHint: newLevel,
        sortOrder: newOrder,
      });
      setToast({ variant: 'success', message: 'Topic created.' });
      setNewSlug('');
      setNewName('');
      setNewDesc('');
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message || 'Create failed.' });
    }
  }

  async function togglePublish(t: AdminGrammarTopic) {
    if (!canWriteContent) return;
    try {
      await adminUpdateGrammarTopic(t.id, { status: t.status === 'published' ? 'draft' : 'published' });
      setToast({ variant: 'success', message: `Topic ${t.status === 'published' ? 'unpublished' : 'published'}.` });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message || 'Update failed.' });
    }
  }

  async function archive(t: AdminGrammarTopic) {
    if (!canWriteContent) return;
    if (!confirm(`Archive topic "${t.name}"? Lessons in this topic will be hidden from learners.`)) return;
    try {
      await adminArchiveGrammarTopic(t.id);
      setToast({ variant: 'success', message: 'Topic archived.' });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message || 'Archive failed.' });
    }
  }

  if (isLoading) return null;

  if (!isAuthenticated || role !== 'admin') return null;

  if (!canWriteContent) {
    return (
      <AdminCatalogLayout
        title="Grammar Topics"
        description="Content write permission is required."
        eyebrow="CMS"
        breadcrumbs={BREADCRUMBS}
        hideViewModeToggle
      >
        <Card className="col-span-full">
          <CardContent className="py-8 text-sm text-admin-fg-muted">
            You do not have permission to manage grammar topics.
          </CardContent>
        </Card>
      </AdminCatalogLayout>
    );
  }

  const filtersNode = (
    <div className="grid w-full max-w-sm gap-3">
      <Select
        value={examFilter}
        onChange={(e) => setExamFilter(e.target.value)}
        label="Exam"
        options={EXAMS}
      />
    </div>
  );

  return (
    <>
      <AdminCatalogLayout
        title="Grammar Topics"
        description="Create and manage the topic taxonomy that groups grammar lessons."
        eyebrow="CMS"
        breadcrumbs={BREADCRUMBS}
        hideViewModeToggle
        filters={filtersNode}
        actions={
          <Button variant="outline" size="sm" asChild>
            <Link href="/admin/content/grammar">Back to Grammar CMS</Link>
          </Button>
        }
      >
        <Card className="col-span-full">
          <CardHeader>
            <CardTitle>Create a new topic</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            <div className="grid gap-3 sm:grid-cols-3">
              <Select
                value={newExam}
                onChange={(e) => setNewExam(e.target.value)}
                label="Exam"
                options={EXAMS}
              />
              <Input
                label="Slug"
                value={newSlug}
                onChange={(e) => setNewSlug(e.target.value.toLowerCase().replace(/[^a-z0-9_-]/g, '_'))}
                placeholder="tenses"
              />
              <Input label="Display name" value={newName} onChange={(e) => setNewName(e.target.value)} placeholder="Tenses for medical narratives" />
              <Input label="Icon emoji" value={newIcon} onChange={(e) => setNewIcon(e.target.value)} placeholder="\u{1F4D8}" />
              <Select label="Level hint" value={newLevel} onChange={(e) => setNewLevel(e.target.value)} options={LEVELS} />
              <Input label="Sort order" type="number" value={String(newOrder)} onChange={(e) => setNewOrder(Number(e.target.value) || 0)} />
            </div>
            <Textarea label="Description" value={newDesc} onChange={(e) => setNewDesc(e.target.value)} placeholder="Short description surfaced to learners" />
            <div className="flex justify-end">
              <Button size="sm" onClick={create}>
                <Plus className="mr-2 h-4 w-4" /> Create topic
              </Button>
            </div>
          </CardContent>
        </Card>

        <Card className="col-span-full">
          <CardHeader>
            <CardTitle>Existing topics ({topics.length})</CardTitle>
          </CardHeader>
          <CardContent>
            {loading ? (
              <div className="grid gap-3 md:grid-cols-2 lg:grid-cols-3">
                {Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-32 rounded-admin" />)}
              </div>
            ) : topics.length === 0 ? (
              <EmptyState
                title="No topics yet"
                description="Create your first topic above to begin organizing lessons."
              />
            ) : (
              <div className="grid gap-3 md:grid-cols-2 lg:grid-cols-3">
                {topics.map((t) => (
                  <Card key={t.id} className="p-4">
                    <div className="flex items-start gap-2">
                      <span className="text-xl">{t.iconEmoji ?? '\u{1F4D8}'}</span>
                      <div className="min-w-0 flex-1">
                        <h3 className="truncate text-sm font-semibold text-admin-fg-strong">{t.name}</h3>
                        <p className="text-xs text-admin-fg-muted">{t.examTypeCode} · {t.slug} · {t.levelHint}</p>
                      </div>
                      <Badge variant={statusToTone(t.status) as 'success' | 'default'}>
                        {t.status}
                      </Badge>
                    </div>
                    {t.description ? <p className="mt-2 line-clamp-3 text-xs text-admin-fg-muted">{t.description}</p> : null}
                    <div className="mt-3 flex flex-wrap gap-2">
                      <Button size="sm" variant={t.status === 'published' ? 'outline' : 'primary'} onClick={() => togglePublish(t)}>
                        {t.status === 'published' ? 'Unpublish' : 'Publish'}
                      </Button>
                      <Button size="sm" variant="outline" onClick={() => archive(t)}>
                        <Trash2 className="mr-1 h-3.5 w-3.5" /> Archive
                      </Button>
                    </div>
                  </Card>
                ))}
              </div>
            )}
          </CardContent>
        </Card>
      </AdminCatalogLayout>

      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}
    </>
  );
}
