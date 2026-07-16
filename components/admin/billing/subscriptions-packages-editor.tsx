'use client';

import { useEffect, useMemo, useState } from 'react';
import { Save, RotateCcw, Search } from 'lucide-react';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Input } from '@/components/admin/ui/input';
import { Textarea } from '@/components/admin/ui/textarea';
import { InlineAlert } from '@/components/ui/alert';
import { fetchAdminCatalogPresentation, saveAdminCatalogPresentation } from '@/lib/api';
import {
  DEFAULT_CATALOG_STOREFRONT,
  type CatalogPresentation,
  type WebsitePackageOverlay,
  type WebsitePackagesPresentation,
} from '@/lib/catalog-presentation';
import {
  WEBSITE_PACKAGES,
  WEBSITE_SECTIONS,
  applyWebsitePackageOverlay,
  type WebsitePackage,
} from '@/lib/catalog-website-packages';

const selectCls = 'rounded-lg border border-admin-border bg-transparent px-3 py-2 text-sm text-admin-fg-default focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]';
const sectionTitle = 'text-base font-bold text-admin-fg-default';
const fieldLabel = 'mb-1 block text-xs font-semibold uppercase tracking-wide text-admin-fg-muted';

function Toggle({ label, checked, onChange }: { label: string; checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <label className="flex items-center gap-2 text-sm text-admin-fg-default">
      <input type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} className="h-4 w-4 rounded border-admin-border" />
      {label}
    </label>
  );
}

function linesToArray(value: string): string[] {
  return value
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}

function arrayToLines(value: string[] | undefined): string {
  return (value ?? []).join('\n');
}

