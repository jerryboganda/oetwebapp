'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { Save, Archive, Rocket, Undo2 } from 'lucide-react';
import { Button } from '@/components/admin/ui/button';
import { Badge, statusToTone } from '@/components/admin/ui/badge';
import { Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { Card, CardContent } from '@/components/admin/ui/card';
import { AdminSettingsLayout } from '@/components/admin/layout/admin-settings-layout';
import {
  fetchAdminPronunciationDrill,
  updateAdminPronunciationDrill,
  archiveAdminPronunciationDrill,
} from '@/lib/api';
import {
  PronunciationDrillForm,
  type DrillForm,
} from '@/components/domain/pronunciation/admin-pronunciation-drill-form';

type FullDrill = {
  id: string;
  label: string;
  targetPhoneme: string;
  profession: string;
  focus: string;
  primaryRuleId: string | null;
  audioModelUrl: string | null;
  exampleWordsJson: string;
  minimalPairsJson: string;
  sentencesJson: string;
  tipsHtml: string;
  difficulty: string;
  status: string;
};

const BASE_BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Content', href: '/admin/content' },
  { label: 'Pronunciation', href: '/admin/content/pronunciation' },
];

export default function EditPronunciationDrillPage() {
  const params = useParams<{ drillId: string }>();
  const drillId = params?.drillId ?? '';
  const router = useRouter();
  const [loading, setLoading] = useState(true);
  const [drill, setDrill] = useState<FullDrill | null>(null);
  const [form, setForm] = useState<DrillForm>({
    word: '',
    phoneticTranscription: '',
    profession: 'all',
    focus: 'phoneme',
    primaryRuleId: '',
    difficulty: 'medium',
    tipsHtml: '',
    exampleWordsJson: '[]',
    minimalPairsJson: '[]',
    sentencesJson: '[]',
    audioUrl: '',
  });
  const [saving, setSaving] = useState(false);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const load = useCallback(async () => {
    if (!drillId) return;
    setLoading(true);
    try {
      const d = (await fetchAdminPronunciationDrill(drillId)) as FullDrill;
      setDrill(d);
      setForm({
        word: d.label,
        phoneticTranscription: d.targetPhoneme,
        profession: d.profession,
        focus: d.focus,
        primaryRuleId: d.primaryRuleId ?? '',
        difficulty: d.difficulty,
        tipsHtml: d.tipsHtml,
        exampleWordsJson: d.exampleWordsJson,
        minimalPairsJson: d.minimalPairsJson,
        sentencesJson: d.sentencesJson,
        audioUrl: d.audioModelUrl ?? '',
      });
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Failed to load drill' });
    } finally {
      setLoading(false);
    }
  }, [drillId]);

  useEffect(() => { void load(); }, [load]);

  async function doSave(desiredStatus?: string) {
    if (!drill) return;
    setSaving(true);
    try {
      await updateAdminPronunciationDrill(drill.id, {
        ...form,
        status: desiredStatus ?? drill.status,
      });
      setToast({ variant: 'success', message: desiredStatus ? `Drill ${desiredStatus}` : 'Drill saved' });
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Save failed' });
    } finally {
      setSaving(false);
    }
  }

  async function doArchive() {
    if (!drill) return;
    if (!confirm('Archive this drill?')) return;
    try {
      await archiveAdminPronunciationDrill(drill.id);
      router.push('/admin/content/pronunciation');
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Archive failed' });
    }
  }

  if (loading) {
    return (
      <AdminSettingsLayout
        title="Loading drill…"
        eyebrow="CMS"
        breadcrumbs={[...BASE_BREADCRUMBS, { label: 'Edit drill' }]}
      >
        <Skeleton className="h-8 w-64 rounded-admin" />
        <Skeleton className="h-96 rounded-admin" />
      </AdminSettingsLayout>
    );
  }

  if (!drill) {
    return (
      <AdminSettingsLayout
        title="Drill not found"
        eyebrow="CMS"
        breadcrumbs={[...BASE_BREADCRUMBS, { label: 'Edit drill' }]}
        actions={
          <Button variant="outline" size="sm" asChild>
            <Link href="/admin/content/pronunciation">Back to drills</Link>
          </Button>
        }
      >
        <Card>
          <CardContent className="py-8 text-sm text-admin-fg-muted">
            This drill could not be loaded. It may have been removed.
          </CardContent>
        </Card>
      </AdminSettingsLayout>
    );
  }

  return (
    <>
      <AdminSettingsLayout
        title={drill.label}
        description={`Phoneme /${drill.targetPhoneme}/${drill.primaryRuleId ? ` • Rule ${drill.primaryRuleId}` : ''} • Status ${drill.status}`}
        eyebrow="CMS"
        breadcrumbs={[...BASE_BREADCRUMBS, { label: drill.label }]}
        actions={
          <div className="flex flex-wrap items-center gap-2">
            <Badge variant={statusToTone(drill.status)}>
              {drill.status}
            </Badge>
            <Button
              variant="secondary"
              size="sm"
              onClick={() => doSave()}
              disabled={saving}
            >
              <Save className="mr-2 h-4 w-4" /> Save
            </Button>
            {drill.status === 'active' ? (
              <Button variant="secondary" size="sm" onClick={() => doSave('draft')} disabled={saving}>
                <Undo2 className="mr-2 h-4 w-4" /> Unpublish
              </Button>
            ) : drill.status !== 'archived' ? (
              <Button size="sm" onClick={() => doSave('active')} disabled={saving}>
                <Rocket className="mr-2 h-4 w-4" /> Publish
              </Button>
            ) : null}
            {drill.status !== 'archived' && (
              <Button variant="destructive" size="sm" onClick={doArchive}>
                <Archive className="mr-2 h-4 w-4" /> Archive
              </Button>
            )}
            <Button variant="outline" size="sm" asChild>
              <Link href="/admin/content/pronunciation">Back</Link>
            </Button>
          </div>
        }
      >
        <PronunciationDrillForm form={form} onChange={setForm} />
      </AdminSettingsLayout>

      {toast && (
        <Toast variant={toast.variant === 'error' ? 'error' : 'success'} message={toast.message} onClose={() => setToast(null)} />
      )}
    </>
  );
}
