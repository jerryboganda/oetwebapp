'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { ArrowLeft, Save, Archive, Rocket, Undo2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
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
      router.push('/admin/pronunciation');
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Archive failed' });
    }
  }

  if (loading) {
    return (
      <div className="space-y-6 p-4 sm:p-6">
        <Skeleton className="h-8 w-64" />
        <Skeleton className="h-96 rounded-2xl" />
      </div>
    );
  }

  if (!drill) {
    return (
      <div className="space-y-4 p-4 sm:p-6">
        <Link href="/admin/pronunciation" className="text-sm text-primary hover:underline">← Back</Link>
        <p className="text-muted">Drill not found.</p>
      </div>
    );
  }

  return (
    <div className="space-y-6 p-4 sm:p-6">
      <header className="flex flex-wrap items-center gap-3">
        <Link href="/admin/pronunciation" aria-label="Back to pronunciation drills" className="text-muted hover:text-navy dark:hover:text-white">
          <ArrowLeft className="h-5 w-5" />
        </Link>
        <div className="flex-1 min-w-0">
          <h1 className="text-2xl font-bold text-navy dark:text-white truncate">{drill.label}</h1>
          <div className="flex items-center gap-2 text-xs text-muted">
            <span className="font-mono">/{drill.targetPhoneme}/</span>
            {drill.primaryRuleId && <span className="font-mono">{drill.primaryRuleId}</span>}
            <Badge variant={drill.status === 'active' ? 'success' : drill.status === 'archived' ? 'muted' : 'warning'}>
              {drill.status}
            </Badge>
          </div>
        </div>
      </header>

      <PronunciationDrillForm form={form} onChange={setForm} />

      <div className="flex flex-wrap justify-end gap-2">
        <Link href="/admin/pronunciation">
          <Button variant="ghost">Cancel</Button>
        </Link>
        <Button
          variant="secondary"
          onClick={() => doSave()}
          loading={saving}
          className="gap-2"
        >
          <Save className="h-4 w-4" /> Save changes
        </Button>
        {drill.status === 'active' ? (
          <Button variant="secondary" onClick={() => doSave('draft')} loading={saving} className="gap-2">
            <Undo2 className="h-4 w-4" /> Unpublish
          </Button>
        ) : drill.status !== 'archived' ? (
          <Button variant="primary" onClick={() => doSave('active')} loading={saving} className="gap-2">
            <Rocket className="h-4 w-4" /> Publish
          </Button>
        ) : null}
        {drill.status !== 'archived' && (
          <Button variant="destructive" onClick={doArchive} className="gap-2">
            <Archive className="h-4 w-4" /> Archive
          </Button>
        )}
      </div>

      {toast && (
        <Toast variant={toast.variant === 'error' ? 'error' : 'success'} message={toast.message} onClose={() => setToast(null)} />
      )}
    </div>
  );
}
