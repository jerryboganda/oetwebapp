'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { BookMarked, Plus, Library, Wand2 } from 'lucide-react';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import {
  adminListGrammarTopics,
  adminListGrammarLessonsV2,
  adminArchiveGrammarLessonV2,
  adminPublishGrammarLessonV2,
  adminUnpublishGrammarLessonV2,
} from '@/lib/api';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Input, Select } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import type { AdminGrammarLessonRow, AdminGrammarTopic } from '@/lib/grammar/types';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

type AdminLessonRow = AdminGrammarLessonRow;

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

  return (
    <AdminRouteWorkspace role="main" aria-label="Grammar CMS">
      <AdminRouteHero
        eyebrow="CMS"
        icon={Library}
        accent="navy"
        title="Grammar CMS"
        description="Manage grammar topics, authored lessons, and AI drafts."
        aside={canWriteContent ? (
          <div className="rounded-2xl border border-border bg-background-light p-4 shadow-sm">
            <div className="flex flex-wrap gap-2">
              <Link href="/admin/content/grammar/topics">
                <Button variant="outline" className="inline-flex items-center gap-2">
                  <BookMarked className="h-4 w-4" /> Topics
                </Button>
              </Link>
              <Link href="/admin/content/grammar/ai-draft">
                <Button variant="outline" className="inline-flex items-center gap-2">
                  <Wand2 className="h-4 w-4" /> AI draft
                </Button>
              </Link>
              <Link href="/admin/content/grammar/lessons/new">
                <Button className="inline-flex items-center gap-2">
                  <Plus className="h-4 w-4" /> New lesson
                </Button>
              </Link>
            </div>
          </div>
        ) : undefined}
      />

      <AdminRoutePanel>
        <div className="grid gap-3 sm:grid-cols-4">
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
      </AdminRoutePanel>

      <section>
        <h2 className="mb-3 text-sm font-semibold uppercase tracking-[0.18em] text-muted">Topics ({topics.length})</h2>
        {loading ? (
          <div className="grid gap-3 md:grid-cols-2 lg:grid-cols-3">
            {Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-24 rounded-2xl" />)}
          </div>
        ) : topics.length === 0 ? (
          <Card className="border-dashed p-6 text-sm text-muted">
            No topics yet.{canWriteContent ? (
              <>
                {' '}
                <Link href="/admin/content/grammar/topics" className="text-primary hover:underline">Create the first one →</Link>
              </>
            ) : null}
          </Card>
        ) : (
          <div className="grid gap-3 md:grid-cols-2 lg:grid-cols-3">
            {topics.map((t) => (
              <Card key={t.id} className="p-4">
                <div className="flex items-center gap-2">
                  <span className="text-xl">{t.iconEmoji ?? '📘'}</span>
                  <h3 className="text-sm font-semibold text-navy">{t.name}</h3>
                  <Badge className="ml-auto text-[10px]">{t.status}</Badge>
                </div>
                {t.description ? <p className="mt-2 line-clamp-2 text-xs text-muted">{t.description}</p> : null}
                <div className="mt-3 flex items-center justify-between text-xs text-muted">
                  <span>{t.slug}</span>
                  {canWriteContent ? (
                    <Link href={`/admin/content/grammar/topics?id=${encodeURIComponent(t.id)}`} className="font-semibold text-primary hover:underline">
                      Manage
                    </Link>
                  ) : null}
                </div>
              </Card>
            ))}
          </div>
        )}
      </section>

      <section>
        <h2 className="mb-3 text-sm font-semibold uppercase tracking-[0.18em] text-muted">Lessons ({lessons.length})</h2>
        {loading ? (
          <Skeleton className="h-64 rounded-2xl" />
        ) : lessons.length === 0 ? (
          <Card className="border-dashed p-6 text-sm text-muted">
            No lessons yet.{canWriteContent ? (
              <>
                {' '}
                <Link href="/admin/content/grammar/lessons/new" className="text-primary hover:underline">Author the first one →</Link>
              </>
            ) : null}
          </Card>
        ) : (
          <Card className="overflow-x-auto p-0">
            <table className="w-full text-sm">
              <thead className="bg-background-light text-left text-xs uppercase tracking-[0.12em] text-muted">
                <tr>
                  <th className="p-3">Title</th>
                  <th className="p-3">Topic</th>
                  <th className="p-3">Level</th>
                  <th className="p-3">Mins</th>
                  <th className="p-3">State</th>
                  <th className="p-3">Updated</th>
                  {hasLessonActions ? <th className="p-3">Actions</th> : null}
                </tr>
              </thead>
              <tbody>
                {lessons.map((l) => (
                  <tr key={l.id} className="border-t border-border">
                    <td className="p-3 font-medium text-navy">{l.title}</td>
                    <td className="p-3 text-muted">{l.topicId ? (topicMap.get(l.topicId)?.name ?? '—') : '—'}</td>
                    <td className="p-3 text-muted">{l.level}</td>
                    <td className="p-3 text-muted">{l.estimatedMinutes}</td>
                    <td className="p-3">
                      <Badge className={stateColor(l.publishState)}>{l.publishState}</Badge>
                    </td>
                    <td className="p-3 text-xs text-muted">{new Date(l.updatedAt).toLocaleDateString()}</td>
                    {hasLessonActions ? (
                      <td className="p-3">
                        <div className="flex flex-wrap gap-2">
                          {canWriteContent ? (
                            <Link href={`/admin/content/grammar/lessons/${encodeURIComponent(l.id)}`}>
                              <Button variant="outline" size="sm">Edit</Button>
                            </Link>
                          ) : null}
                          {canPublishContent ? (
                            l.publishState === 'published' ? (
                              <Button variant="outline" size="sm" onClick={() => unpublish(l.id)}>Unpublish</Button>
                            ) : (
                              <Button size="sm" onClick={() => publish(l.id)}>Publish</Button>
                            )
                          ) : null}
                          {canWriteContent ? <Button variant="outline" size="sm" onClick={() => archive(l.id)}>Archive</Button> : null}
                        </div>
                      </td>
                    ) : null}
                  </tr>
                ))}
              </tbody>
            </table>
          </Card>
        )}
      </section>

      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}
    </AdminRouteWorkspace>
  );
}

function stateColor(state: string) {
  switch (state) {
    case 'published': return 'bg-success/10 text-success';
    case 'draft': return 'bg-surface text-navy';
    case 'review': return 'bg-warning/10 text-warning';
    case 'archived': return 'bg-danger/10 text-danger';
    default: return 'bg-surface text-navy';
  }
}
