'use client';

import { useState, useEffect, useMemo } from 'react';
import { useRouter } from 'next/navigation';
import { Card, CardContent } from '@/components/ui/card';
import { DataTable, type Column } from '@/components/ui/data-table';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { EmptyState } from '@/components/ui/empty-error';
import { Toast } from '@/components/ui/alert';
import { Plus, Search, FileText, History } from 'lucide-react';
import { mockContentLibrary, type AdminContentItem } from '@/lib/mock-admin-data';
import { analytics } from '@/lib/analytics';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';

export default function AdminContentLibrary() {
  const { isAuthenticated, role } = useAdminAuth();
  const router = useRouter();
  const [data, setData] = useState<AdminContentItem[]>([]);
  const [pageStatus, setPageStatus] = useState<'loading' | 'success' | 'error' | 'empty'>('loading');
  const [selectedFilters, setSelectedFilters] = useState<Record<string, string[]>>({});
  const [searchQuery, setSearchQuery] = useState('');
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  useEffect(() => {
    const load = async () => {
      setPageStatus('loading');
      await new Promise(resolve => setTimeout(resolve, 400));
      const sorted = [...mockContentLibrary].sort(
        (a, b) => new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime()
      );
      setData(sorted);
      setPageStatus(sorted.length > 0 ? 'success' : 'empty');
    };
    load();
  }, []);

  const filterGroups: FilterGroup[] = [
    {
      id: 'type',
      label: 'Type',
      options: [
        { id: 'Writing Task', label: 'Writing' },
        { id: 'Speaking Roleplay', label: 'Speaking' },
        { id: 'Reading Part A', label: 'Reading' },
        { id: 'Listening Part C', label: 'Listening' },
      ],
    },
    {
      id: 'profession',
      label: 'Profession',
      options: [
        { id: 'Medicine', label: 'Medicine' },
        { id: 'Nursing', label: 'Nursing' },
        { id: 'Dentistry', label: 'Dentistry' },
        { id: 'All', label: 'All Professions' },
      ],
    },
    {
      id: 'status',
      label: 'Status',
      options: [
        { id: 'published', label: 'Published' },
        { id: 'draft', label: 'Draft' },
        { id: 'archived', label: 'Archived' },
      ],
    }
  ];

  const handleFilterChange = (groupId: string, optionId: string) => {
    setSelectedFilters(prev => {
      const current = prev[groupId] || [];
      const updated = current.includes(optionId)
        ? current.filter(id => id !== optionId)
        : [...current, optionId];
      return { ...prev, [groupId]: updated };
    });
  };

  const clearFilters = () => {
    setSelectedFilters({});
    setSearchQuery('');
  };

  const filteredData = useMemo(() => {
    return data.filter(item => {
      if (selectedFilters.type?.length && !selectedFilters.type.includes(item.type)) return false;
      if (selectedFilters.profession?.length && !selectedFilters.profession.includes(item.profession)) return false;
      if (selectedFilters.status?.length && !selectedFilters.status.includes(item.status)) return false;
      if (searchQuery) {
        const q = searchQuery.toLowerCase();
        return item.title.toLowerCase().includes(q) || item.id.toLowerCase().includes(q) || item.author.toLowerCase().includes(q);
      }
      return true;
    });
  }, [data, selectedFilters, searchQuery]);

  const columns: Column<AdminContentItem>[] = [
    { key: 'id', header: 'ID', render: (row) => <span className="font-mono text-xs">{row.id}</span> },
    { 
      key: 'title', 
      header: 'Title', 
      render: (row) => (
        <span 
          className="font-medium text-primary cursor-pointer hover:underline"
          onClick={() => { analytics.track('admin_content_updated', { contentId: row.id }); router.push(`/admin/content/${row.id}`); }}
        >
          {row.title}
        </span>
      )
    },
    { key: 'type', header: 'Type', render: (row) => <span>{row.type}</span> },
    { key: 'profession', header: 'Profession', render: (row) => <span>{row.profession}</span> },
    { key: 'author', header: 'Author', render: (row) => <span className="text-muted text-sm">{row.author}</span> },
    { 
      key: 'status', 
      header: 'Status', 
      render: (row) => {
        const variant = row.status === 'published' ? 'success' : row.status === 'archived' ? 'muted' : 'warning';
        return <Badge variant={variant as any} className="capitalize">{row.status}</Badge>;
      }
    },
    { 
      key: 'revisions', 
      header: 'Revisions', 
      render: (row) => (
        <span className="flex items-center gap-1 text-muted text-xs">
          <History className="w-3.5 h-3.5" /> {row.revisionCount}
        </span>
      )
    },
    { key: 'updatedAt', header: 'Last Updated', render: (row) => <span className="text-muted text-xs">{new Date(row.updatedAt).toLocaleDateString()}</span> },
  ];

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <div className="max-w-7xl mx-auto p-4 md:p-8 space-y-8" role="main" aria-label="Content Library">
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-navy tracking-tight">Content Library</h1>
          <p className="text-sm text-muted mt-1">Manage tasks, practice materials, and model answers.</p>
        </div>
        <Button onClick={() => router.push('/admin/content/new')} className="gap-2">
          <Plus className="w-4 h-4" /> New Content
        </Button>
      </div>

      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => window.location.reload()}
        emptyContent={
          <EmptyState
            icon={<FileText className="w-12 h-12 text-muted" />}
            title="No Content Items"
            description="Create your first content item to get started."
            action={{ label: 'Create Content', onClick: () => router.push('/admin/content/new') }}
          />
        }
      >
        <div className="bg-surface p-4 rounded-xl border border-gray-200/60 shadow-sm space-y-3">
          <div className="relative max-w-sm">
            <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-muted" />
            <Input
              placeholder="Search by title, ID, or author..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className="pl-9"
            />
          </div>
          <FilterBar 
            groups={filterGroups}
            selected={selectedFilters}
            onChange={handleFilterChange}
            onClear={clearFilters}
          />
        </div>

        <Card padding="none" className="overflow-hidden">
          <CardContent className="p-0">
            {filteredData.length === 0 ? (
              <EmptyState
                icon={<Search className="w-12 h-12 text-muted" />}
                title="No Matching Content"
                description="Try adjusting your search or filters."
                action={{ label: 'Clear Filters', onClick: clearFilters }}
              />
            ) : (
              <DataTable 
                data={filteredData}
                columns={columns}
                keyExtractor={(item) => item.id}
              />
            )}
          </CardContent>
        </Card>
      </AsyncStateWrapper>
    </div>
  );
}
