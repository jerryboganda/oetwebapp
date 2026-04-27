'use client';

import Link from 'next/link';
import { useEffect, useMemo, useState } from 'react';
import { CheckCircle2, MailPlus, Search, ShieldAlert, ShieldCheck, Upload, UserMinus, Users, UserX } from 'lucide-react';
import { AdminRoutePanel, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { Pagination } from '@/components/ui/pagination';
import { inviteAdminUser } from '@/lib/api';
import { getAdminUsersPageData } from '@/lib/admin';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { useProfessions } from '@/lib/hooks/use-professions';
import type { AdminUserRow, AdminUsersPageData } from '@/lib/types/admin';

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
  professionId: '',
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
  const [summary, setSummary] = useState<AdminUsersPageData['summary'] | undefined>(undefined);
  const [inviteForm, setInviteForm] = useState<InviteFormState>(defaultInviteForm);
  const [isInviteOpen, setIsInviteOpen] = useState(false);
  const [isInviting, setIsInviting] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);
  const { options: professionOptions } = useProfessions();

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
        setSummary(result.summary);
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

  const totalPages = Math.max(1, Math.ceil(total / pageSize));

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
            {user.profession ? (
              <p className="text-xs text-muted">Profession: {user.profession}</p>
            ) : null}
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
          <div className="flex flex-wrap gap-1">
            <Badge variant={user.status === 'active' ? 'success' : user.status === 'deleted' ? 'danger' : 'muted'}>
              {user.status}
            </Badge>
            {user.lockedOut ? <Badge variant="danger">Locked</Badge> : null}
            {user.mfaEnabled ? <Badge variant="success">MFA</Badge> : null}
          </div>
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
      {
        key: 'createdAt',
        header: 'Created',
        render: (user) => (
          <span className="text-sm text-muted">
            {user.createdAt ? new Date(user.createdAt).toLocaleDateString() : '-'}
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

      <div className="flex flex-wrap gap-1.5">
        <Badge variant={user.status === 'active' ? 'success' : user.status === 'deleted' ? 'danger' : 'muted'}>
          {user.status}
        </Badge>
        {user.lockedOut ? <Badge variant="danger">Locked</Badge> : null}
        {user.mfaEnabled ? <Badge variant="success">MFA</Badge> : null}
      </div>

      <div className="grid grid-cols-2 gap-3 text-sm">
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Last login</p>
          <p className="mt-1 font-medium text-navy">{user.lastLogin ? new Date(user.lastLogin).toLocaleString() : 'Never'}</p>
        </div>
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Created</p>
          <p className="mt-1 font-medium text-navy">{user.createdAt ? new Date(user.createdAt).toLocaleDateString() : '-'}</p>
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
    setSummary(result.summary);
    setPageStatus(result.total > 0 ? 'success' : 'empty');
  }

  async function handleInviteUser() {
    const name = inviteForm.name.trim();
    const email = inviteForm.email.trim();
    const emailLooksValid = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email);
    if (!email || !name) {
      setToast({ variant: 'error', message: 'Name and email are required.' });
      return;
    }
    if (!emailLooksValid) {
      setToast({ variant: 'error', message: 'Please enter a valid email address.' });
      return;
    }
    if (inviteForm.role !== 'admin' && !inviteForm.professionId) {
      setToast({ variant: 'error', message: 'Please pick a profession for this account.' });
      return;
    }
    setIsInviting(true);
    try {
      const result = await inviteAdminUser({
        name,
        email,
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

  const summaryCards = [
    { label: 'Active', value: summary?.active ?? users.filter((u) => u.status === 'active').length, icon: CheckCircle2, tone: 'text-emerald-600' },
    { label: 'Suspended', value: summary?.suspended ?? users.filter((u) => u.status === 'suspended').length, icon: UserMinus, tone: 'text-amber-600' },
    { label: 'Deleted', value: summary?.deleted ?? users.filter((u) => u.status === 'deleted').length, icon: UserX, tone: 'text-rose-600' },
    { label: 'MFA enabled', value: summary?.mfaEnabled ?? users.filter((u) => u.mfaEnabled).length, icon: ShieldCheck, tone: 'text-emerald-600' },
    { label: 'Locked out', value: summary?.lockedOut ?? users.filter((u) => u.lockedOut).length, icon: ShieldAlert, tone: 'text-rose-600' },
  ];

  return (
    <AdminRouteWorkspace role="main" aria-label="User operations">
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <header className="flex flex-col gap-4 border-b border-border/60 pb-4 md:flex-row md:items-end md:justify-between">
        <div className="space-y-1">
          <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-muted">Admin workspace</p>
          <h1 className="text-2xl font-semibold text-navy">User Operations</h1>
          <p className="max-w-2xl text-sm text-muted">
            Manage learner, expert, and admin accounts. Invite, suspend, restore, audit, and recover access - all backed by the live admin API and full audit trail.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Link href="/admin/users/import">
            <Button variant="outline" className="gap-2">
              <Upload className="h-4 w-4" />
              Bulk Import
            </Button>
          </Link>
          <Button onClick={() => setIsInviteOpen(true)} className="gap-2">
            <MailPlus className="h-4 w-4" />
            Invite User
          </Button>
        </div>
      </header>

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
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
          {summaryCards.map(({ label, value, icon: Icon, tone }) => (
            <div key={label} className="rounded-2xl border border-border/60 bg-surface p-4 shadow-sm">
              <div className="flex items-start justify-between gap-2">
                <div>
                  <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">{label}</p>
                  <p className="mt-1 text-2xl font-semibold text-navy">{value ?? 0}</p>
                </div>
                <Icon className={`h-5 w-5 ${tone}`} aria-hidden="true" />
              </div>
            </div>
          ))}
        </div>

        <AdminRoutePanel title="Directory" description={`Showing ${users.length} of ${total.toLocaleString()} accounts (page ${page} of ${totalPages}). Filter by role, status, or free-text search across name, email, and ID.`}>
          <div className="max-w-sm">
            <div className="relative">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" />
              <Input placeholder="Search by name, email, or ID" value={searchQuery} onChange={(event) => { setPage(1); setSearchQuery(event.target.value); }} className="pl-9" />
            </div>
          </div>
          <FilterBar groups={filterGroups} selected={filters} onChange={handleFilterChange} onClear={() => { setPage(1); setFilters({ role: [], status: [] }); setSearchQuery(''); }} />
          <Pagination
            page={page}
            pageSize={pageSize}
            total={total}
            onPageChange={setPage}
            onPageSizeChange={setPageSize}
            itemLabel="user"
            itemLabelPlural="users"
          />
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
              options={[{ value: '', label: 'Select a profession...' }, ...professionOptions]}
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
