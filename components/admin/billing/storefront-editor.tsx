'use client';

import { useEffect, useMemo, useState } from 'react';
import { Save, RotateCcw, Plus, Trash2 } from 'lucide-react';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Input } from '@/components/admin/ui/input';
import { Textarea } from '@/components/admin/ui/textarea';
import { InlineAlert } from '@/components/ui/alert';
import { fetchAdminCatalogPresentation, saveAdminCatalogPresentation } from '@/lib/api';
import {
  DEFAULT_CATALOG_STOREFRONT,
  resolveStorefrontConfig,
  CATALOG_ICON_KEYS,
  type CatalogStorefrontConfig,
  type CatalogCardPresentation,
  type CatalogPresentation,
} from '@/lib/catalog-presentation';
import type { LearnerSurfaceAccent } from '@/lib/learner-surface';

const ACCENT_OPTIONS: LearnerSurfaceAccent[] = ['primary', 'navy', 'amber', 'blue', 'indigo', 'purple', 'rose', 'emerald', 'slate'];

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

export function StorefrontEditor() {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [savedAt, setSavedAt] = useState<string | null>(null);
  const [planCodes, setPlanCodes] = useState<string[]>([]);
  const [addOnCodes, setAddOnCodes] = useState<string[]>([]);
  const [config, setConfig] = useState<CatalogStorefrontConfig>(DEFAULT_CATALOG_STOREFRONT);
  const [byCode, setByCode] = useState<Record<string, CatalogCardPresentation>>({});
  const [selectedCode, setSelectedCode] = useState<string>('');

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const res = await fetchAdminCatalogPresentation();
        if (cancelled) return;
        setPlanCodes(res.planCodes ?? []);
        setAddOnCodes(res.addOnCodes ?? []);
        setConfig(resolveStorefrontConfig(res.presentation));
        setByCode(res.presentation?.byCode ?? {});
        setSelectedCode((res.planCodes ?? [])[0] ?? (res.addOnCodes ?? [])[0] ?? '');
        setError(null);
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Failed to load storefront settings.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const allCodes = useMemo(() => [...planCodes, ...addOnCodes], [planCodes, addOnCodes]);
  const card: CatalogCardPresentation = byCode[selectedCode] ?? {};

  const handleSave = async () => {
    setSaving(true);
    setError(null);
    try {
      const presentation: CatalogPresentation = { storefront: config, byCode };
      await saveAdminCatalogPresentation(presentation);
      setSavedAt(new Date().toLocaleTimeString());
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save storefront settings.');
    } finally {
      setSaving(false);
    }
  };

  const handleReset = () => {
    setConfig(DEFAULT_CATALOG_STOREFRONT);
    setByCode({});
    setSavedAt(null);
  };

  const updateHero = (patch: Partial<CatalogStorefrontConfig['hero']>) =>
    setConfig((c) => ({ ...c, hero: { ...c.hero, ...patch } }));
  const updateSections = (patch: Partial<CatalogStorefrontConfig['sections']>) =>
    setConfig((c) => ({ ...c, sections: { ...c.sections, ...patch } }));
  const updateCta = (patch: Partial<CatalogStorefrontConfig['cta']>) =>
    setConfig((c) => ({ ...c, cta: { ...c.cta, ...patch } }));
  const updateCard = (patch: Partial<CatalogCardPresentation>) =>
    setByCode((m) => ({ ...m, [selectedCode]: { ...(m[selectedCode] ?? {}), ...patch } }));

  if (loading) {
    return <div className="h-64 animate-pulse rounded-2xl border border-admin-border bg-admin-bg-page" />;
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-admin-border bg-admin-bg-page px-4 py-3">
        <div className="text-sm text-admin-fg-muted">
          Changes apply to the public catalog and the in-dashboard Subscriptions and Packages page immediately on save.
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
          <h2 className={sectionTitle}>Hero</h2>
          <div className="grid gap-4 md:grid-cols-2">
            <Input label="Eyebrow" value={config.hero.eyebrow} onChange={(e) => updateHero({ eyebrow: e.target.value })} />
            <Input label="Title" value={config.hero.title} onChange={(e) => updateHero({ title: e.target.value })} />
          </div>
          <Textarea label="Subtitle" value={config.hero.subtitle} onChange={(e) => updateHero({ subtitle: e.target.value })} />
          <div>
            <span className={fieldLabel}>Accent</span>
            <select className={selectCls} value={config.hero.accent} onChange={(e) => updateHero({ accent: e.target.value as LearnerSurfaceAccent })}>
              {ACCENT_OPTIONS.map((a) => <option key={a} value={a}>{a}</option>)}
            </select>
          </div>
          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <span className={fieldLabel}>Highlights</span>
              <Button
                variant="ghost"
                size="sm"
                startIcon={<Plus className="h-4 w-4" />}
                onClick={() => updateHero({ highlights: [...config.hero.highlights, { label: '', value: '', iconKey: '' }] })}
              >
                Add highlight
              </Button>
            </div>
            {config.hero.highlights.map((h, i) => (
              <div key={i} className="grid gap-2 sm:grid-cols-[1fr_1fr_auto_auto] sm:items-end">
                <Input label="Label" value={h.label} onChange={(e) => updateHero({ highlights: config.hero.highlights.map((x, idx) => (idx === i ? { ...x, label: e.target.value } : x)) })} />
                <Input label="Value" value={h.value} onChange={(e) => updateHero({ highlights: config.hero.highlights.map((x, idx) => (idx === i ? { ...x, value: e.target.value } : x)) })} />
                <select className={selectCls} value={h.iconKey ?? ''} onChange={(e) => updateHero({ highlights: config.hero.highlights.map((x, idx) => (idx === i ? { ...x, iconKey: e.target.value } : x)) })}>
                  <option value="">no icon</option>
                  {CATALOG_ICON_KEYS.map((k) => <option key={k} value={k}>{k}</option>)}
                </select>
                <Button variant="ghost" size="sm" onClick={() => updateHero({ highlights: config.hero.highlights.filter((_, idx) => idx !== i) })}>
                  <Trash2 className="h-4 w-4" />
                </Button>
              </div>
            ))}
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardContent className="space-y-3 pt-6">
          <h2 className={sectionTitle}>Sections</h2>
          <div className="grid gap-3 sm:grid-cols-2">
            <Toggle label="Show profession filters and search" checked={config.sections.showFilters} onChange={(v) => updateSections({ showFilters: v })} />
            <Toggle label="Show the compare-all-packages matrix" checked={config.sections.showCompareMatrix} onChange={(v) => updateSections({ showCompareMatrix: v })} />
            <Toggle label="Show the add-ons reference section" checked={config.sections.showAddOns} onChange={(v) => updateSections({ showAddOns: v })} />
            <Toggle label="Show the footer call-to-action" checked={config.sections.showCta} onChange={(v) => updateSections({ showCta: v })} />
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardContent className="space-y-4 pt-6">
          <h2 className={sectionTitle}>Footer call-to-action</h2>
          <div className="grid gap-4 md:grid-cols-2">
            <Input label="Title" value={config.cta.title} onChange={(e) => updateCta({ title: e.target.value })} />
            <Input label="Subtitle" value={config.cta.subtitle} onChange={(e) => updateCta({ subtitle: e.target.value })} />
            <Input label="Primary button label" value={config.cta.primaryLabel} onChange={(e) => updateCta({ primaryLabel: e.target.value })} />
            <Input label="Primary button link" value={config.cta.primaryHref} onChange={(e) => updateCta({ primaryHref: e.target.value })} />
            <Input label="Secondary button label" value={config.cta.secondaryLabel} onChange={(e) => updateCta({ secondaryLabel: e.target.value })} />
            <Input label="Secondary button link" value={config.cta.secondaryHref} onChange={(e) => updateCta({ secondaryHref: e.target.value })} />
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardContent className="space-y-3 pt-6">
          <h2 className={sectionTitle}>Categories</h2>
          <p className="text-sm text-admin-fg-muted">Rename, reorder, or hide each product category section.</p>
          <div className="space-y-2">
            {config.categories.map((cat, i) => (
              <div key={cat.key} className="grid gap-2 sm:grid-cols-[1fr_120px_auto] sm:items-end">
                <Input label={cat.key} value={cat.label} onChange={(e) => setConfig((c) => ({ ...c, categories: c.categories.map((x, idx) => (idx === i ? { ...x, label: e.target.value } : x)) }))} />
                <Input label="Order" type="number" value={String(cat.displayOrder)} onChange={(e) => setConfig((c) => ({ ...c, categories: c.categories.map((x, idx) => (idx === i ? { ...x, displayOrder: Number(e.target.value) || 0 } : x)) }))} />
                <div className="pb-2">
                  <Toggle label="Visible" checked={cat.visible} onChange={(v) => setConfig((c) => ({ ...c, categories: c.categories.map((x, idx) => (idx === i ? { ...x, visible: v } : x)) }))} />
                </div>
              </div>
            ))}
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardContent className="space-y-3 pt-6">
          <div className="flex items-center justify-between">
            <h2 className={sectionTitle}>Add-on legend</h2>
            <Button variant="ghost" size="sm" startIcon={<Plus className="h-4 w-4" />} onClick={() => setConfig((c) => ({ ...c, legend: [...c.legend, { key: '', label: '', description: '' }] }))}>
              Add legend item
            </Button>
          </div>
          {config.legend.map((l, i) => (
            <div key={i} className="grid gap-2 sm:grid-cols-[120px_1fr_2fr_auto] sm:items-end">
              <Input label="Key" value={l.key} onChange={(e) => setConfig((c) => ({ ...c, legend: c.legend.map((x, idx) => (idx === i ? { ...x, key: e.target.value } : x)) }))} />
              <Input label="Label" value={l.label} onChange={(e) => setConfig((c) => ({ ...c, legend: c.legend.map((x, idx) => (idx === i ? { ...x, label: e.target.value } : x)) }))} />
              <Input label="Description" value={l.description} onChange={(e) => setConfig((c) => ({ ...c, legend: c.legend.map((x, idx) => (idx === i ? { ...x, description: e.target.value } : x)) }))} />
              <Button variant="ghost" size="sm" onClick={() => setConfig((c) => ({ ...c, legend: c.legend.filter((_, idx) => idx !== i) }))}>
                <Trash2 className="h-4 w-4" />
              </Button>
            </div>
          ))}
        </CardContent>
      </Card>

      <Card>
        <CardContent className="space-y-3 pt-6">
          <h2 className={sectionTitle}>Profession labels</h2>
          <div className="grid gap-3 sm:grid-cols-2">
            {Object.keys(config.professionLabels).map((key) => (
              <Input key={key} label={key} value={config.professionLabels[key]} onChange={(e) => setConfig((c) => ({ ...c, professionLabels: { ...c.professionLabels, [key]: e.target.value } }))} />
            ))}
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardContent className="space-y-4 pt-6">
          <h2 className={sectionTitle}>Per-package presentation</h2>
          {allCodes.length === 0 ? (
            <p className="text-sm text-admin-fg-muted">No packages found.</p>
          ) : (
            <>
              <div>
                <span className={fieldLabel}>Package</span>
                <select className={selectCls} value={selectedCode} onChange={(e) => setSelectedCode(e.target.value)}>
                  {planCodes.length > 0 ? (
                    <optgroup label="Plans">
                      {planCodes.map((code) => <option key={code} value={code}>{code}</option>)}
                    </optgroup>
                  ) : null}
                  {addOnCodes.length > 0 ? (
                    <optgroup label="Add-ons">
                      {addOnCodes.map((code) => <option key={code} value={code}>{code}</option>)}
                    </optgroup>
                  ) : null}
                </select>
              </div>
              <div className="grid gap-4 md:grid-cols-2">
                <Input label="Marketing tagline" value={card.tagline ?? ''} onChange={(e) => updateCard({ tagline: e.target.value })} />
                <Input label="Badge label (when featured)" value={card.badgeLabel ?? ''} onChange={(e) => updateCard({ badgeLabel: e.target.value })} placeholder="Most popular" />
                <Input label="Image URL (optional)" value={card.imageUrl ?? ''} onChange={(e) => updateCard({ imageUrl: e.target.value })} />
                <Input label="Display order override" type="number" value={card.displayOrder != null ? String(card.displayOrder) : ''} onChange={(e) => updateCard({ displayOrder: e.target.value === '' ? null : Number(e.target.value) })} />
                <div>
                  <span className={fieldLabel}>Icon</span>
                  <select className={selectCls} value={card.iconKey ?? ''} onChange={(e) => updateCard({ iconKey: e.target.value })}>
                    <option value="">auto (by category)</option>
                    {CATALOG_ICON_KEYS.map((k) => <option key={k} value={k}>{k}</option>)}
                  </select>
                </div>
                <div>
                  <span className={fieldLabel}>Accent</span>
                  <select className={selectCls} value={card.accent ?? ''} onChange={(e) => updateCard({ accent: (e.target.value || undefined) as LearnerSurfaceAccent | undefined })}>
                    <option value="">inherit</option>
                    {ACCENT_OPTIONS.map((a) => <option key={a} value={a}>{a}</option>)}
                  </select>
                </div>
              </div>
              <Textarea label="Feature bullets (one per line)" value={(card.featureBullets ?? []).join('\n')} onChange={(e) => updateCard({ featureBullets: e.target.value.split('\n') })} rows={4} />
              <Toggle label="Featured (Most popular ribbon)" checked={card.featured ?? false} onChange={(v) => updateCard({ featured: v })} />
            </>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
