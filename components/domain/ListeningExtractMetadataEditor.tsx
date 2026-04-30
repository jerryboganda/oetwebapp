'use client';

import { useCallback, useEffect, useState } from 'react';
import { Database, Loader2, Plus, Save, Trash2, Wand2 } from 'lucide-react';
import { AdminRoutePanel } from '@/components/domain/admin-route-surface';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import {
  backfillListeningPaper,
  getListeningExtracts,
  replaceListeningExtracts,
  type ListeningAuthoredExtract,
  type ListeningAuthoredSpeaker,
  type ListeningExtractKind,
  type ListeningPartCode,
} from '@/lib/listening-authoring-api';

type LoadStatus = 'loading' | 'ready' | 'error';

const PART_OPTIONS: { value: ListeningPartCode; label: string }[] = [
  { value: 'A1', label: 'A1 - Consultation 1' },
  { value: 'A2', label: 'A2 - Consultation 2' },
  { value: 'B', label: 'B - Workplace extracts' },
  { value: 'C1', label: 'C1 - Presentation 1' },
  { value: 'C2', label: 'C2 - Presentation 2' },
];

const KIND_OPTIONS: { value: ListeningExtractKind; label: string }[] = [
  { value: 'consultation', label: 'Consultation' },
  { value: 'workplace', label: 'Workplace' },
  { value: 'presentation', label: 'Presentation' },
];

const GENDER_OPTIONS = [
  { value: '', label: 'Not set' },
  { value: 'f', label: 'Female' },
  { value: 'm', label: 'Male' },
  { value: 'nb', label: 'Non-binary' },
];

const defaultKindForPart = (partCode: ListeningPartCode): ListeningExtractKind => {
  if (partCode === 'B') return 'workplace';
  if (partCode === 'C1' || partCode === 'C2') return 'presentation';
  return 'consultation';
};

const defaultTitleForPart = (partCode: ListeningPartCode) => {
  if (partCode === 'A1') return 'Consultation 1';
  if (partCode === 'A2') return 'Consultation 2';
  if (partCode === 'B') return 'Workplace extract';
  if (partCode === 'C1') return 'Presentation 1';
  return 'Presentation 2';
};

const buildDefaultExtracts = (): ListeningAuthoredExtract[] =>
  PART_OPTIONS.map((part, index) => ({
    partCode: part.value,
    displayOrder: index,
    kind: defaultKindForPart(part.value),
    title: defaultTitleForPart(part.value),
    accentCode: null,
    speakers: [],
    audioStartMs: null,
    audioEndMs: null,
  }));

const normalizeNumber = (value: string): number | null => {
  if (value.trim() === '') return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) && parsed >= 0 ? Math.round(parsed) : null;
};

