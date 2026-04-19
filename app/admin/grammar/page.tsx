'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { BookMarked, Plus, Sparkles, Library, Wand2 } from 'lucide-react';
import {
  adminListGrammarTopics,
  adminListGrammarLessonsV2,
  adminArchiveGrammarLessonV2,
  adminPublishGrammarLesson,
  adminUnpublishGrammarLesson,
} from '@/lib/api';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Input, Select } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import type { AdminGrammarTopic } from '@/lib/grammar/types';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

interface AdminLessonRow {
  id: string;
  title: string;
  examTypeCode: string;
  topicId: string | null;
  level: string;
  estimatedMinutes: number;
  status: string;
  publishState: string;
  updatedAt: string;
}

export default function AdminGrammarDashboard() {
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
    try {
      await adminPublishGrammarLesson(id);
      setToast({ variant: 'success', message: 'Lesson published.' });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message || 'Publish failed.' });
    }
  }

  async function unpublish(id: string) {
    try {
      await adminUnpublishGrammarLesson(id);
      setToast({ variant: 'success', message: 'Lesson unpublished.' });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message || 'Unpublish failed.' });
    }
  }

  async function archive(id: string) {
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
    <div className="space-y-6 p-4 sm:p-6">
      <header className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <div className="flex items-center gap-2">
            <Library className="h-6 w-6 text-primary" />
            <h1 className="text-2xl font-bold text-navy dark:text-white">Grammar CMS</h1>
          </div>
          <p className="mt-1 text-sm text-muted">Manage grammar topics, authored lessons, and AI drafts.</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Link href="/admin/grammar/topics">
            <Button variant="outline" className="inline-flex items-center gap-2">
              <BookMarked className="h-4 w-4" /> Topics
            </Button>
          </Link>
          <Link href="/admin/grammar/ai-draft">
            <Button variant="outline" className="inline-flex items-center gap-2">
              <Wand2 className="h-4 w-4" /> AI draft
            </Button>
          </Link>
          <Link href="/admin/grammar/lessons/new">
            <Button className="inline-flex items-center gap-2">
              <Plus className="h-4 w-4" /> New lesson
            </Button>
          </Link>
        </div>
      </header>

      <Card className="p-4">
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
      </Card>

      <section>
        <h2 className="mb-3 text-sm font-semibold uppercase tracking-[0.18em] text-muted">Topics ({topics.length})</h2>
        {loading ? (
          <div className="grid gap-3 md:grid-cols-2 lg:grid-cols-3">
            {Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-24 rounded-2xl" />)}
          </div>
        ) : topics.length === 0 ? (
          <Card className="border-dashed p-6 text-sm text-muted">
            No topics yet.{' '}
            <Link href="/admin/grammar/topics" className="text-primary hover:underline">Create the first one →</Link>
          </Card>
        ) : (
          <div className="grid gap-3 md:grid-cols-2 lg:grid-cols-3">
            {topics.map((t) => (
              <Card key={t.id} className="p-4">
                <div className="flex items-center gap-2">
                  <span className="text-xl">{t.iconEmoji ?? '📘'}</span>
                  <h3 className="text-sm font-semibold text-navy dark:text-white">{t.name}</h3>
                  <Badge className="ml-auto text-[10px]">{t.status}</Badge>
                </div>
                {t.description ? <p className="mt-2 line-clamp-2 text-xs text-muted">{t.description}</p> : null}
                <div className="mt-3 flex items-center justify-between text-xs text-muted">
                  <span>{t.slug}</span>
                  <Link href={`/admin/grammar/topics?id=${encodeURIComponent(t.id)}`} className="font-semibold text-primary hover:underline">
                    Manage
                  </Link>
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
            No lessons yet.{' '}
            <Link href="/admin/grammar/lessons/new" className="text-primary hover:underline">Author the first one →</Link>
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
                  <th className="p-3">Actions</th>
                </tr>
              </thead>
              <tbody>
                {lessons.map((l) => (
                  <tr key={l.id} className="border-t border-border">
                    <td className="p-3 font-medium text-navy dark:text-white">{l.title}</td>
                    <td className="p-3 text-muted">{l.topicId ? (topicMap.get(l.topicId)?.name ?? '—') : '—'}</td>
                    <td className="p-3 text-muted">{l.level}</td>
                    <td className="p-3 text-muted">{l.estimatedMinutes}</td>
                    <td className="p-3">
                      <Badge className={stateColor(l.publishState)}>{l.publishState}</Badge>
                    </td>
                    <td className="p-3 text-xs text-muted">{new Date(l.updatedAt).toLocaleDateString()}</td>
                    <td className="p-3">
                      <div className="flex flex-wrap gap-2">
                        <Link href={`/admin/grammar/lessons/${encodeURIComponent(l.id)}`}>
                          <Button variant="outline" size="sm">Edit</Button>
                        </Link>
                        {l.publishState === 'published' ? (
                          <Button variant="outline" size="sm" onClick={() => unpublish(l.id)}>Unpublish</Button>
                        ) : (
                          <Button size="sm" onClick={() => publish(l.id)}>Publish</Button>
                        )}
                        <Button variant="outline" size="sm" onClick={() => archive(l.id)}>Archive</Button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </Card>
        )}
      </section>

      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}
    </div>
  );
}

function stateColor(state: string) {
  switch (state) {
    case 'published': return 'bg-emerald-100 text-emerald-800';
    case 'draft': return 'bg-slate-100 text-slate-700';
    case 'review': return 'bg-amber-100 text-amber-800';
    case 'archived': return 'bg-rose-100 text-rose-800';
    default: return 'bg-slate-100 text-slate-700';
  }
}
