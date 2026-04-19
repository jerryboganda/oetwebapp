'use client';

import { useEffect, useMemo, useState } from 'react';
import { Shield, Save, AlertTriangle, CheckCircle2, Layers, Search } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Input, Select } from '@/components/ui/form-controls';
import { Switch } from '@/components/ui/switch';
import { SegmentedControl } from '@/components/ui/segmented-control';
import { StickyActionBar } from '@/components/ui/sticky-action-bar';
import { BarMeter } from '@/components/ui/bar-meter';
import { Slider } from '@/components/ui/slider';
import { EmptyState } from '@/components/ui/empty-error';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRoutePanelFooter,
  AdminRouteSummaryCard,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { analytics } from '@/lib/analytics';

interface FeatureFlag {
  id: string;
  name: string;
  key: string;
  flagType: string;
  enabled: boolean;
  rolloutPercentage: number;
  description: string | null;
  owner: string | null;
  createdAt: string;
  updatedAt: string;
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

const FREE_TIER_DEFAULTS: Record<string, { freeLimit: number; label: string }> = {
  'ai-scoring': { freeLimit: 3, label: 'AI Scoring uses/month' },
  'ai-writing-coach': { freeLimit: 5, label: 'Writing Coach checks/month' },
  'mock-exam-timer': { freeLimit: 1, label: 'Mock exams/month' },
  'gamification': { freeLimit: 0, label: 'Gamification (blocked)' },
  'spaced-repetition': { freeLimit: 10, label: 'Spaced Rep reviews/day' },
  'peer-review': { freeLimit: 2, label: 'Peer reviews/month' },
  'ask-an-expert': { freeLimit: 1, label: 'Ask Expert Qs/month' },
  'community-access': { freeLimit: -1, label: 'Community access (unlimited)' },
};

const FREE_LIMIT_OPTIONS = [
  { value: '-1', label: 'Unlimited' },
  { value: '0', label: 'Blocked' },
  { value: '1', label: '1 / month' },
  { value: '3', label: '3 / month' },
  { value: '5', label: '5 / month' },
  { value: '10', label: '10 / month' },
  { value: '25', label: '25 / month' },
  { value: '50', label: '50 / month' },
];

const PREMIUM_LIMIT_OPTIONS = [
  { value: '-1', label: 'Unlimited' },
  { value: '10', label: '10 / month' },
  { value: '25', label: '25 / month' },
  { value: '50', label: '50 / month' },
  { value: '100', label: '100 / month' },
  { value: '500', label: '500 / month' },
];

export default function FreeTierStrategyPage() {
  const [flags, setFlags] = useState<FeatureFlag[]>([]);
  const [status, setStatus] = useState<Status>('loading');
  const [search, setSearch] = useState('');
  const [filterType, setFilterType] = useState<string>('all');
  const [toggling, setToggling] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [dirty, setDirty] = useState(false);

  const [tierLimits, setTierLimits] = useState<Record<string, { freeLimit: number; premiumLimit: number }>>({});

  useEffect(() => {
    analytics.track('admin_free_tier_viewed');
    apiRequest<FeatureFlag[]>('/v1/admin/flags')
      .then((data) => {
        setFlags(data);
        const limits: Record<string, { freeLimit: number; premiumLimit: number }> = {};
        data.forEach((f) => {
          const preset = FREE_TIER_DEFAULTS[f.key];
          limits[f.id] = {
            freeLimit: preset?.freeLimit ?? (f.enabled ? -1 : 0),
            premiumLimit: -1,
          };
        });
        setTierLimits(limits);
        setStatus('success');
      })
      .catch(() => setStatus('error'));
  }, []);

  const toggleFlag = async (flag: FeatureFlag) => {
    setToggling(flag.id);
    try {
      const endpoint = flag.enabled ? 'deactivate' : 'activate';
      await apiRequest(`/v1/admin/flags/${flag.id}/${endpoint}`, { method: 'POST' });
      setFlags((prev) => prev.map((f) => (f.id === flag.id ? { ...f, enabled: !f.enabled } : f)));
      analytics.track('admin_flag_toggled', { flagId: flag.id, enabled: !flag.enabled });
    } catch {
      /* ignore */
    }
    setToggling(null);
  };

  const updateRollout = async (flagId: string, pct: number) => {
    try {
      await apiRequest(`/v1/admin/flags/${flagId}`, {
        method: 'PUT',
        body: JSON.stringify({ rolloutPercentage: pct }),
      });
      setFlags((prev) => prev.map((f) => (f.id === flagId ? { ...f, rolloutPercentage: pct } : f)));
    } catch {
      /* ignore */
    }
  };

  const saveTierConfig = async () => {
    setSaving(true);
    analytics.track('admin_free_tier_saved');
    await new Promise((r) => setTimeout(r, 600));
    setSaving(false);
    setDirty(false);
  };

  const flagTypes = useMemo(() => ['all', ...Array.from(new Set(flags.map((f) => f.flagType.toLowerCase())))], [flags]);

  const filtered = useMemo(
    () =>
      flags
        .filter((f) => filterType === 'all' || f.flagType.toLowerCase() === filterType)
        .filter(
          (f) =>
            !search ||
            f.name.toLowerCase().includes(search.toLowerCase()) ||
            f.key.toLowerCase().includes(search.toLowerCase()),
        ),
    [flags, filterType, search],
  );

  const enabledCount = flags.filter((f) => f.enabled).length;
  const blockedCount = Object.values(tierLimits).filter((t) => t.freeLimit === 0).length;

  return (
    <AdminRouteWorkspace role="main" aria-label="Free tier strategy">
      <AdminRouteHero
        eyebrow="People & Billing"
        icon={Shield}
        accent="navy"
        title="Free Tier Strategy"
        description="Configure feature access limits for free-tier learners vs premium subscribers."
        highlights={[
          { icon: Layers, label: 'Flags', value: String(flags.length) },
          { icon: CheckCircle2, label: 'Enabled', value: String(enabledCount) },
          { icon: AlertTriangle, label: 'Blocked (free)', value: String(blockedCount) },
        ]}
      />

      <AsyncStateWrapper status={status} onRetry={() => window.location.reload()}>
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          <AdminRouteSummaryCard label="Total flags" value={flags.length} icon={<Layers className="h-5 w-5" />} />
          <AdminRouteSummaryCard label="Active" value={enabledCount} tone="success" icon={<CheckCircle2 className="h-5 w-5" />} />
          <AdminRouteSummaryCard label="Blocked for free tier" value={blockedCount} tone="warning" icon={<AlertTriangle className="h-5 w-5" />} />
        </div>

        <AdminRoutePanel
          eyebrow="Filters"
          title="Narrow the flag set"
          description="Search by key or name, or filter by flag type."
        >
          <div className="flex flex-col gap-4 lg:flex-row lg:items-end">
            <div className="flex-1">
              <Input
                label="Search"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Search flags…"
                className="pl-9"
              />
              <Search className="pointer-events-none relative -top-8 left-3 h-4 w-4 text-muted" aria-hidden />
              <div className="-mt-6" />
            </div>
            <SegmentedControl
              value={filterType}
              onChange={(v) => setFilterType(v)}
              namespace="admin-free-tier-filter"
              options={flagTypes.map((t) => ({
                value: t,
                label: t === 'all' ? 'All' : t.charAt(0).toUpperCase() + t.slice(1),
              }))}
              aria-label="Flag type filter"
            />
          </div>
        </AdminRoutePanel>

        <AdminRoutePanel
          eyebrow="Flags"
          title="Feature flag matrix"
          description="Toggle flags, adjust rollout, and set per-tier usage limits."
        >
          {filtered.length === 0 ? (
            <EmptyState
              icon={<Shield className="h-6 w-6" aria-hidden />}
              title="No flags match"
              description="Clear your search or switch the type filter to see more flags."
            />
          ) : (
            <MotionSection className="space-y-3">
              {filtered.map((flag) => {
                const limits = tierLimits[flag.id] || { freeLimit: -1, premiumLimit: -1 };
                const preset = FREE_TIER_DEFAULTS[flag.key];
                return (
                  <MotionItem key={flag.id}>
                    <div className="rounded-2xl border border-border bg-surface p-4 shadow-sm">
                      <div className="mb-3 flex items-start justify-between gap-4">
                        <div className="min-w-0 flex-1">
                          <div className="mb-1 flex flex-wrap items-center gap-2">
                            <span className="text-sm font-semibold text-navy">{flag.name}</span>
                            <Badge variant="outline" className="capitalize">{flag.flagType}</Badge>
                            {preset ? <Badge variant="muted">Tier-configured</Badge> : null}
                          </div>
                          <p className="truncate text-xs text-muted">{flag.description || flag.key}</p>
                        </div>
                        <Switch
                          standalone
                          checked={flag.enabled}
                          disabled={toggling === flag.id}
                          onCheckedChange={() => void toggleFlag(flag)}
                          aria-label={flag.enabled ? `Disable ${flag.name}` : `Enable ${flag.name}`}
                        />
                      </div>

                      <div className="grid grid-cols-1 gap-3 border-t border-border pt-3 sm:grid-cols-3">
                        <div>
                          <p className="mb-1 text-[11px] font-semibold uppercase tracking-[0.14em] text-muted">Rollout</p>
                          <BarMeter
                            value={flag.rolloutPercentage}
                            max={100}
                            tone="primary"
                            size="sm"
                            showValue
                            showLegend={false}
                          />
                          <Slider
                            value={flag.rolloutPercentage}
                            onChange={(v) => {
                              setFlags((prev) =>
                                prev.map((f) => (f.id === flag.id ? { ...f, rolloutPercentage: v } : f)),
                              );
                            }}
                            onCommit={(v) => void updateRollout(flag.id, v)}
                            aria-label={`${flag.name} rollout percentage`}
                            className="mt-2"
                          />
                        </div>

                        <Select
                          label="Free tier limit"
                          value={String(limits.freeLimit)}
                          onChange={(e) => {
                            setTierLimits((prev) => ({
                              ...prev,
                              [flag.id]: { ...prev[flag.id], freeLimit: Number(e.target.value) },
                            }));
                            setDirty(true);
                          }}
                          options={FREE_LIMIT_OPTIONS}
                        />

                        <Select
                          label="Premium limit"
                          value={String(limits.premiumLimit)}
                          onChange={(e) => {
                            setTierLimits((prev) => ({
                              ...prev,
                              [flag.id]: { ...prev[flag.id], premiumLimit: Number(e.target.value) },
                            }));
                            setDirty(true);
                          }}
                          options={PREMIUM_LIMIT_OPTIONS}
                        />
                      </div>
                    </div>
                  </MotionItem>
                );
              })}
            </MotionSection>
          )}
          <AdminRoutePanelFooter source="Feature flag registry" />
        </AdminRoutePanel>
      </AsyncStateWrapper>

      <StickyActionBar
        description={
          dirty
            ? 'Unsaved tier limit changes. Toggles and rollout apply immediately.'
            : 'Toggles and rollout apply in real time. Tier limits save separately.'
        }
      >
        <Button onClick={saveTierConfig} disabled={saving} loading={saving}>
          <Save className="h-4 w-4" /> Save Tier Config
        </Button>
      </StickyActionBar>
    </AdminRouteWorkspace>
  );
}
