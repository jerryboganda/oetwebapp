'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { Shield, ShieldCheck, Users, Save, ChevronRight, CheckCircle2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Checkbox, Input } from '@/components/ui/form-controls';
import { StickyActionBar } from '@/components/ui/sticky-action-bar';
import { EmptyState } from '@/components/ui/empty-error';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { analytics } from '@/lib/analytics';

interface AdminUser {
  id: string;
  name: string;
  email: string;
  role: string;
  lastActiveAt: string | null;
}

interface PermissionGrant {
  permission: string;
  grantedBy: string;
  grantedAt: string;
}

interface PermissionsData {
  userId: string;
  permissions: PermissionGrant[];
  allPermissions: string[];
}

type Status = 'loading' | 'error' | 'success';

async function apiRequest<T = unknown>(path: string, init?: RequestInit): Promise<T> {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}${path}`, {
    ...init,
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
      ...init?.headers,
    },
  });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

const PERM_META: Record<string, { label: string; desc: string; group: string }> = {
  'content:read': { label: 'Content Read', desc: 'View content library and items', group: 'Content' },
  'content:write': { label: 'Content Write', desc: 'Create, edit, and archive content', group: 'Content' },
  'content:publish': { label: 'Content Publish', desc: 'Publish and un-publish content items', group: 'Content' },
  'billing:read': { label: 'Billing Read', desc: 'View plans, subscriptions, invoices', group: 'Billing' },
  'billing:write': { label: 'Billing Write', desc: 'Modify plans, coupons, subscriptions', group: 'Billing' },
  'users:read': { label: 'Users Read', desc: 'View user accounts and profiles', group: 'Users' },
  'users:write': { label: 'Users Write', desc: 'Modify users, credits, suspensions', group: 'Users' },
  review_ops: { label: 'Review Operations', desc: 'Manage review queue, assignments, SLA', group: 'Operations' },
  quality_analytics: { label: 'Quality Analytics', desc: 'Access scoring quality dashboards', group: 'Operations' },
  ai_config: { label: 'AI Configuration', desc: 'Configure AI models and routing', group: 'System' },
  feature_flags: { label: 'Feature Flags', desc: 'Manage feature flag rollouts', group: 'System' },
  audit_logs: { label: 'Audit Logs', desc: 'View and export audit trail', group: 'System' },
  system_admin: { label: 'System Admin', desc: 'Full system access (superadmin)', group: 'System' },
};

const GROUPS = ['Content', 'Billing', 'Users', 'Operations', 'System'];

const PRESETS = [
  { label: 'Content Editor', perms: ['content:read', 'content:write'] },
  { label: 'Content Publisher', perms: ['content:read', 'content:write', 'content:publish'] },
  { label: 'Billing Admin', perms: ['billing:read', 'billing:write'] },
  { label: 'Quality Manager', perms: ['review_ops', 'quality_analytics'] },
] as const;

export default function AdminRolesPage() {
  const [admins, setAdmins] = useState<AdminUser[]>([]);
  const [status, setStatus] = useState<Status>('loading');
  const [selectedUser, setSelectedUser] = useState<string | null>(null);
  const [userPerms, setUserPerms] = useState<PermissionsData | null>(null);
  const [editPerms, setEditPerms] = useState<Set<string>>(new Set());
  const [saving, setSaving] = useState(false);
  const [search, setSearch] = useState('');
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    analytics.track('admin_roles_viewed');
    apiRequest<{ users: AdminUser[] }>('/v1/admin/users?role=admin&limit=200')
      .then((data) => {
        setAdmins(data.users || []);
        setStatus('success');
      })
      .catch(() => setStatus('error'));
  }, []);

  const loadPermissions = useCallback(async (userId: string) => {
    setSelectedUser(userId);
    setSaved(false);
    try {
      const data = await apiRequest<PermissionsData>(`/v1/admin/permissions/${userId}`);
      setUserPerms(data);
      setEditPerms(new Set(data.permissions.map((p) => p.permission)));
    } catch {
      /* ignore */
    }
  }, []);

  const togglePerm = (perm: string) => {
    setEditPerms((prev) => {
      const next = new Set(prev);
      if (next.has(perm)) next.delete(perm);
      else next.add(perm);
      return next;
    });
    setSaved(false);
  };

  const savePermissions = async () => {
    if (!selectedUser) return;
    setSaving(true);
    try {
      await apiRequest(`/v1/admin/permissions/${selectedUser}`, {
        method: 'PUT',
        body: JSON.stringify({ Permissions: Array.from(editPerms) }),
      });
      setSaved(true);
      analytics.track('admin_permissions_updated', { userId: selectedUser, count: editPerms.size });
    } catch {
      /* ignore */
    }
    setSaving(false);
  };

  const applyPreset = (preset: string) => {
    if (preset === 'System Admin') {
      setEditPerms(new Set(userPerms?.allPermissions ?? []));
    } else {
      const match = PRESETS.find((p) => p.label === preset);
      setEditPerms(new Set(match?.perms ?? []));
    }
    setSaved(false);
  };

  const filtered = useMemo(
    () =>
      admins.filter(
        (a) =>
          !search ||
          a.name.toLowerCase().includes(search.toLowerCase()) ||
          a.email.toLowerCase().includes(search.toLowerCase()),
      ),
    [admins, search],
  );

  const selectedAdmin = admins.find((a) => a.id === selectedUser) ?? null;

  return (
    <AdminRouteWorkspace role="main" aria-label="Admin roles and permissions">
      <AdminRouteHero
        eyebrow="Governance"
        icon={Shield}
        accent="navy"
        title="Admin Roles & Permissions"
        description="Manage granular access control for admin team members across content, billing, people, and system operations."
        highlights={[
          { icon: Users, label: 'Admins', value: String(admins.length) },
          {
            icon: ShieldCheck,
            label: 'Selected permissions',
            value: selectedUser ? String(editPerms.size) : '—',
          },
        ]}
      />

      <AsyncStateWrapper status={status} onRetry={() => window.location.reload()}>
        <div className="grid gap-6 lg:grid-cols-5">
          <div className="space-y-4 lg:col-span-2">
            <AdminRoutePanel
              eyebrow="Team"
              title="Admins"
              description={`${admins.length} active admin accounts.`}
            >
              <Input
                label="Search admins"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Search admins…"
              />
              {filtered.length === 0 ? (
                <EmptyState
                  icon={<Users className="h-6 w-6" aria-hidden />}
                  title="No admins"
                  description="Adjust your search or grant admin role to an existing user."
                />
              ) : (
                <MotionSection className="space-y-2">
                  {filtered.map((admin) => {
                    const selected = selectedUser === admin.id;
                    return (
                      <MotionItem key={admin.id}>
                        <Button
                          type="button"
                          variant="outline"
                          fullWidth
                          onClick={() => void loadPermissions(admin.id)}
                          className={`group h-auto justify-between rounded-2xl px-4 py-3 text-left ${
                            selected
                              ? 'border-primary bg-primary/5 text-primary hover:bg-primary/10'
                              : 'bg-surface hover:border-border-hover'
                          }`}
                          aria-pressed={selected}
                        >
                          <div className="min-w-0 flex-1">
                            <p className="truncate text-sm font-semibold text-navy">{admin.name}</p>
                            <p className="truncate text-xs text-muted">{admin.email}</p>
                          </div>
                          <ChevronRight className={`h-4 w-4 shrink-0 transition-transform ${selected ? 'translate-x-0.5 text-primary' : 'text-muted'}`} aria-hidden />
                        </Button>
                      </MotionItem>
                    );
                  })}
                </MotionSection>
              )}
            </AdminRoutePanel>
          </div>

          <div className="space-y-6 lg:col-span-3">
            {!selectedUser ? (
              <AdminRoutePanel
                eyebrow="Editor"
                title="Select an admin"
                description="Pick a team member on the left to manage their granular permissions."
              >
                <EmptyState
                  icon={<ShieldCheck className="h-6 w-6" aria-hidden />}
                  title="No admin selected"
                  description="Select an admin account to inspect, edit, and save their permissions."
                />
              </AdminRoutePanel>
            ) : userPerms ? (
              <>
                <AdminRoutePanel
                  eyebrow="Presets"
                  title={`Quick presets${selectedAdmin ? ` for ${selectedAdmin.name}` : ''}`}
                  description="Apply a pre-configured role baseline, then fine-tune below."
                >
                  <div className="flex flex-wrap gap-2">
                    {PRESETS.map((preset) => (
                      <Button key={preset.label} size="sm" variant="outline" onClick={() => applyPreset(preset.label)}>
                        {preset.label}
                      </Button>
                    ))}
                    <Button size="sm" variant="outline" onClick={() => applyPreset('System Admin')}>
                      System Admin
                    </Button>
                  </div>
                </AdminRoutePanel>

                {GROUPS.map((group) => {
                  const perms = Object.entries(PERM_META).filter(([, m]) => m.group === group);
                  if (perms.length === 0) return null;
                  return (
                    <AdminRoutePanel key={group} eyebrow={group} title={`${group} permissions`}>
                      <div className="space-y-2">
                        {perms.map(([key, meta]) => {
                          const checked = editPerms.has(key);
                          return (
                            <div
                              key={key}
                              className="flex items-start justify-between gap-3 rounded-2xl border border-border bg-background-light px-4 py-3 shadow-sm"
                            >
                              <Checkbox
                                label={meta.label}
                                checked={checked}
                                onChange={() => togglePerm(key)}
                                className="flex-1 border-0 bg-transparent p-0 shadow-none hover:border-0"
                              />
                              <div className="flex items-start gap-2">
                                {checked ? <Badge variant="muted">Granted</Badge> : null}
                              </div>
                            </div>
                          );
                        })}
                        <p className="px-1 pt-1 text-xs text-muted">
                          {perms.map(([, m]) => m.desc).join(' · ')}
                        </p>
                      </div>
                    </AdminRoutePanel>
                  );
                })}
              </>
            ) : null}
          </div>
        </div>
      </AsyncStateWrapper>

      {selectedUser ? (
        <StickyActionBar
          description={
            <span className="flex items-center gap-2">
              {saved ? (
                <span className="inline-flex items-center gap-1 text-success">
                  <CheckCircle2 className="h-4 w-4" /> Saved
                </span>
              ) : null}
              <span>{editPerms.size} permissions selected</span>
            </span>
          }
        >
          <Button onClick={savePermissions} disabled={saving} loading={saving}>
            <Save className="h-4 w-4" /> Save Permissions
          </Button>
        </StickyActionBar>
      ) : null}
    </AdminRouteWorkspace>
  );
}
