'use client';

import { useCallback, useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { adminCreateGrammarLessonV2, adminListGrammarTopics } from '@/lib/api';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { AdminSettingsLayout } from '@/components/admin/layout/admin-settings-layout';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import { Toast } from '@/components/ui/alert';
import { GrammarLessonEditor, emptyDraft, draftToApi, type LessonDraft } from '@/components/domain/grammar/grammar-lesson-editor';
import type { AdminGrammarTopic } from '@/lib/grammar/types';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Content', href: '/admin/content' },
  { label: 'Grammar', href: '/admin/content/grammar' },
  { label: 'New lesson' },
];

export default function NewGrammarLessonPage() {
  const router = useRouter();
  const { isAuthenticated, isLoading, role } = useAdminAuth();
  const { user } = useCurrentUser();
  const canWriteContent = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);
  const [topics, setTopics] = useState<AdminGrammarTopic[]>([]);
  const [saving, setSaving] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  useEffect(() => {
    if (!canWriteContent) return;
    (async () => {
      try {
        const data = (await adminListGrammarTopics({})) as AdminGrammarTopic[];
        setTopics(data || []);
      } catch (e) {
        setToast({ variant: 'error', message: (e as Error).message || 'Failed to load topics.' });
      }
    })();
  }, [canWriteContent]);

  const onSave = useCallback(async (draft: LessonDraft) => {
    if (!canWriteContent) return;
    setSaving(true);
    try {
      const body = draftToApi(draft);
      const res = (await adminCreateGrammarLessonV2(body)) as { id: string };
      setToast({ variant: 'success', message: 'Lesson created.' });
      setTimeout(() => router.push(`/admin/content/grammar/lessons/${encodeURIComponent(res.id)}`), 400);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message || 'Create failed.' });
    } finally {
      setSaving(false);
    }
  }, [canWriteContent, router]);

  if (isLoading) return null;
  if (!isAuthenticated || role !== 'admin') return null;

  if (!canWriteContent) {
    return (
      <AdminSettingsLayout
        title="New grammar lesson"
        description="Content write permission is required."
        eyebrow="CMS"
        breadcrumbs={BREADCRUMBS}
      >
        <Card>
          <CardContent className="py-8 text-sm text-admin-fg-muted">
            You do not have permission to author lessons.
          </CardContent>
        </Card>
      </AdminSettingsLayout>
    );
  }

  return (
    <>
      <AdminSettingsLayout
        title="New grammar lesson"
        description="Author a new grammar lesson with content blocks and exercises before publishing."
        eyebrow="CMS"
        breadcrumbs={BREADCRUMBS}
        actions={
          <Button variant="outline" size="sm" asChild>
            <Link href="/admin/content/grammar">Back to Grammar CMS</Link>
          </Button>
        }
      >
        <GrammarLessonEditor
          initial={emptyDraft()}
          topics={topics.map((t) => ({ id: t.id, name: t.name, slug: t.slug }))}
          onSave={onSave}
          saving={saving}
        />
      </AdminSettingsLayout>

      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}
    </>
  );
}
