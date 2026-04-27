'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { ArrowLeft, Save, Archive, Rocket, Undo2, Mic } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import {
  AdminRouteHero,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
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
      <AdminRouteWorkspace role="main" aria-label="Edit pronunciation drill">
        <Skeleton className="h-8 w-64" />
        <Skeleton className="h-96 rounded-2xl" />
      </AdminRouteWorkspace>
    );
  }

  if (!drill) {
    return (
      <AdminRouteWorkspace role="main" aria-label="Edit pronunciation drill">
        <Link href="/admin/pronunciation" className="text-sm text-primary hover:underline">← Back</Link>
        <p className="text-muted">Drill not found.</p>
      </AdminRouteWorkspace>
    );
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="Edit pronunciation drill">
      <Link href="/admin/pronunciation" className="inline-flex items-center gap-1 text-sm text-muted hover:text-navy" aria-label="Back to pronunciation drills">
        <ArrowLeft className="h-4 w-4" /> Back to pronunciation drills
      </Link>

      <AdminRouteHero
        eyebrow="CMS"
        icon={Mic}
        accent="navy"
        title={drill.label}
        description={`Phoneme /${drill.targetPhoneme}/${drill.primaryRuleId ? ` • Rule ${drill.primaryRuleId}` : ''} • Status ${drill.status}`}
        aside={(
          <div className="rounded-2xl border border-border bg-background-light p-4 shadow-sm">
            <div className="flex flex-wrap items-center gap-2">
              <Badge variant={drill.status === 'active' ? 'success' : drill.status === 'archived' ? 'muted' : 'warning'}>
                {drill.status}
              </Badge>
              <Button
                variant="secondary"
                onClick={() => doSave()}
                loading={saving}
                className="gap-2"
              >
                <Save className="h-4 w-4" /> Save
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
          </div>
        )}
      />

      <PronunciationDrillForm form={form} onChange={setForm} />

      {toast && (
        <Toast variant={toast.variant === 'error' ? 'error' : 'success'} message={toast.message} onClose={() => setToast(null)} />
      )}
    </AdminRouteWorkspace>
  );
}
