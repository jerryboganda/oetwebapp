'use client';

import Link from 'next/link';
import { useEffect, useMemo, useState } from 'react';
import { MailPlus, Search, Users } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteSummaryCard, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { inviteAdminUser } from '@/lib/api';
import { getAdminUsersPageData } from '@/lib/admin';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type { AdminUserRow } from '@/lib/types/admin';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

interface InviteFormState {
  name: string;
  email: string;
  role: 'learner' | 'expert' | 'admin';
  professionId: string;
}

const defaultInviteForm: InviteFormState = {
  name: '',
  email: '',
  role: 'learner',
  professionId: 'nursing',
};

export default function UsersPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [filters, setFilters] = useState<Record<string, string[]>>({ role: [], status: [] });
  const [searchQuery, setSearchQuery] = useState('');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [total, setTotal] = useState(0);
  const [users, setUsers] = useState<AdminUserRow[]>([]);
  const [inviteForm, setInviteForm] = useState<InviteFormState>(defaultInviteForm);
  const [isInviteOpen, setIsInviteOpen] = useState(false);
  const [isInviting, setIsInviting] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  const selectedRole = filters.role?.[0];
  const selectedStatus = filters.status?.[0];

  useEffect(() => {
    let cancelled = false;

    async function loadUsers() {
      setPageStatus('loading');
      try {
        const result = await getAdminUsersPageData({
          role: selectedRole,
          status: selectedStatus,
          search: searchQuery || undefined,
          page,
          pageSize,
        });

        if (cancelled) return;

        setUsers(result.items);
        setTotal(result.total);
        setPage(result.page);
        setPageSize(result.pageSize);
        setPageStatus(result.total > 0 ? 'success' : 'empty');
      } catch (error) {
        console.error(error);
        if (!cancelled) {
          setPageStatus('error');
          setToast({ variant: 'error', message: 'Unable to load users right now.' });
        }
      }
    }

    const handle = window.setTimeout(loadUsers, 200);
    return () => {
      cancelled = true;
      window.clearTimeout(handle);
    };
  }, [page, pageSize, selectedRole, selectedStatus, searchQuery]);

  const filterGroups: FilterGroup[] = [
    {
      id: 'role',
      label: 'Role',
      options: [
        { id: 'learner', label: 'Learner' },
        { id: 'expert', label: 'Expert' },
        { id: 'admin', label: 'Admin' },
      ],
    },
    {
      id: 'status',
      label: 'Status',
      options: [
        { id: 'active', label: 'Active' },
        { id: 'suspended', label: 'Suspended' },
        { id: 'deleted', label: 'Deleted' },
      ],
    },
  ];

  const roleCounts = useMemo(() => ({
    learners: users.filter((user) => user.role === 'learner').length,
    experts: users.filter((user) => user.role === 'expert').length,
    admins: users.filter((user) => user.role === 'admin').length,
  }), [users]);

  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  const pageStart = total === 0 ? 0 : ((page - 1) * pageSize) + 1;
  const pageEnd = total === 0 ? 0 : Math.min(total, page * pageSize);

  const columns: Column<AdminUserRow>[] = useMemo(
    () => [
      {
        key: 'name',
        header: 'User',
        render: (user) => (
          <div className="space-y-1">
            <Link href={`/admin/users/${user.id}`} className="font-medium text-primary hover:underline">
              {user.name}
            </Link>
            <p className="text-sm text-muted">{user.email}</p>
          </div>
        ),
      },
      {
        key: 'role',
        header: 'Role',
        render: (user) => (
          <Badge variant={user.role === 'admin' ? 'danger' : user.role === 'expert' ? 'warning' : 'default'}>
            {user.role}
          </Badge>
        ),
      },
      {
        key: 'status',
        header: 'Status',
        render: (user) => (
          <Badge variant={user.status === 'active' ? 'success' : user.status === 'deleted' ? 'danger' : 'muted'}>
            {user.status}
          </Badge>
        ),
      },
      {
        key: 'lastLogin',
        header: 'Last Login',
        render: (user) => (
          <span className="text-sm text-muted">
            {user.lastLogin ? new Date(user.lastLogin).toLocaleString() : 'Never'}
          </span>
        ),
      },
    ],
    [],
  );

  const mobileCardRender = (user: AdminUserRow) => (
    <div className="space-y-3">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <Link href={`/admin/users/${user.id}`} className="truncate font-semibold text-primary hover:underline">
            {user.name}
          </Link>
          <p className="truncate text-sm text-muted">{user.email}</p>
        </div>
        <Badge variant={user.role === 'admin' ? 'danger' : user.role === 'expert' ? 'warning' : 'default'}>
          {user.role}
        </Badge>
      </div>

      <div className="grid grid-cols-2 gap-3 text-sm">
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Status</p>
          <p className="mt-1 font-medium text-navy">{user.status}</p>
        </div>
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Last login</p>
          <p className="mt-1 font-medium text-navy">{user.lastLogin ? new Date(user.lastLogin).toLocaleString() : 'Never'}</p>
        </div>
      </div>

      <div className="flex justify-end">
        <Link href={`/admin/users/${user.id}`} className="inline-flex items-center justify-center rounded-2xl border border-border/60 bg-surface px-4 py-2 text-sm font-semibold text-navy shadow-sm hover:bg-surface">
          View profile
        </Link>
      </div>
    </div>
  );

  function handleFilterChange(groupId: string, optionId: string) {
    setPage(1);
    setFilters((current) => ({
      ...current,
      [groupId]: current[groupId]?.includes(optionId) ? [] : [optionId],
    }));
  }

  async function reloadUsers() {
    const result = await getAdminUsersPageData({
      role: selectedRole,
      status: selectedStatus,
      search: searchQuery || undefined,
      page,
      pageSize,
    });
    setUsers(result.items);
    setTotal(result.total);
    setPage(result.page);
    setPageSize(result.pageSize);
    setPageStatus(result.total > 0 ? 'success' : 'empty');
  }

  async function handleInviteUser() {
    setIsInviting(true);
    try {
      const result = await inviteAdminUser({
        name: inviteForm.name,
        email: inviteForm.email,
        role: inviteForm.role,
        professionId: inviteForm.role === 'admin' ? undefined : inviteForm.professionId,
      });

      await reloadUsers();
      setIsInviteOpen(false);
      setInviteForm(defaultInviteForm);
      setToast({
        variant: 'success',
        message: `Invitation sent to ${result.email}. Setup expires ${new Date(result.invitation.expiresAt).toLocaleString()}.`,
      });
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to send this invitation.' });
    } finally {
      setIsInviting(false);
    }
  }

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <AdminRouteWorkspace role="main" aria-label="User operations">
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <AdminRouteSectionHeader
        title="User Operations"
        description="Manage learner, expert, and admin accounts with real invite, access, and status controls."
        actions={
          <Button onClick={() => setIsInviteOpen(true)} className="gap-2">
            <MailPlus className="h-4 w-4" />
            Invite User
          </Button>
        }
      />

      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => window.location.reload()}
        emptyContent={
          <EmptyState
            icon={<Users className="h-10 w-10 text-muted" />}
            title="No users found"
            description="Invite the first learner, expert, or admin account to start operating the platform."
            action={{ label: 'Invite User', onClick: () => setIsInviteOpen(true) }}
          />
        }
      >
        <div className="grid gap-4 md:grid-cols-3">
          <AdminRouteSummaryCard label="Learners" value={roleCounts.learners} hint="Accounts currently visible in the active filter window." icon={Users} />
          <AdminRouteSummaryCard label="Experts" value={roleCounts.experts} hint="Operational reviewer accounts in the current result set." icon={Users} accent="amber" />
          <AdminRouteSummaryCard label="Admins" value={roleCounts.admins} hint="Administrative accounts visible to this query." icon={Users} accent="rose" />
        </div>

        <AdminRoutePanel title="Directory" description="This directory is powered by the live admin users endpoint with real role and status filtering.">
          <div className="max-w-sm">
            <div className="relative">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" />
              <Input placeholder="Search by name, email, or ID" value={searchQuery} onChange={(event) => { setPage(1); setSearchQuery(event.target.value); }} className="pl-9" />
            </div>
          </div>
          <FilterBar groups={filterGroups} selected={filters} onChange={handleFilterChange} onClear={() => { setPage(1); setFilters({ role: [], status: [] }); setSearchQuery(''); }} />
          <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
            <div className="text-sm text-muted">
              Showing {pageStart}-{pageEnd} of {total} users
            </div>
            <div className="flex flex-col gap-3 md:flex-row md:items-center md:gap-4">
              <div className="min-w-36">
                <Select
                  label="Rows per page"
                  value={String(pageSize)}
                  onChange={(event) => { setPage(1); setPageSize(Number(event.target.value)); }}
                  options={[
                    { value: '10', label: '10' },
                    { value: '20', label: '20' },
                    { value: '50', label: '50' },
                  ]}
                />
              </div>
              <div className="flex items-center gap-2 pt-5 md:pt-0">
                <Button variant="outline" onClick={() => setPage((current) => Math.max(1, current - 1))} disabled={page <= 1}>
                  Previous
                </Button>
                <span className="text-sm text-muted">
                  Page {page} of {totalPages}
                </span>
                <Button variant="outline" onClick={() => setPage((current) => Math.min(totalPages, current + 1))} disabled={page >= totalPages}>
                  Next
                </Button>
              </div>
            </div>
          </div>
          <DataTable columns={columns} data={users} keyExtractor={(user) => user.id} mobileCardRender={mobileCardRender} />
        </AdminRoutePanel>
      </AsyncStateWrapper>

      <Modal open={isInviteOpen} onClose={() => setIsInviteOpen(false)} title="Invite User">
        <div className="space-y-4 py-2">
          <Input label="Full Name" value={inviteForm.name} onChange={(event) => setInviteForm((current) => ({ ...current, name: event.target.value }))} />
          <Input label="Email Address" type="email" value={inviteForm.email} onChange={(event) => setInviteForm((current) => ({ ...current, email: event.target.value }))} />
          <Select
            label="Role"
            value={inviteForm.role}
            onChange={(event) => setInviteForm((current) => ({ ...current, role: event.target.value as InviteFormState['role'] }))}
            options={[
              { value: 'learner', label: 'Learner' },
              { value: 'expert', label: 'Expert' },
              { value: 'admin', label: 'Admin' },
            ]}
          />
          {inviteForm.role !== 'admin' ? (
            <Select
              label={inviteForm.role === 'expert' ? 'Primary Specialty' : 'Profession'}
              value={inviteForm.professionId}
              onChange={(event) => setInviteForm((current) => ({ ...current, professionId: event.target.value }))}
              options={[
                { value: 'nursing', label: 'Nursing' },
                { value: 'medicine', label: 'Medicine' },
                { value: 'dentistry', label: 'Dentistry' },
                { value: 'pharmacy', label: 'Pharmacy' },
                { value: 'physiotherapy', label: 'Physiotherapy' },
              ]}
            />
          ) : null}

          <div className="rounded-xl border border-border bg-background-light p-3 text-sm text-muted">
            The backend will create the account, send a setup challenge, and log the invitation in audit history.
          </div>

          <div className="flex justify-end gap-3 border-t border-border pt-4">
            <Button variant="outline" onClick={() => setIsInviteOpen(false)}>
              Cancel
            </Button>
            <Button onClick={handleInviteUser} loading={isInviting}>
              Send Invite
            </Button>
          </div>
        </div>
      </Modal>
    </AdminRouteWorkspace>
  );
}
