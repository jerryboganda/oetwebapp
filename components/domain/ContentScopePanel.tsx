'use client';

import { useMemo } from 'react';
import { Checkbox, Input, Select } from '@/components/ui/form-controls';

/**
 * Standard sub-test codes that can appear in
 * `BillingPlan.EntitlementsJson.content.subtests`. Mirrors the codes used by
 * `ContentEntitlementService` on the backend (case-insensitive).
 */
const SUBTEST_CODES = ['listening', 'reading', 'writing', 'speaking'] as const;
type SubtestCode = (typeof SUBTEST_CODES)[number];

type ContentScope = {
  tier: 'free' | 'premium';
  subtests: string[];
  papers: string[];
};

const DEFAULT_SCOPE: ContentScope = { tier: 'premium', subtests: [...SUBTEST_CODES], papers: [] };

/**
 * Best-effort parse of an arbitrary entitlements JSON blob. Returns the
 * existing object plus a normalised `content` scope. Never throws.
 */
function parseEntitlements(json: string): { obj: Record<string, unknown>; scope: ContentScope } {
  let obj: Record<string, unknown> = {};
  try {
    const parsed = JSON.parse(json);
    if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
      obj = parsed as Record<string, unknown>;
    }
  } catch {
    // ignore — caller still sees the raw textarea below
  }
  const raw = obj.content;
  const scope: ContentScope = { ...DEFAULT_SCOPE };
  if (raw && typeof raw === 'object' && !Array.isArray(raw)) {
    const c = raw as Record<string, unknown>;
    if (c.tier === 'free' || c.tier === 'premium') scope.tier = c.tier;
    if (Array.isArray(c.subtests)) {
      scope.subtests = c.subtests.filter((x): x is string => typeof x === 'string');
    }
    if (Array.isArray(c.papers)) {
      scope.papers = c.papers.filter((x): x is string => typeof x === 'string');
    }
  }
  return { obj, scope };
}

function serialise(obj: Record<string, unknown>, next: ContentScope): string {
  const merged: Record<string, unknown> = { ...obj, content: { tier: next.tier, subtests: next.subtests, papers: next.papers } };
  return JSON.stringify(merged, null, 2);
}

/**
 * Visual editor for the `content` scope of a billing plan's
 * `EntitlementsJson`. Reads/writes the same blob the raw textarea below
 * exposes, so admins can use either path. Keeps any unrelated keys in the
 * blob untouched (additive merge).
 */
export function ContentScopePanel({
  entitlementsJson,
  onChange,
}: {
  entitlementsJson: string;
  onChange: (nextJson: string) => void;
}) {
  const { obj, scope } = useMemo(() => parseEntitlements(entitlementsJson), [entitlementsJson]);

  const update = (next: Partial<ContentScope>) => {
    const merged: ContentScope = { ...scope, ...next };
    onChange(serialise(obj, merged));
  };

  const toggleSubtest = (code: SubtestCode, checked: boolean) => {
    const set = new Set(scope.subtests.map((s) => s.toLowerCase()));
    if (checked) set.add(code);
    else set.delete(code);
    update({ subtests: Array.from(set) });
  };

  const handlePapersChange = (raw: string) => {
    const parts = raw.split(',').map((s) => s.trim()).filter(Boolean);
    update({ papers: parts });
  };

  return (
    <div className="rounded-md border border-border bg-muted/20 p-4">
      <div className="mb-2 text-sm font-semibold text-navy">Content access scope</div>
      <p className="mb-3 text-xs text-muted">
        Controls which paid Listening / Reading / Writing / Speaking papers this plan unlocks via
        <code className="mx-1 rounded bg-muted px-1 py-0.5 text-[11px]">ContentEntitlementService</code>.
        Free papers (tagged <code className="mx-1 rounded bg-muted px-1 py-0.5 text-[11px]">access:free</code>) are accessible regardless.
      </p>
      <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
        <Select
          label="Plan tier"
          value={scope.tier}
          onChange={(e) => update({ tier: (e.target.value === 'free' ? 'free' : 'premium') })}
          options={[
            { value: 'premium', label: 'Premium (grants any subtest selected below)' },
            { value: 'free', label: 'Free (no subscription tier — papers must be in the lists below)' },
          ]}
        />
        <div>
          <label className="mb-1 block text-sm font-medium">Granted sub-tests</label>
          <div className="flex flex-wrap gap-3">
            {SUBTEST_CODES.map((code) => {
              const checked = scope.subtests.map((s) => s.toLowerCase()).includes(code);
              return (
                <Checkbox
                  key={code}
                  label={code.charAt(0).toUpperCase() + code.slice(1)}
                  checked={checked}
                  onChange={(e) => toggleSubtest(code, e.target.checked)}
                />
              );
            })}
          </div>
        </div>
      </div>
      <div className="mt-3">
        <Input
          label="Granted paper IDs (optional, comma-separated)"
          value={scope.papers.join(', ')}
          onChange={(e) => handlePapersChange(e.target.value)}
          placeholder="paper-rd-001, paper-lt-007"
          hint="Use this to grant individual papers without unlocking the whole sub-test."
        />
      </div>
    </div>
  );
}
