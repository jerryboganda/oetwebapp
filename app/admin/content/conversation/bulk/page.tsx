'use client';

import { useMemo, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, MessagesSquare, Play } from 'lucide-react';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Input } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import {
  adminConversationAiDraft,
  createAdminConversationTemplate,
  publishAdminConversationTemplate,
} from '@/lib/api';
import { buildConversationCreatePayload } from '../ai-draft/page';

const PROFESSIONS = [
  'medicine',
  'nursing',
  'dentistry',
  'pharmacy',
  'physiotherapy',
  'optometry',
  'radiography',
  'occupational-therapy',
  'speech-pathology',
  'podiatry',
  'dietetics',
  'veterinary',
];

const TASK_TYPES = ['oet-roleplay', 'oet-handover'] as const;
type TaskType = (typeof TASK_TYPES)[number];

type RowResult = {
  profession: string;
  taskType: TaskType;
  status: 'pending' | 'running' | 'ok' | 'error';
  templateId?: string;
  warning?: string | null;
  error?: string;
};

type ToastState = { variant: 'success' | 'error'; message: string } | null;

export default function ConversationBulkPage() {
  const { user } = useCurrentUser();
  const canWrite = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);
  const canPublish = hasPermission(user?.adminPermissions, AdminPermission.ContentPublish);

  const [selectedProfessions, setSelectedProfessions] = useState<Set<string>>(
    new Set(['medicine', 'nursing']),
  );
  const [selectedTaskTypes, setSelectedTaskTypes] = useState<Set<TaskType>>(
    new Set(['oet-roleplay']),
  );
  const [countPerCell, setCountPerCell] = useState(1);
  const [autoPublish, setAutoPublish] = useState(false);
  const [running, setRunning] = useState(false);
  const [results, setResults] = useState<RowResult[]>([]);
  const [toast, setToast] = useState<ToastState>(null);

  const totalCells = selectedProfessions.size * selectedTaskTypes.size * countPerCell;

  const toggleProf = (p: string) => setSelectedProfessions((prev) => {
    const next = new Set(prev);
    if (next.has(p)) next.delete(p); else next.add(p);
    return next;
  });
  const toggleTask = (t: TaskType) => setSelectedTaskTypes((prev) => {
    const next = new Set(prev);
    if (next.has(t)) next.delete(t); else next.add(t);
    return next;
  });

  const plannedRows = useMemo<RowResult[]>(() => {
    const out: RowResult[] = [];
    for (const p of selectedProfessions) {
      for (const t of selectedTaskTypes) {
        for (let i = 0; i < countPerCell; i++) {
          out.push({ profession: p, taskType: t, status: 'pending' });
        }
      }
    }
    return out;
  }, [selectedProfessions, selectedTaskTypes, countPerCell]);

  const run = async () => {
    if (!canWrite || running || plannedRows.length === 0) return;
    setRunning(true);
    const working = plannedRows.map((r) => ({ ...r }));
    setResults(working);
    let ok = 0;
    let failed = 0;
    for (let i = 0; i < working.length; i++) {
      working[i].status = 'running';
      setResults([...working]);
      try {
        const draft = await adminConversationAiDraft({
          profession: working[i].profession,
          taskType: working[i].taskType,
          durationSeconds: 300,
        });
        const created = (await createAdminConversationTemplate(
          buildConversationCreatePayload(draft, working[i].profession),
        )) as { id: string };
        if (autoPublish && canPublish) {
          await publishAdminConversationTemplate(created.id);
        }
        working[i].status = 'ok';
        working[i].templateId = created.id;
        working[i].warning = draft.warning ?? null;
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
      message: `Bulk conversation: ${ok} created, ${failed} failed.`,
    });
  };

  return (
    <AdminRouteWorkspace role="main" aria-label="Conversation bulk runner">
      <Link
        href="/admin/content/conversation"
        className="inline-flex items-center gap-1 text-sm text-muted hover:text-navy"
      >
        <ArrowLeft className="h-4 w-4" /> Back to conversation templates
      </Link>

      <AdminRouteHero
        eyebrow="CMS"
        icon={MessagesSquare}
        accent="navy"
        title="Bulk-generate conversation templates"
        description="Pick a set of professions and task types and generate multiple grounded templates in one batch."
      />

      <AdminRoutePanel title="Matrix">
        <div className="grid gap-4 sm:grid-cols-2">
          <div>
            <div className="mb-2 text-sm font-medium">Professions ({selectedProfessions.size})</div>
            <div className="flex flex-wrap gap-2">
              {PROFESSIONS.map((p) => {
                const active = selectedProfessions.has(p);
                return (
                  <button
                    key={p}
                    type="button"
                    onClick={() => toggleProf(p)}
                    className={`rounded-full border px-3 py-1 text-xs capitalize transition ${
                      active ? 'border-primary bg-primary text-white' : 'border-border bg-background-light text-muted'
                    }`}
                  >
                    {p}
                  </button>
                );
              })}
            </div>
          </div>
          <div>
            <div className="mb-2 text-sm font-medium">Task types ({selectedTaskTypes.size})</div>
            <div className="flex flex-wrap gap-2">
              {TASK_TYPES.map((t) => {
                const active = selectedTaskTypes.has(t);
                return (
                  <button
                    key={t}
                    type="button"
                    onClick={() => toggleTask(t)}
                    className={`rounded-full border px-3 py-1 text-xs transition ${
                      active ? 'border-primary bg-primary text-white' : 'border-border bg-background-light text-muted'
                    }`}
                  >
                    {t}
                  </button>
                );
              })}
            </div>
          </div>
          <Input
            type="number"
            min={1}
            max={10}
            value={countPerCell}
            onChange={(e) => setCountPerCell(Math.max(1, Math.min(10, Number(e.target.value) || 1)))}
            label="Count per cell (1–10)"
          />
          <div className="flex items-end">
            <label className="inline-flex items-center gap-2 text-sm">
              <input
                type="checkbox"
                checked={autoPublish}
                onChange={(e) => setAutoPublish(e.target.checked)}
                disabled={!canPublish}
              />
              <span>Auto-publish after create {!canPublish && '(requires publish permission)'}</span>
            </label>
          </div>
        </div>
        <div className="mt-3 flex items-center justify-between">
          <div className="text-xs text-muted">Total to generate: <Badge variant="muted">{totalCells}</Badge></div>
          <Button
            onClick={() => void run()}
            disabled={!canWrite || running || totalCells === 0}
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
                  <th className="px-4 py-2">Profession</th>
                  <th className="px-4 py-2">Task type</th>
                  <th className="px-4 py-2">Status</th>
                  <th className="px-4 py-2">Detail</th>
                </tr>
              </thead>
              <tbody>
                {results.map((r, i) => (
                  <tr key={i} className="border-t border-border">
                    <td className="px-4 py-2 text-xs text-muted">{i + 1}</td>
                    <td className="px-4 py-2 text-xs capitalize">{r.profession}</td>
                    <td className="px-4 py-2 text-xs">{r.taskType}</td>
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
                    <td className="px-4 py-2 text-xs">
                      {r.templateId && (
                        <Link
                          href={`/admin/content/conversation/${r.templateId}`}
                          className="text-primary hover:underline"
                        >
                          Open template
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
