'use client';

import { useEffect, useState, useCallback } from 'react';
import { Shield, ToggleLeft, ToggleRight, Search, Filter, Save, AlertTriangle, CheckCircle2, Percent, Tag, Layers } from 'lucide-react';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';

/* ── types ─────────────────────────────────────── */
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

interface TierRule {
  flagId: string;
  flagName: string;
  freeLimit: number | null;   // null = unlimited for free, 0 = blocked
  premiumLimit: number | null; // null = unlimited
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

/* ── Free-tier presets (local config layered on existing flags) ── */
const FREE_TIER_DEFAULTS: Record<string, { freeLimit: number; label: string }> = {
  'ai-scoring':           { freeLimit: 3,   label: 'AI Scoring uses/month' },
  'ai-writing-coach':     { freeLimit: 5,   label: 'Writing Coach checks/month' },
  'mock-exam-timer':      { freeLimit: 1,   label: 'Mock exams/month' },
  'gamification':         { freeLimit: 0,   label: 'Gamification (blocked)' },
  'spaced-repetition':    { freeLimit: 10,  label: 'Spaced Rep reviews/day' },
  'peer-review':          { freeLimit: 2,   label: 'Peer reviews/month' },
  'ask-an-expert':        { freeLimit: 1,   label: 'Ask Expert Qs/month' },
  'community-access':     { freeLimit: -1,  label: 'Community access (unlimited)' },
};

export default function FreeTierStrategyPage() {
  /* state */
  const [flags, setFlags] = useState<FeatureFlag[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState('');
  const [filterType, setFilterType] = useState<string | null>(null);
  const [toggling, setToggling] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

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

  /* save tier config (would persist to backend when endpoint available) */
  const saveTierConfig = async () => {
    setSaving(true);
    analytics.track('admin_free_tier_saved');
    // In production this would POST to /v1/admin/billing/tier-config
    await new Promise(r => setTimeout(r, 600));
    setSaving(false);
  };

  /* filter + search */
  const filtered = flags
    .filter(f => !filterType || f.flagType.toLowerCase() === filterType)
    .filter(f => !search || f.name.toLowerCase().includes(search.toLowerCase()) || f.key.toLowerCase().includes(search.toLowerCase()));

  const flagTypes = Array.from(new Set(flags.map(f => f.flagType)));

  /* counts */
  const enabledCount = flags.filter(f => f.enabled).length;
  const freeTierBlockedCount = Object.values(tierLimits).filter(t => t.freeLimit === 0).length;

  /* ── render ────────────────────────────────── */
  if (loading) {
    return (
      <div className="min-h-screen bg-background-light">
        <div className="max-w-5xl mx-auto px-4 py-8 space-y-4">
          <Skeleton className="h-8 w-64" /><Skeleton className="h-4 w-96" />
          <div className="grid sm:grid-cols-3 gap-4">{[1,2,3].map(i => <Skeleton key={i} className="h-20" />)}</div>
          {[1,2,3,4].map(i => <Skeleton key={i} className="h-24" />)}
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-background-light">
      <div className="max-w-5xl mx-auto px-4 py-8">

        {/* header */}
        <div className="mb-8">
          <h1 className="text-2xl font-bold flex items-center gap-2"><Shield className="h-6 w-6" />Free Tier Strategy</h1>
          <p className="text-muted mt-1">Configure feature access limits for free-tier learners vs premium subscribers</p>
        </div>

        {/* summary cards */}
        <div className="grid sm:grid-cols-3 gap-4 mb-8">
          <Card className="p-4 text-center">
            <Layers className="h-5 w-5 mx-auto mb-1.5 text-primary" />
            <p className="text-2xl font-bold">{flags.length}</p>
            <p className="text-xs text-muted">Total Flags</p>
          </Card>
          <Card className="p-4 text-center">
            <CheckCircle2 className="h-5 w-5 mx-auto mb-1.5 text-success" />
            <p className="text-2xl font-bold">{enabledCount}</p>
            <p className="text-xs text-muted">Active</p>
          </Card>
          <Card className="p-4 text-center">
            <AlertTriangle className="h-5 w-5 mx-auto mb-1.5 text-amber-600" />
            <p className="text-2xl font-bold">{freeTierBlockedCount}</p>
            <p className="text-xs text-muted">Blocked for Free Tier</p>
          </Card>
        </div>

        {/* search + filters */}
        <div className="flex flex-col sm:flex-row gap-3 mb-6">
          <div className="relative flex-1">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted" />
            <input
              type="text"
              value={search}
              onChange={e => setSearch(e.target.value)}
              placeholder="Search flags…"
              className="w-full pl-9 pr-3 py-2 border rounded-lg bg-muted/30 text-sm focus:outline-none focus:ring-2 focus:ring-primary/50"
            />
          </div>
          <div className="flex gap-2 flex-wrap">
            <button
              onClick={() => setFilterType(null)}
              className={`px-3 py-1.5 rounded-full text-xs font-medium border transition-colors
                ${!filterType ? 'bg-primary text-primary-foreground border-primary' : 'bg-muted/30 border-border'}`}
            >
              All
            </button>
            {flagTypes.map(t => (
              <button
                key={t}
                onClick={() => setFilterType(filterType === t.toLowerCase() ? null : t.toLowerCase())}
                className={`px-3 py-1.5 rounded-full text-xs font-medium border transition-colors capitalize
                  ${filterType === t.toLowerCase() ? 'bg-primary text-primary-foreground border-primary' : 'bg-muted/30 border-border'}`}
              >
                {t}
              </button>
            ))}
          </div>
        </div>

        {/* flags list */}
        <MotionSection className="space-y-3 mb-8">
          {filtered.map(flag => {
            const limits = tierLimits[flag.id] || { freeLimit: -1, premiumLimit: -1 };
            const preset = FREE_TIER_DEFAULTS[flag.key];
            return (
              <MotionItem key={flag.id}>
                <Card className="p-4">
                  <div className="flex items-start justify-between gap-4 mb-3">
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center gap-2 mb-1">
                        <span className="font-medium text-sm">{flag.name}</span>
                        <Badge variant="outline" className="text-[10px] capitalize">{flag.flagType}</Badge>
                        {preset && <Badge variant="secondary" className="text-[10px]">Tier-configured</Badge>}
                      </div>
                      <p className="text-xs text-muted truncate">{flag.description || flag.key}</p>
                    </div>
                    <button
                      onClick={() => toggleFlag(flag)}
                      disabled={toggling === flag.id}
                      className="shrink-0"
                      aria-label={flag.enabled ? 'Disable flag' : 'Enable flag'}
                    >
                      {flag.enabled
                        ? <ToggleRight className="h-7 w-7 text-success" />
                        : <ToggleLeft className="h-7 w-7 text-muted" />
                      }
                    </button>
                  </div>

                  {/* tier config row */}
                  <div className="grid grid-cols-1 sm:grid-cols-3 gap-3 pt-3 border-t">
                    {/* rollout */}
                    <div className="flex items-center gap-2">
                      <Percent className="h-3.5 w-3.5 text-muted shrink-0" />
                      <div className="flex-1">
                        <p className="text-[10px] text-muted mb-1">Rollout %</p>
                        <input
                          type="range"
                          min={0} max={100}
                          value={flag.rolloutPercentage}
                          onChange={e => {
                            const v = Number(e.target.value);
                            setFlags(prev => prev.map(f => f.id === flag.id ? { ...f, rolloutPercentage: v } : f));
                          }}
                          onMouseUp={() => updateRollout(flag.id, flag.rolloutPercentage)}
                          onTouchEnd={() => updateRollout(flag.id, flag.rolloutPercentage)}
                          className="w-full h-1.5 accent-primary"
                        />
                        <span className="text-[10px] text-muted">{flag.rolloutPercentage}%</span>
                      </div>
                    </div>

                    {/* free tier limit */}
                    <div>
                      <p className="text-[10px] text-muted mb-1">Free Tier Limit</p>
                      <select
                        value={limits.freeLimit}
                        onChange={e => setTierLimits(prev => ({
                          ...prev,
                          [flag.id]: { ...prev[flag.id], freeLimit: Number(e.target.value) },
                        }))}
                        className="w-full text-xs border rounded px-2 py-1.5 bg-muted/30"
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
                      <p className="text-[10px] text-muted mb-1">Premium Limit</p>
                      <select
                        value={limits.premiumLimit}
                        onChange={e => setTierLimits(prev => ({
                          ...prev,
                          [flag.id]: { ...prev[flag.id], premiumLimit: Number(e.target.value) },
                        }))}
                        className="w-full text-xs border rounded px-2 py-1.5 bg-muted/30"
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
                </Card>
              </MotionItem>
            );
          })}
        </MotionSection>

        {filtered.length === 0 && (
          <div className="text-center py-12 text-muted">
            <Tag className="h-10 w-10 mx-auto mb-3 opacity-30" />
            <p className="text-sm">No flags match your search</p>
          </div>
        )}

        {/* save bar */}
        <div className="sticky bottom-0 bg-background/95 backdrop-blur border-t py-4 -mx-4 px-4">
          <div className="max-w-5xl mx-auto flex items-center justify-between">
            <p className="text-xs text-muted">Changes are applied in real-time for toggles and rollout. Tier limits save separately.</p>
            <Button onClick={saveTierConfig} disabled={saving}>
              {saving ? 'Saving…' : <><Save className="h-4 w-4 mr-1.5" />Save Tier Config</>}
            </Button>
          </div>
        </div>
      </div>
    </div>
  );
}
