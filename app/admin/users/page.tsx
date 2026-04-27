'use client';

import Link from 'next/link';
import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  CheckCircle2,
  FileKey,
  GraduationCap,
  KeyRound,
  MailPlus,
  Plus,
  Search,
  ShieldAlert,
  ShieldCheck,
  Trash2,
  Upload,
  UserMinus,
  Users,
  UserX,
} from 'lucide-react';
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
import {
  applyPermissionTemplate,
  createPermissionTemplate,
  deletePermissionTemplate,
  fetchAdminPermissions,
  fetchAllPermissions,
  fetchPermissionTemplates,
  inviteAdminUser,
  updateAdminPermissions,
} from '@/lib/api';
import { getAdminUsersPageData } from '@/lib/admin';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { useProfessions } from '@/lib/hooks/use-professions';
import type {
  AdminPermissionsResponse,
  AdminUserRow,
  AdminUsersPageData,
  PermissionTemplate,
} from '@/lib/types/admin';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;
type HubTab = 'all' | 'learners' | 'tutors' | 'admins';
// API still uses the legacy "expert" role string. We translate at the UI edge:
// hub label "Tutors" ↔ API role "expert".
const ROLE_FOR_TAB: Record<HubTab, string | undefined> = {
  all: undefined,
  learners: 'learner',
  tutors: 'expert',
  admins: 'admin',
};

interface InviteFormState {
  name: string;
  email: string;
  // UI uses "tutor"; mapped to API "expert" on submit.
  role: 'learner' | 'tutor' | 'admin';
  professionId: string;
}

const defaultInviteForm: InviteFormState = {
  name: '',
  email: '',
  role: 'learner',
  professionId: '',
};

const PERM_GROUPS: Record<string, string> = {
  'content:read': 'Content',
  'content:write': 'Content',
  'content:publish': 'Content',
  'content:editor_review': 'Content',
  'content:publisher_approval': 'Content',
  'billing:read': 'Billing',
  'billing:write': 'Billing',
  'users:read': 'Users',
  'users:write': 'Users',
  'review_ops': 'Operations',
  'quality_analytics': 'Operations',
  'ai_config': 'System',
  'feature_flags': 'System',
  'audit_logs': 'System',
  'system_admin': 'System',
  'manage_permissions': 'System',
};

const PRESETS: Record<string, string[] | 'all'> = {
  'Content Editor': ['content:read', 'content:write'],
  'Content Publisher': ['content:read', 'content:write', 'content:publish'],
  'Billing Admin': ['billing:read', 'billing:write'],
  'Quality Manager': ['review_ops', 'quality_analytics'],
  'System Admin': 'all',
};

function permLabel(key: string) {
  return key
    .replace(/_/g, ' ')
    .replace(/:/g, ' · ')
    .replace(/\b\w/g, (c) => c.toUpperCase());
}

function uiRoleLabel(role: string) {
  return role === 'expert' ? 'tutor' : role;
}

