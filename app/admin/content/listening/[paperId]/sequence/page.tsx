'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import {
  AlertTriangle,
  ArrowDown,
  ArrowLeft,
  ArrowUp,
  CheckCircle2,
  ListOrdered,
  Plus,
  RotateCcw,
  Save,
  Trash2,
} from 'lucide-react';
import { AdminSettingsLayout } from '@/components/admin/layout/admin-settings-layout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  deriveListeningSequence,
  getListeningSequence,
  replaceListeningSequence,
  validateListeningSequence,
  type ListeningPartCode,
  type ListeningSequence,
  type ListeningSequenceItem,
  type ListeningSequenceItemType,
  type ListeningSequenceValidationReport,
} from '@/lib/listening-authoring-api';

type LoadState = 'loading' | 'ready' | 'error';
type SaveState = 'idle' | 'saving' | 'saved' | 'error';

const TYPE_OPTIONS: { value: ListeningSequenceItemType; label: string }[] = [
  { value: 'instruction', label: 'Instruction' },
  { value: 'reading_time', label: 'Reading time' },
  { value: 'beep', label: 'Beep' },
  { value: 'audio_extract', label: 'Audio extract' },
  { value: 'local_check_time', label: 'Local check time' },
  { value: 'global_check_time', label: 'Global check time' },
  { value: 'section_transition', label: 'Section transition' },
  { value: 'auto_submit', label: 'Auto-submit' },
];

const PART_OPTIONS: { value: string; label: string }[] = [
  { value: '', label: '—' },
  { value: 'A1', label: 'A1' },
  { value: 'A2', label: 'A2' },
  { value: 'B', label: 'B' },
  { value: 'C1', label: 'C1' },
  { value: 'C2', label: 'C2' },
];

