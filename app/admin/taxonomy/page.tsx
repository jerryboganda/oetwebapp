'use client';

import { useState, useEffect } from 'react';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { Card, CardContent } from '@/components/ui/card';
import { DataTable, type Column } from '@/components/ui/data-table';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { EmptyState } from '@/components/ui/empty-error';
import { Toast } from '@/components/ui/alert';
import { Plus, Edit2, ListTree } from 'lucide-react';
import { mockTaxonomy, type AdminTaxonomyNode } from '@/lib/mock-admin-data';
import { analytics } from '@/lib/analytics';

export default function AdminTaxonomyPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [data, setData] = useState<AdminTaxonomyNode[]>([]);
  const [pageStatus, setPageStatus] = useState<'loading' | 'success' | 'empty'>('loading');
  const [selectedFilters, setSelectedFilters] = useState<Record<string, string[]>>({});
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  useEffect(() => {
    const load = async () => {
      setPageStatus('loading');
      await new Promise(resolve => setTimeout(resolve, 300));
      setData(mockTaxonomy);
      setPageStatus(mockTaxonomy.length > 0 ? 'success' : 'empty');
    };
    load();
  }, []);

  const filterGroups: FilterGroup[] = [
    {
      id: 'type',
      label: 'Type',
      options: [
        { id: 'profession', label: 'Profession' },
        { id: 'category', label: 'Category' },
      ],
    },
    {
      id: 'status',
      label: 'Status',
      options: [
        { id: 'active', label: 'Active' },
        { id: 'archived', label: 'Archived' },
      ],
    },
  ];

  const handleFilterChange = (groupId: string, optionId: string) => {
    setSelectedFilters(prev => {
      const current = prev[groupId] || [];
      const updated = current.includes(optionId) ? current.filter(id => id !== optionId) : [...current, optionId];
      return { ...prev, [groupId]: updated };
    });
  };

  const clearFilters = () => setSelectedFilters({});

  const filteredData = data.filter(item => {
    if (selectedFilters.type?.length && !selectedFilters.type.includes(item.type)) return false;
    if (selectedFilters.status?.length && !selectedFilters.status.includes(item.status)) return false;
    return true;
  });

  const columns: Column<AdminTaxonomyNode>[] = [
    { key: 'label', header: 'Profession / Category', render: (row) => <span className="font-semibold text-navy">{row.label}</span> },
    { key: 'slug', header: 'Slug', render: (row) => <span className="font-mono text-xs text-muted">{row.slug}</span> },
    { key: 'type', header: 'Type', render: (row) => <span className="capitalize">{row.type}</span> },
    { key: 'count', header: 'Linked Content', render: (row) => <span className="font-medium">{row.contentCount} items</span> },
    { 
      key: 'status', 
      header: 'Status', 
      render: (row) => {
        const variant = row.status === 'active' ? 'success' : 'muted';
        return <Badge variant={variant} className="capitalize">{row.status}</Badge>;
      }
    },
    {
      key: 'actions',
      header: '',
      render: (row) => (
        <div className="flex justify-end">
          <Button variant="ghost" size="sm" className="px-2 text-muted hover:text-navy" onClick={() => {
            analytics.track('admin_taxonomy_changed', { nodeId: row.id, action: 'edit' });
            setToast({ variant: 'success', message: `Editing ${row.label}...` });
          }}>
            <Edit2 className="w-4 h-4" />
          </Button>
        </div>
      )
    }
  ];

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <div className="max-w-6xl mx-auto p-4 md:p-8 space-y-8" role="main" aria-label="Profession Taxonomy">
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-navy tracking-tight">Profession Taxonomy</h1>
          <p className="text-sm text-muted mt-1">Manage standard professions and categories used across the platform.</p>
        </div>
        <Button className="gap-2">
          <Plus className="w-4 h-4" /> Add Node
        </Button>
      </div>

      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => window.location.reload()}
        emptyContent={
          <EmptyState
            icon={<ListTree className="w-12 h-12 text-muted" />}
            title="No Taxonomy Entries"
            description="Create your first profession or category entry."
          />
        }
      >
        <div className="bg-surface p-4 rounded-xl border border-gray-200/60 shadow-sm">
          <FilterBar
            groups={filterGroups}
            selected={selectedFilters}
            onChange={handleFilterChange}
            onClear={clearFilters}
          />
        </div>

        <Card padding="none" className="overflow-hidden">
          <CardContent className="p-0">
            <DataTable 
              data={filteredData}
              columns={columns}
              keyExtractor={(item) => item.id}
            />
          </CardContent>
        </Card>
      </AsyncStateWrapper>
    </div>
  );
}
