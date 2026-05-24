'use client';

import { useEffect, useState } from 'react';
import {
  AlertTriangle,
  CheckCircle2,
  Layers,
  Percent,
  Search,
  Shield,
  Tag,
  ToggleLeft,
  ToggleRight,
} from 'lucide-react';

import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { KpiStrip } from '@/components/admin/layout/admin-operations-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { Input } from '@/components/admin/ui/input';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { cn } from '@/lib/utils';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';

/* ── types ─────────────────────────────────────── */
interface FeatureFlag {
  id: string;
  name: string;
  key: string;
  flagType: string | null;
  enabled: boolean;
  rolloutPercentage: number;
  description: string | null;
  owner: string | null;
  createdAt: string;
  updatedAt: string;
}

/* ── api helper ───────────────────────────────── */
const apiRequest = apiClient.request;

/* ── Free-tier presets (local config layered on existing flags) ── */
const FREE_TIER_DEFAULTS: Record<string, { freeLimit: number; label: string }> = {
  'ai-scoring':           { freeLimit: 3,   label: 'AI Scoring uses/month' },
  'ai-writing-coach':     { freeLimit: 5,   label: 'Writing Coach checks/month' },
  'mock-exam-timer':      { freeLimit: 1,   label: 'Mock exams/month' },
  'gamification':         { freeLimit: 0,   label: 'Gamification (blocked)' },
  'spaced-repetition':    { freeLimit: 10,  label: 'Spaced Rep reviews/day' },
  'peer-review':          { freeLimit: 2,   label: 'Peer reviews/month' },
  'ask-an-expert':        { freeLimit: 1,   label: 'Ask a Tutor Qs/month' },
  'community-access':     { freeLimit: -1,  label: 'Community access (unlimited)' },
};

function normalizeFlagType(value: string | null | undefined): string {
  return value?.trim().toLowerCase() || 'release';
}

function toSearchableText(value: string | null | undefined): string {
  return value?.toLowerCase() ?? '';
}

