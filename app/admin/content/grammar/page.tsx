'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { BookMarked, Plus, Wand2 } from 'lucide-react';
import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import {
  adminListGrammarTopics,
  adminListGrammarLessonsV2,
  adminArchiveGrammarLessonV2,
  adminForceDeleteGrammarLessonV2,
  adminPublishGrammarLessonV2,
  adminUnpublishGrammarLessonV2,
} from '@/lib/api';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { Input, Select } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import type { AdminGrammarLessonRow, AdminGrammarTopic } from '@/lib/grammar/types';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

type AdminLessonRow = AdminGrammarLessonRow;

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Content', href: '/admin/content' },
  { label: 'Grammar' },
];

export default function AdminGrammarDashboard() {
  const { user } = useCurrentUser();
  const canWriteContent = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);
  const canPublishContent = hasPermission(user?.adminPermissions, AdminPermission.ContentPublish);
  const hasLessonActions = canWriteContent || canPublishContent;
  const [topics, setTopics] = useState<AdminGrammarTopic[]>([]);
  const [lessons, setLessons] = useState<AdminLessonRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [toast, setToast] = useState<ToastState>(null);
  const [filterExam, setFilterExam] = useState('oet');
  const [filterStatus, setFilterStatus] = useState('');
  const [filterTopic, setFilterTopic] = useState('');
  const [search, setSearch] = useState('');

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const [topicsData, lessonsData] = await Promise.all([
        adminListGrammarTopics({ examTypeCode: filterExam }) as Promise<AdminGrammarTopic[]>,
        adminListGrammarLessonsV2({
          examTypeCode: filterExam,
          status: filterStatus || undefined,
          topicId: filterTopic || undefined,
          search: search || undefined,
          pageSize: 100,
        }) as Promise<{ items: AdminLessonRow[] }>,
      ]);
      setTopics(topicsData || []);
      setLessons(lessonsData?.items || []);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message || 'Failed to load grammar data.' });
    } finally {
      setLoading(false);
    }
  }, [filterExam, filterStatus, filterTopic, search]);

  useEffect(() => {
    queueMicrotask(() => void reload());
  }, [reload]);

  const topicMap = useMemo(() => {
    const map = new Map<string, AdminGrammarTopic>();
    topics.forEach((t) => map.set(t.id, t));
    return map;
  }, [topics]);

  async function publish(id: string) {
    if (!canPublishContent) return;
    try {
      await adminPublishGrammarLessonV2(id);
      setToast({ variant: 'success', message: 'Lesson published.' });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message || 'Publish failed.' });
    }
  }

  async function unpublish(id: string) {
    if (!canPublishContent) return;
    try {
      await adminUnpublishGrammarLessonV2(id);
      setToast({ variant: 'success', message: 'Lesson unpublished.' });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message || 'Unpublish failed.' });
    }
  }

  async function archive(id: string) {
    if (!canWriteContent) return;
    if (!confirm('Archive this lesson? Learners will no longer see it.')) return;
    try {
      await adminArchiveGrammarLessonV2(id);
      setToast({ variant: 'success', message: 'Lesson archived.' });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message || 'Archive failed.' });
    }
  }

  async function forceDelete(id: string) {
    if (!canWriteContent) return;
    if (!confirm('Permanently delete this archived lesson AND all learner progress for it? This cannot be undone.')) return;
    try {
      await adminForceDeleteGrammarLessonV2(id);
      setToast({ variant: 'success', message: 'Lesson permanently deleted.' });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message || 'Delete failed.' });
    }
  }

  const filtersNode = (
    <div className="grid w-full gap-3 sm:grid-cols-4">
      <Select
        value={filterExam}
        onChange={(e) => setFilterExam(e.target.value)}
        label="Exam"
        options={[
          { value: 'oet', label: 'OET' },
          { value: 'ielts', label: 'IELTS' },
          { value: 'pte', label: 'PTE' },
        ]}
      />
      <Select
        value={filterTopic}
        onChange={(e) => setFilterTopic(e.target.value)}
        label="Topic"
        options={[{ value: '', label: 'Any topic' }, ...topics.map((t) => ({ value: t.id, label: t.name }))]}
      />
      <Select
        value={filterStatus}
        onChange={(e) => setFilterStatus(e.target.value)}
        label="Publish state"
        options={[
          { value: '', label: 'Any' },
          { value: 'draft', label: 'Draft' },
          { value: 'review', label: 'In review' },
          { value: 'published', label: 'Published' },
          { value: 'archived', label: 'Archived' },
        ]}
      />
      <Input
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        label="Search"
        placeholder="title or description"
      />
    </div>
  );

  return (
    <>
      <AdminCatalogLayout
        title="Grammar CMS"
        description="Manage grammar topics, authored lessons, and AI drafts."
        eyebrow="CMS"
        breadcrumbs={BREADCRUMBS}
        hideViewModeToggle
        filters={filtersNode}
        actions={canWriteContent ? (
          <div className="flex flex-wrap gap-2">
            <Button variant="outline" size="sm" asChild>
              <Link href="/admin/content/grammar/topics">
                <BookMarked className="mr-2 h-4 w-4" /> Topics
              </Link>
            </Button>
            <Button variant="outline" size="sm" asChild>
              <Link href="/admin/content/grammar/ai-draft">
                <Wand2 className="mr-2 h-4 w-4" /> AI draft
              </Link>
            </Button>
            <Button size="sm" asChild>
              <Link href="/admin/content/grammar/lessons/new">
                <Plus className="mr-2 h-4 w-4" /> New lesson
              </Link>
            </Button>
          </div>
        ) : undefined}
      >
        <Card className="col-span-full">
          <CardHeader>
            <CardTitle>Topics ({topics.length})</CardTitle>
          </CardHeader>
          <CardContent>
            {loading ? (
              <div className="grid gap-3 md:grid-cols-2 lg:grid-cols-3">
                {Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-24 rounded-admin" />)}
              </div>
            ) : topics.length === 0 ? (
              <EmptyState
                title="No topics yet"
                description={canWriteContent ? 'Create the first topic to start organizing lessons.' : 'Topics will appear here once created.'}
                primaryAction={canWriteContent ? { label: 'Create topic', href: '/admin/content/grammar/topics' } : undefined}
              />
            ) : (
              <div className="grid gap-3 md:grid-cols-2 lg:grid-cols-3">
                {topics.map((t) => (
                  <Card key={t.id} className="p-4">
                    <div className="flex items-center gap-2">
                      <span className="text-xl">{t.iconEmoji ?? '\u{1F4D8}'}</span>
                      <h3 className="text-sm font-semibold text-admin-fg-strong">{t.name}</h3>
                      <Badge variant={t.status === 'published' ? 'success' : 'default'} className="ml-auto">{t.status}</Badge>
                    </div>
                    {t.description ? <p className="mt-2 line-clamp-2 text-xs text-admin-fg-muted">{t.description}</p> : null}
                    <div className="mt-3 flex items-center justify-between text-xs text-admin-fg-muted">
                      <span>{t.slug}</span>
                      {canWriteContent ? (
                        <Link href={`/admin/content/grammar/topics?id=${encodeURIComponent(t.id)}`} className="font-semibold text-[var(--admin-primary)] hover:underline">
                          Manage
                        </Link>
                      ) : null}
                    </div>
                  </Card>
                ))}
              </div>
            )}
          </CardContent>
        </Card>

        <Card className="col-span-full">
          <CardHeader>
            <CardTitle>Lessons ({lessons.length})</CardTitle>
          </CardHeader>
          <CardContent>
            {loading ? (
              <Skeleton className="h-64 rounded-admin" />
            ) : lessons.length === 0 ? (
              <EmptyState
                title="No lessons yet"
                description={canWriteContent ? 'Author your first lesson to populate the library.' : 'Lessons will appear here once created.'}
                primaryAction={canWriteContent ? { label: 'Author lesson', href: '/admin/content/grammar/lessons/new' } : undefined}
              />
            ) : (
              <div className="overflow-x-auto rounded-admin border border-admin-border">
                <table className="w-full text-sm">
                  <thead className="bg-admin-bg-subtle text-left text-xs uppercase tracking-[0.12em] text-admin-fg-muted">
                    <tr>
                      <th scope="col" className="p-3">Title</th>
                      <th scope="col" className="p-3">Topic</th>
                      <th scope="col" className="p-3">Level</th>
                      <th scope="col" className="p-3">Mins</th>
                      <th scope="col" className="p-3">State</th>
                      <th scope="col" className="p-3">Updated</th>
                      {hasLessonActions ? <th scope="col" className="p-3">Actions</th> : null}
                    </tr>
                  </thead>
                  <tbody>
                    {lessons.map((l) => (
                      <tr key={l.id} className="border-t border-admin-border">
                        <td className="p-3 font-medium text-admin-fg-strong">{l.title}</td>
                        <td className="p-3 text-admin-fg-muted">{l.topicId ? (topicMap.get(l.topicId)?.name ?? '-') : '-'}</td>
                        <td className="p-3 text-admin-fg-muted">{l.level}</td>
                        <td className="p-3 text-admin-fg-muted">{l.estimatedMinutes}</td>
                        <td className="p-3">
                          <Badge variant={stateToVariant(l.publishState)}>{l.publishState}</Badge>
                        </td>
                        <td className="p-3 text-xs text-admin-fg-muted">{new Date(l.updatedAt).toLocaleDateString()}</td>
                        {hasLessonActions ? (
                          <td className="p-3">
                            <div className="flex flex-wrap gap-2">
                              {canWriteContent ? (
                                <Button variant="outline" size="sm" asChild>
                                  <Link href={`/admin/content/grammar/lessons/${encodeURIComponent(l.id)}`}>Edit</Link>
                                </Button>
                              ) : null}
                              {canPublishContent ? (
                                l.publishState === 'published' ? (
                                  <Button variant="outline" size="sm" onClick={() => unpublish(l.id)}>Unpublish</Button>
                                ) : (
                                  <Button size="sm" onClick={() => publish(l.id)}>Publish</Button>
                                )
                              ) : null}
                              {canWriteContent ? <Button variant="outline" size="sm" onClick={() => archive(l.id)}>Archive</Button> : null}
                              {canWriteContent && l.publishState === 'archived' ? <Button variant="outline" size="sm" className="text-red-600 border-red-300 hover:bg-red-50" onClick={() => forceDelete(l.id)}>Force delete</Button> : null}
                            </div>
                          </td>
                        ) : null}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </CardContent>
        </Card>
      </AdminCatalogLayout>

      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}
    </>
  );
}

function stateToVariant(state: string): 'success' | 'warning' | 'danger' | 'default' {
  switch (state) {
    case 'published': return 'success';
    case 'review': return 'warning';
    case 'archived': return 'danger';
    default: return 'default';
  }
}
