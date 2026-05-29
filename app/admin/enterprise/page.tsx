'use client';

import { useEffect, useState } from 'react';
import { Building2, GraduationCap, Plus } from 'lucide-react';

import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { KpiStrip } from '@/components/admin/layout/admin-operations-layout';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Toast } from '@/components/ui/alert';
import { Input, Select } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';
import { BulkActionBar } from '@/components/ui/bulk-action-bar';

const SPONSOR_PORTAL_ENABLED = process.env.NEXT_PUBLIC_SPONSOR_PORTAL_ENABLED === 'true';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

interface Sponsor {
  id: string;
  name: string;
  type: string;
  contactEmail: string;
  organizationName: string | null;
  status: string;
  createdAt: string;
}

interface Cohort {
  id: string;
  sponsorId: string;
  name: string;
  examTypeCode: string;
  startDate: string | null;
  endDate: string | null;
  maxSeats: number;
  enrolledCount: number;
  status: string;
  createdAt: string;
}

function adminRequest<T = unknown>(path: string, init?: RequestInit): Promise<T> {
  return apiClient.request<T>(path, init);
}

const STATUS_BADGE: Record<
  string,
  { label: string; variant: 'default' | 'success' | 'danger' | 'info' | 'warning' }
> = {
  active: { label: 'Active', variant: 'success' },
  draft: { label: 'Draft', variant: 'warning' },
  completed: { label: 'Completed', variant: 'info' },
  archived: { label: 'Archived', variant: 'danger' },
};