export default function UsersPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [tab, setTab] = useState<HubTab>('all');
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [filters, setFilters] = useState<Record<string, string[]>>({ status: [] });
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

  const selectedStatus = filters.status?.[0];
  const tabRole = ROLE_FOR_TAB[tab];

  // Read initial tab from URL ?tab=tutors|admins|learners (used by legacy redirects).
  useEffect(() => {
    if (typeof window === 'undefined') return;
    const param = new URLSearchParams(window.location.search).get('tab');
    if (param && ['all', 'learners', 'tutors', 'admins'].includes(param)) {
      setTab(param as HubTab);
    } else {
      const legacyRole = new URLSearchParams(window.location.search).get('role');
      if (legacyRole === 'expert' || legacyRole === 'tutor') setTab('tutors');
      else if (legacyRole === 'admin') setTab('admins');
      else if (legacyRole === 'learner') setTab('learners');
    }
  }, []);

  useEffect(() => {
    let cancelled = false;

    async function loadUsers() {
      setPageStatus('loading');
      try {
        const result = await getAdminUsersPageData({
          role: tabRole,
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
  }, [page, pageSize, tabRole, selectedStatus, searchQuery]);

  // Reset page when switching tabs.
  useEffect(() => {
    setPage(1);
  }, [tab]);

  const filterGroups: FilterGroup[] = [
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
            {uiRoleLabel(user.role)}
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
          {uiRoleLabel(user.role)}
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
      role: tabRole,
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
      // Translate UI "tutor" → API "expert".
      const apiRole = inviteForm.role === 'tutor' ? 'expert' : inviteForm.role;
      const result = await inviteAdminUser({
        name,
        email,
        role: apiRole as 'learner' | 'expert' | 'admin',
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

  const tabs: { id: HubTab; label: string; icon: typeof Users }[] = [
    { id: 'all', label: 'All Users', icon: Users },
    { id: 'learners', label: 'Learners', icon: Users },
    { id: 'tutors', label: 'Tutors', icon: GraduationCap },
    { id: 'admins', label: 'Admins & Permissions', icon: KeyRound },
  ];

  return (
    <AdminRouteWorkspace role="main" aria-label="User operations">
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <header className="flex flex-col gap-4 border-b border-border/60 pb-4 md:flex-row md:items-end md:justify-between">
        <div className="space-y-1">
          <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-muted">Admin workspace</p>
          <h1 className="text-2xl font-semibold text-navy">User Operations</h1>
          <p className="max-w-2xl text-sm text-muted">
            One hub for every account on the platform — learners, tutors, and admins. Invite, suspend, restore,
            audit, recover access, and manage admin permissions, all backed by the live admin API.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Link href="/admin/users/import">
            <Button variant="outline" className="gap-2">
              <Upload className="h-4 w-4" />
              Bulk Import
            </Button>
          </Link>
          <Button onClick={() => { setInviteForm({ ...defaultInviteForm, role: tab === 'tutors' ? 'tutor' : tab === 'admins' ? 'admin' : 'learner' }); setIsInviteOpen(true); }} className="gap-2">
            <MailPlus className="h-4 w-4" />
            Invite User
          </Button>
        </div>
      </header>

      <div className="flex flex-wrap gap-1 border-b border-border" role="tablist" aria-label="User type">
        {tabs.map(({ id, label, icon: Icon }) => (
          <button
            key={id}
            type="button"
            role="tab"
            aria-selected={tab === id}
            onClick={() => setTab(id)}
            className={`flex items-center gap-2 px-4 py-2 text-sm font-medium border-b-2 transition-colors ${tab === id ? 'border-primary text-primary' : 'border-transparent text-muted hover:text-foreground'}`}
          >
            <Icon className="h-4 w-4" />
            {label}
          </button>
        ))}
      </div>

      {tab === 'admins' ? (
        <AdminsAndPermissionsTab onToast={setToast} />
      ) : (
        <AsyncStateWrapper
          status={pageStatus}
          onRetry={() => window.location.reload()}
          emptyContent={
            <EmptyState
              icon={<Users className="h-10 w-10 text-muted" />}
              title={tab === 'all' ? 'No users found' : `No ${tab} found`}
              description="Invite the first account to start operating the platform."
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

          <AdminRoutePanel title="Directory" description={`Showing ${users.length} of ${total.toLocaleString()} accounts (page ${page} of ${totalPages}). Filter by status, or free-text search across name, email, and ID.`}>
            <div className="max-w-sm">
              <div className="relative">
                <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" />
                <Input placeholder="Search by name, email, or ID" value={searchQuery} onChange={(event) => { setPage(1); setSearchQuery(event.target.value); }} className="pl-9" />
              </div>
            </div>
            <FilterBar groups={filterGroups} selected={filters} onChange={handleFilterChange} onClear={() => { setPage(1); setFilters({ status: [] }); setSearchQuery(''); }} />
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
      )}

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
              { value: 'tutor', label: 'Tutor' },
              { value: 'admin', label: 'Admin' },
            ]}
          />
          {inviteForm.role !== 'admin' ? (
            <Select
              label={inviteForm.role === 'tutor' ? 'Primary Specialty' : 'Profession'}
              value={inviteForm.professionId}
              onChange={(event) => setInviteForm((current) => ({ ...current, professionId: event.target.value }))}
              options={[{ value: '', label: 'Select a profession...' }, ...professionOptions]}
            />
          ) : null}

          <div className="rounded-xl border border-border bg-background-light p-3 text-sm text-muted">
            The backend creates the account, sends a setup challenge, and logs the invitation in audit history.
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

/* ════════════════════════════════════════════════════════════════ */
/*  Admins & Permissions tab — folds the old /permissions + /roles  */
/* ════════════════════════════════════════════════════════════════ */

interface AllPermissionsResponse {
  permissions: { key: string }[];
}
interface PermissionTemplatesResponse {
  templates: PermissionTemplate[];
}
interface ApplyTemplateResponse {
  permissions: string[];
  templateName: string;
}

function AdminsAndPermissionsTab({ onToast }: { onToast: (t: ToastState) => void }) {
  const [allPerms, setAllPerms] = useState<string[]>([]);
  const [admins, setAdmins] = useState<AdminUserRow[]>([]);
  const [templates, setTemplates] = useState<PermissionTemplate[]>([]);
  const [status, setStatus] = useState<PageStatus>('loading');
  const [search, setSearch] = useState('');
  const [selectedAdmin, setSelectedAdmin] = useState<AdminUserRow | null>(null);
  const [editPerms, setEditPerms] = useState<Set<string>>(new Set());
  const [isMutating, setIsMutating] = useState(false);

  // Template management modal state.
  const [showTemplates, setShowTemplates] = useState(false);
  const [showCreateTpl, setShowCreateTpl] = useState(false);
  const [showApplyTpl, setShowApplyTpl] = useState(false);
  const [newTplName, setNewTplName] = useState('');
  const [newTplDesc, setNewTplDesc] = useState('');
  const [newTplPerms, setNewTplPerms] = useState<Set<string>>(new Set());
  const [creatingTpl, setCreatingTpl] = useState(false);

  const groups = useMemo(() => {
    const set = new Set<string>();
    for (const p of allPerms) set.add(PERM_GROUPS[p] ?? 'Other');
    return Array.from(set);
  }, [allPerms]);

  const reloadAll = useCallback(async () => {
    setStatus('loading');
    try {
      const [permsResp, adminData, tplResp] = await Promise.all([
        fetchAllPermissions() as Promise<AllPermissionsResponse>,
        getAdminUsersPageData({ role: 'admin', pageSize: 100 }),
        fetchPermissionTemplates() as Promise<PermissionTemplatesResponse>,
      ]);
      setAllPerms((permsResp.permissions ?? []).map((p) => p.key));
      setAdmins(adminData.items);
      setTemplates(tplResp.templates ?? []);
      setStatus(adminData.items.length > 0 ? 'success' : 'empty');
    } catch (err) {
      console.error(err);
      setStatus('error');
    }
  }, []);

  useEffect(() => { reloadAll(); }, [reloadAll]);

  const filtered = admins.filter(
    (a) => !search || a.name.toLowerCase().includes(search.toLowerCase()) || a.email.toLowerCase().includes(search.toLowerCase()),
  );

  async function openPermissions(user: AdminUserRow) {
    setSelectedAdmin(user);
    try {
      const raw = (await fetchAdminPermissions(user.id)) as AdminPermissionsResponse;
      const keys = (raw.permissions ?? []).map((p: string | { permission: string }) => (typeof p === 'string' ? p : p.permission));
      setEditPerms(new Set(keys));
    } catch {
      setEditPerms(new Set());
    }
  }

  function togglePermission(key: string) {
    setEditPerms((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key); else next.add(key);
      return next;
    });
  }

  function applyPreset(preset: keyof typeof PRESETS) {
    const value = PRESETS[preset];
    setEditPerms(new Set(value === 'all' ? allPerms : value));
  }

  async function savePermissions() {
    if (!selectedAdmin) return;
    setIsMutating(true);
    try {
      await updateAdminPermissions(selectedAdmin.id, Array.from(editPerms));
      onToast({ variant: 'success', message: `Permissions updated for ${selectedAdmin.name}.` });
      setSelectedAdmin(null);
    } catch (err) {
      console.error(err);
      onToast({ variant: 'error', message: 'Failed to update permissions.' });
    } finally {
      setIsMutating(false);
    }
  }

  async function handleApplyTemplate(templateId: string) {
    if (!selectedAdmin) return;
    setIsMutating(true);
    try {
      const result = (await applyPermissionTemplate(selectedAdmin.id, templateId)) as ApplyTemplateResponse;
      setEditPerms(new Set(result.permissions ?? []));
      onToast({ variant: 'success', message: `Template "${result.templateName}" applied to ${selectedAdmin.name}.` });
      setShowApplyTpl(false);
    } catch (err) {
      console.error(err);
      onToast({ variant: 'error', message: 'Failed to apply template.' });
    } finally {
      setIsMutating(false);
    }
  }

  async function handleCreateTemplate() {
    if (!newTplName.trim()) return;
    setCreatingTpl(true);
    try {
      await createPermissionTemplate(newTplName.trim(), newTplDesc.trim(), Array.from(newTplPerms));
      onToast({ variant: 'success', message: `Template "${newTplName}" created.` });
      setShowCreateTpl(false);
      setNewTplName('');
      setNewTplDesc('');
      setNewTplPerms(new Set());
      const tplResp = (await fetchPermissionTemplates()) as PermissionTemplatesResponse;
      setTemplates(tplResp.templates ?? []);
    } catch (err) {
      console.error(err);
      onToast({ variant: 'error', message: 'Failed to create template.' });
    } finally {
      setCreatingTpl(false);
    }
  }

  async function handleDeleteTemplate(id: string, name: string) {
    try {
      await deletePermissionTemplate(id);
      onToast({ variant: 'success', message: `Template "${name}" deleted.` });
      const tplResp = (await fetchPermissionTemplates()) as PermissionTemplatesResponse;
      setTemplates(tplResp.templates ?? []);
    } catch (err) {
      console.error(err);
      onToast({ variant: 'error', message: 'Failed to delete template.' });
    }
  }

  return (
    <AsyncStateWrapper
      status={status}
      onRetry={reloadAll}
      emptyContent={<EmptyState icon={<KeyRound className="h-10 w-10 text-muted" />} title="No admins" description="Invite an admin from the Invite User button to manage permissions." />}
      errorMessage="Unable to load admins or permissions."
    >
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="relative max-w-sm flex-1">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" />
          <Input placeholder="Search admins by name or email" value={search} onChange={(event) => setSearch(event.target.value)} className="pl-9" />
        </div>
        <Button variant="outline" size="sm" className="gap-2" onClick={() => setShowTemplates(true)}>
          <FileKey className="h-4 w-4" />
          Permission Templates ({templates.length})
        </Button>
      </div>

      <AdminRoutePanel title="Admins" description="Click any admin to grant or revoke individual permissions and apply templates.">
        <div className="divide-y divide-border">
          {filtered.map((admin) => (
            <div key={admin.id} className="flex items-center justify-between py-3">
              <div className="min-w-0 flex-1">
                <Link href={`/admin/users/${admin.id}`} className="text-sm font-medium text-primary hover:underline">
                  {admin.name}
                </Link>
                <p className="text-xs text-muted">{admin.email}</p>
              </div>
              <Button size="sm" variant="outline" onClick={() => openPermissions(admin)} className="gap-1.5">
                <KeyRound className="h-3.5 w-3.5" />
                Manage
              </Button>
            </div>
          ))}
          {filtered.length === 0 ? (
            <p className="py-6 text-center text-sm text-muted">No admins match your search.</p>
          ) : null}
        </div>
      </AdminRoutePanel>

      {/* Per-admin permission editor */}
      {selectedAdmin ? (
        <Modal open={!!selectedAdmin} onClose={() => setSelectedAdmin(null)} title={`Permissions — ${selectedAdmin.name}`}>
          <div className="space-y-4 max-h-[60vh] overflow-y-auto p-4">
            <div className="flex flex-wrap items-center gap-2 border-b border-border pb-3">
              <span className="text-xs font-semibold uppercase tracking-wide text-muted">Quick presets:</span>
              {(Object.keys(PRESETS) as (keyof typeof PRESETS)[]).map((preset) => (
                <Button key={preset} size="sm" variant="outline" className="h-7 text-xs" onClick={() => applyPreset(preset)}>
                  {preset}
                </Button>
              ))}
              {templates.length > 0 ? (
                <Button size="sm" variant="outline" className="h-7 text-xs gap-1" onClick={() => setShowApplyTpl(true)}>
                  <FileKey className="h-3.5 w-3.5" />
                  Apply Template
                </Button>
              ) : null}
            </div>
            {groups.map((group) => (
              <div key={group}>
                <h4 className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted">{group}</h4>
                <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                  {allPerms.filter((p) => (PERM_GROUPS[p] ?? 'Other') === group).map((perm) => (
                    <label key={perm} className="flex cursor-pointer items-center gap-2 text-sm">
                      <input
                        type="checkbox"
                        checked={editPerms.has(perm)}
                        onChange={() => togglePermission(perm)}
                        className="rounded border-border text-primary focus:ring-primary"
                      />
                      {permLabel(perm)}
                    </label>
                  ))}
                </div>
              </div>
            ))}
            <p className="text-xs text-muted">{editPerms.size} permission(s) selected.</p>
          </div>
          <div className="flex justify-end gap-2 border-t border-border px-4 pb-4 pt-4">
            <Button variant="outline" onClick={() => setSelectedAdmin(null)}>Cancel</Button>
            <Button onClick={savePermissions} loading={isMutating}>Save Permissions</Button>
          </div>
        </Modal>
      ) : null}

      {/* Apply template modal */}
      {showApplyTpl && selectedAdmin ? (
        <Modal open onClose={() => setShowApplyTpl(false)} title="Apply Template">
          <div className="divide-y divide-border p-4">
            {templates.map((tpl) => (
              <div key={tpl.id} className="flex items-center justify-between gap-3 py-3">
                <div className="min-w-0 flex-1">
                  <p className="text-sm font-medium">{tpl.name}</p>
                  {tpl.description ? <p className="text-xs text-muted">{tpl.description}</p> : null}
                  <div className="mt-1 flex flex-wrap gap-1">
                    {tpl.permissions.map((p) => (
                      <Badge key={p} variant="muted" className="text-[10px]">{permLabel(p)}</Badge>
                    ))}
                  </div>
                </div>
                <Button size="sm" onClick={() => handleApplyTemplate(tpl.id)} loading={isMutating}>Apply</Button>
              </div>
            ))}
          </div>
        </Modal>
      ) : null}

      {/* Templates manager modal */}
      {showTemplates ? (
        <Modal open onClose={() => setShowTemplates(false)} title="Permission Templates">
          <div className="space-y-4 p-4">
            <div className="flex justify-end">
              <Button size="sm" onClick={() => setShowCreateTpl(true)} className="gap-1.5">
                <Plus className="h-4 w-4" />
                New Template
              </Button>
            </div>
            <div className="divide-y divide-border rounded-xl border border-border">
              {templates.length === 0 ? (
                <p className="py-6 text-center text-sm text-muted">No templates yet — create one to standardize role bundles.</p>
              ) : null}
              {templates.map((tpl) => (
                <div key={tpl.id} className="flex items-start justify-between gap-3 px-4 py-3">
                  <div className="min-w-0 flex-1">
                    <p className="text-sm font-medium text-navy">{tpl.name}</p>
                    {tpl.description ? <p className="text-xs text-muted">{tpl.description}</p> : null}
                    <div className="mt-1 flex flex-wrap gap-1">
                      {tpl.permissions.map((p) => (
                        <Badge key={p} variant="muted" className="text-[10px]">{permLabel(p)}</Badge>
                      ))}
                    </div>
                  </div>
                  <Button size="sm" variant="ghost" className="text-destructive" onClick={() => handleDeleteTemplate(tpl.id, tpl.name)}>
                    <Trash2 className="h-3.5 w-3.5" />
                  </Button>
                </div>
              ))}
            </div>
          </div>
        </Modal>
      ) : null}

      {/* Create template modal */}
      {showCreateTpl ? (
        <Modal open onClose={() => setShowCreateTpl(false)} title="Create Permission Template">
          <div className="space-y-4 p-4">
            <Input label="Name" value={newTplName} onChange={(e) => setNewTplName(e.target.value)} placeholder="e.g. Content Editor" />
            <Input label="Description" value={newTplDesc} onChange={(e) => setNewTplDesc(e.target.value)} placeholder="Optional" />
            <div className="max-h-[40vh] space-y-3 overflow-y-auto">
              {groups.map((group) => (
                <div key={group}>
                  <h4 className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted">{group}</h4>
                  <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                    {allPerms.filter((p) => (PERM_GROUPS[p] ?? 'Other') === group).map((perm) => (
                      <label key={perm} className="flex cursor-pointer items-center gap-2 text-sm">
                        <input
                          type="checkbox"
                          checked={newTplPerms.has(perm)}
                          onChange={() => setNewTplPerms((prev) => { const next = new Set(prev); if (next.has(perm)) next.delete(perm); else next.add(perm); return next; })}
                          className="rounded border-border text-primary focus:ring-primary"
                        />
                        {permLabel(perm)}
                      </label>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </div>
          <div className="flex justify-end gap-2 border-t border-border px-4 pb-4 pt-4">
            <Button variant="outline" onClick={() => setShowCreateTpl(false)}>Cancel</Button>
            <Button onClick={handleCreateTemplate} loading={creatingTpl} disabled={!newTplName.trim()}>Create Template</Button>
          </div>
        </Modal>
      ) : null}
    </AsyncStateWrapper>
  );
}
