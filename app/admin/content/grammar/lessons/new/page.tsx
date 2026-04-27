'use client';

import { useCallback, useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, FilePlus } from 'lucide-react';
import { adminCreateGrammarLessonV2, adminListGrammarTopics } from '@/lib/api';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import {
  AdminRouteHero,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import { Toast } from '@/components/ui/alert';
import { GrammarLessonEditor, emptyDraft, draftToApi, type LessonDraft } from '@/components/domain/grammar/grammar-lesson-editor';
import type { AdminGrammarTopic } from '@/lib/grammar/types';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

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
      <AdminRouteWorkspace role="main" aria-label="New grammar lesson">
        <p className="text-sm text-muted">Content write permission is required.</p>
      </AdminRouteWorkspace>
    );
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="New grammar lesson">
      <Link href="/admin/content/grammar" className="inline-flex items-center gap-1 text-sm text-muted hover:text-navy" aria-label="Back">
        <ArrowLeft className="h-4 w-4" /> Back to Grammar CMS
      </Link>

      <AdminRouteHero
        eyebrow="CMS"
        icon={FilePlus}
        accent="navy"
        title="New grammar lesson"
        description="Author a new grammar lesson with content blocks and exercises before publishing."
      />

      <GrammarLessonEditor
        initial={emptyDraft()}
        topics={topics.map((t) => ({ id: t.id, name: t.name, slug: t.slug }))}
        onSave={onSave}
        saving={saving}
      />

      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}
    </AdminRouteWorkspace>
  );
}