export default function FreeTierStrategyPage() {
  useAdminAuth();
  /* state */
  const [flags, setFlags] = useState<FeatureFlag[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState('');
  const [filterType, setFilterType] = useState<string | null>(null);
  const [toggling, setToggling] = useState<string | null>(null);

  /* tier limits (local editable state) */
  const [tierLimits, setTierLimits] = useState<Record<string, { freeLimit: number; premiumLimit: number }>>({});

  /* load flags */
  useEffect(() => {
    analytics.track('admin_free_tier_viewed');
    apiRequest<FeatureFlag[]>('/v1/admin/flags')
      .then(data => {
        setFlags(data);
        // init tier limits from defaults + flags
        const limits: Record<string, { freeLimit: number; premiumLimit: number }> = {};
        data.forEach(f => {
          const preset = FREE_TIER_DEFAULTS[f.key];
          limits[f.id] = {
            freeLimit: preset?.freeLimit ?? (f.enabled ? -1 : 0),
            premiumLimit: -1, // unlimited for premium by default
          };
        });
        setTierLimits(limits);
      })
      .catch(() => {})
      .finally(() => setLoading(false));
  }, []);

  /* toggle flag on/off */
  const toggleFlag = async (flag: FeatureFlag) => {
    setToggling(flag.id);
    try {
      const endpoint = flag.enabled ? 'deactivate' : 'activate';
      await apiRequest(`/v1/admin/flags/${flag.id}/${endpoint}`, { method: 'POST' });
      setFlags(prev => prev.map(f => f.id === flag.id ? { ...f, enabled: !f.enabled } : f));
      analytics.track('admin_flag_toggled', { flagId: flag.id, enabled: !flag.enabled });
    } catch { /* */ }
    setToggling(null);
  };

  /* update rollout % */
  const updateRollout = async (flagId: string, pct: number) => {
    try {
      await apiRequest(`/v1/admin/flags/${flagId}`, {
        method: 'PUT',
        body: JSON.stringify({ rolloutPercentage: pct }),
      });
      setFlags(prev => prev.map(f => f.id === flagId ? { ...f, rolloutPercentage: pct } : f));
    } catch { /* */ }
  };

  /* filter + search */
  const filtered = flags
    .filter(f => !filterType || normalizeFlagType(f.flagType) === filterType)
    .filter(f => !search || toSearchableText(f.name).includes(search.toLowerCase()) || toSearchableText(f.key).includes(search.toLowerCase()));

  const flagTypes = Array.from(new Set(flags.map(f => normalizeFlagType(f.flagType))));

  /* counts */
  const enabledCount = flags.filter(f => f.enabled).length;
  const freeTierBlockedCount = Object.values(tierLimits).filter(t => t.freeLimit === 0).length;

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Free Tier Strategy' },
  ];

  /* ── render ────────────────────────────────── */
  if (loading) {
    return (
      <AdminTableLayout
        title="Free Tier Strategy"
        description="Configure feature access limits for free-tier learners vs premium subscribers."
        breadcrumbs={breadcrumbs}
        banner={
          <KpiStrip>
            {[0, 1, 2].map((i) => (
              <Skeleton key={i} className="h-24" variant="bare" />
            ))}
          </KpiStrip>
        }
      >
        <div className="space-y-3 p-4">
          {[0, 1, 2, 3].map((i) => (
            <Skeleton key={i} className="h-28" variant="bare" />
          ))}
        </div>
      </AdminTableLayout>
    );
  }

  return (
    <AdminTableLayout
      title="Free Tier Strategy"
      description="Configure feature access limits for free-tier learners vs premium subscribers."
      eyebrow="Admin Workspace"
      breadcrumbs={breadcrumbs}
      banner={
        <div className="space-y-4">
          <KpiStrip>
            <KpiTile
              label="Total Flags"
              value={flags.length}
              icon={<Layers className="h-4 w-4" />}
              tone="primary"
            />
            <KpiTile
              label="Active"
              value={enabledCount}
              icon={<CheckCircle2 className="h-4 w-4" />}
              tone="success"
            />
            <KpiTile
              label="Blocked for Free Tier"
              value={freeTierBlockedCount}
              icon={<AlertTriangle className="h-4 w-4" />}
              tone="warning"
            />
          </KpiStrip>

          <Card surface="tinted-warning">
            <CardContent className="p-4 pt-4 text-sm text-admin-fg-default">
              Tier-limit editing is read-only until a backend persistence endpoint
              is available. Feature flag toggles and rollout percentages still
              save through the existing admin flag API.
            </CardContent>
          </Card>

          {/* search + filters */}
          <div className="flex flex-col gap-3 sm:flex-row">
            <div className="relative flex-1">
              <Search
                className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-admin-fg-muted"
                aria-hidden="true"
              />
              <Input
                type="text"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Search flags…"
                aria-label="Search flags"
                className="pl-9"
              />
            </div>
            <div className="flex flex-wrap gap-2">
              <Button
                variant={!filterType ? 'primary' : 'secondary'}
                size="sm"
                onClick={() => setFilterType(null)}
              >
                All
              </Button>
              {flagTypes.map((t) => (
                <Button
                  key={t}
                  variant={filterType === t.toLowerCase() ? 'primary' : 'secondary'}
                  size="sm"
                  onClick={() =>
                    setFilterType(filterType === t.toLowerCase() ? null : t.toLowerCase())
                  }
                  className="capitalize"
                >
                  {t}
                </Button>
              ))}
            </div>
          </div>
        </div>
      }
      footer={
        <div className="sticky bottom-0 -mx-4 border-t border-admin-border bg-admin-bg-surface/95 px-4 py-4 backdrop-blur">
          <div className="mx-auto flex max-w-5xl items-center justify-between gap-3">
            <p className="text-xs text-admin-fg-muted">
              Changes are applied in real-time for toggles and rollout. Tier
              limits are read-only until backend support ships.
            </p>
            <Button variant="primary" disabled>
              Backend endpoint required
            </Button>
          </div>
        </div>
      }
    >
      {/* flags list */}
      {filtered.length === 0 ? (
        <EmptyState
          illustration={<Tag />}
          title="No flags match your search"
          description="Try a different search term or clear the type filter to see more results."
          size="sm"
        />
      ) : (
        <div className="space-y-3 p-4">
          {filtered.map((flag) => {
            const limits = tierLimits[flag.id] || { freeLimit: -1, premiumLimit: -1 };
            const preset = FREE_TIER_DEFAULTS[flag.key];
            const flagType = normalizeFlagType(flag.flagType);
            return (
              <Card key={flag.id}>
                <CardContent className="p-4 pt-4">
                  <div className="mb-3 flex items-start justify-between gap-4">
                    <div className="min-w-0 flex-1">
                      <div className="mb-1 flex items-center gap-2">
                        <span className="text-sm font-medium text-admin-fg-strong">
                          {flag.name}
                        </span>
                        <Badge
                          variant="default"
                          intensity="tinted"
                          size="sm"
                          className="capitalize"
                        >
                          {flagType}
                        </Badge>
                        {preset && (
                          <Badge variant="info" intensity="tinted" size="sm">
                            Tier-configured
                          </Badge>
                        )}
                      </div>
                      <p className="truncate text-xs text-admin-fg-muted">
                        {flag.description || flag.key}
                      </p>
                    </div>
                    <button
                      type="button"
                      onClick={() => toggleFlag(flag)}
                      disabled={toggling === flag.id}
                      className={cn(
                        'shrink-0 rounded-admin-sm focus-visible:outline-none focus-visible:ring-2',
                        'focus-visible:ring-[var(--admin-primary)] focus-visible:ring-offset-2',
                        'focus-visible:ring-offset-[var(--admin-bg-surface)]',
                      )}
                      aria-label={flag.enabled ? 'Disable flag' : 'Enable flag'}
                    >
                      {flag.enabled ? (
                        <ToggleRight className="h-7 w-7 text-[var(--admin-success)]" />
                      ) : (
                        <ToggleLeft className="h-7 w-7 text-admin-fg-muted" />
                      )}
                    </button>
                  </div>

                  {/* tier config row */}
                  <div className="grid grid-cols-1 gap-3 border-t border-admin-border pt-3 sm:grid-cols-3">
                    {/* rollout */}
                    <div className="flex items-center gap-2">
                      <Percent
                        className="h-3.5 w-3.5 shrink-0 text-admin-fg-muted"
                        aria-hidden="true"
                      />
                      <div className="flex-1">
                        <p className="mb-1 text-[10px] uppercase tracking-wider text-admin-fg-muted">
                          Rollout %
                        </p>
                        <input
                          type="range"
                          min={0}
                          max={100}
                          value={flag.rolloutPercentage}
                          onChange={(e) => {
                            const v = Number(e.target.value);
                            setFlags((prev) =>
                              prev.map((f) =>
                                f.id === flag.id ? { ...f, rolloutPercentage: v } : f,
                              ),
                            );
                          }}
                          onMouseUp={() => updateRollout(flag.id, flag.rolloutPercentage)}
                          onTouchEnd={() => updateRollout(flag.id, flag.rolloutPercentage)}
                          className="h-1.5 w-full accent-[var(--admin-primary)]"
                          aria-label={`Rollout percentage for ${flag.name}`}
                        />
                        <span className="text-[10px] text-admin-fg-muted">
                          {flag.rolloutPercentage}%
                        </span>
                      </div>
                    </div>

                    {/* free tier limit */}
                    <div>
                      <p className="mb-1 text-[10px] uppercase tracking-wider text-admin-fg-muted">
                        Free Tier Limit
                      </p>
                      <select
                        value={limits.freeLimit}
                        disabled
                        onChange={() => undefined}
                        aria-label={`Free tier limit for ${flag.name}`}
                        className={cn(
                          'w-full rounded-admin-sm border border-admin-border bg-admin-bg-subtle',
                          'px-2 py-1.5 text-xs text-admin-fg-default',
                          'disabled:cursor-not-allowed disabled:opacity-60',
                        )}
                      >
                        <option value={-1}>Unlimited</option>
                        <option value={0}>Blocked</option>
                        <option value={1}>1/month</option>
                        <option value={3}>3/month</option>
                        <option value={5}>5/month</option>
                        <option value={10}>10/month</option>
                        <option value={25}>25/month</option>
                        <option value={50}>50/month</option>
                      </select>
                    </div>

                    {/* premium tier limit */}
                    <div>
                      <p className="mb-1 text-[10px] uppercase tracking-wider text-admin-fg-muted">
                        Premium Limit
                      </p>
                      <select
                        value={limits.premiumLimit}
                        disabled
                        onChange={() => undefined}
                        aria-label={`Premium limit for ${flag.name}`}
                        className={cn(
                          'w-full rounded-admin-sm border border-admin-border bg-admin-bg-subtle',
                          'px-2 py-1.5 text-xs text-admin-fg-default',
                          'disabled:cursor-not-allowed disabled:opacity-60',
                        )}
                      >
                        <option value={-1}>Unlimited</option>
                        <option value={10}>10/month</option>
                        <option value={25}>25/month</option>
                        <option value={50}>50/month</option>
                        <option value={100}>100/month</option>
                        <option value={500}>500/month</option>
                      </select>
                    </div>
                  </div>
                </CardContent>
              </Card>
            );
          })}
        </div>
      )}
    </AdminTableLayout>
  );
}