function firstParam(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

function reindex(items: ListeningSequenceItem[]): ListeningSequenceItem[] {
  return items.map((item, index) => ({ ...item, index }));
}

function blankItem(index: number): ListeningSequenceItem {
  return {
    index,
    type: 'instruction',
    partCode: null,
    extractDisplayOrder: null,
    durationMs: 0,
    label: null,
  };
}

/**
 * WS4 — admin sequence builder. Lets an author reorder, retime, insert and
 * remove the FSM phases of a paper's explicit exam-sequence, reset to the
 * policy-derived canonical sequence, and see live 42-question coverage plus
 * validation errors before saving. Papers with no authored sequence fall back
 * to the canonical timing at runtime, so the editor seeds from the derived
 * shape when none exists yet.
 */
export default function AdminListeningSequencePage() {
  const params = useParams<{ paperId?: string | string[] }>();
  const paperId = firstParam(params?.paperId);
  const { isAuthenticated, role } = useAdminAuth();

  const [load, setLoad] = useState<LoadState>('loading');
  const [save, setSave] = useState<SaveState>('idle');
  const [error, setError] = useState<string | null>(null);
  const [items, setItems] = useState<ListeningSequenceItem[]>([]);
  const [version, setVersion] = useState(1);
  const [isAuthored, setIsAuthored] = useState(false);
  const [report, setReport] = useState<ListeningSequenceValidationReport | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const refresh = useCallback(async () => {
    if (!paperId) return;
    setLoad('loading');
    setError(null);
    try {
      const state = await getListeningSequence(paperId);
      if (state.sequence && state.sequence.items.length > 0) {
        setItems(reindex(state.sequence.items));
        setVersion(state.sequence.version ?? 1);
        setIsAuthored(true);
      } else {
        // No authored sequence yet — seed the editor from the canonical
        // sequence the runtime would otherwise derive from policy.
        const derived = await deriveListeningSequence(paperId);
        setItems(reindex(derived.sequence.items));
        setVersion(derived.sequence.version ?? 1);
        setIsAuthored(false);
      }
      setLoad('ready');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not load the exam-sequence.');
      setLoad('error');
    }
  }, [paperId]);

  useEffect(() => {
    if (!isAuthenticated || role !== 'admin') return;
    void refresh();
  }, [isAuthenticated, role, refresh]);

  // Live validation — debounced against the current draft.
  useEffect(() => {
    if (!paperId || load !== 'ready') return;
    const sequence: ListeningSequence = { items: reindex(items), version };
    const handle = setTimeout(() => {
      validateListeningSequence(paperId, sequence)
        .then(setReport)
        .catch(() => setReport(null));
    }, 250);
    return () => clearTimeout(handle);
  }, [paperId, load, items, version]);

  const counts = report?.counts;

  const setItemField = useCallback(
    <K extends keyof ListeningSequenceItem>(index: number, key: K, value: ListeningSequenceItem[K]) => {
      setItems((current) => current.map((item, i) => (i === index ? { ...item, [key]: value } : item)));
    },
    [],
  );

  const move = useCallback((index: number, delta: number) => {
    setItems((current) => {
      const target = index + delta;
      if (target < 0 || target >= current.length) return current;
      const next = [...current];
      const [moved] = next.splice(index, 1);
      next.splice(target, 0, moved);
      return reindex(next);
    });
  }, []);

  const insertAfter = useCallback((index: number) => {
    setItems((current) => {
      const next = [...current];
      next.splice(index + 1, 0, blankItem(index + 1));
      return reindex(next);
    });
  }, []);

  const remove = useCallback((index: number) => {
    setItems((current) => reindex(current.filter((_, i) => i !== index)));
  }, []);

  const resetToCanonical = useCallback(async () => {
    if (!paperId) return;
    try {
      const derived = await deriveListeningSequence(paperId);
      setItems(reindex(derived.sequence.items));
      setVersion(derived.sequence.version ?? 1);
      setToast({ variant: 'success', message: 'Reset to the canonical policy-derived sequence.' });
    } catch (e) {
      setToast({ variant: 'error', message: e instanceof Error ? e.message : 'Could not derive the canonical sequence.' });
    }
  }, [paperId]);

  const onSave = useCallback(async () => {
    if (!paperId) return;
    setSave('saving');
    setError(null);
    try {
      const result = await replaceListeningSequence(paperId, { items: reindex(items), version });
      if (result.sequence) {
        setItems(reindex(result.sequence.items));
        setVersion(result.sequence.version ?? 1);
      }
      setReport(result.report);
      setIsAuthored(true);
      setSave('saved');
      setToast({ variant: 'success', message: 'Exam-sequence saved.' });
    } catch (e) {
      setSave('error');
      const msg = e instanceof Error ? e.message : 'Could not save the exam-sequence.';
      setError(msg);
      setToast({ variant: 'error', message: msg });
    }
  }, [paperId, items, version]);

  const errors = useMemo(
    () => report?.issues.filter((i) => i.severity === 'error') ?? [],
    [report],
  );
  const canSave = load === 'ready' && (report?.isValid ?? false) && save !== 'saving';

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Content', href: '/admin/content' },
    { label: 'Listening', href: '/admin/content/listening' },
    { label: 'Sequence' },
  ];

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminSettingsLayout title="Listening: sequence" breadcrumbs={breadcrumbs}>
        <Card><CardContent className="p-6"><p className="text-sm text-admin-fg-muted">Admin access required.</p></CardContent></Card>
      </AdminSettingsLayout>
    );
  }

  return (
    <AdminSettingsLayout
      eyebrow="Authoring"
      icon={<ListOrdered className="h-5 w-5" />}
      title="Listening: sequence"
      description={`Paper ${paperId ?? ''}. Optional explicit exam-sequence. When unset, the runtime derives the canonical timing from policy — editing here makes the order and per-phase durations explicit.`}
      breadcrumbs={breadcrumbs}
      actions={
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="ghost" size="sm" asChild>
            <Link href={`/admin/content/listening/${paperId}/structure`}>
              <ArrowLeft className="h-4 w-4 mr-1.5" />
              Back to structure
            </Link>
          </Button>
          <Button variant="outline" size="sm" onClick={resetToCanonical} disabled={load !== 'ready'}>
            <RotateCcw className="h-4 w-4 mr-1.5" />
            Reset to canonical
          </Button>
          <Button variant="primary" size="sm" onClick={onSave} disabled={!canSave} loading={save === 'saving'} loadingText="Saving…">
            <Save className="h-4 w-4 mr-1.5" />
            Save sequence
          </Button>
        </div>
      }
    >
      {load === 'loading' && <Skeleton className="h-96 rounded-admin" />}
      {load === 'error' && error && <InlineAlert variant="error">{error}</InlineAlert>}

      {load === 'ready' && (
        <div className="space-y-6">
          <Card>
            <CardHeader><CardTitle>Coverage + validation</CardTitle></CardHeader>
            <CardContent>
              <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
                <KpiTile label="Part A" value={`${counts?.partACount ?? 0} / 24`} tone={counts?.partACount === 24 ? 'success' : 'warning'} />
                <KpiTile label="Part B" value={`${counts?.partBCount ?? 0} / 6`} tone={counts?.partBCount === 6 ? 'success' : 'warning'} />
                <KpiTile label="Part C" value={`${counts?.partCCount ?? 0} / 12`} tone={counts?.partCCount === 12 ? 'success' : 'warning'} />
                <KpiTile label="Total" value={`${counts?.totalItems ?? 0} / 42`} tone={counts?.totalItems === 42 ? 'success' : 'warning'} />
              </div>

              <div className="mt-4">
                {report?.isValid ? (
                  <InlineAlert variant="success">
                    <span className="inline-flex items-center gap-1.5">
                      <CheckCircle2 className="h-4 w-4" aria-hidden="true" />
                      Sequence is valid: 1:1 with the canonical OET phases, every audio extract resolves, and coverage is 42 items.
                    </span>
                  </InlineAlert>
                ) : (
                  <InlineAlert variant="error">
                    <div className="flex items-start gap-1.5">
                      <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
                      <div>
                        <p className="font-semibold">{errors.length} validation error(s). Fix before saving.</p>
                        <ul className="mt-1 list-disc space-y-0.5 pl-4">
                          {errors.slice(0, 8).map((issue) => (
                            <li key={issue.code + issue.message} className="text-sm">{issue.message}</li>
                          ))}
                        </ul>
                      </div>
                    </div>
                  </InlineAlert>
                )}
              </div>

              {!isAuthored && (
                <p className="mt-3 text-xs text-admin-fg-muted">
                  This paper has no authored sequence yet — the phases below are the canonical sequence derived from policy. Saving makes them explicit.
                </p>
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader><CardTitle>Phases ({items.length})</CardTitle></CardHeader>
            <CardContent className="p-0">
              <ul className="divide-y divide-admin-border">
                {items.map((item, index) => (
                  <li key={index} className="grid items-end gap-3 p-4 md:grid-cols-[2.5rem_1fr_8rem_8rem_1fr_auto]">
                    <span className="pb-2 font-mono text-sm text-admin-fg-muted">{index + 1}</span>

                    <Select
                      label="Phase type"
                      value={item.type}
                      onChange={(e) => setItemField(index, 'type', e.target.value as ListeningSequenceItemType)}
                      options={TYPE_OPTIONS}
                    />

                    <Select
                      label="Part"
                      value={item.partCode ?? ''}
                      onChange={(e) => setItemField(index, 'partCode', (e.target.value || null) as ListeningPartCode | null)}
                      options={PART_OPTIONS}
                    />

                    <Input
                      label="Duration (ms)"
                      type="number"
                      min={0}
                      value={item.durationMs ?? 0}
                      onChange={(e) => {
                        const raw = e.target.value;
                        setItemField(index, 'durationMs', raw === '' ? 0 : Number(raw));
                      }}
                    />

                    <Input
                      label="Label"
                      value={item.label ?? ''}
                      onChange={(e) => setItemField(index, 'label', e.target.value || null)}
                      placeholder="a1_preview"
                    />

                    <div className="flex items-center gap-1 pb-1">
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        aria-label={`Move phase ${index + 1} up`}
                        onClick={() => move(index, -1)}
                        disabled={index === 0}
                      >
                        <ArrowUp className="h-4 w-4" aria-hidden="true" />
                      </Button>
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        aria-label={`Move phase ${index + 1} down`}
                        onClick={() => move(index, 1)}
                        disabled={index === items.length - 1}
                      >
                        <ArrowDown className="h-4 w-4" aria-hidden="true" />
                      </Button>
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        aria-label={`Insert phase after ${index + 1}`}
                        onClick={() => insertAfter(index)}
                      >
                        <Plus className="h-4 w-4" aria-hidden="true" />
                      </Button>
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        aria-label={`Remove phase ${index + 1}`}
                        onClick={() => remove(index)}
                      >
                        <Trash2 className="h-4 w-4" aria-hidden="true" />
                      </Button>
                    </div>
                  </li>
                ))}
              </ul>
              {items.length === 0 && (
                <div className="p-6">
                  <Button type="button" variant="outline" onClick={() => insertAfter(-1)}>
                    <Plus className="h-4 w-4 mr-1.5" />
                    Add first phase
                  </Button>
                </div>
              )}
            </CardContent>
          </Card>

          <div className="flex items-center justify-end gap-2">
            <Badge variant={report?.isValid ? 'success' : 'default'}>
              {report?.isValid ? 'Valid' : 'Invalid'}
            </Badge>
            <Button variant="primary" onClick={onSave} disabled={!canSave} loading={save === 'saving'} loadingText="Saving…">
              <Save className="h-4 w-4 mr-1.5" />
              Save sequence
            </Button>
          </div>
        </div>
      )}

      {toast && (
        <Toast
          variant={toast.variant}
          message={toast.message}
          onClose={() => setToast(null)}
        />
      )}
    </AdminSettingsLayout>
  );
}
