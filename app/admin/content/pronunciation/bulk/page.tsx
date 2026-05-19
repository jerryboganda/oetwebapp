'use client';

import { useMemo, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, Mic, Play } from 'lucide-react';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Input, Select } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import {
  adminPronunciationAiDraft,
  createAdminPronunciationDrill,
  updateAdminPronunciationDrill,
} from '@/lib/api';
import { sanitizeBodyHtml } from '@/lib/wizard/sanitize-html';

const PROFESSION_OPTIONS = [
  { value: 'medicine', label: 'Medicine' },
  { value: 'nursing', label: 'Nursing' },
  { value: 'dentistry', label: 'Dentistry' },
  { value: 'pharmacy', label: 'Pharmacy' },
  { value: 'physiotherapy', label: 'Physiotherapy' },
  { value: 'optometry', label: 'Optometry' },
];

const FOCUS_OPTIONS = [
  { value: 'phoneme', label: 'Phoneme' },
  { value: 'cluster', label: 'Cluster' },
  { value: 'stress', label: 'Word stress' },
  { value: 'intonation', label: 'Intonation' },
];

const DIFFICULTY_OPTIONS = [
  { value: 'easy', label: 'Easy' },
  { value: 'medium', label: 'Medium' },
  { value: 'hard', label: 'Hard' },
];

interface DraftResult {
  targetPhoneme: string;
  label: string;
  difficulty: string;
  focus: string;
  exampleWords: string[];
  minimalPairs: Array<{ a: string; b: string }>;
  sentences: string[];
  tipsHtml: string;
  appliedRuleIds: string[];
  primaryRuleId: string | null;
  warning: string | null;
  selfCheckNotes: string | null;
}

type RowResult = {
  phoneme: string;
  status: 'pending' | 'running' | 'ok' | 'error';
  drillId?: string;
  warning?: string | null;
  error?: string;
  finalStatus?: string;
};

type ToastState = { variant: 'success' | 'error'; message: string } | null;

