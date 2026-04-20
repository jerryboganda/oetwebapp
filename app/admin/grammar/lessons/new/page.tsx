'use client';

import { useCallback, useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft } from 'lucide-react';
import { adminCreateGrammarLessonV2, adminListGrammarTopics } from '@/lib/api';
import { Toast } from '@/components/ui/alert';
import { GrammarLessonEditor, emptyDraft, draftToApi, type LessonDraft } from '@/components/domain/grammar/grammar-lesson-editor';
import type { AdminGrammarTopic } from '@/lib/grammar/types';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

export default function NewGrammarLessonPage() {
  const router = useRouter();
  const [topics, setTopics] = useState<AdminGrammarTopic[]>([]);
  const [saving, setSaving] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  useEffect(() => {
    (async () => {
      try {
        const data = (await adminListGrammarTopics({})) as AdminGrammarTopic[];
        setTopics(data || []);
      } catch (e) {
        setToast({ variant: 'error', message: (e as Error).message || 'Failed to load topics.' });
      }
    })();
  }, []);

  const onSave = useCallback(async (draft: LessonDraft) => {
    setSaving(true);
    try {
      const body = draftToApi(draft);
      const res = (await adminCreateGrammarLessonV2(body)) as { id: string };
      setToast({ variant: 'success', message: 'Lesson created.' });
      setTimeout(() => router.push(`/admin/grammar/lessons/${encodeURIComponent(res.id)}`), 400);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message || 'Create failed.' });
    } finally {
      setSaving(false);
    }
  }, [router]);

  return (
    <div className="space-y-6 p-4 sm:p-6">
      <header className="flex items-center gap-2">
        <Link href="/admin/grammar" className="text-muted hover:text-navy" aria-label="Back">
          <ArrowLeft className="h-5 w-5" />
        </Link>
        <h1 className="text-2xl font-bold text-navy">New grammar lesson</h1>
      </header>

      <GrammarLessonEditor
        initial={emptyDraft()}
        topics={topics.map((t) => ({ id: t.id, name: t.name, slug: t.slug }))}
        onSave={onSave}
        saving={saving}
      />

      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}
    </div>
  );
}
