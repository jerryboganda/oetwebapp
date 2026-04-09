'use client';

import { useEffect, useState } from 'react';
import { AdminRouteSectionHeader, AdminRoutePanel, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { Badge } from '@/components/ui/badge';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { fetchAdminContentPrograms, fetchAdminContentPackages } from '@/lib/api';
import type { ContentProgram, ContentPackage } from '@/lib/types/content-hierarchy';
import { GitBranch, Package, RefreshCw } from 'lucide-react';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';

export default function AdminContentHierarchyPage() {
  const { isAuthenticated } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [programs, setPrograms] = useState<ContentProgram[]>([]);
  const [packages, setPackages] = useState<ContentPackage[]>([]);
  const [tab, setTab] = useState<'programs' | 'packages'>('programs');
  const [reloadNonce, setReloadNonce] = useState(0);

  useEffect(() => {
    let cancelled = false;
    async function load() {
      setPageStatus('loading');
      try {
        const [progRes, pkgRes] = await Promise.all([
          fetchAdminContentPrograms({ pageSize: 100 }),
          fetchAdminContentPackages({ pageSize: 100 }),
        ]);
        if (cancelled) return;
        const progs = progRes?.items ?? progRes ?? [];
        const pkgs = pkgRes?.items ?? pkgRes ?? [];
        setPrograms(Array.isArray(progs) ? progs : []);
        setPackages(Array.isArray(pkgs) ? pkgs : []);
        setPageStatus(progs.length === 0 && pkgs.length === 0 ? 'empty' : 'success');
      } catch {
        if (!cancelled) setPageStatus('error');
      }
    }
    if (isAuthenticated) void load();
    return () => { cancelled = true; };
  }, [isAuthenticated, reloadNonce]);

  const programColumns: Column<ContentProgram>[] = [
    { key: 'title', header: 'Title', render: (row) => <span className="font-medium">{row.title}</span> },
    { key: 'programType', header: 'Type', render: (row) => <Badge variant="info">{row.programType}</Badge> },
    { key: 'instructionLanguage', header: 'Language', render: (row) => row.instructionLanguage },
    { key: 'status', header: 'Status', render: (row) => (
      <Badge variant={row.status === 'Published' ? 'success' : 'muted'}>{row.status}</Badge>
    )},
    { key: 'estimatedDurationMinutes', header: 'Duration (min)', render: (row) => row.estimatedDurationMinutes },
  ];

  const packageColumns: Column<ContentPackage>[] = [
    { key: 'title', header: 'Title', render: (row) => <span className="font-medium">{row.title}</span> },
    { key: 'packageType', header: 'Type', render: (row) => <Badge variant="info">{row.packageType}</Badge> },
    { key: 'status', header: 'Status', render: (row) => (
      <Badge variant={row.status === 'Published' ? 'success' : 'muted'}>{row.status}</Badge>
    )},
    { key: 'displayOrder', header: 'Order', render: (row) => row.displayOrder },
  ];

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        title="Content Hierarchy"
        description="Manage programs, tracks, modules, and lesson hierarchy"
        icon={GitBranch}
        actions={
          <button
            onClick={() => setReloadNonce((n) => n + 1)}
            className="inline-flex items-center gap-2 rounded-md border px-3 py-1.5 text-sm hover:bg-muted"
          >
            <RefreshCw className="w-4 h-4" /> Refresh
          </button>
        }
      />

      <div className="flex gap-2 mb-4">
        <button
          onClick={() => setTab('programs')}
          className={`px-4 py-2 text-sm rounded-md ${tab === 'programs' ? 'bg-primary text-primary-foreground' : 'bg-muted hover:bg-muted/80'}`}
        >
          <GitBranch className="w-4 h-4 inline mr-1" /> Programs ({programs.length})
        </button>
        <button
          onClick={() => setTab('packages')}
          className={`px-4 py-2 text-sm rounded-md ${tab === 'packages' ? 'bg-primary text-primary-foreground' : 'bg-muted hover:bg-muted/80'}`}
        >
          <Package className="w-4 h-4 inline mr-1" /> Packages ({packages.length})
        </button>
      </div>

      <AdminRoutePanel title={tab === 'programs' ? 'Programs' : 'Packages'}>
        <AsyncStateWrapper
          status={pageStatus}
          errorMessage="Failed to load content hierarchy"
          onRetry={() => setReloadNonce((n) => n + 1)}
          emptyContent={
            <EmptyState
              title="No content hierarchy yet"
              description="Import content or create programs to build your hierarchy."
              icon={<GitBranch className="w-8 h-8 text-muted-foreground" />}
            />
          }
        >
          {tab === 'programs' ? (
            <DataTable columns={programColumns} data={programs} keyExtractor={(row) => row.id} />
          ) : (
            <DataTable columns={packageColumns} data={packages} keyExtractor={(row) => row.id} />
          )}
        </AsyncStateWrapper>
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
