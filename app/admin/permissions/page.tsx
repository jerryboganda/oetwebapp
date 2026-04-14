'use client';

import { useEffect, useState, useCallback } from 'react';
import { KeyRound, Search, Plus, Trash2, FileKey, Users } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { EmptyState } from '@/components/ui/empty-error';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import {
  fetchAllPermissions,
  fetchAdminPermissions,
  updateAdminPermissions,
  fetchPermissionTemplates,
  createPermissionTemplate,
  deletePermissionTemplate,
  applyPermissionTemplate,
} from '@/lib/api';
import { getAdminUsersPageData } from '@/lib/admin';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type { AdminPermissionsResponse, AdminUserRow, PermissionTemplate } from '@/lib/types/admin';

type Tab = 'templates' | 'users';
type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

const PERM_GROUPS: Record<string, string> = {
  'content:read': 'Content', 'content:write': 'Content', 'content:publish': 'Content',
  'billing:read': 'Billing', 'billing:write': 'Billing',
  'users:read': 'Users', 'users:write': 'Users',
  'review_ops': 'Operations', 'quality_analytics': 'Operations',
  'ai_config': 'System', 'feature_flags': 'System', 'audit_logs': 'System',
  'system_admin': 'System', 'manage_permissions': 'System',
};

function permLabel(key: string) {
  return key.replace(/_/g, ' ').replace(/:/g, ' ').replace(/\b\w/g, c => c.toUpperCase());
}

export default function PermissionsPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [tab, setTab] = useState<Tab>('templates');
  const [toast, setToast] = useState<ToastState>(null);

  // ── All permissions from backend ──
  const [allPerms, setAllPerms] = useState<string[]>([]);

  useEffect(() => {
    fetchAllPermissions()
      .then((r: any) => setAllPerms((r.permissions ?? []).map((p: any) => p.key)))
      .catch(() => {});
  }, []);

  const groups = [...new Set(allPerms.map(p => PERM_GROUPS[p] ?? 'Other'))];

  return (
    <AdminRouteWorkspace>
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
      <AdminRouteSectionHeader
        title="Permission Management"
        description="Manage permission templates and user access control."
      />

      {/* Tab bar */}
      <div className="flex gap-1 mb-6 border-b border-border">
        <button
          onClick={() => setTab('templates')}
          className={`px-4 py-2 text-sm font-medium border-b-2 transition-colors ${tab === 'templates' ? 'border-primary text-primary' : 'border-transparent text-muted hover:text-foreground'}`}
        >
          <FileKey className="inline w-4 h-4 mr-1.5 -mt-0.5" />Templates
        </button>
        <button
          onClick={() => setTab('users')}
          className={`px-4 py-2 text-sm font-medium border-b-2 transition-colors ${tab === 'users' ? 'border-primary text-primary' : 'border-transparent text-muted hover:text-foreground'}`}
        >
          <Users className="inline w-4 h-4 mr-1.5 -mt-0.5" />User Permissions
        </button>
      </div>

      {tab === 'templates' && (
        <TemplatesTab
          allPerms={allPerms}
          groups={groups}
          onToast={setToast}
        />
      )}
      {tab === 'users' && (
        <UserPermissionsTab
          allPerms={allPerms}
          groups={groups}
          onToast={setToast}
        />
      )}
    </AdminRouteWorkspace>
  );
}

/* ════════════════════════════════════════════ */
/*  Templates Tab                              */
/* ════════════════════════════════════════════ */

