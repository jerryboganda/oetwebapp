'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { Headphones, ArrowLeft, Loader2, Play, RotateCcw, CheckCircle2, XCircle } from 'lucide-react';
import {
  AdminRoutePanel,
  AdminRouteSectionHeader,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Toast } from '@/components/ui/alert';
import { DataTable, type Column } from '@/components/ui/data-table';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { listContentPapers, type ContentPaperDto } from '@/lib/content-upload-api';
import {
  adminListeningBackfillAll,
  adminListeningBackfillPaper,
} from '@/lib/api';
import type { ListeningBackfillReport } from '@/lib/types/admin/listening-authoring';

type ToastState = { variant: 'success' | 'error'; message: string } | null;
type LogEntry = { at: string; level: 'info' | 'success' | 'error'; message: string };

const PART_CODES = ['A1', 'A2', 'B', 'C1', 'C2'] as const;

function audioCount(p: ContentPaperDto) {
  const audio = (p.assets ?? []).filter((a) => a.role === 'Audio');
  const parts = new Set<string>();
  for (const a of audio) {
    if (a.part) parts.add(a.part);
  }
  return parts.size;
}

export default function ListeningBackfillDashboard() {
  const { isAuthenticated, role } = useAdminAuth();
  const [papers, setPapers] = useState<ContentPaperDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [running, setRunning] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastState>(null);
  const [log, setLog] = useState<LogEntry[]>([]);

  const append = useCallback((level: LogEntry['level'], message: string) => {
    setLog((prev) =>
      [{ at: new Date().toLocaleTimeString(), level, message }, ...prev].slice(0, 200),
    );
  }, []);

  const load = useCallback(async () => {
    if (!isAuthenticated || role !== 'admin') return;
    setLoading(true);
    try {
      const data = await listContentPapers({ subtest: 'listening', pageSize: 200 });
      setPapers(data);
    } catch (e) {
      setToast({ variant: 'error', message: `Load papers failed: ${(e as Error).message}` });
    } finally {
      setLoading(false);
    }
  }, [isAuthenticated, role]);

  useEffect(() => {
    void load();
  }, [load]);

  const runAll = useCallback(async () => {
    if (!confirm('Run system-wide listening backfill across every paper?')) return;
    setRunning(true);
    append('info', 'Starting system-wide backfill…');
    try {
      const res = await adminListeningBackfillAll();
      append(
        res.successCount === res.count ? 'success' : 'error',
        `Backfill complete: ${res.successCount}/${res.count} papers succeeded.`,
      );
      for (const r of res.reports) {
        append(
          r.success ? 'success' : 'error',
          `${r.paperId}: ${r.success ? `OK (${r.questionsCreated}q, ${r.optionsCreated}o)` : r.reason ?? 'failed'}`,
        );
      }
      setToast({
        variant: res.successCount === res.count ? 'success' : 'error',
        message: `Backfilled ${res.successCount}/${res.count} papers.`,
      });
      await load();
    } catch (e) {
      append('error', `Backfill all failed: ${(e as Error).message}`);
      setToast({ variant: 'error', message: `Backfill all failed: ${(e as Error).message}` });
    } finally {
      setRunning(false);
    }
  }, [append, load]);

  const runOne = useCallback(
    async (p: ContentPaperDto) => {
      setBusyId(p.id);
      append('info', `Backfilling ${p.slug}…`);
      try {
        const report: ListeningBackfillReport = await adminListeningBackfillPaper(p.id);
        append(
          report.success ? 'success' : 'error',
          `${p.slug}: ${report.success ? `OK (${report.questionsCreated}q)` : report.reason ?? 'failed'}`,
        );
        setToast({
          variant: report.success ? 'success' : 'error',
          message: report.success
            ? `Backfilled ${p.slug}.`
            : `Failed: ${report.reason ?? 'unknown'}`,
        });
        await load();
      } catch (e) {
        append('error', `${p.slug}: ${(e as Error).message}`);
        setToast({ variant: 'error', message: `Backfill failed: ${(e as Error).message}` });
      } finally {
        setBusyId(null);
      }
    },
    [append, load],
  );

  const columns: Column<ContentPaperDto>[] = useMemo(
    () => [
      {
        key: 'title',
        header: 'Title',
        render: (p) => (
          <Link
            href={`/admin/content/listening/${p.id}`}
            className="font-medium hover:text-primary"
          >
            {p.title}
          </Link>
        ),
      },
      { key: 'slug', header: 'Slug', render: (p) => <span className="font-mono text-xs">{p.slug}</span> },
      {
        key: 'status',
        header: 'Status',
        render: (p) => (
          <Badge
            variant={
              p.status === 'Published'
                ? 'success'
                : p.status === 'Archived'
                  ? 'muted'
                  : p.status === 'InReview'
                    ? 'warning'
                    : 'default'
            }
          >
            {p.status}
          </Badge>
        ),
      },
      {
        key: 'audio',
        header: 'Audio per part',
        render: (p) => {
          const c = audioCount(p);
          const total = PART_CODES.length;
          const ok = c === total;
          return (
            <span className="inline-flex items-center gap-1 text-sm">
              {ok ? (
                <CheckCircle2 className="h-3 w-3 text-success" />
              ) : (
                <XCircle className="h-3 w-3 text-danger" />
              )}
              {c}/{total}
            </span>
          );
        },
      },
      {
        key: 'actions',
        header: 'Actions',
        render: (p) => (
          <Button
            variant="ghost"
            size="sm"
            disabled={busyId === p.id || running}
            onClick={() => void runOne(p)}
          >
            {busyId === p.id ? (
              <Loader2 className="h-3 w-3 animate-spin" />
            ) : (
              <RotateCcw className="h-3 w-3" />
            )}
            Backfill this paper
          </Button>
        ),
      },
    ],
    [busyId, runOne, running],
  );

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminRouteWorkspace>
        <p className="text-sm text-muted">Admin access required.</p>
      </AdminRouteWorkspace>
    );
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="Listening Backfill Dashboard">
      <div className="mb-2">
        <Link
          href="/admin/content/listening"
          className="inline-flex items-center gap-1 text-sm text-muted hover:text-navy"
        >
          <ArrowLeft className="h-3 w-3" /> Back to Listening papers
        </Link>
      </div>

      <AdminRouteSectionHeader
        icon={<Headphones className="w-6 h-6" />}
        title="Listening backfill dashboard"
        description="Project authored JSON structure into the relational ListeningPart/Extract/Question/Option tables. Use the per-paper button to fix one paper, or 'Backfill all drafts' for a full sweep."
      />

      <AdminRoutePanel title="System-wide actions">
        <div className="flex items-center gap-2">
          <Button variant="primary" onClick={() => void runAll()} disabled={running}>
            {running ? <Loader2 className="h-4 w-4 animate-spin" /> : <Play className="h-4 w-4" />}
            Backfill all drafts
          </Button>
          <p className="text-xs text-muted">
            Calls <code>POST /v1/admin/listening/backfill</code> — re-projects every Listening paper.
          </p>
        </div>
      </AdminRoutePanel>

      <AdminRoutePanel title={`Listening papers (${papers.length})`}>
        {loading ? (
          <div className="flex items-center gap-2 text-sm text-muted">
            <Loader2 className="h-4 w-4 animate-spin" /> Loading…
          </div>
        ) : (
          <DataTable data={papers} columns={columns} keyExtractor={(p) => p.id} />
        )}
      </AdminRoutePanel>

      <AdminRoutePanel title="Activity log">
        {log.length === 0 ? (
          <p className="text-sm text-muted">No activity yet.</p>
        ) : (
          <ul className="max-h-80 space-y-1 overflow-auto font-mono text-xs">
            {log.map((entry, i) => (
              <li
                key={`${entry.at}-${i}`}
                className={
                  entry.level === 'success'
                    ? 'text-success'
                    : entry.level === 'error'
                      ? 'text-danger'
                      : 'text-muted'
                }
              >
                [{entry.at}] {entry.message}
              </li>
            ))}
          </ul>
        )}
      </AdminRoutePanel>

      {toast && (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      )}
    </AdminRouteWorkspace>
  );
}
