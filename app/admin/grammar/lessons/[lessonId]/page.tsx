'use client';

import { useCallback, useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, FileEdit } from 'lucide-react';
import {
  adminGetGrammarLessonV2,
  adminUpdateGrammarLessonV2,
  adminListGrammarTopics,
  adminPublishGrammarLesson,
  adminUnpublishGrammarLesson,
  adminEvaluateGrammarPublishGate,
  adminArchiveGrammarLessonV2,
} from '@/lib/api';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Toast } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import {
  GrammarLessonEditor,
  draftToApi,
  type LessonDraft,
  type ContentBlockDraft,
  type ExerciseDraft,
} from '@/components/domain/grammar/grammar-lesson-editor';
import type { AdminGrammarLessonFull, AdminGrammarTopic, GrammarExerciseType } from '@/lib/grammar/types';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

export default function EditGrammarLessonPage() {
  const params = useParams<{ lessonId: string }>();
  const router = useRouter();
  const lessonId = params?.lessonId ?? '';

  const [topics, setTopics] = useState<AdminGrammarTopic[]>([]);
  const [lesson, setLesson] = useState<AdminGrammarLessonFull | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);
  const [publishGate, setPublishGate] = useState<{ canPublish: boolean; errors: string[] } | null>(null);

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const [t, l] = await Promise.all([
        adminListGrammarTopics({}) as Promise<AdminGrammarTopic[]>,
        adminGetGrammarLessonV2(lessonId) as Promise<AdminGrammarLessonFull>,
      ]);
      setTopics(t || []);
      setLesson(l);
      try {
        const gate = (await adminEvaluateGrammarPublishGate(lessonId)) as { canPublish: boolean; errors: string[] };
        setPublishGate(gate);
      } catch {
        setPublishGate(null);
      }
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message || 'Load failed.' });
    } finally {
      setLoading(false);
    }
  }, [lessonId]);

  useEffect(() => {
    if (!lessonId) return;
    queueMicrotask(() => void reload());
  }, [lessonId, reload]);

  const onSave = useCallback(async (draft: LessonDraft) => {
    setSaving(true);
    try {
      const body = draftToApi(draft);
      await adminUpdateGrammarLessonV2(lessonId, body);
      setToast({ variant: 'success', message: 'Lesson updated.' });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message || 'Update failed.' });
    } finally {
      setSaving(false);
    }
  }, [lessonId, reload]);

  const onPublish = useCallback(async () => {
    try {
      await adminPublishGrammarLesson(lessonId);
      setToast({ variant: 'success', message: 'Lesson published.' });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message || 'Publish failed.' });
    }
  }, [lessonId, reload]);

  const onUnpublish = useCallback(async () => {
    try {
      await adminUnpublishGrammarLesson(lessonId);
      setToast({ variant: 'success', message: 'Lesson unpublished.' });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message || 'Unpublish failed.' });
    }
  }, [lessonId, reload]);

  const onArchive = useCallback(async () => {
    if (!confirm('Archive this lesson?')) return;
    try {
      await adminArchiveGrammarLessonV2(lessonId);
      setToast({ variant: 'success', message: 'Archived.' });
      setTimeout(() => router.push('/admin/grammar'), 400);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message || 'Archive failed.' });
    }
  }, [lessonId, router]);

  if (loading || !lesson) {
    return (
      <AdminRouteWorkspace role="main" aria-label="Edit grammar lesson">
        <Skeleton className="h-8 w-64 rounded" />
        <Skeleton className="h-64 rounded-2xl" />
      </AdminRouteWorkspace>
    );
  }

  const initial: LessonDraft = {
    examTypeCode: lesson.examTypeCode,
    topicId: lesson.topicId,
    title: lesson.title,
    description: lesson.description ?? '',
    level: lesson.level,
    category: lesson.category,
    estimatedMinutes: lesson.estimatedMinutes,
    sortOrder: lesson.sortOrder,
    sourceProvenance: lesson.sourceProvenance,
    prerequisiteLessonIds: Array.isArray(lesson.prerequisiteLessonIds)
      ? lesson.prerequisiteLessonIds.map(String)
      : [],
    contentBlocks: lesson.contentBlocks.map<ContentBlockDraft>((b) => ({
      id: b.id ?? undefined,
      sortOrder: b.sortOrder,
      type: b.type,
      contentMarkdown: b.contentMarkdown,
    })),
    exercises: lesson.exercises.map<ExerciseDraft>((e) => ({
      id: e.id ?? undefined,
      sortOrder: e.sortOrder,
      type: e.type as GrammarExerciseType,
      promptMarkdown: e.promptMarkdown,
      options: Array.isArray(e.options) ? e.options : [],
      correctAnswer: e.correctAnswer ?? '',
      acceptedAnswers: Array.isArray(e.acceptedAnswers) ? e.acceptedAnswers.map(String) : [],
      explanationMarkdown: e.explanationMarkdown ?? '',
      difficulty: (e.difficulty as ExerciseDraft['difficulty']) ?? 'intermediate',
      points: e.points,
    })),
  };

  return (
    <AdminRouteWorkspace role="main" aria-label="Edit grammar lesson">
      <Link href="/admin/grammar" className="inline-flex items-center gap-1 text-sm text-muted hover:text-navy" aria-label="Back">
        <ArrowLeft className="h-4 w-4" /> Back to Grammar CMS
      </Link>

      <AdminRouteHero
        eyebrow="CMS"
        icon={FileEdit}
        accent="navy"
        title={`Edit: ${lesson.title}`}
        description={`State: ${lesson.publishState} · v${lesson.version}`}
        aside={(
          <div className="rounded-2xl border border-border bg-background-light p-4 shadow-sm">
            <Badge className={lesson.publishState === 'published' ? 'bg-emerald-100 text-emerald-800' : 'bg-slate-100 text-slate-700'}>
              {lesson.publishState} · v{lesson.version}
            </Badge>
            <div className="mt-3 flex flex-wrap gap-2">
              {lesson.publishState === 'published' ? (
                <Button variant="outline" size="sm" onClick={onUnpublish}>Unpublish</Button>
              ) : null}
              <Button variant="outline" size="sm" onClick={onArchive}>Archive</Button>
            </div>
          </div>
        )}
      />

      <AdminRoutePanel title="Publish gate">
        <p className="text-sm text-muted">
          Publish gate:{' '}
          {publishGate === null
            ? 'checking…'
            : publishGate.canPublish
              ? <span className="font-semibold text-success">Pass</span>
              : <span className="font-semibold text-danger">Fail ({publishGate.errors.length} issue{publishGate.errors.length === 1 ? '' : 's'})</span>}
        </p>
        {publishGate && !publishGate.canPublish ? (
          <ul className="mt-2 list-disc pl-5 text-sm text-danger">
            {publishGate.errors.map((e, i) => <li key={i}>{e}</li>)}
          </ul>
        ) : null}
      </AdminRoutePanel>

      <GrammarLessonEditor
        initial={initial}
        topics={topics.map((t) => ({ id: t.id, name: t.name, slug: t.slug }))}
        onSave={onSave}
        onPublish={publishGate?.canPublish ? onPublish : undefined}
        publishable={publishGate?.canPublish}
        publishErrors={publishGate && !publishGate.canPublish ? publishGate.errors : null}
        saving={saving}
      />

      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}
    </AdminRouteWorkspace>
  );
}