export function SubscriptionsPackagesEditor() {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [savedAt, setSavedAt] = useState<string | null>(null);
  const [byCode, setByCode] = useState<Record<string, WebsitePackageOverlay>>({});
  const [sections, setSections] = useState<WebsitePackagesPresentation['sections']>({});
  const [selectedCode, setSelectedCode] = useState<string>('');
  const [query, setQuery] = useState('');

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const res = await fetchAdminCatalogPresentation();
        if (cancelled) return;
        setByCode(res.presentation?.websitePackages?.byCode ?? {});
        setSections(res.presentation?.websitePackages?.sections ?? {});
        setSelectedCode(WEBSITE_PACKAGES[0]?.code ?? '');
        setError(null);
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Failed to load package settings.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const filteredPackages = useMemo(() => {
    const needle = query.trim().toLowerCase();
    if (!needle) return WEBSITE_PACKAGES;
    return WEBSITE_PACKAGES.filter(
      (pkg) =>
        pkg.name.toLowerCase().includes(needle) ||
        pkg.code.toLowerCase().includes(needle) ||
        pkg.section.toLowerCase().includes(needle),
    );
  }, [query]);

  const selectedPkg = useMemo(
    () => WEBSITE_PACKAGES.find((p) => p.code === selectedCode) ?? WEBSITE_PACKAGES[0],
    [selectedCode],
  );

  const resolved = useMemo(() => {
    if (!selectedPkg) return null;
    return applyWebsitePackageOverlay(selectedPkg, byCode[selectedPkg.code]);
  }, [selectedPkg, byCode]);

  const handleSave = async () => {
    setSaving(true);
    setError(null);
    try {
      const presentation: CatalogPresentation = {
        storefront: {},
        byCode: {},
        websitePackages: { byCode, sections },
      };
      await saveAdminCatalogPresentation(presentation);
      setSavedAt(new Date().toLocaleTimeString());
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save package settings.');
    } finally {
      setSaving(false);
    }
  };

  const handleReset = () => {
    setByCode({});
    setSections({});
    setSavedAt(null);
  };

  const updateCard = (patch: Partial<WebsitePackageOverlay>) => {
    if (!selectedPkg) return;
    setByCode((m) => ({
      ...m,
      [selectedPkg.code]: { ...(m[selectedPkg.code] ?? {}), ...patch },
    }));
  };

  const updateSection = (key: string, patch: { title?: string; description?: string }) => {
    setSections((s) => ({
      ...s,
      [key]: { ...(s?.[key] ?? {}), ...patch },
    }));
  };

  if (loading) {
    return <div className="h-64 animate-pulse rounded-2xl border border-admin-border bg-admin-bg-page" />;
  }

  if (!resolved || !selectedPkg) {
    return <InlineAlert variant="error">No website packages found.</InlineAlert>;
  }

  const card = byCode[selectedPkg.code] ?? {};

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-admin-border bg-admin-bg-page px-4 py-3">
        <div className="text-sm text-admin-fg-muted">
          Changes apply to the learner dashboard /subscriptions page immediately on save.
          {savedAt ? <span className="ml-2 font-semibold text-admin-fg-default">Saved at {savedAt}.</span> : null}
        </div>
        <div className="flex items-center gap-2">
          <Button variant="ghost" size="sm" onClick={handleReset} startIcon={<RotateCcw className="h-4 w-4" />}>
            Reset to defaults
          </Button>
          <Button size="sm" onClick={handleSave} disabled={saving} startIcon={<Save className="h-4 w-4" />}>
            {saving ? 'Saving...' : 'Save changes'}
          </Button>
        </div>
      </div>

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <Card>
        <CardContent className="space-y-4 pt-6">
          <h2 className={sectionTitle}>Select a package</h2>
          <div className="relative">
            <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-admin-fg-muted" />
            <input
              type="text"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Search packages by name, code, or section..."
              className="w-full rounded-lg border border-admin-border bg-transparent py-2 pl-9 pr-3 text-sm text-admin-fg-default focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
            />
          </div>
          <div>
            <span className={fieldLabel}>Package</span>
            <select className={selectCls} value={selectedPkg.code} onChange={(e) => setSelectedCode(e.target.value)}>
              {WEBSITE_SECTIONS.map((section) => {
                const sectionPackages = filteredPackages.filter((p) => p.section === section.key);
                if (sectionPackages.length === 0) return null;
                return (
                  <optgroup key={section.key} label={section.title}>
                    {sectionPackages.map((pkg) => (
                      <option key={pkg.code} value={pkg.code}>
                        Package {pkg.packageNo} — {pkg.name}
                      </option>
                    ))}
                  </optgroup>
                );
              })}
            </select>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardContent className="space-y-4 pt-6">
          <div className="flex items-center justify-between">
            <h2 className={sectionTitle}>{resolved.name}</h2>
            <span className="text-xs font-medium text-admin-fg-muted">{selectedPkg.code}</span>
          </div>
          <div className="grid gap-4 md:grid-cols-2">
            <Input label="Name" value={card.name ?? resolved.name} onChange={(e) => updateCard({ name: e.target.value })} />
            <Input
              label="Package number"
              type="number"
              value={card.packageNo != null ? String(card.packageNo) : String(resolved.packageNo)}
              onChange={(e) => updateCard({ packageNo: e.target.value === '' ? undefined : Number(e.target.value) })}
            />
          </div>
          <Input
            label="Format line"
            value={card.formatLine ?? resolved.formatLine}
            onChange={(e) => updateCard({ formatLine: e.target.value })}
          />
          <Textarea
            label="Description"
            value={card.description ?? resolved.description}
            onChange={(e) => updateCard({ description: e.target.value })}
            rows={3}
          />
          <Textarea
            label="Meta chips (one per line)"
            value={arrayToLines(card.metaChips ?? resolved.metaChips)}
            onChange={(e) => updateCard({ metaChips: linesToArray(e.target.value) })}
            rows={3}
          />
          <Textarea
            label="Badges (one per line)"
            value={arrayToLines(card.badges ?? resolved.badges)}
            onChange={(e) => updateCard({ badges: linesToArray(e.target.value) })}
            rows={2}
          />
          <Textarea
            label="Feature bullets / ticks (one per line)"
            value={arrayToLines(card.features ?? resolved.features)}
            onChange={(e) => updateCard({ features: linesToArray(e.target.value) })}
            rows={6}
          />
          <Textarea
            label="Best for"
            value={card.bestFor ?? resolved.bestFor}
            onChange={(e) => updateCard({ bestFor: e.target.value })}
            rows={2}
          />
          <Toggle label="Featured (highlighted card)" checked={card.featured ?? resolved.featured} onChange={(v) => updateCard({ featured: v })} />
        </CardContent>
      </Card>

      <Card>
        <CardContent className="space-y-4 pt-6">
          <h2 className={sectionTitle}>Section headings</h2>
          <p className="text-sm text-admin-fg-muted">Override the titles and descriptions shown above each group of packages.</p>
          <div className="space-y-3">
            {WEBSITE_SECTIONS.map((section) => (
              <div key={section.key} className="grid gap-3 md:grid-cols-2">
                <Input
                  label={section.key}
                  value={sections?.[section.key]?.title ?? section.title}
                  onChange={(e) => updateSection(section.key, { title: e.target.value })}
                />
                <Input
                  label="Description"
                  value={sections?.[section.key]?.description ?? section.description}
                  onChange={(e) => updateSection(section.key, { description: e.target.value })}
                />
              </div>
            ))}
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
