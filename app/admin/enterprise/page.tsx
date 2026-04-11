'use client';

import { useEffect, useState } from 'react';
import { Building2, Users, Plus, ChevronRight, GraduationCap } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteSummaryCard, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Toast, InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { Card, CardContent } from '@/components/ui/card';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { analytics } from '@/lib/analytics';

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

async function adminRequest<T = unknown>(path: string, init?: RequestInit): Promise<T> {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}${path}`, {
    ...init,
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}`, ...init?.headers },
  });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

const STATUS_BADGE: Record<string, { label: string; variant: 'default' | 'success' | 'destructive' | 'outline' }> = {
  active: { label: 'Active', variant: 'success' },
  draft: { label: 'Draft', variant: 'outline' },
  completed: { label: 'Completed', variant: 'default' },
  archived: { label: 'Archived', variant: 'destructive' },
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

  useEffect(() => {
    analytics.track('admin_view', { page: 'enterprise' });
    loadData();
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
    { key: 'name', header: 'Name', render: (r) => <span className="font-medium">{r.name}</span> },
    { key: 'type', header: 'Type', render: (r) => <Badge variant="outline" className="capitalize">{r.type}</Badge> },
    { key: 'contactEmail', header: 'Email', render: (r) => <span className="text-sm">{r.contactEmail}</span> },
    { key: 'organizationName', header: 'Organization', render: (r) => <span className="text-sm">{r.organizationName || '—'}</span> },
    { key: 'status', header: 'Status', render: (r) => { const b = STATUS_BADGE[r.status] ?? { label: r.status, variant: 'outline' as const }; return <Badge variant={b.variant}>{b.label}</Badge>; } },
    { key: 'createdAt', header: 'Created', render: (r) => <span className="text-xs text-muted">{new Date(r.createdAt).toLocaleDateString()}</span> },
  ];

  const cohortColumns: Column<Cohort>[] = [
    { key: 'name', header: 'Cohort', render: (r) => <span className="font-medium">{r.name}</span> },
    { key: 'examTypeCode', header: 'Exam', render: (r) => <span className="uppercase text-sm">{r.examTypeCode}</span> },
    { key: 'seats', header: 'Seats', render: (r) => <span className="text-sm">{r.enrolledCount}/{r.maxSeats}</span> },
    { key: 'status', header: 'Status', render: (r) => { const b = STATUS_BADGE[r.status] ?? { label: r.status, variant: 'outline' as const }; return <Badge variant={b.variant}>{b.label}</Badge>; } },
    { key: 'dates', header: 'Period', render: (r) => <span className="text-xs text-muted">{r.startDate || '—'} → {r.endDate || '—'}</span> },
  ];

  return (
    <AdminRouteWorkspace>
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <AdminRouteSectionHeader title="Enterprise Channel" icon={<Building2 className="w-5 h-5" />} />

      <div className="grid grid-cols-3 gap-4 mb-6">
        <AdminRouteSummaryCard label="Sponsors" value={sponsors.length} />
        <AdminRouteSummaryCard label="Active Cohorts" value={cohorts.filter((c) => c.status === 'active').length} />
        <AdminRouteSummaryCard label="Total Seats" value={cohorts.reduce((s, c) => s + c.maxSeats, 0)} />
      </div>

      <div className="flex gap-2 mb-4">
        <Button variant={tab === 'sponsors' ? 'default' : 'outline'} size="sm" onClick={() => setTab('sponsors')}>
          <Building2 className="w-4 h-4 mr-1" /> Sponsors
        </Button>
        <Button variant={tab === 'cohorts' ? 'default' : 'outline'} size="sm" onClick={() => setTab('cohorts')}>
          <GraduationCap className="w-4 h-4 mr-1" /> Cohorts
        </Button>
        <div className="flex-1" />
        {tab === 'sponsors' && (
          <Button size="sm" onClick={() => setShowCreate(true)}>
            <Plus className="w-4 h-4 mr-1" /> New Sponsor
          </Button>
        )}
        {tab === 'cohorts' && (
          <Button size="sm" onClick={() => setShowCohortCreate(true)}>
            <Plus className="w-4 h-4 mr-1" /> New Cohort
          </Button>
        )}
      </div>

      <AsyncStateWrapper status={status} errorMessage="Failed to load enterprise data." onRetry={loadData}>
        <AdminRoutePanel>
          {tab === 'sponsors' && <DataTable columns={sponsorColumns} data={sponsors} />}
          {tab === 'cohorts' && <DataTable columns={cohortColumns} data={cohorts} />}
        </AdminRoutePanel>
      </AsyncStateWrapper>

      {/* Create Sponsor Modal */}
      {showCreate && (
        <Modal onClose={() => setShowCreate(false)} title="Create Sponsor">
          <div className="space-y-4 p-4">
            <div>
              <label className="text-sm font-medium">Name</label>
              <Input value={newSponsorName} onChange={(e) => setNewSponsorName(e.target.value)} placeholder="Sponsor name" />
            </div>
            <div>
              <label className="text-sm font-medium">Type</label>
              <Select value={newSponsorType} onChange={(e) => setNewSponsorType(e.target.value)}>
                <option value="employer">Employer</option>
                <option value="institution">Institution</option>
                <option value="parent">Parent</option>
              </Select>
            </div>
            <div>
              <label className="text-sm font-medium">Contact Email</label>
              <Input type="email" value={newSponsorEmail} onChange={(e) => setNewSponsorEmail(e.target.value)} placeholder="contact@example.com" />
            </div>
            <div>
              <label className="text-sm font-medium">Organization (optional)</label>
              <Input value={newSponsorOrg} onChange={(e) => setNewSponsorOrg(e.target.value)} placeholder="Organization name" />
            </div>
            <div className="flex justify-end gap-2">
              <Button variant="outline" onClick={() => setShowCreate(false)}>Cancel</Button>
              <Button onClick={handleCreateSponsor} disabled={submitting}>{submitting ? 'Creating…' : 'Create'}</Button>
            </div>
          </div>
        </Modal>
      )}

      {/* Create Cohort Modal */}
      {showCohortCreate && (
        <Modal onClose={() => setShowCohortCreate(false)} title="Create Cohort">
          <div className="space-y-4 p-4">
            <div>
              <label className="text-sm font-medium">Sponsor</label>
              <Select value={cohortSponsorId} onChange={(e) => setCohortSponsorId(e.target.value)}>
                <option value="">Select sponsor…</option>
                {sponsors.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
              </Select>
            </div>
            <div>
              <label className="text-sm font-medium">Cohort Name</label>
              <Input value={cohortName} onChange={(e) => setCohortName(e.target.value)} placeholder="2026 Q2 Nursing Batch" />
            </div>
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="text-sm font-medium">Exam Type</label>
                <Select value={cohortExamType} onChange={(e) => setCohortExamType(e.target.value)}>
                  <option value="oet">OET</option>
                </Select>
              </div>
              <div>
                <label className="text-sm font-medium">Max Seats</label>
                <Input type="number" value={cohortMaxSeats} onChange={(e) => setCohortMaxSeats(e.target.value)} />
              </div>
            </div>
            <div className="flex justify-end gap-2">
              <Button variant="outline" onClick={() => setShowCohortCreate(false)}>Cancel</Button>
              <Button onClick={handleCreateCohort} disabled={submitting}>{submitting ? 'Creating…' : 'Create'}</Button>
            </div>
          </div>
        </Modal>
      )}
    </AdminRouteWorkspace>
  );
}
