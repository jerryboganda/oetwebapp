'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { BookOpen, Plus, Upload, Sparkles, Trash2, Edit3 } from 'lucide-react';
import { AdminDashboardShell } from '@/components/layout';
import { AdminRouteWorkspace, AdminRoutePanel, AdminRouteSectionHeader } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { EmptyState } from '@/components/ui/empty-error';
import { Pagination } from '@/components/ui/pagination';
import {
  fetchAdminVocabularyItems,
  deleteAdminVocabularyItem,
  fetchAdminVocabularyCategories,
} from '@/lib/api';

type VocabRow = {
  id: string;
  term: string;
  definition: string;
  professionId: string | null;
  category: string;
  difficulty: string;
  exampleSentence: string | null;
  status: 'draft' | 'active' | 'archived';
};

type ListResponse = {
  total: number;
  page: number;
  pageSize: number;
  items: VocabRow[];
};


export default function AdminVocabularyPage() {
  const router = useRouter();
  const [loading, setLoading] = useState(true);
  const [rows, setRows] = useState<VocabRow[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [search, setSearch] = useState('');
  const [category, setCategory] = useState<string>('');
  const [profession, setProfession] = useState<string>('');
  const [status, setStatus] = useState<string>('');
  const [categories, setCategories] = useState<Array<{ category: string; total: number }>>([]);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  useEffect(() => {
    fetchAdminVocabularyCategories()
      .then(d => {
        const c = (d as { categories?: Array<{ category: string; total: number }> }).categories ?? [];
        setCategories(c);
      })
      .catch(() => {});
  }, []);

  useEffect(() => {
    let cancelled = false;
    const t = setTimeout(async () => {
      setLoading(true);
      try {
        const res = await fetchAdminVocabularyItems({
          profession: profession || undefined,
          category: category || undefined,
          status: status || undefined,
          search: search || undefined,
          page,
          pageSize,
        });
        if (cancelled) return;
        const data = res as ListResponse;
        setRows(data.items ?? []);
        setTotal(data.total ?? 0);
      } catch {
        if (!cancelled) setToast({ variant: 'error', message: 'Failed to load vocabulary items.' });
      } finally {
        if (!cancelled) setLoading(false);
      }
    }, 300);
    return () => { cancelled = true; clearTimeout(t); };
  }, [search, category, profession, status, page, pageSize]);

  async function handleDelete(id: string, term: string) {
    if (!confirm(`Delete "${term}"? Referenced terms will be archived instead.`)) return;
    try {
      await deleteAdminVocabularyItem(id);
      setToast({ variant: 'success', message: `Deleted "${term}".` });
      setRows(prev => prev.filter(r => r.id !== id));
    } catch {
      setToast({ variant: 'error', message: `Failed to delete "${term}".` });
    }
  }

  const columns = useMemo<Column<VocabRow>[]>(() => [
    {
      key: 'term',
      header: 'Term',
      render: (row) => (
        <div className="flex flex-col">
          <span className="font-semibold text-navy">{row.term}</span>
          <span className="text-xs text-muted line-clamp-1">{row.definition}</span>
        </div>
      ),
    },
    { key: 'category', header: 'Category', render: (row) => <span className="capitalize text-sm">{row.category.replace(/_/g, ' ')}</span> },
    { key: 'profession', header: 'Profession', render: (row) => <span className="text-sm capitalize">{row.professionId ?? '—'}</span>, hideOnMobile: true },
    { key: 'difficulty', header: 'Difficulty', render: (row) => <span className="text-sm capitalize">{row.difficulty}</span>, hideOnMobile: true },
    {
      key: 'status',
      header: 'Status',
      render: (row) => (
        <Badge variant={row.status === 'active' ? 'success' : row.status === 'draft' ? 'warning' : 'muted'}>
          {row.status}
        </Badge>
      ),
    },
    {
      key: 'actions',
      header: '',
      render: (row) => (
        <div className="flex items-center justify-end gap-2">
          <Link href={`/admin/content/vocabulary/${row.id}`} aria-label={`Edit ${row.term}`}>
            <Edit3 className="h-4 w-4 text-muted hover:text-primary" />
          </Link>
          <button onClick={() => handleDelete(row.id, row.term)} aria-label={`Delete ${row.term}`}>
            <Trash2 className="h-4 w-4 text-muted hover:text-danger" />
          </button>
        </div>
      ),
    },
  ], []);

  const pageStatus = loading ? 'loading' : rows.length === 0 ? 'empty' : 'success';

  return (
    <AdminDashboardShell>
      {toast && (
        <Toast variant={toast.variant === 'error' ? 'error' : 'success'} message={toast.message} onClose={() => setToast(null)} />
      )}
      <AdminRouteWorkspace>
        <AdminRouteSectionHeader
          eyebrow="CMS"
          title="Vocabulary"
          description="Create, edit, import, and AI-draft OET vocabulary terms. All mutations are audit-logged."
          icon={BookOpen}
          actions={
            <div className="flex flex-wrap gap-2">
              <Link href="/admin/content/vocabulary/import">
                <Button variant="secondary" size="sm"><Upload className="mr-1.5 h-4 w-4" />Import CSV</Button>
              </Link>
              <Link href="/admin/content/vocabulary/ai-draft">
                <Button variant="secondary" size="sm"><Sparkles className="mr-1.5 h-4 w-4" />AI draft</Button>
              </Link>
              <Link href="/admin/content/vocabulary/new">
                <Button variant="primary" size="sm"><Plus className="mr-1.5 h-4 w-4" />New term</Button>
              </Link>
            </div>
          }
        />

        <AdminRoutePanel>
          <div className="mb-4 grid gap-3 sm:grid-cols-4">
            <Input
              placeholder="Search term or definition…"
              value={search}
              onChange={(e) => { setSearch(e.target.value); setPage(1); }}
            />
            <select
              value={category}
              onChange={(e) => { setCategory(e.target.value); setPage(1); }}
              className="rounded-xl border border-border bg-surface px-3 py-2 text-sm"
            >
              <option value="">All categories ({total})</option>
              {categories.map(c => (
                <option key={c.category} value={c.category}>
                  {c.category.replace(/_/g, ' ')} ({c.total})
                </option>
              ))}
            </select>
            <select
              value={profession}
              onChange={(e) => { setProfession(e.target.value); setPage(1); }}
              className="rounded-xl border border-border bg-surface px-3 py-2 text-sm"
            >
              <option value="">All professions</option>
              <option value="medicine">Medicine</option>
              <option value="nursing">Nursing</option>
              <option value="dentistry">Dentistry</option>
              <option value="pharmacy">Pharmacy</option>
            </select>
            <select
              value={status}
              onChange={(e) => { setStatus(e.target.value); setPage(1); }}
              className="rounded-xl border border-border bg-surface px-3 py-2 text-sm"
            >
              <option value="">All statuses</option>
              <option value="active">Active</option>
              <option value="draft">Draft</option>
              <option value="archived">Archived</option>
            </select>
          </div>

          <AsyncStateWrapper status={pageStatus}>
            <DataTable columns={columns} data={rows} keyExtractor={(r) => r.id} />
            <Pagination
              page={page}
              pageSize={pageSize}
              total={total}
              onPageChange={setPage}
              onPageSizeChange={setPageSize}
              itemLabel="term"
              itemLabelPlural="terms"
            />
          </AsyncStateWrapper>

          {pageStatus === 'empty' && (
            <EmptyState
              icon={<BookOpen className="h-6 w-6" />}
              title="No vocabulary terms yet"
              description="Start by adding a term, importing a CSV, or generating AI drafts for admin review."
              action={{ label: 'New term', onClick: () => router.push('/admin/content/vocabulary/new') }}
            />
          )}
        </AdminRoutePanel>
      </AdminRouteWorkspace>
    </AdminDashboardShell>
  );
}
