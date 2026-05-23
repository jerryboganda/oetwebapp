'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { ArrowLeft, BrainCircuit, CheckCircle2, ListChecks, XCircle } from 'lucide-react';
import {
  AdminRoutePanel,
  AdminRouteSectionHeader,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  getListeningStructure,
  type ListeningAuthoredQuestion,
  type ListeningAuthoredQuestionList,
} from '@/lib/listening-authoring-api';

type LoadState = 'loading' | 'ready' | 'error';

function firstParam(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

/**
 * Per-paper structure browse — lists the 42 authored questions and links to
 * the per-question PATCH form. Authors land here from the listening papers
 * list page via the "Questions" action.
 */
export default function AdminListeningStructurePage() {
  const params = useParams<{ paperId?: string | string[] }>();
  const paperId = firstParam(params?.paperId);
  const { isAuthenticated, role } = useAdminAuth();

  const [state, setState] = useState<LoadState>('loading');
  const [doc, setDoc] = useState<ListeningAuthoredQuestionList | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!paperId) return;
    setState('loading');
    try {
      const result = await getListeningStructure(paperId);
      setDoc(result);
      setState('ready');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not load structure.');
      setState('error');
    }
  }, [paperId]);

  useEffect(() => {
    if (!isAuthenticated || role !== 'admin') return;
    void load();
  }, [isAuthenticated, role, load]);

  const counts = doc?.counts;

  const columns = useMemo<Column<ListeningAuthoredQuestion>[]>(() => [
    {
      key: 'number',
      header: '#',
      render: (q) => <span className="font-mono text-sm">{q.number}</span>,
    },
    {
      key: 'partCode',
      header: 'Part',
      render: (q) => <Badge variant="info">{q.partCode}</Badge>,
    },
    {
      key: 'type',
      header: 'Type',
      render: (q) => (
        <span className="text-xs uppercase tracking-widest text-muted">
          {q.type === 'multiple_choice_3' ? 'MCQ' : 'Gap fill'}
        </span>
      ),
    },
    {
      key: 'stem',
      header: 'Stem',
      render: (q) => (
        <span className="line-clamp-1 text-sm text-navy">{q.stem || <em className="text-muted">empty</em>}</span>
      ),
    },
    {
      key: 'correctAnswer',
      header: 'Answer',
      render: (q) => (
        <span className="line-clamp-1 text-sm text-navy">
          {q.correctAnswer || <em className="text-muted">empty</em>}
        </span>
      ),
    },
    {
      key: 'evidence',
      header: 'Evidence',
      render: (q) => q.transcriptExcerpt
        ? <CheckCircle2 className="h-4 w-4 text-success" aria-label="Transcript evidence authored" />
        : <XCircle className="h-4 w-4 text-muted" aria-label="No transcript evidence" />,
    },
    {
      key: 'actions',
      header: 'Actions',
      render: (q) => (
        <Link
          href={`/admin/content/listening/${paperId}/questions/${q.id}`}
          className="inline-flex min-h-9 items-center rounded px-3 py-2 text-sm font-semibold text-navy hover:bg-background-light"
        >
          Edit
        </Link>
      ),
    },
  ], [paperId]);

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminRouteWorkspace>
        <p className="text-sm text-muted">Admin access required.</p>
      </AdminRouteWorkspace>
    );
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="Listening structure browse">
      <AdminRouteSectionHeader
        icon={<ListChecks className="w-6 h-6" />}
        title="Listening — structure"
        description={`Paper ${paperId ?? ''}. 42-question canonical map (A1 + A2 + B + C1 + C2). Edit any question via the per-question form.`}
      />

      <div className="flex flex-wrap items-center gap-2">
        <Button variant="ghost" className="gap-2" asChild>
          <Link href="/admin/content/listening">
            <ArrowLeft className="h-4 w-4" />
            Back to papers
          </Link>
        </Button>

        <Button variant="outline" className="gap-2" asChild>
          <Link href={`/admin/content/listening/${paperId}/extractions`}>
            <BrainCircuit className="h-4 w-4" />
            AI Extractions
          </Link>
        </Button>
      </div>

      {state === 'loading' && <Skeleton className="h-48 rounded-2xl" />}
      {state === 'error' && error && <InlineAlert variant="error">{error}</InlineAlert>}

      {state === 'ready' && doc && (
        <>
          <AdminRoutePanel title="Counts">
            <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
              <div className="rounded-2xl border border-border bg-background-light p-4">
                <p className="text-xs font-black uppercase tracking-widest text-muted">Part A</p>
                <p className="mt-1 text-2xl font-bold text-navy">{counts?.partACount ?? 0} / 24</p>
              </div>
              <div className="rounded-2xl border border-border bg-background-light p-4">
                <p className="text-xs font-black uppercase tracking-widest text-muted">Part B</p>
                <p className="mt-1 text-2xl font-bold text-navy">{counts?.partBCount ?? 0} / 6</p>
              </div>
              <div className="rounded-2xl border border-border bg-background-light p-4">
                <p className="text-xs font-black uppercase tracking-widest text-muted">Part C</p>
                <p className="mt-1 text-2xl font-bold text-navy">{counts?.partCCount ?? 0} / 12</p>
              </div>
              <div className="rounded-2xl border border-border bg-background-light p-4">
                <p className="text-xs font-black uppercase tracking-widest text-muted">Total</p>
                <p className="mt-1 text-2xl font-bold text-navy">{counts?.totalItems ?? 0} / 42</p>
              </div>
            </div>
          </AdminRoutePanel>

          <AdminRoutePanel title="Questions">
            <DataTable<ListeningAuthoredQuestion>
              data={doc.questions}
              columns={columns}
              keyExtractor={(q) => q.id}
            />
          </AdminRoutePanel>
        </>
      )}
    </AdminRouteWorkspace>
  );
}
