'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { GraduationCap, Plus, RefreshCcw, Trash2 } from 'lucide-react';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { apiClient } from '@/lib/api';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import type { WritingLessonDto, WritingLessonQuizQuestionDto } from '@/lib/writing/types';

type LessonStatus = WritingLessonDto['status'];

interface LessonForm {
  id?: string;
  originalStatus?: LessonStatus;
  subSkill: string;
  orderInCourse: number;
  title: string;
  bodyMarkdown: string;
  videoUrl: string;
  estimatedMinutes: number;
  quizQuestionsJson: string;
  status: LessonStatus;
}

const EMPTY_FORM: LessonForm = {
  subSkill: 'W1',
  orderInCourse: 1,
  title: '',
  bodyMarkdown: '',
  videoUrl: '',
  estimatedMinutes: 8,
  quizQuestionsJson: '[]',
  status: 'draft',
};

const statusTone = (status: LessonStatus) => status === 'published' ? 'success' : status === 'archived' ? 'muted' : 'warning';

export default function AdminWritingLessonsPage() {
  const { user } = useCurrentUser();
  const canWriteContent = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);
  const canPublishContent = hasPermission(user?.adminPermissions, AdminPermission.ContentPublish);
  const [items, setItems] = useState<WritingLessonDto[]>([]);
  const [editing, setEditing] = useState<LessonForm | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const response = await apiClient.get<{ items: WritingLessonDto[] }>('/v1/admin/writing/lessons');
      setItems(response.items);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load lessons.');
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const sortedItems = useMemo(() => items.slice().sort((a, b) => a.subSkill.localeCompare(b.subSkill) || a.orderInCourse - b.orderInCourse), [items]);

  const save = async () => {
    if (!editing) return;
    if (!canWriteContent) {
      setError('Content write permission is required to save lessons.');
      return;
    }
    if ((editing.originalStatus === 'published' || editing.status === 'published') && !canPublishContent) {
      setError('Content publish permission is required to modify published lessons.');
      return;
    }
    setBusy('save');
    try {
      let quizQuestions: WritingLessonQuizQuestionDto[] = [];
      try {
        quizQuestions = JSON.parse(editing.quizQuestionsJson) as WritingLessonQuizQuestionDto[];
      } catch {
        setError('Quiz questions must be a JSON array.');
        setBusy(null);
        return;
      }
      const payload = {
        subSkill: editing.subSkill,
        orderInCourse: editing.orderInCourse,
        title: editing.title.trim(),
        bodyMarkdown: editing.bodyMarkdown,
        videoUrl: editing.videoUrl.trim() || null,
        estimatedMinutes: editing.estimatedMinutes,
        quizQuestions,
        status: editing.status,
      };
      if (editing.id) {
        await apiClient.put(`/v1/admin/writing/lessons/${encodeURIComponent(editing.id)}`, payload);
      } else {
        await apiClient.post('/v1/admin/writing/lessons', payload);
      }
      setEditing(null);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Save failed.');
    } finally {
      setBusy(null);
    }
  };

  const remove = async (id: string) => {
    if (!window.confirm('Delete this lesson?')) return;
    setBusy(`del-${id}`);
    try {
      await apiClient.delete(`/v1/admin/writing/lessons/${encodeURIComponent(id)}`);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Delete failed.');
    } finally {
      setBusy(null);
    }
  };

  const edit = (lesson: WritingLessonDto) => setEditing({
    id: lesson.id,
    originalStatus: lesson.status,
    subSkill: lesson.subSkill,
    orderInCourse: lesson.orderInCourse,
    title: lesson.title,
    bodyMarkdown: lesson.bodyMarkdown,
    videoUrl: lesson.videoUrl ?? '',
    estimatedMinutes: lesson.estimatedMinutes,
    quizQuestionsJson: JSON.stringify(lesson.quizQuestions, null, 2),
    status: lesson.status,
  });

  const publishLocked = (editing?.originalStatus === 'published' || editing?.status === 'published') && !canPublishContent;

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold text-navy"><GraduationCap className="mr-2 inline h-5 w-5 text-amber-600" aria-hidden="true" /> Writing Lessons</h1>
          <p className="mt-1 text-sm text-muted">Author W1-W8 lesson bodies, videos, timing, status, and quiz JSON.</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button onClick={() => void load()} variant="outline"><RefreshCcw className="h-4 w-4" aria-hidden="true" /> Refresh</Button>
          {canWriteContent ? <Button onClick={() => setEditing({ ...EMPTY_FORM })}><Plus className="h-4 w-4" aria-hidden="true" /> New lesson</Button> : null}
        </div>
      </header>

      {error ? <p role="alert" className="rounded border border-red-300 bg-red-50 p-3 text-sm text-red-800">{error}</p> : null}

      <Card>
        <CardContent>
          <table className="w-full text-sm" aria-label="Writing lessons">
            <thead>
              <tr className="border-b border-border text-xs uppercase tracking-wider text-muted">
                <th className="py-2 text-left">Lesson</th>
                <th className="text-left">Skill</th>
                <th className="text-left">Order</th>
                <th className="text-left">Minutes</th>
                <th className="text-left">Quiz</th>
                <th className="text-left">Status</th>
                <th className="text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {sortedItems.length === 0 ? <tr><td colSpan={7} className="py-4 text-center text-xs text-muted">No lessons yet.</td></tr> : null}
              {sortedItems.map((lesson) => (
                <tr key={lesson.id} className="border-b border-border/60">
                  <td className="py-2 font-bold text-navy">{lesson.title}</td>
                  <td>{lesson.subSkill}</td>
                  <td>{lesson.orderInCourse}</td>
                  <td>{lesson.estimatedMinutes}</td>
                  <td>{lesson.quizQuestions.length}</td>
                  <td><Badge variant={statusTone(lesson.status)} size="sm">{lesson.status}</Badge></td>
                  <td className="text-right">
                    {canWriteContent && (lesson.status !== 'published' || canPublishContent) ? (
                      <>
                        <Button size="sm" variant="outline" onClick={() => edit(lesson)}>Edit</Button>
                        <Button size="sm" variant="ghost" onClick={() => void remove(lesson.id)} loading={busy === `del-${lesson.id}`} aria-label="Delete lesson"><Trash2 className="h-4 w-4" aria-hidden="true" /></Button>
                      </>
                    ) : <span className="text-xs text-muted">View only</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </CardContent>
      </Card>

      {editing ? (
        <aside role="dialog" aria-modal="true" aria-label="Lesson editor" className="fixed inset-0 z-50 flex items-stretch justify-end bg-black/40">
          <div className="flex h-full w-full max-w-3xl flex-col gap-3 overflow-y-auto bg-surface p-5 pt-[max(1.25rem,env(safe-area-inset-top))] pb-[max(1.25rem,env(safe-area-inset-bottom))] shadow-2xl">
            <header className="flex items-center justify-between gap-2">
              <h2 className="text-lg font-bold text-navy">{editing.id ? 'Edit lesson' : 'New lesson'}</h2>
              <Button variant="ghost" onClick={() => setEditing(null)}>Close</Button>
            </header>

            <div className="grid gap-2 md:grid-cols-3">
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">Sub-skill<select value={editing.subSkill} onChange={(event) => setEditing({ ...editing, subSkill: event.target.value })} className="min-h-9 rounded border border-border bg-background px-2 text-sm">{['W1', 'W2', 'W3', 'W4', 'W5', 'W6', 'W7', 'W8'].map((skill) => <option key={skill} value={skill}>{skill}</option>)}</select></label>
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">Order<input type="number" min={0} value={editing.orderInCourse} onChange={(event) => setEditing({ ...editing, orderInCourse: Number(event.target.value) })} className="min-h-9 rounded border border-border bg-background px-2 text-sm" /></label>
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">Minutes<input type="number" min={1} max={60} value={editing.estimatedMinutes} onChange={(event) => setEditing({ ...editing, estimatedMinutes: Number(event.target.value) })} className="min-h-9 rounded border border-border bg-background px-2 text-sm" /></label>
            </div>

            <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">Title<input value={editing.title} onChange={(event) => setEditing({ ...editing, title: event.target.value })} className="min-h-9 rounded border border-border bg-background px-2 text-sm" /></label>
            <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">Video URL<input value={editing.videoUrl} onChange={(event) => setEditing({ ...editing, videoUrl: event.target.value })} className="min-h-9 rounded border border-border bg-background px-2 text-sm" /></label>
            <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">Body markdown<textarea rows={12} value={editing.bodyMarkdown} onChange={(event) => setEditing({ ...editing, bodyMarkdown: event.target.value })} className="rounded border border-border bg-background p-2 text-sm font-mono" /></label>
            <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">Quiz questions JSON<textarea rows={10} value={editing.quizQuestionsJson} onChange={(event) => setEditing({ ...editing, quizQuestionsJson: event.target.value })} className="rounded border border-border bg-background p-2 text-sm font-mono" /></label>
            <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">Status<select value={editing.status} onChange={(event) => setEditing({ ...editing, status: event.target.value as LessonStatus })} className="min-h-9 rounded border border-border bg-background px-2 text-sm"><option value="draft">Draft</option>{canPublishContent || editing.status === 'published' ? <option value="published" disabled={!canPublishContent}>Published</option> : null}<option value="archived">Archived</option></select></label>
            {publishLocked ? <p className="rounded border border-amber-300 bg-amber-50 p-2 text-xs text-amber-900">Content publish permission is required to modify a published lesson.</p> : null}

            <footer className="mt-auto flex items-center justify-end gap-2">
              <Button variant="outline" onClick={() => setEditing(null)}>Cancel</Button>
              <Button onClick={() => void save()} loading={busy === 'save'} disabled={publishLocked || !canWriteContent}>Save</Button>
            </footer>
          </div>
        </aside>
      ) : null}
    </div>
  );
}