export default function EnterprisePage() {
  useAdminAuth();
  const [sponsors, setSponsors] = useState<Sponsor[]>([]);
  const [cohorts, setCohorts] = useState<Cohort[]>([]);
  const [status, setStatus] = useState<PageStatus>('loading');
  const [toast, setToast] = useState<ToastState>(null);
  const [showCreate, setShowCreate] = useState(false);
  const [showCohortCreate, setShowCohortCreate] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [tab, setTab] = useState<'sponsors' | 'cohorts'>('sponsors');

  // Create sponsor form
  const [newSponsorName, setNewSponsorName] = useState('');
  const [newSponsorType, setNewSponsorType] = useState('employer');
  const [newSponsorEmail, setNewSponsorEmail] = useState('');
  const [newSponsorOrg, setNewSponsorOrg] = useState('');

  // Create cohort form
  const [cohortSponsorId, setCohortSponsorId] = useState('');
  const [cohortName, setCohortName] = useState('');
  const [cohortExamType, setCohortExamType] = useState('oet');
  const [cohortMaxSeats, setCohortMaxSeats] = useState('30');
  const [selectedKeys, setSelectedKeys] = useState<Set<string>>(new Set());

  useEffect(() => {
    analytics.track('admin_view', { page: 'enterprise' });
    if (SPONSOR_PORTAL_ENABLED) {
      loadData();
    }
  }, []);

  async function loadData() {
    try {
      setStatus('loading');
      const [sponsorData, cohortData] = await Promise.all([
        adminRequest<{ items: Sponsor[]; total: number }>('/v1/admin/sponsors?pageSize=100'),
        adminRequest<{ items: Cohort[]; total: number }>('/v1/admin/cohorts?pageSize=100'),
      ]);
      setSponsors(sponsorData.items);
      setCohorts(cohortData.items);
      setStatus(sponsorData.items.length > 0 || cohortData.items.length > 0 ? 'success' : 'empty');
    } catch {
      setStatus('error');
    }
  }

  async function handleCreateSponsor() {
    if (!newSponsorName || !newSponsorEmail) return;
    setSubmitting(true);
    try {
      await adminRequest('/v1/admin/sponsors', {
        method: 'POST',
        body: JSON.stringify({ name: newSponsorName, type: newSponsorType, contactEmail: newSponsorEmail, organizationName: newSponsorOrg || null }),
      });
      setToast({ variant: 'success', message: `Sponsor "${newSponsorName}" created.` });
      setShowCreate(false);
      setNewSponsorName(''); setNewSponsorEmail(''); setNewSponsorOrg('');
      loadData();
    } catch {
      setToast({ variant: 'error', message: 'Failed to create sponsor.' });
    } finally {
      setSubmitting(false);
    }
  }

  async function handleCreateCohort() {
    if (!cohortSponsorId || !cohortName) return;
    setSubmitting(true);
    try {
      await adminRequest('/v1/admin/cohorts', {
        method: 'POST',
        body: JSON.stringify({ sponsorId: cohortSponsorId, name: cohortName, examTypeCode: cohortExamType, maxSeats: Number(cohortMaxSeats) }),
      });
      setToast({ variant: 'success', message: `Cohort "${cohortName}" created.` });
      setShowCohortCreate(false);
      setCohortName(''); setCohortSponsorId('');
      loadData();
    } catch {
      setToast({ variant: 'error', message: 'Failed to create cohort.' });
    } finally {
      setSubmitting(false);
    }
  }

  const sponsorColumns: Column<Sponsor>[] = [
    { key: 'name', header: 'Name', render: (r) => <span className="font-medium text-admin-fg-strong">{r.name}</span> },
    { key: 'type', header: 'Type', render: (r) => <Badge variant="default" intensity="tinted" className="capitalize">{r.type}</Badge> },
    { key: 'contactEmail', header: 'Email', render: (r) => <span className="text-sm text-admin-fg-default">{r.contactEmail}</span> },
    { key: 'organizationName', header: 'Organization', render: (r) => <span className="text-sm text-admin-fg-default">{r.organizationName || '-'}</span> },
    { key: 'status', header: 'Status', render: (r) => { const b = STATUS_BADGE[r.status] ?? { label: r.status, variant: 'default' as const }; return <Badge variant={b.variant} intensity="tinted">{b.label}</Badge>; } },
    { key: 'createdAt', header: 'Created', render: (r) => <span className="text-xs text-admin-fg-muted">{new Date(r.createdAt).toLocaleDateString()}</span> },
  ];

  const cohortColumns: Column<Cohort>[] = [
    { key: 'name', header: 'Cohort', render: (r) => <span className="font-medium text-admin-fg-strong">{r.name}</span> },
    { key: 'examTypeCode', header: 'Exam', render: (r) => <span className="text-sm uppercase text-admin-fg-default">{r.examTypeCode}</span> },
    { key: 'seats', header: 'Seats', render: (r) => <span className="text-sm tabular-nums text-admin-fg-default">{r.enrolledCount}/{r.maxSeats}</span> },
    { key: 'status', header: 'Status', render: (r) => { const b = STATUS_BADGE[r.status] ?? { label: r.status, variant: 'default' as const }; return <Badge variant={b.variant} intensity="tinted">{b.label}</Badge>; } },
    { key: 'dates', header: 'Period', render: (r) => <span className="text-xs text-admin-fg-muted">{r.startDate || '-'} → {r.endDate || '-'}</span> },
  ];

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Enterprise' },
  ];

  if (!SPONSOR_PORTAL_ENABLED) {
    return (
      <AdminTableLayout
        title="Enterprise Channel"
        description="Sponsor and cohort management is held until the sponsor launch gate is explicitly opened."
        breadcrumbs={breadcrumbs}
      >
        <div className="p-6">
          <EmptyState
            illustration={<Building2 />}
            title="Sponsor portal not yet launched"
            description="Sponsor finance, learner attribution, reporting, contracts, and privacy evidence are not launch-ready. Backend sponsor and enterprise endpoints are disabled by default with the same feature gate."
          />
        </div>
      </AdminTableLayout>
    );
  }

  return (
    <AdminTableLayout
      title="Enterprise Channel"
      description="Manage sponsors, cohorts, and seat allocation across enterprise customers."
      eyebrow="Operations"
      breadcrumbs={breadcrumbs}
      actions={
        tab === 'sponsors' ? (
          <Button size="md" onClick={() => setShowCreate(true)}>
            <Plus className="mr-1 h-4 w-4" /> New Sponsor
          </Button>
        ) : (
          <Button size="md" onClick={() => setShowCohortCreate(true)}>
            <Plus className="mr-1 h-4 w-4" /> New Cohort
          </Button>
        )
      }
      banner={
        <div className="space-y-4">
          {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

          <KpiStrip>
            <KpiTile label="Sponsors" value={sponsors.length} icon={<Building2 className="h-4 w-4" />} tone="primary" />
            <KpiTile
              label="Active Cohorts"
              value={cohorts.filter((c) => c.status === 'active').length}
              icon={<GraduationCap className="h-4 w-4" />}
              tone="success"
            />
            <KpiTile
              label="Total Seats"
              value={cohorts.reduce((s, c) => s + c.maxSeats, 0)}
              tone="info"
            />
          </KpiStrip>

          <div
            role="tablist"
            aria-label="Enterprise sections"
            className="flex gap-2"
          >
            <Button
              role="tab"
              aria-selected={tab === 'sponsors'}
              variant={tab === 'sponsors' ? 'primary' : 'secondary'}
              size="sm"
              onClick={() => setTab('sponsors')}
            >
              <Building2 className="mr-1 h-4 w-4" /> Sponsors
            </Button>
            <Button
              role="tab"
              aria-selected={tab === 'cohorts'}
              variant={tab === 'cohorts' ? 'primary' : 'secondary'}
              size="sm"
              onClick={() => setTab('cohorts')}
            >
              <GraduationCap className="mr-1 h-4 w-4" /> Cohorts
            </Button>
          </div>
        </div>
      }
    >
      <AsyncStateWrapper status={status} errorMessage="Failed to load enterprise data." onRetry={loadData}>
        <div className="p-4">
          {tab === 'sponsors' && (
            <DataTable
              columns={sponsorColumns}
              data={sponsors}
              keyExtractor={(row) => row.id}
              selectable
              selectedKeys={selectedKeys}
              onSelectionChange={setSelectedKeys}
            />
          )}
          {tab === 'sponsors' && selectedKeys.size > 0 && (
            <BulkActionBar
              selectedCount={selectedKeys.size}
              onClearSelection={() => setSelectedKeys(new Set())}
              actions={[
                { key: 'archive', label: 'Archive selected', variant: 'danger', onClick: () => {} },
              ]}
            />
          )}
          {tab === 'cohorts' && <DataTable columns={cohortColumns} data={cohorts} keyExtractor={(row) => row.id} />}
        </div>
      </AsyncStateWrapper>

      {/* Create Sponsor Modal */}
      {showCreate && (
        <Modal open={showCreate} onClose={() => setShowCreate(false)} title="Create Sponsor">
          <div className="space-y-4 p-4">
            <div>
              <label className="text-sm font-medium text-admin-fg-strong">Name</label>
              <Input value={newSponsorName} onChange={(e) => setNewSponsorName(e.target.value)} placeholder="Sponsor name" />
            </div>
            <div>
              <label className="text-sm font-medium text-admin-fg-strong">Type</label>
              <Select value={newSponsorType} onChange={(e) => setNewSponsorType(e.target.value)} options={[{ value: 'employer', label: 'Employer' }, { value: 'institution', label: 'Institution' }, { value: 'parent', label: 'Parent' }]} />
            </div>
            <div>
              <label className="text-sm font-medium text-admin-fg-strong">Contact Email</label>
              <Input type="email" value={newSponsorEmail} onChange={(e) => setNewSponsorEmail(e.target.value)} placeholder="contact@example.com" />
            </div>
            <div>
              <label className="text-sm font-medium text-admin-fg-strong">Organization (optional)</label>
              <Input value={newSponsorOrg} onChange={(e) => setNewSponsorOrg(e.target.value)} placeholder="Organization name" />
            </div>
            <div className="flex justify-end gap-2">
              <Button variant="secondary" onClick={() => setShowCreate(false)}>Cancel</Button>
              <Button onClick={handleCreateSponsor} disabled={submitting}>{submitting ? 'Creating…' : 'Create'}</Button>
            </div>
          </div>
        </Modal>
      )}

      {/* Create Cohort Modal */}
      {showCohortCreate && (
        <Modal open={showCohortCreate} onClose={() => setShowCohortCreate(false)} title="Create Cohort">
          <div className="space-y-4 p-4">
            <div>
              <label className="text-sm font-medium text-admin-fg-strong">Sponsor</label>
              <Select value={cohortSponsorId} onChange={(e) => setCohortSponsorId(e.target.value)} options={[{ value: '', label: 'Select sponsor…' }, ...sponsors.map((s) => ({ value: s.id, label: s.name }))]} />
            </div>
            <div>
              <label className="text-sm font-medium text-admin-fg-strong">Cohort Name</label>
              <Input value={cohortName} onChange={(e) => setCohortName(e.target.value)} placeholder="2026 Q2 Nursing Batch" />
            </div>
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="text-sm font-medium text-admin-fg-strong">Exam Type</label>
                <Select value={cohortExamType} onChange={(e) => setCohortExamType(e.target.value)} options={[{ value: 'oet', label: 'OET' }]} />
              </div>
              <div>
                <label className="text-sm font-medium text-admin-fg-strong">Max Seats</label>
                <Input type="number" value={cohortMaxSeats} onChange={(e) => setCohortMaxSeats(e.target.value)} />
              </div>
            </div>
            <div className="flex justify-end gap-2">
              <Button variant="secondary" onClick={() => setShowCohortCreate(false)}>Cancel</Button>
              <Button onClick={handleCreateCohort} disabled={submitting}>{submitting ? 'Creating…' : 'Create'}</Button>
            </div>
          </div>
        </Modal>
      )}
    </AdminTableLayout>
  );
}
