'use client';

import { useCallback, useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import Link from 'next/link';
import {
  adminGetGrammarLessonV2,
  adminUpdateGrammarLessonV2,
  adminListGrammarTopics,
  adminPublishGrammarLessonV2,
  adminUnpublishGrammarLessonV2,
  adminFetchGrammarPublishGate,
  adminArchiveGrammarLessonV2,
} from '@/lib/api';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { AdminSettingsLayout } from '@/components/admin/layout/admin-settings-layout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import { Toast } from '@/components/ui/alert';
import { Button } from '@/components/admin/ui/button';
import { Badge, statusToTone } from '@/components/admin/ui/badge';
import { Skeleton } from '@/components/admin/ui/skeleton';
import {
  GrammarLessonEditor,
  draftToApi,
  type LessonDraft,
  type ContentBlockDraft,
  type ExerciseDraft,
} from '@/components/domain/grammar/grammar-lesson-editor';
import type { AdminGrammarLessonFull, AdminGrammarTopic, GrammarExerciseType } from '@/lib/grammar/types';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

const BASE_BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Content', href: '/admin/content' },
  { label: 'Grammar', href: '/admin/content/grammar' },
];

export default function EditGrammarLessonPage() {
  const params = useParams<{ lessonId: string }>();
  const router = useRouter();
  const { isAuthenticated, isLoading, role } = useAdminAuth();
  const { user } = useCurrentUser();
  const canWriteContent = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);
  const canPublishContent = hasPermission(user?.adminPermissions, AdminPermission.ContentPublish);
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
        const gate = (await adminFetchGrammarPublishGate(lessonId)) as { canPublish: boolean; errors: string[] };
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
    if (!lessonId || !canWriteContent) return;
    queueMicrotask(() => void reload());
  }, [canWriteContent, lessonId, reload]);

  const onSave = useCallback(async (draft: LessonDraft) => {
    if (!canWriteContent) return;
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
  }, [canWriteContent, lessonId, reload]);

  const onPublish = useCallback(async () => {
    if (!canPublishContent) return;
    try {
      await adminPublishGrammarLessonV2(lessonId);
      setToast({ variant: 'success', message: 'Lesson published.' });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message || 'Publish failed.' });
    }
  }, [canPublishContent, lessonId, reload]);

  const onUnpublish = useCallback(async () => {
    if (!canPublishContent) return;
    try {
      await adminUnpublishGrammarLessonV2(lessonId);
      setToast({ variant: 'success', message: 'Lesson unpublished.' });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message || 'Unpublish failed.' });
    }
  }, [canPublishContent, lessonId, reload]);

  const onArchive = useCallback(async () => {
    if (!canWriteContent) return;
    if (!confirm('Archive this lesson?')) return;
    try {
      await adminArchiveGrammarLessonV2(lessonId);
      setToast({ variant: 'success', message: 'Archived.' });
      setTimeout(() => router.push('/admin/content/grammar'), 400);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message || 'Archive failed.' });
    }
  }, [canWriteContent, lessonId, router]);

  if (isLoading) return null;
  if (!isAuthenticated || role !== 'admin') return null;

  if (!canWriteContent) {
    return (
      <AdminSettingsLayout
        title="Edit grammar lesson"
        description="Content write permission is required."
        eyebrow="CMS"
        breadcrumbs={[...BASE_BREADCRUMBS, { label: 'Edit lesson' }]}
      >
        <Card>
          <CardContent className="py-8 text-sm text-admin-fg-muted">
            You do not have permission to edit lessons.
          </CardContent>
        </Card>
      </AdminSettingsLayout>
    );
  }

  if (loading || !lesson) {
    return (
      <AdminSettingsLayout
        title="Loading lesson…"
        eyebrow="CMS"
        breadcrumbs={[...BASE_BREADCRUMBS, { label: 'Edit lesson' }]}
      >
        <Skeleton className="h-8 w-64 rounded-admin" />
        <Skeleton className="h-64 rounded-admin" />
      </AdminSettingsLayout>
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
    <>
      <AdminSettingsLayout
        title={`Edit: ${lesson.title}`}
        description={`State: ${lesson.publishState} · v${lesson.version}`}
        eyebrow="CMS"
        breadcrumbs={[...BASE_BREADCRUMBS, { label: lesson.title }]}
        actions={
          <div className="flex flex-wrap items-center gap-2">
            <Badge variant={statusToTone(lesson.publishState) as 'success' | 'default'}>
              {lesson.publishState} · v{lesson.version}
            </Badge>
            {canPublishContent && lesson.publishState === 'published' ? (
              <Button variant="outline" size="sm" onClick={onUnpublish}>Unpublish</Button>
            ) : null}
            <Button variant="outline" size="sm" onClick={onArchive}>Archive</Button>
            <Button variant="outline" size="sm" asChild>
              <Link href="/admin/content/grammar">Back to CMS</Link>
            </Button>
          </div>
        }
      >
        <Card>
          <CardHeader>
            <CardTitle>Publish gate</CardTitle>
          </CardHeader>
          <CardContent className="space-y-2">
            <p className="text-sm text-admin-fg-muted">
              Publish gate:{' '}
              {publishGate === null
                ? 'checking…'
                : publishGate.canPublish
                  ? <span className="font-semibold text-[var(--admin-success)]">Pass</span>
                  : <span className="font-semibold text-[var(--admin-danger)]">Fail ({publishGate.errors.length} issue{publishGate.errors.length === 1 ? '' : 's'})</span>}
            </p>
            {publishGate && !publishGate.canPublish ? (
              <ul className="list-disc pl-5 text-sm text-[var(--admin-danger)]">
                {publishGate.errors.map((e, i) => <li key={i}>{e}</li>)}
              </ul>
            ) : null}
          </CardContent>
        </Card>

        <GrammarLessonEditor
          initial={initial}
          topics={topics.map((t) => ({ id: t.id, name: t.name, slug: t.slug }))}
          onSave={onSave}
          onPublish={canPublishContent && publishGate?.canPublish ? onPublish : undefined}
          publishable={publishGate?.canPublish}
          publishErrors={publishGate && !publishGate.canPublish ? publishGate.errors : null}
          saving={saving}
        />
      </AdminSettingsLayout>

      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}
    </>
  );
}
