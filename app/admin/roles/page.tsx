'use client';

import { useEffect, useState, useCallback } from 'react';
import { Shield, ShieldCheck, Users, Search, Save, ChevronRight, CheckCircle2 } from 'lucide-react';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';

/* ── types ─────────────────────────────────────── */
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

/* ── api helper ───────────────────────────────── */
async function apiRequest<T = unknown>(path: string, init?: RequestInit): Promise<T> {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}${path}`, { ...init, headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json', ...init?.headers } });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

/* ── permission labels ────────────────────────── */
const PERM_META: Record<string, { label: string; desc: string; group: string }> = {
  'content:read':       { label: 'Content Read',       desc: 'View content library and items',         group: 'Content' },
  'content:write':      { label: 'Content Write',      desc: 'Create, edit, and archive content',      group: 'Content' },
  'content:publish':    { label: 'Content Publish',    desc: 'Publish and un-publish content items',   group: 'Content' },
  'billing:read':       { label: 'Billing Read',       desc: 'View plans, subscriptions, invoices',    group: 'Billing' },
  'billing:write':      { label: 'Billing Write',      desc: 'Modify plans, coupons, subscriptions',   group: 'Billing' },
  'users:read':         { label: 'Users Read',         desc: 'View user accounts and profiles',        group: 'Users' },
  'users:write':        { label: 'Users Write',        desc: 'Modify users, credits, suspensions',     group: 'Users' },
  'review_ops':         { label: 'Review Operations',  desc: 'Manage review queue, assignments, SLA',  group: 'Operations' },
  'quality_analytics':  { label: 'Quality Analytics',  desc: 'Access scoring quality dashboards',      group: 'Operations' },
  'ai_config':          { label: 'AI Configuration',   desc: 'Configure AI models and routing',        group: 'System' },
  'feature_flags':      { label: 'Feature Flags',      desc: 'Manage feature flag rollouts',           group: 'System' },
  'audit_logs':         { label: 'Audit Logs',         desc: 'View and export audit trail',            group: 'System' },
  'system_admin':       { label: 'System Admin',       desc: 'Full system access (superadmin)',        group: 'System' },
};

const GROUPS = ['Content', 'Billing', 'Users', 'Operations', 'System'];

export default function AdminRolesPage() {
  /* state */
  const [admins, setAdmins] = useState<AdminUser[]>([]);
  const [loading, setLoading] = useState(true);
  const [selectedUser, setSelectedUser] = useState<string | null>(null);
  const [userPerms, setUserPerms] = useState<PermissionsData | null>(null);
  const [editPerms, setEditPerms] = useState<Set<string>>(new Set());
  const [saving, setSaving] = useState(false);
  const [search, setSearch] = useState('');
  const [saved, setSaved] = useState(false);

  /* load admin list */
  useEffect(() => {
    analytics.track('admin_roles_viewed');
    apiRequest<{ users: AdminUser[] }>('/v1/admin/users?role=admin&limit=200')
      .then(data => setAdmins(data.users || []))
      .catch(() => {})
      .finally(() => setLoading(false));
  }, []);

  /* load user permissions */
  const loadPermissions = useCallback(async (userId: string) => {
    setSelectedUser(userId);
    setSaved(false);
    try {
      const data = await apiRequest<PermissionsData>(`/v1/admin/permissions/${userId}`);
      setUserPerms(data);
      setEditPerms(new Set(data.permissions.map(p => p.permission)));
    } catch { /* */ }
  }, []);

  /* toggle a permission */
  const togglePerm = (perm: string) => {
    setEditPerms(prev => {
      const next = new Set(prev);
      if (next.has(perm)) next.delete(perm);
      else next.add(perm);
      return next;
    });
    setSaved(false);
  };

  /* save permissions */
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
    } catch { /* */ }
    setSaving(false);
  };

  /* preset roles */
  const applyPreset = (preset: string) => {
    const presets: Record<string, string[]> = {
      'Content Editor':   ['content:read', 'content:write'],
      'Content Publisher': ['content:read', 'content:write', 'content:publish'],
      'Billing Admin':    ['billing:read', 'billing:write'],
      'Quality Manager':  ['review_ops', 'quality_analytics'],
      'System Admin':     userPerms?.allPermissions || [],
    };
    setEditPerms(new Set(presets[preset] || []));
    setSaved(false);
  };

  /* filtered admins */
  const filtered = admins.filter(a =>
    !search || a.name.toLowerCase().includes(search.toLowerCase()) || a.email.toLowerCase().includes(search.toLowerCase())
  );

  /* ── render ────────────────────────────────── */
  if (loading) {
    return (
      <div className="min-h-screen bg-background-light">
        <div className="max-w-5xl mx-auto px-4 py-8 space-y-4">
          <Skeleton className="h-8 w-64" /><Skeleton className="h-4 w-96" />
          {[1,2,3,4].map(i => <Skeleton key={i} className="h-20" />)}
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-background-light">
      <div className="max-w-5xl mx-auto px-4 py-8">

        {/* header */}
        <div className="mb-8">
          <h1 className="text-2xl font-bold flex items-center gap-2"><Shield className="h-6 w-6" />Admin Roles & Permissions</h1>
          <p className="text-muted mt-1">Manage granular access control for admin team members</p>
        </div>

        <div className="grid lg:grid-cols-5 gap-6">
          {/* left — admin list */}
          <div className="lg:col-span-2 space-y-3">
            <div className="relative mb-3">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted" />
              <input
                type="text"
                value={search}
                onChange={e => setSearch(e.target.value)}
                placeholder="Search admins…"
                className="w-full pl-9 pr-3 py-2 border rounded-lg bg-muted/30 text-sm focus:outline-none focus:ring-2 focus:ring-primary/50"
              />
            </div>

            <MotionSection className="space-y-2">
              {filtered.map(admin => (
                <MotionItem key={admin.id}>
                  <Card
                    className={`p-3 cursor-pointer transition-colors ${selectedUser === admin.id ? 'border-primary bg-primary/5' : 'hover:border-primary/30'}`}
                    onClick={() => loadPermissions(admin.id)}
                  >
                    <div className="flex items-center justify-between">
                      <div className="min-w-0 flex-1">
                        <p className="font-medium text-sm truncate">{admin.name}</p>
                        <p className="text-xs text-muted truncate">{admin.email}</p>
                      </div>
                      <ChevronRight className="h-4 w-4 text-muted shrink-0" />
                    </div>
                  </Card>
                </MotionItem>
              ))}
              {filtered.length === 0 && (
                <div className="text-center py-8 text-muted">
                  <Users className="h-8 w-8 mx-auto mb-2 opacity-30" />
                  <p className="text-sm">No admins found</p>
                </div>
              )}
            </MotionSection>
          </div>

          {/* right — permissions editor */}
          <div className="lg:col-span-3">
            {!selectedUser && (
              <div className="flex items-center justify-center h-64 text-muted">
                <div className="text-center">
                  <ShieldCheck className="h-10 w-10 mx-auto mb-3 opacity-30" />
                  <p className="text-sm">Select an admin to manage permissions</p>
                </div>
              </div>
            )}

            {selectedUser && userPerms && (
              <div className="space-y-4">
                {/* preset roles */}
                <Card className="p-4">
                  <p className="text-sm font-medium mb-3">Quick Presets</p>
                  <div className="flex flex-wrap gap-2">
                    {['Content Editor', 'Content Publisher', 'Billing Admin', 'Quality Manager', 'System Admin'].map(preset => (
                      <Button key={preset} size="sm" variant="outline" onClick={() => applyPreset(preset)} className="text-xs h-7">
                        {preset}
                      </Button>
                    ))}
                  </div>
                </Card>

                {/* permissions by group */}
                {GROUPS.map(group => {
                  const perms = Object.entries(PERM_META).filter(([, m]) => m.group === group);
                  return (
                    <Card key={group} className="p-4">
                      <p className="text-sm font-semibold mb-3 text-muted uppercase tracking-wider">{group}</p>
                      <div className="space-y-2">
                        {perms.map(([key, meta]) => {
                          const checked = editPerms.has(key);
                          return (
                            <label key={key} className="flex items-start gap-3 cursor-pointer group">
                              <input
                                type="checkbox"
                                checked={checked}
                                onChange={() => togglePerm(key)}
                                className="mt-1 h-4 w-4 rounded border-border text-primary focus:ring-primary/50"
                              />
                              <div className="flex-1 min-w-0">
                                <div className="flex items-center gap-2">
                                  <span className="text-sm font-medium">{meta.label}</span>
                                  {checked && <Badge variant="muted" className="text-[10px]">Granted</Badge>}
                                </div>
                                <p className="text-xs text-muted">{meta.desc}</p>
                              </div>
                            </label>
                          );
                        })}
                      </div>
                    </Card>
                  );
                })}

                {/* save */}
                <div className="flex items-center justify-between pt-2">
                  <p className="text-xs text-muted">{editPerms.size} permissions selected</p>
                  <div className="flex items-center gap-3">
                    {saved && <span className="text-xs text-success flex items-center gap-1"><CheckCircle2 className="h-3.5 w-3.5" />Saved</span>}
                    <Button onClick={savePermissions} disabled={saving}>
                      {saving ? 'Saving…' : <><Save className="h-4 w-4 mr-1.5" />Save Permissions</>}
                    </Button>
                  </div>
                </div>
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
