'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, Plus, Save, Trash2 } from 'lucide-react';
import {
  adminListGrammarTopics,
  adminCreateGrammarTopic,
  adminUpdateGrammarTopic,
  adminArchiveGrammarTopic,
} from '@/lib/api';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
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

export default function AdminGrammarTopicsPage() {
  const [topics, setTopics] = useState<AdminGrammarTopic[]>([]);
  const [loading, setLoading] = useState(true);
  const [examFilter, setExamFilter] = useState('oet');
  const [toast, setToast] = useState<ToastState>(null);

  const [newExam, setNewExam] = useState('oet');
  const [newSlug, setNewSlug] = useState('');
  const [newName, setNewName] = useState('');
  const [newDesc, setNewDesc] = useState('');
  const [newIcon, setNewIcon] = useState('📘');
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
    queueMicrotask(() => void reload());
  }, [reload]);

  async function create() {
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
    try {
      await adminUpdateGrammarTopic(t.id, { status: t.status === 'published' ? 'draft' : 'published' });
      setToast({ variant: 'success', message: `Topic ${t.status === 'published' ? 'unpublished' : 'published'}.` });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message || 'Update failed.' });
    }
  }

  async function archive(t: AdminGrammarTopic) {
    if (!confirm(`Archive topic "${t.name}"? Lessons in this topic will be hidden from learners.`)) return;
    try {
      await adminArchiveGrammarTopic(t.id);
      setToast({ variant: 'success', message: 'Topic archived.' });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message || 'Archive failed.' });
    }
  }

  return (
    <div className="space-y-6 p-4 sm:p-6">
      <header className="flex flex-wrap items-center gap-2">
        <Link href="/admin/grammar" className="text-muted hover:text-navy" aria-label="Back">
          <ArrowLeft className="h-5 w-5" />
        </Link>
        <h1 className="text-2xl font-bold text-navy dark:text-white">Grammar Topics</h1>
      </header>

      <Card className="p-4">
        <h2 className="mb-3 text-sm font-semibold text-navy dark:text-white">Create a new topic</h2>
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
          <Input label="Icon emoji" value={newIcon} onChange={(e) => setNewIcon(e.target.value)} placeholder="📘" />
          <Select label="Level hint" value={newLevel} onChange={(e) => setNewLevel(e.target.value)} options={LEVELS} />
          <Input label="Sort order" type="number" value={String(newOrder)} onChange={(e) => setNewOrder(Number(e.target.value) || 0)} />
        </div>
        <Textarea label="Description" value={newDesc} onChange={(e) => setNewDesc(e.target.value)} placeholder="Short description surfaced to learners" />
        <div className="mt-3 flex justify-end">
          <Button className="inline-flex items-center gap-2" onClick={create}>
            <Plus className="h-4 w-4" /> Create topic
          </Button>
        </div>
      </Card>

      <section>
        <div className="mb-3 flex items-center justify-between">
          <h2 className="text-sm font-semibold uppercase tracking-[0.18em] text-muted">Existing topics</h2>
          <Select
            value={examFilter}
            onChange={(e) => setExamFilter(e.target.value)}
            label=""
            options={EXAMS}
          />
        </div>

        {loading ? (
          <Card className="p-6 text-muted">Loading…</Card>
        ) : topics.length === 0 ? (
          <Card className="border-dashed p-6 text-muted">No topics yet.</Card>
        ) : (
          <div className="grid gap-3 md:grid-cols-2 lg:grid-cols-3">
            {topics.map((t) => (
              <Card key={t.id} className="p-4">
                <div className="flex items-start gap-2">
                  <span className="text-xl">{t.iconEmoji ?? '📘'}</span>
                  <div className="min-w-0 flex-1">
                    <h3 className="truncate text-sm font-semibold text-navy dark:text-white">{t.name}</h3>
                    <p className="text-xs text-muted">{t.examTypeCode} · {t.slug} · {t.levelHint}</p>
                  </div>
                  <Badge className={t.status === 'published' ? 'bg-emerald-100 text-emerald-800' : 'bg-slate-100 text-slate-700'}>
                    {t.status}
                  </Badge>
                </div>
                {t.description ? <p className="mt-2 line-clamp-3 text-xs text-muted">{t.description}</p> : null}
                <div className="mt-3 flex flex-wrap gap-2">
                  <Button size="sm" variant={t.status === 'published' ? 'outline' : 'primary'} onClick={() => togglePublish(t)}>
                    {t.status === 'published' ? 'Unpublish' : 'Publish'}
                  </Button>
                  <Button size="sm" variant="outline" onClick={() => archive(t)} className="inline-flex items-center gap-1">
                    <Trash2 className="h-3.5 w-3.5" /> Archive
                  </Button>
                </div>
              </Card>
            ))}
          </div>
        )}
      </section>

      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}
    </div>
  );
}