export default function PronunciationBulkPage() {
  const { user } = useCurrentUser();
  const canWrite = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);

  const [raw, setRaw] = useState('θ\nð\nʃ\nʒ');
  const [profession, setProfession] = useState('medicine');
  const [focus, setFocus] = useState('phoneme');
  const [difficulty, setDifficulty] = useState('medium');
  const [activateAfter, setActivateAfter] = useState(false);
  const [running, setRunning] = useState(false);
  const [results, setResults] = useState<RowResult[]>([]);
  const [toast, setToast] = useState<ToastState>(null);

  const phonemes = useMemo(
    () => raw.split('\n').map((s) => s.trim()).filter(Boolean),
    [raw],
  );

  const run = async () => {
    if (!canWrite || running || phonemes.length === 0) return;
    setRunning(true);
    const working: RowResult[] = phonemes.map((p) => ({ phoneme: p, status: 'pending' }));
    setResults(working);
    let ok = 0;
    let failed = 0;
    for (let i = 0; i < working.length; i++) {
      working[i].status = 'running';
      setResults([...working]);
      try {
        const draft = (await adminPronunciationAiDraft({
          phoneme: working[i].phoneme,
          focus,
          profession,
          difficulty,
        })) as DraftResult;
        const created = (await createAdminPronunciationDrill({
          word: draft.label,
          phoneticTranscription: draft.targetPhoneme,
          profession,
          focus: draft.focus,
          primaryRuleId: draft.primaryRuleId,
          difficulty: draft.difficulty,
          tipsHtml: sanitizeBodyHtml(draft.tipsHtml ?? ''),
          exampleWordsJson: JSON.stringify(draft.exampleWords ?? []),
          minimalPairsJson: JSON.stringify(draft.minimalPairs ?? []),
          sentencesJson: JSON.stringify(draft.sentences ?? []),
          status: 'draft',
        })) as { id: string };
        working[i].drillId = created.id;
        working[i].warning = draft.warning;
        if (activateAfter) {
          await updateAdminPronunciationDrill(created.id, { status: 'active' });
          working[i].finalStatus = 'active';
        } else {
          working[i].finalStatus = 'draft';
        }
        working[i].status = 'ok';
        ok++;
      } catch (err) {
        working[i].status = 'error';
        working[i].error = err instanceof Error ? err.message : 'unknown error';
        failed++;
      }
      setResults([...working]);
    }
    setRunning(false);
    setToast({
      variant: failed === 0 ? 'success' : 'error',
      message: `Bulk pronunciation: ${ok} created, ${failed} failed.`,
    });
  };

  return (
    <AdminRouteWorkspace role="main" aria-label="Pronunciation bulk runner">
      <Link
        href="/admin/content/pronunciation"
        className="inline-flex items-center gap-1 text-sm text-muted hover:text-navy"
      >
        <ArrowLeft className="h-4 w-4" /> Back to pronunciation drills
      </Link>

      <AdminRouteHero
        eyebrow="CMS"
        icon={Mic}
        accent="navy"
        title="Bulk-generate pronunciation drills"
        description="Paste one phoneme or focus token per line. Each is sent to the grounded AI gateway and saved as a drill."
      />

      <AdminRoutePanel title="Parameters">
        <div className="grid gap-3 sm:grid-cols-3">
          <Select
            value={profession}
            onChange={(e) => setProfession(e.target.value)}
            label="Profession"
            options={PROFESSION_OPTIONS}
          />
          <Select
            value={focus}
            onChange={(e) => setFocus(e.target.value)}
            label="Focus"
            options={FOCUS_OPTIONS}
          />
          <Select
            value={difficulty}
            onChange={(e) => setDifficulty(e.target.value)}
            label="Difficulty"
            options={DIFFICULTY_OPTIONS}
          />
          <div className="sm:col-span-3">
            <label className="text-sm font-medium">Phonemes (one per line)</label>
            <textarea
              value={raw}
              onChange={(e) => setRaw(e.target.value)}
              rows={5}
              className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 font-mono text-sm focus:outline-none focus:ring-2 focus:ring-primary"
            />
          </div>
          <div className="sm:col-span-3 flex items-center justify-between">
            <label className="inline-flex items-center gap-2 text-sm">
              <input
                type="checkbox"
                checked={activateAfter}
                onChange={(e) => setActivateAfter(e.target.checked)}
              />
              <span>Activate drills after create (status → active)</span>
            </label>
            <div className="text-xs text-muted">{phonemes.length} item(s) ready</div>
          </div>
        </div>
        <div className="mt-3 flex justify-end">
          <Button
            onClick={() => void run()}
            disabled={!canWrite || running || phonemes.length === 0}
            className="inline-flex items-center gap-2"
          >
            <Play className="h-4 w-4" /> {running ? 'Running…' : 'Generate batch'}
          </Button>
        </div>
      </AdminRoutePanel>

      {results.length > 0 && (
        <AdminRoutePanel title="Results">
          <Card className="overflow-hidden">
            <table className="min-w-full text-sm">
              <thead className="bg-background-light text-left text-xs uppercase tracking-[0.15em] text-muted">
                <tr>
                  <th className="px-4 py-2">#</th>
                  <th className="px-4 py-2">Phoneme</th>
                  <th className="px-4 py-2">Status</th>
                  <th className="px-4 py-2">Final</th>
                  <th className="px-4 py-2">Detail</th>
                </tr>
              </thead>
              <tbody>
                {results.map((r, i) => (
                  <tr key={i} className="border-t border-border">
                    <td className="px-4 py-2 text-xs text-muted">{i + 1}</td>
                    <td className="px-4 py-2 font-mono text-sm">{r.phoneme}</td>
                    <td className="px-4 py-2">
                      <Badge
                        variant={
                          r.status === 'ok' ? 'success'
                            : r.status === 'error' ? 'danger'
                            : r.status === 'running' ? 'warning'
                            : 'muted'
                        }
                      >
                        {r.status}
                      </Badge>
                    </td>
                    <td className="px-4 py-2 text-xs">{r.finalStatus ?? '—'}</td>
                    <td className="px-4 py-2 text-xs">
                      {r.drillId && (
                        <Link
                          href={`/admin/content/pronunciation/${r.drillId}`}
                          className="text-primary hover:underline"
                        >
                          Open drill
                        </Link>
                      )}
                      {r.warning && <div className="text-warning">{r.warning}</div>}
                      {r.error && <div className="text-danger">{r.error}</div>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </Card>
        </AdminRoutePanel>
      )}

      {toast && (
        <Toast
          variant={toast.variant === 'error' ? 'error' : 'success'}
          message={toast.message}
          onClose={() => setToast(null)}
        />
      )}
    </AdminRouteWorkspace>
  );
}
