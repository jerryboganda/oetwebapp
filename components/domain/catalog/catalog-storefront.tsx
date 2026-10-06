'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { useAuth } from '@/contexts/auth-context';
import { fetchPublicCatalog } from '@/lib/api';
import { useEntitlementSnapshot } from '@/lib/query/hooks';
import type { PublicCatalogPlanRow } from '@/lib/types/admin';
import {
  type PublicCatalogResponseWithPresentation,
  type CatalogPresentation,
  resolveStorefrontConfig,
  resolveCardPresentation,
  groupPlansByCategory,
  sortAddOns,
} from '@/lib/catalog-presentation';
import { CatalogPlanCard } from './catalog-plan-card';
import { CatalogPlanDetailDrawer } from './catalog-detail-drawer';
import { CatalogCompareMatrix } from './catalog-compare-matrix';
import {
  CatalogHero,
  CatalogCta,
  CatalogFilters,
  CatalogAddOnsSection,
  CatalogEntitlementSummary,
} from './catalog-sections';
import { useAddToCart } from '@/lib/cart/use-add-to-cart';
import {
  buildCustomWebsitePackages,
  resolveWebsitePackageWithOverlay,
  type WebsitePackage,
} from '@/lib/catalog-website-packages';
import { useRevalidateOnResume } from '@/hooks/use-revalidate-on-resume';

export interface CatalogStorefrontProps {
  variant: 'dashboard' | 'public';
}