function TemplatesTab({ allPerms, groups, onToast }: { allPerms: string[]; groups: string[]; onToast: (t: ToastState) => void }) {
  const [templates, setTemplates] = useState<PermissionTemplate[]>([]);
  const [status, setStatus] = useState<PageStatus>('loading');
  const [showCreate, setShowCreate] = useState(false);
  const [newName, setNewName] = useState('');
  const [newDesc, setNewDesc] = useState('');
  const [newPerms, setNewPerms] = useState<Set<string>>(new Set());
  const [creating, setCreating] = useState(false);

  const load = useCallback(async () => {
    setStatus('loading');
    try {
      const r = await fetchPermissionTemplates() as any;
      const items = r.templates ?? [];
      setTemplates(items);
      setStatus(items.length > 0 ? 'success' : 'empty');
    } catch {
      setStatus('error');
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  async function handleCreate() {
    if (!newName.trim()) return;
    setCreating(true);
    try {
      await createPermissionTemplate(newName.trim(), newDesc.trim(), Array.from(newPerms));
      onToast({ variant: 'success', message: `Template "${newName}" created.` });
      setShowCreate(false);
      setNewName('');
      setNewDesc('');
      setNewPerms(new Set());
      load();
    } catch {
      onToast({ variant: 'error', message: 'Failed to create template.' });
    } finally {
      setCreating(false);
    }
  }

  async function handleDelete(id: string, name: string) {
    try {
      await deletePermissionTemplate(id);
      onToast({ variant: 'success', message: `Template "${name}" deleted.` });
      load();
    } catch {
      onToast({ variant: 'error', message: 'Failed to delete template.' });
    }
  }

  return (
    <>
      <div className="flex justify-end mb-4">
        <Button size="sm" onClick={() => setShowCreate(true)}>
          <Plus className="w-4 h-4 mr-1.5" />New Template
        </Button>
      </div>

      <AsyncStateWrapper
        status={status}
        emptyContent={<EmptyState icon={<FileKey className="w-12 h-12" />} title="No templates yet" description="Create a permission template to get started." />}
        errorMessage="Unable to load permission templates."
      >
        <AdminRoutePanel>
          <div className="divide-y divide-border dark:divide-border">
            {templates.map((tpl) => (
              <div key={tpl.id} className="flex items-center justify-between py-3 px-4">
                <div className="min-w-0 flex-1">
                  <p className="text-sm font-medium text-navy dark:text-navy">{tpl.name}</p>
                  {tpl.description && <p className="text-xs text-muted mt-0.5">{tpl.description}</p>}
                  <div className="flex flex-wrap gap-1 mt-1.5">
                    {tpl.permissions.map((p) => (
                      <Badge key={p} variant="muted" className="text-[10px]">{permLabel(p)}</Badge>
                    ))}
                  </div>
                </div>
                <Button size="sm" variant="ghost" className="text-destructive" onClick={() => handleDelete(tpl.id, tpl.name)}>
                  <Trash2 className="w-3.5 h-3.5" />
                </Button>
              </div>
            ))}
          </div>
        </AdminRoutePanel>
      </AsyncStateWrapper>

      {showCreate && (
        <Modal open onClose={() => setShowCreate(false)} title="Create Permission Template">
          <div className="space-y-4 p-4">
            <div>
              <label className="block text-xs font-medium mb-1">Name</label>
              <Input value={newName} onChange={(e) => setNewName(e.target.value)} placeholder="e.g. Content Editor" />
            </div>
            <div>
              <label className="block text-xs font-medium mb-1">Description</label>
              <Input value={newDesc} onChange={(e) => setNewDesc(e.target.value)} placeholder="Optional description" />
            </div>
            <div className="max-h-[40vh] overflow-y-auto space-y-3">
              {groups.map((group) => (
                <div key={group}>
                  <h4 className="text-xs font-semibold uppercase tracking-wide text-muted mb-2">{group}</h4>
                  <div className="grid grid-cols-2 gap-2">
                    {allPerms.filter((p) => (PERM_GROUPS[p] ?? 'Other') === group).map((perm) => (
                      <label key={perm} className="flex items-center gap-2 text-sm cursor-pointer">
                        <input
                          type="checkbox"
                          checked={newPerms.has(perm)}
                          onChange={() => setNewPerms(prev => { const n = new Set(prev); if (n.has(perm)) n.delete(perm); else n.add(perm); return n; })}
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
          <div className="flex justify-end gap-2 pt-4 border-t border-border px-4 pb-4">
            <Button variant="ghost" onClick={() => setShowCreate(false)}>Cancel</Button>
            <Button onClick={handleCreate} disabled={creating || !newName.trim()}>
              {creating ? 'Creating...' : 'Create Template'}
            </Button>
          </div>
        </Modal>
      )}
    </>
  );
}

/* ════════════════════════════════════════════ */
/*  User Permissions Tab                       */
/* ════════════════════════════════════════════ */

function UserPermissionsTab({ allPerms, groups, onToast }: { allPerms: string[]; groups: string[]; onToast: (t: ToastState) => void }) {
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [admins, setAdmins] = useState<AdminUserRow[]>([]);
  const [searchTerm, setSearchTerm] = useState('');
  const [selectedUser, setSelectedUser] = useState<AdminUserRow | null>(null);
  const [userPerms, setUserPerms] = useState<string[]>([]);
  const [isMutating, setIsMutating] = useState(false);

  // Templates for apply-template feature
  const [templates, setTemplates] = useState<PermissionTemplate[]>([]);
  const [showApplyTemplate, setShowApplyTemplate] = useState(false);

  useEffect(() => {
    let cancelled = false;
    async function load() {
      setPageStatus('loading');
      try {
        const [data, tplData] = await Promise.all([
          getAdminUsersPageData({ role: 'admin', pageSize: 100 }),
          fetchPermissionTemplates() as Promise<any>,
        ]);
        if (cancelled) return;
        setAdmins(data.items);
        setTemplates(tplData.templates ?? []);
        setPageStatus(data.items.length > 0 ? 'success' : 'empty');
      } catch {
        if (!cancelled) setPageStatus('error');
      }
    }
    load();
    return () => { cancelled = true; };
  }, []);

  const filtered = admins.filter(a =>
    !searchTerm || a.name.toLowerCase().includes(searchTerm.toLowerCase()) || a.email.toLowerCase().includes(searchTerm.toLowerCase()),
  );

  async function openPermissions(user: AdminUserRow) {
    setSelectedUser(user);
    try {
      const raw = await fetchAdminPermissions(user.id) as AdminPermissionsResponse;
      setUserPerms((raw.permissions ?? []).map((p: any) => typeof p === 'string' ? p : p.permission));
    } catch {
      setUserPerms([]);
    }
  }

  function togglePermission(key: string) {
    setUserPerms((prev) =>
      prev.includes(key) ? prev.filter((p) => p !== key) : [...prev, key],
    );
  }

  async function savePermissions() {
    if (!selectedUser) return;
    setIsMutating(true);
    try {
      await updateAdminPermissions(selectedUser.id, userPerms);
      onToast({ variant: 'success', message: `Permissions updated for ${selectedUser.name}` });
      setSelectedUser(null);
    } catch {
      onToast({ variant: 'error', message: 'Failed to update permissions.' });
    } finally {
      setIsMutating(false);
    }
  }

  async function handleApplyTemplate(templateId: string) {
    if (!selectedUser) return;
    setIsMutating(true);
    try {
      const result = await applyPermissionTemplate(selectedUser.id, templateId) as any;
      setUserPerms(result.permissions ?? []);
      onToast({ variant: 'success', message: `Template "${result.templateName}" applied to ${selectedUser.name}` });
      setShowApplyTemplate(false);
    } catch {
      onToast({ variant: 'error', message: 'Failed to apply template.' });
    } finally {
      setIsMutating(false);
    }
  }

  return (
    <>
      <AsyncStateWrapper
        status={pageStatus}
        emptyContent={<EmptyState icon={<KeyRound className="w-12 h-12" />} title="No admin users" description="No admin users found in the system." />}
        errorMessage="Unable to load admin users."
      >
        <div className="mb-4">
          <div className="relative max-w-xs">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted" />
            <Input
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              placeholder="Search admins..."
              className="pl-9"
            />
          </div>
        </div>
        <AdminRoutePanel>
          <div className="divide-y divide-border dark:divide-border">
            {filtered.map((admin) => (
              <div key={admin.id} className="flex items-center justify-between py-3 px-4">
                <div>
                  <p className="text-sm font-medium text-navy dark:text-navy">{admin.name}</p>
                  <p className="text-xs text-muted">{admin.email}</p>
                </div>
                <Button size="sm" variant="outline" onClick={() => openPermissions(admin)}>
                  <KeyRound className="w-3.5 h-3.5 mr-1.5" />
                  Manage
                </Button>
              </div>
            ))}
          </div>
        </AdminRoutePanel>
      </AsyncStateWrapper>

      {selectedUser && (
        <Modal
          open={!!selectedUser}
          onClose={() => setSelectedUser(null)}
          title={`Permissions — ${selectedUser.name}`}
        >
          <div className="space-y-4 max-h-[60vh] overflow-y-auto p-4">
            {templates.length > 0 && (
              <div className="flex items-center gap-2 pb-3 border-b border-border">
                <Button size="sm" variant="outline" onClick={() => setShowApplyTemplate(true)}>
                  <FileKey className="w-3.5 h-3.5 mr-1.5" />Apply Template
                </Button>
              </div>
            )}
            {groups.map((group) => (
              <div key={group}>
                <h4 className="text-xs font-semibold uppercase tracking-wide text-muted mb-2">{group}</h4>
                <div className="grid grid-cols-2 gap-2">
                  {allPerms.filter((p) => (PERM_GROUPS[p] ?? 'Other') === group).map((perm) => (
                    <label key={perm} className="flex items-center gap-2 text-sm cursor-pointer">
                      <input
                        type="checkbox"
                        checked={userPerms.includes(perm)}
                        onChange={() => togglePermission(perm)}
                        className="rounded border-border text-primary focus:ring-primary"
                      />
                      {permLabel(perm)}
                    </label>
                  ))}
                </div>
              </div>
            ))}
          </div>
          <div className="flex justify-end gap-2 pt-4 border-t border-border px-4 pb-4">
            <Button variant="ghost" onClick={() => setSelectedUser(null)}>Cancel</Button>
            <Button onClick={savePermissions} disabled={isMutating}>
              {isMutating ? 'Saving...' : 'Save Permissions'}
            </Button>
          </div>
        </Modal>
      )}

      {showApplyTemplate && selectedUser && (
        <Modal open onClose={() => setShowApplyTemplate(false)} title="Apply Template">
          <div className="divide-y divide-border p-4">
            {templates.map((tpl) => (
              <div key={tpl.id} className="flex items-center justify-between py-3">
                <div className="min-w-0 flex-1">
                  <p className="text-sm font-medium">{tpl.name}</p>
                  {tpl.description && <p className="text-xs text-muted">{tpl.description}</p>}
                  <div className="flex flex-wrap gap-1 mt-1">
                    {tpl.permissions.map((p) => (
                      <Badge key={p} variant="muted" className="text-[10px]">{permLabel(p)}</Badge>
                    ))}
                  </div>
                </div>
                <Button size="sm" onClick={() => handleApplyTemplate(tpl.id)} disabled={isMutating}>
                  Apply
                </Button>
              </div>
            ))}
          </div>
        </Modal>
      )}
    </>
  );
}
