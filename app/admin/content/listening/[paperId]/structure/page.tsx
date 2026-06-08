'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { ArrowLeft, BrainCircuit, CheckCircle2, FileText, Headphones, HelpCircle, ListChecks, ListOrdered, XCircle } from 'lucide-react';
import { AdminSettingsLayout } from '@/components/admin/layout/admin-settings-layout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Skeleton } from '@/components/admin/ui/skeleton';
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
      render: (q) => <span className="font-mono text-sm text-admin-fg-strong">{q.number}</span>,
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
        <span className="text-xs uppercase tracking-widest text-admin-fg-muted">
          {q.type === 'multiple_choice_3' ? 'MCQ' : 'Gap fill'}
        </span>
      ),
    },
    {
      key: 'stem',
      header: 'Stem',
      render: (q) => (
        <span className="line-clamp-1 text-sm text-admin-fg-strong">{q.stem || <em className="text-admin-fg-muted">empty</em>}</span>
      ),
    },
    {
      key: 'correctAnswer',
      header: 'Answer',
      render: (q) => (
        <span className="line-clamp-1 text-sm text-admin-fg-strong">
          {q.correctAnswer || <em className="text-admin-fg-muted">empty</em>}
        </span>
      ),
    },
    {
      key: 'evidence',
      header: 'Evidence',
      render: (q) => q.transcriptExcerpt
        ? <CheckCircle2 className="h-4 w-4 text-[var(--admin-success)]" aria-label="Transcript evidence authored" />
        : <XCircle className="h-4 w-4 text-admin-fg-muted" aria-label="No transcript evidence" />,
    },
    {
      key: 'actions',
      header: 'Actions',
      render: (q) => (
        <Link
          href={`/admin/content/listening/${paperId}/questions/${q.id}`}
          className="inline-flex min-h-9 items-center rounded-admin px-3 py-2 text-sm font-semibold text-admin-fg-strong hover:bg-admin-bg-subtle"
        >
          Edit
        </Link>
      ),
    },
  ], [paperId]);

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Content', href: '/admin/content' },
    { label: 'Listening', href: '/admin/content/listening' },
    { label: 'Structure' },
  ];

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminSettingsLayout title="Listening: structure" breadcrumbs={breadcrumbs}>
        <Card><CardContent className="p-6"><p className="text-sm text-admin-fg-muted">Admin access required.</p></CardContent></Card>
      </AdminSettingsLayout>
    );
  }

  return (
    <AdminSettingsLayout
      eyebrow="Authoring"
      icon={<ListChecks className="h-5 w-5" />}
      title="Listening: structure"
      description={`Paper ${paperId ?? ''}. 42-question canonical map (A1 + A2 + B + C1 + C2). Edit any question via the per-question form.`}
      breadcrumbs={breadcrumbs}
      actions={
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="ghost" size="sm" asChild>
            <Link href="/admin/content/listening">
              <ArrowLeft className="h-4 w-4 mr-1.5" />
              Back to papers
            </Link>
          </Button>
          <Button variant="outline" size="sm" asChild>
            <Link href={`/admin/content/listening/${paperId}/part-a`}>
              <FileText className="h-4 w-4 mr-1.5" />
              Part A notes
            </Link>
          </Button>
          <Button variant="outline" size="sm" asChild>
            <Link href={`/admin/content/listening/${paperId}/audio`}>
              <Headphones className="h-4 w-4 mr-1.5" />
              Audio &amp; timers
            </Link>
          </Button>
          <Button variant="outline" size="sm" asChild>
            <Link href={`/admin/content/listening/${paperId}/questions`}>
              <HelpCircle className="h-4 w-4 mr-1.5" />
              Questions
            </Link>
          </Button>
          <Button variant="outline" size="sm" asChild>
            <Link href={`/admin/content/listening/${paperId}/sequence`}>
              <ListOrdered className="h-4 w-4 mr-1.5" />
              Sequence
            </Link>
          </Button>
          <Button variant="outline" size="sm" asChild>
            <Link href={`/admin/content/listening/${paperId}/extractions`}>
              <BrainCircuit className="h-4 w-4 mr-1.5" />
              AI Extractions
            </Link>
          </Button>
        </div>
      }
    >
      {state === 'loading' && <Skeleton className="h-48 rounded-admin" />}
      {state === 'error' && error && <InlineAlert variant="error">{error}</InlineAlert>}

      {state === 'ready' && doc && (
        <div className="space-y-6">
          <Card>
            <CardHeader><CardTitle>Counts</CardTitle></CardHeader>
            <CardContent>
              <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
                <KpiTile label="Part A" value={`${counts?.partACount ?? 0} / 24`} tone="default" />
                <KpiTile label="Part B" value={`${counts?.partBCount ?? 0} / 6`} tone="default" />
                <KpiTile label="Part C" value={`${counts?.partCCount ?? 0} / 12`} tone="default" />
                <KpiTile label="Total" value={`${counts?.totalItems ?? 0} / 42`} tone={counts?.totalItems === 42 ? 'success' : 'warning'} />
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader><CardTitle>Questions</CardTitle></CardHeader>
            <CardContent className="p-0">
              <DataTable<ListeningAuthoredQuestion>
                data={doc.questions}
                columns={columns}
                keyExtractor={(q) => q.id}
              />
            </CardContent>
          </Card>
        </div>
      )}
    </AdminSettingsLayout>
  );
}