export function CatalogStorefront({ variant }: CatalogStorefrontProps) {
  const { addToCart } = useAddToCart();
  const [data, setData] = useState<PublicCatalogResponseWithPresentation | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const { user } = useAuth();
  const userId = user?.userId ?? '';
  // The dashboard variant shows the owned plan. It reads the shared entitlement query
  // (cached, deduped with the shell and dashboard) instead of fetching a private copy.
  const entitlement = useEntitlementSnapshot(userId, {
    enabled: variant === 'dashboard' && Boolean(userId),
  }).data ?? null;
  const [selectedPlan, setSelectedPlan] = useState<PublicCatalogPlanRow | null>(null);
  const [activeProfession, setActiveProfession] = useState('all');
  const [query, setQuery] = useState('');

  const requestIdRef = useRef(0);
  const hasDataRef = useRef(false);

  // First load and the silent refresh on resume: `loading` never flips back to true and a
  // failed refresh keeps the catalogue already on screen.
  const loadCatalog = useCallback(async () => {
    requestIdRef.current += 1;
    const requestId = requestIdRef.current;
    try {
      const response = await fetchPublicCatalog();
      if (requestId !== requestIdRef.current) return;
      hasDataRef.current = true;
      setData(response);
      setError(null);
    } catch (err) {
      if (requestId !== requestIdRef.current) return;
      if (!hasDataRef.current) setError(err instanceof Error ? err.message : 'Could not load the catalogue.');
    } finally {
      if (requestId === requestIdRef.current) setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadCatalog();
    return () => {
      // Drop the reply of any request still in flight once this page is gone.
      requestIdRef.current += 1;
    };
  }, [loadCatalog]);

  useRevalidateOnResume(() => {
    void loadCatalog();
  });

  const presentation: CatalogPresentation | null = data?.presentation ?? null;
  const config = useMemo(() => resolveStorefrontConfig(presentation), [presentation]);
  const plans = useMemo(() => data?.plans ?? [], [data]);
  const addOns = useMemo(() => sortAddOns(data?.addOns ?? []), [data]);

  // Package copy with the admin overlay applied, keyed by the live plan code. Plans created in
  // Pricing (no static definition) are included once their overlay carries a section; any other
  // plan is absent, so its card keeps using the plan row itself.
  const websitePackageByPlanCode = useMemo(() => {
    const map = new Map<string, WebsitePackage>();
    for (const plan of plans) {
      const merged = resolveWebsitePackageWithOverlay(plan.code, presentation);
      if (merged) map.set(plan.code, merged);
    }
    for (const custom of buildCustomWebsitePackages(plans, presentation)) map.set(custom.code, custom);
    return map;
  }, [plans, presentation]);

  const professions = useMemo(() => {
    const canonicalOrder = [
      'all',
      'medicine',
      'nursing',
      'pharmacy',
      'physiotherapy',
      'other-allied-health',
      'radiography',
      'allied_health',
    ];
    const present = new Set<string>(['all']);
    for (const plan of plans) present.add(plan.profession);
    return canonicalOrder.filter((p) => present.has(p));
  }, [plans]);

  const filteredPlans = useMemo(() => {
    const needle = query.trim().toLowerCase();
    return plans.filter((plan) => {
      // A plan allocated to 'all' disciplines shows under every discipline tab (matches the
      // storefront's "apply for all professions" semantics, mirroring subscriptions-catalog).
      const professionMatch =
        activeProfession === 'all' || plan.profession === activeProfession || plan.profession === 'all';
      const merged = websitePackageByPlanCode.get(plan.code);
      const queryMatch =
        needle.length === 0 ||
        plan.name.toLowerCase().includes(needle) ||
        (plan.description ?? '').toLowerCase().includes(needle) ||
        (merged?.name ?? '').toLowerCase().includes(needle) ||
        (merged?.description ?? '').toLowerCase().includes(needle);
      return professionMatch && queryMatch;
    });
  }, [plans, activeProfession, query, websitePackageByPlanCode]);

  const groups = useMemo(
    () => groupPlansByCategory(filteredPlans, config, presentation),
    [filteredPlans, config, presentation],
  );

  const ownedPlanCode = entitlement?.planCode ?? null;
  // Keep an open drawer on the freshest plan row (price) after a silent refresh.
  const drawerPlan = selectedPlan ? plans.find((plan) => plan.code === selectedPlan.code) ?? selectedPlan : null;

  if (loading) {
    return (
      <div className="space-y-6">
        <div className="h-40 animate-pulse rounded-2xl border border-border bg-surface" />
        <div className="grid gap-5 md:grid-cols-2 lg:grid-cols-3">
          {[0, 1, 2, 3, 4, 5].map((index) => (
            <div key={index} className="h-72 animate-pulse rounded-2xl border border-border bg-surface" />
          ))}
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="space-y-6">
        <CatalogHero config={config} />
        <div className="rounded-2xl border border-border bg-surface p-8 text-center text-muted">{error}</div>
      </div>
    );
  }

  return (
    <div className="space-y-8">
      <CatalogHero config={config} />
      {variant === 'dashboard' && entitlement ? <CatalogEntitlementSummary snapshot={entitlement} /> : null}
      {config.sections.showFilters ? (
        <CatalogFilters
          professions={professions}
          activeProfession={activeProfession}
          onProfessionChange={setActiveProfession}
          query={query}
          onQueryChange={setQuery}
          config={config}
        />
      ) : null}
      {config.sections.showCompareMatrix ? (
        <CatalogCompareMatrix plans={filteredPlans} config={config} presentation={presentation} />
      ) : null}

      {groups.length === 0 ? (
        <div className="rounded-2xl border border-border bg-surface p-10 text-center">
          <p className="text-sm text-muted">No packages match your filters right now.</p>
        </div>
      ) : (
        groups.map((group) => (
          <section key={group.key} className="space-y-4">
            <LearnerSurfaceSectionHeader title={group.label} description={group.description} />
            <div className="grid gap-5 md:grid-cols-2 lg:grid-cols-3">
              {group.plans.map((plan) => (
                <CatalogPlanCard
                  key={plan.code}
                  plan={plan}
                  presentation={resolveCardPresentation(plan.code, presentation)}
                  config={config}
                  owned={ownedPlanCode != null && ownedPlanCode === plan.code}
                  websitePackage={websitePackageByPlanCode.get(plan.code)}
                  onSelect={setSelectedPlan}
                />
              ))}
            </div>
          </section>
        ))
      )}

      {config.sections.showAddOns ? <CatalogAddOnsSection addOns={addOns} presentation={presentation} /> : null}
      {config.sections.showCta ? <CatalogCta config={config} /> : null}

      <CatalogPlanDetailDrawer
        plan={drawerPlan}
        presentation={presentation}
        config={config}
        owned={ownedPlanCode != null && drawerPlan != null && ownedPlanCode === drawerPlan.code}
        variant={variant}
        websitePackage={drawerPlan ? websitePackageByPlanCode.get(drawerPlan.code) : undefined}
        onClose={() => setSelectedPlan(null)}
        onAddToCart={(plan) => {
          const websitePackage = websitePackageByPlanCode.get(plan.code);
          addToCart({
            code: plan.code,
            kind: 'plan',
            name: websitePackage?.name ?? plan.name,
            price: plan.price,
            currency: plan.currency,
          });
          setSelectedPlan(null);
        }}
      />
    </div>
  );
}
