'use client';

import { useCallback, useEffect, useState } from 'react';
import { Loader2, Save, Sparkles } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { Badge } from '@/components/ui/badge';
import { AdminRoutePanel } from '@/components/domain/admin-route-surface';
import {
  adminListeningGetExtracts,
  adminListeningPatchExtract,
  adminListeningProposeExtraction,
} from '@/lib/api';
import type { ListeningAuthoredExtract } from '@/lib/types/admin/listening-authoring';

interface Props {
  paperId: string;
  onToast: (variant: 'success' | 'error', message: string) => void;
}

const KIND_OPTIONS = [
  { value: 'consultation', label: 'Consultation' },
  { value: 'workplace', label: 'Workplace' },
  { value: 'presentation', label: 'Presentation' },
];

const ACCENT_OPTIONS = [
  { value: 'en-GB', label: 'en-GB' },
  { value: 'en-AU', label: 'en-AU' },
  { value: 'en-IE', label: 'en-IE' },
  { value: 'en-US', label: 'en-US' },
];

export function ExtractsTab({ paperId, onToast }: Props) {
  const [items, setItems] = useState<ListeningAuthoredExtract[]>([]);
  const [loading, setLoading] = useState(true);
  const [savingCode, setSavingCode] = useState<string | null>(null);
  const [proposing, setProposing] = useState(false);
  const [edits, setEdits] = useState<Record<string, Partial<ListeningAuthoredExtract>>>({});

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const data = await adminListeningGetExtracts(paperId);
      setItems(data.extracts ?? []);
      setEdits({});
    } catch (e) {
      onToast('error', `Load extracts failed: ${(e as Error).message}`);
    } finally {
      setLoading(false);
    }
  }, [onToast, paperId]);

  useEffect(() => {
    void load();
  }, [load]);

  const updateEdit = useCallback((code: string, patch: Partial<ListeningAuthoredExtract>) => {
    setEdits((prev) => ({ ...prev, [code]: { ...prev[code], ...patch } }));
  }, []);

  const save = useCallback(
    async (ex: ListeningAuthoredExtract) => {
      const patch = edits[ex.partCode];
      if (!patch) return;
      setSavingCode(ex.partCode);
      try {
        const next = await adminListeningPatchExtract(paperId, ex.partCode, {
          title: patch.title,
          kind: patch.kind,
          accentCode: patch.accentCode ?? undefined,
          displayOrder: patch.displayOrder,
          speakers: patch.speakers,
        });
        setItems(next.extracts ?? []);
        setEdits((prev) => {
          const c = { ...prev };
          delete c[ex.partCode];
          return c;
        });
        onToast('success', `Extract ${ex.partCode} saved.`);
      } catch (e) {
        onToast('error', `Save ${ex.partCode} failed: ${(e as Error).message}`);
      } finally {
        setSavingCode(null);
      }
    },
    [edits, onToast, paperId],
  );

  const propose = useCallback(async () => {
    setProposing(true);
    try {
      const res = await adminListeningProposeExtraction(paperId);
      onToast(
        'success',
        `AI draft created (${res.questions.length} items, status=${res.status}${
          res.isStub ? ', stub' : ''
        }). Review in Extractions tab.`,
      );
    } catch (e) {
      onToast('error', `AI propose failed: ${(e as Error).message}`);
    } finally {
      setProposing(false);
    }
  }, [onToast, paperId]);

  if (loading) {
    return (
      <div className="flex items-center gap-2 text-sm text-muted">
        <Loader2 className="h-4 w-4 animate-spin" /> Loading extracts…
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <AdminRoutePanel title="AI extraction">
        <div className="flex items-center gap-2">
          <Button variant="primary" onClick={() => void propose()} disabled={proposing}>
            {proposing ? <Loader2 className="h-4 w-4 animate-spin" /> : <Sparkles className="h-4 w-4" />}
            Propose structure with AI
          </Button>
          <p className="text-xs text-muted">
            Persists an AI proposal as a Pending draft — review/approve in the Extractions tab.
          </p>
        </div>
      </AdminRoutePanel>

      <AdminRoutePanel title={`Extracts (${items.length}/5)`}>
        <div className="space-y-3">
          {items.length === 0 ? (
            <p className="text-sm text-muted">No extracts authored yet.</p>
          ) : (
            items.map((ex) => {
              const e = edits[ex.partCode] ?? {};
              const title = e.title ?? ex.title;
              const kind = e.kind ?? ex.kind;
              const accent = e.accentCode ?? ex.accentCode ?? '';
              const speakerCount = (e.speakers ?? ex.speakers ?? []).length;
              const durationSec =
                ex.audioStartMs != null && ex.audioEndMs != null
                  ? Math.round((ex.audioEndMs - ex.audioStartMs) / 1000)
                  : null;
              const dirty = !!edits[ex.partCode];
              return (
                <div key={ex.partCode} className="rounded-xl border border-border bg-background-light p-3">
                  <div className="mb-2 flex items-center justify-between gap-2">
                    <div className="flex items-center gap-2 text-sm font-semibold text-navy">
                      <Badge variant="info">{ex.partCode}</Badge>
                      <span className="font-mono text-xs text-muted">
                        #{ex.displayOrder} · speakers: {speakerCount} ·{' '}
                        {durationSec != null ? `${durationSec}s audio` : 'no audio window'}
                      </span>
                    </div>
                    <Button
                      variant="primary"
                      size="sm"
                      disabled={!dirty || savingCode === ex.partCode}
                      onClick={() => void save(ex)}
                    >
                      {savingCode === ex.partCode ? (
                        <Loader2 className="h-3 w-3 animate-spin" />
                      ) : (
                        <Save className="h-3 w-3" />
                      )}
                      Save
                    </Button>
                  </div>
                  <div className="grid gap-2 md:grid-cols-3">
                    <Input
                      label="Title"
                      value={title}
                      onChange={(ev) => updateEdit(ex.partCode, { title: ev.target.value })}
                    />
                    <Select
                      label="Kind"
                      value={kind}
                      onChange={(ev) => updateEdit(ex.partCode, { kind: ev.target.value })}
                      options={KIND_OPTIONS}
                    />
                    <Select
                      label="Accent"
                      value={accent}
                      onChange={(ev) => updateEdit(ex.partCode, { accentCode: ev.target.value })}
                      options={ACCENT_OPTIONS}
                    />
                  </div>
                </div>
              );
            })
          )}
        </div>
      </AdminRoutePanel>
    </div>
  );
}