export function ListeningExtractMetadataEditor({ paperId }: { paperId: string }) {
  const [status, setStatus] = useState<LoadStatus>('loading');
  const [extracts, setExtracts] = useState<ListeningAuthoredExtract[]>([]);
  const [saving, setSaving] = useState(false);
  const [backfilling, setBackfilling] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const load = useCallback(async () => {
    setStatus('loading');
    try {
      const data = await getListeningExtracts(paperId);
      if (data.extracts.length > 0) {
        setExtracts(data.extracts);
        setDirty(false);
      } else {
        setExtracts(buildDefaultExtracts());
        setDirty(true);
      }
      setStatus('ready');
    } catch (error) {
      setStatus('error');
      setToast({ variant: 'error', message: `Failed to load extract metadata: ${(error as Error).message}` });
    }
  }, [paperId]);

  useEffect(() => { void load(); }, [load]);

  const updateExtract = (index: number, patch: Partial<ListeningAuthoredExtract>) => {
    setExtracts((prev) => prev.map((extract, i) => (i === index ? { ...extract, ...patch } : extract)));
    setDirty(true);
  };

  const updateSpeaker = (extractIndex: number, speakerIndex: number, patch: Partial<ListeningAuthoredSpeaker>) => {
    setExtracts((prev) => prev.map((extract, i) => {
      if (i !== extractIndex) return extract;
      return {
        ...extract,
        speakers: extract.speakers.map((speaker, j) => (j === speakerIndex ? { ...speaker, ...patch } : speaker)),
      };
    }));
    setDirty(true);
  };

  const addSpeaker = (extractIndex: number) => {
    setExtracts((prev) => prev.map((extract, i) => {
      if (i !== extractIndex) return extract;
      const nextNumber = extract.speakers.length + 1;
      return {
        ...extract,
        speakers: [...extract.speakers, { id: `s${nextNumber}`, role: 'speaker', gender: null, accent: null }],
      };
    }));
    setDirty(true);
  };

  const removeSpeaker = (extractIndex: number, speakerIndex: number) => {
    setExtracts((prev) => prev.map((extract, i) => (
      i === extractIndex
        ? { ...extract, speakers: extract.speakers.filter((_, j) => j !== speakerIndex) }
        : extract
    )));
    setDirty(true);
  };

  const addExtract = () => {
    const partCode = PART_OPTIONS.find((part) => !extracts.some((extract) => extract.partCode === part.value))?.value;
    if (!partCode) return;
    setExtracts((prev) => [
      ...prev,
      {
        partCode,
        displayOrder: prev.length,
        kind: defaultKindForPart(partCode),
        title: defaultTitleForPart(partCode),
        accentCode: null,
        speakers: [],
        audioStartMs: null,
        audioEndMs: null,
      },
    ]);
    setDirty(true);
  };

  const scaffoldExtracts = () => {
    setExtracts(buildDefaultExtracts());
    setDirty(true);
  };

  const removeExtract = (index: number) => {
    setExtracts((prev) => prev.filter((_, i) => i !== index).map((extract, displayOrder) => ({ ...extract, displayOrder })));
    setDirty(true);
  };

  const save = async () => {
    setSaving(true);
    try {
      const normalized = extracts.map((extract, displayOrder) => ({ ...extract, displayOrder }));
      const data = await replaceListeningExtracts(paperId, normalized);
      setExtracts(data.extracts);
      setDirty(false);
      setToast({ variant: 'success', message: 'Listening extract metadata saved.' });
    } catch (error) {
      setToast({ variant: 'error', message: `Save failed: ${(error as Error).message}` });
    } finally {
      setSaving(false);
    }
  };

  const partCounts = extracts.reduce<Record<string, number>>((counts, extract) => {
    counts[extract.partCode] = (counts[extract.partCode] ?? 0) + 1;
    return counts;
  }, {});
  const hasMissingPart = PART_OPTIONS.some((part) => !extracts.some((extract) => extract.partCode === part.value));
  const hasDuplicateParts = Object.values(partCounts).some((count) => count > 1);
  const canBackfill = status === 'ready' && !saving && !backfilling && !dirty && !hasDuplicateParts;
  const partOptionsForIndex = (index: number) => PART_OPTIONS.map((part) => ({
    ...part,
    disabled: extracts.some((extract, extractIndex) => extractIndex !== index && extract.partCode === part.value),
  }));

  const backfill = async () => {
    if (status !== 'ready' || dirty || hasDuplicateParts) {
      setToast({ variant: 'error', message: 'Save valid extract metadata before running relational backfill.' });
      return;
    }
    setBackfilling(true);
    try {
      const report = await backfillListeningPaper(paperId);
      setToast({
        variant: report.success ? 'success' : 'error',
        message: report.success
          ? `Backfill complete: ${report.questionsCreated} questions, ${report.extractsCreated} extracts.`
          : report.reason,
      });
    } catch (error) {
      setToast({ variant: 'error', message: `Backfill failed: ${(error as Error).message}` });
    } finally {
      setBackfilling(false);
    }
  };

  return (
    <AdminRoutePanel
      title="Listening extract metadata"
      description="Author accent, speaker, and audio-window metadata for the learner player and transcript-backed review."
      actions={(
        <>
          <Button variant="outline" onClick={scaffoldExtracts} disabled={saving || backfilling} className="gap-2">
            <Wand2 className="h-4 w-4" /> Scaffold
          </Button>
          <Button variant="outline" onClick={backfill} disabled={!canBackfill} className="gap-2">
            {backfilling ? <Loader2 className="h-4 w-4 animate-spin" /> : <Database className="h-4 w-4" />} Backfill
          </Button>
          <Button onClick={save} disabled={saving || backfilling || !dirty || hasDuplicateParts} className="gap-2">
            {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />} Save
          </Button>
        </>
      )}
    >
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      {status === 'loading' ? (
        <div className="flex items-center gap-3 rounded-lg border border-border bg-background-light px-4 py-3 text-sm text-muted">
          <Loader2 className="h-4 w-4 animate-spin" /> Loading extract metadata...
        </div>
      ) : status === 'error' ? (
        <InlineAlert variant="error" title="Extract metadata unavailable">Reload this page or check the admin API response.</InlineAlert>
      ) : (
        <div className="space-y-4">
          {hasDuplicateParts ? (
            <InlineAlert variant="warning" title="Duplicate parts">Each extract metadata row must target a different Listening part before it can be saved or backfilled.</InlineAlert>
          ) : null}
          <div className="flex items-center justify-between gap-3">
            <p className="text-sm text-muted">{extracts.length} authored extract{extracts.length === 1 ? '' : 's'}</p>
            <Button variant="outline" size="sm" onClick={addExtract} disabled={!hasMissingPart} className="gap-2">
              <Plus className="h-4 w-4" /> Extract
            </Button>
          </div>

          {extracts.map((extract, index) => (
            <div key={`${extract.partCode}-${index}`} className="space-y-4 rounded-lg border border-border bg-surface p-4">
              <div className="grid gap-3 md:grid-cols-5">
                <Select
                  label="Part"
                  value={extract.partCode}
                  options={partOptionsForIndex(index)}
                  onChange={(event) => updateExtract(index, {
                    partCode: event.target.value as ListeningPartCode,
                    kind: defaultKindForPart(event.target.value as ListeningPartCode),
                  })}
                />
                <Select
                  label="Kind"
                  value={extract.kind}
                  options={KIND_OPTIONS}
                  onChange={(event) => updateExtract(index, { kind: event.target.value as ListeningExtractKind })}
                />
                <Input
                  label="Title"
                  value={extract.title}
                  onChange={(event) => updateExtract(index, { title: event.target.value })}
                />
                <Input
                  label="Accent code"
                  value={extract.accentCode ?? ''}
                  placeholder="en-GB"
                  onChange={(event) => updateExtract(index, { accentCode: event.target.value.trim() || null })}
                />
                <div className="flex items-end justify-end">
                  <Button variant="ghost" size="sm" onClick={() => removeExtract(index)} className="gap-2 text-danger hover:text-danger">
                    <Trash2 className="h-4 w-4" /> Remove
                  </Button>
                </div>
              </div>

              <div className="grid gap-3 md:grid-cols-2">
                <Input
                  label="Audio start ms"
                  inputMode="numeric"
                  value={extract.audioStartMs ?? ''}
                  onChange={(event) => updateExtract(index, { audioStartMs: normalizeNumber(event.target.value) })}
                />
                <Input
                  label="Audio end ms"
                  inputMode="numeric"
                  value={extract.audioEndMs ?? ''}
                  onChange={(event) => updateExtract(index, { audioEndMs: normalizeNumber(event.target.value) })}
                />
              </div>

              <div className="space-y-3">
                <div className="flex items-center justify-between gap-3">
                  <h3 className="text-sm font-semibold text-navy">Speakers</h3>
                  <Button variant="outline" size="sm" onClick={() => addSpeaker(index)} className="gap-2">
                    <Plus className="h-4 w-4" /> Speaker
                  </Button>
                </div>
                {extract.speakers.length === 0 ? (
                  <p className="rounded-lg border border-dashed border-border bg-background-light px-4 py-3 text-sm text-muted">No speakers authored for this extract.</p>
                ) : null}
                {extract.speakers.map((speaker, speakerIndex) => (
                  <div key={`${speaker.id}-${speakerIndex}`} className="grid gap-3 rounded-lg border border-border bg-background-light p-3 md:grid-cols-5">
                    <Input
                      label="ID"
                      value={speaker.id}
                      onChange={(event) => updateSpeaker(index, speakerIndex, { id: event.target.value })}
                    />
                    <Input
                      label="Role"
                      value={speaker.role}
                      onChange={(event) => updateSpeaker(index, speakerIndex, { role: event.target.value })}
                    />
                    <Select
                      label="Gender"
                      value={speaker.gender ?? ''}
                      options={GENDER_OPTIONS}
                      onChange={(event) => updateSpeaker(index, speakerIndex, { gender: (event.target.value || null) as ListeningAuthoredSpeaker['gender'] })}
                    />
                    <Input
                      label="Accent"
                      value={speaker.accent ?? ''}
                      placeholder={extract.accentCode ?? 'en-GB'}
                      onChange={(event) => updateSpeaker(index, speakerIndex, { accent: event.target.value.trim() || null })}
                    />
                    <div className="flex items-end justify-end">
                      <Button variant="ghost" size="sm" onClick={() => removeSpeaker(index, speakerIndex)} className="text-danger hover:text-danger" aria-label="Remove speaker">
                        <Trash2 className="h-4 w-4" />
                      </Button>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          ))}
        </div>
      )}
    </AdminRoutePanel>
  );
}