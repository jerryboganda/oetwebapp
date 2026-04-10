'use client';

import { useEffect, useState } from 'react';
import { KeyRound, Search } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { EmptyState } from '@/components/ui/empty-error';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { fetchAdminPermissions, updateAdminPermissions } from '@/lib/api';
import { getAdminUsersPageData } from '@/lib/admin';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type { AdminPermissionsResponse, AdminUserRow } from '@/lib/types/admin';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

const ALL_PERMISSIONS = [
  { key: 'content:read', label: 'Content Read', group: 'Content' },
  { key: 'content:write', label: 'Content Write', group: 'Content' },
  { key: 'content:publish', label: 'Content Publish', group: 'Content' },
  { key: 'billing:read', label: 'Billing Read', group: 'Billing' },
  { key: 'billing:write', label: 'Billing Write', group: 'Billing' },
  { key: 'users:read', label: 'Users Read', group: 'Users' },
  { key: 'users:write', label: 'Users Write', group: 'Users' },
  { key: 'review_ops', label: 'Review Ops', group: 'Operations' },
  { key: 'quality_analytics', label: 'Quality Analytics', group: 'Operations' },
  { key: 'ai_config', label: 'AI Config', group: 'Operations' },
  { key: 'feature_flags', label: 'Feature Flags', group: 'Operations' },
  { key: 'audit_logs', label: 'Audit Logs', group: 'Operations' },
  { key: 'system_admin', label: 'System Admin', group: 'System' },
];

export default function PermissionsPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [admins, setAdmins] = useState<AdminUserRow[]>([]);
  const [selectedUser, setSelectedUser] = useState<AdminUserRow | null>(null);
  const [userPerms, setUserPerms] = useState<string[]>([]);
  const [isMutating, setIsMutating] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  useEffect(() => {
    let cancelled = false;
    async function load() {
      setPageStatus('loading');
      try {
        const data = await getAdminUsersPageData({ role: 'admin', pageSize: 100 });
        if (cancelled) return;
        setAdmins(data.items);
        setPageStatus(data.items.length > 0 ? 'success' : 'empty');
      } catch {
        if (!cancelled) setPageStatus('error');
      }
    }
    load();
    return () => { cancelled = true; };
  }, []);

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
      setToast({ variant: 'success', message: `Permissions updated for ${selectedUser.name}` });
      setSelectedUser(null);
    } catch {
      setToast({ variant: 'error', message: 'Failed to update permissions.' });
    } finally {
      setIsMutating(false);
    }
  }

  const groups = [...new Set(ALL_PERMISSIONS.map((p) => p.group))];

  return (
    <AdminRouteWorkspace>
      {toast && <Toast variant={toast.variant} onClose={() => setToast(null)}>{toast.message}</Toast>}
      <AdminRouteSectionHeader
        title="Admin Permissions"
        description="Manage granular access control for admin users."
      />

      <AsyncStateWrapper
        status={pageStatus}
        empty={<EmptyState icon={<KeyRound className="w-12 h-12" />} heading="No admin users" body="No admin users found in the system." />}
        error={<EmptyState variant="error" heading="Error loading admins" body="Unable to load admin users." />}
      >
        <AdminRoutePanel>
          <div className="divide-y divide-neutral-200 dark:divide-neutral-700">
            {admins.map((admin) => (
              <div key={admin.id} className="flex items-center justify-between py-3 px-4">
                <div>
                  <p className="text-sm font-medium text-neutral-900 dark:text-neutral-100">{admin.name}</p>
                  <p className="text-xs text-neutral-500">{admin.email}</p>
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
            {groups.map((group) => (
              <div key={group}>
                <h4 className="text-xs font-semibold uppercase tracking-wide text-neutral-500 mb-2">{group}</h4>
                <div className="grid grid-cols-2 gap-2">
                  {ALL_PERMISSIONS.filter((p) => p.group === group).map((perm) => (
                    <label key={perm.key} className="flex items-center gap-2 text-sm cursor-pointer">
                      <input
                        type="checkbox"
                        checked={userPerms.includes(perm.key)}
                        onChange={() => togglePermission(perm.key)}
                        className="rounded border-neutral-300 text-blue-600 focus:ring-blue-500"
                      />
                      {perm.label}
                    </label>
                  ))}
                </div>
              </div>
            ))}
          </div>
          <div className="flex justify-end gap-2 pt-4 border-t border-neutral-200 dark:border-neutral-700 px-4 pb-4">
            <Button variant="ghost" onClick={() => setSelectedUser(null)}>Cancel</Button>
            <Button onClick={savePermissions} disabled={isMutating}>
              {isMutating ? 'Saving...' : 'Save Permissions'}
            </Button>
          </div>
        </Modal>
      )}
    </AdminRouteWorkspace>
  );
}
