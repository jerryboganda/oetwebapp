'use client';

import { useEffect, useMemo, useState } from 'react';
import { LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { fetchPublicCatalog, fetchMyEntitlementSnapshot, type MyEntitlementSnapshot } from '@/lib/api';
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

export interface CatalogStorefrontProps {
  variant: 'dashboard' | 'public';
}

export function CatalogStorefront({ variant }: CatalogStorefrontProps) {
  const { addToCart } = useAddToCart();
  const [data, setData] = useState<PublicCatalogResponseWithPresentation | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [entitlement, setEntitlement] = useState<MyEntitlementSnapshot | null>(null);
  const [selectedPlan, setSelectedPlan] = useState<PublicCatalogPlanRow | null>(null);
  const [activeProfession, setActiveProfession] = useState('all');
  const [query, setQuery] = useState('');

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const response = (await fetchPublicCatalog()) as PublicCatalogResponseWithPresentation;
        if (!cancelled) {
          setData(response);
          setError(null);
        }
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load the catalogue.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (variant !== 'dashboard') return undefined;
    let cancelled = false;
    void (async () => {
      try {
        const snapshot = await fetchMyEntitlementSnapshot();
        if (!cancelled) setEntitlement(snapshot);
      } catch {
        // Entitlement is optional context for the dashboard variant; ignore failures.
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [variant]);

  const presentation: CatalogPresentation | null = data?.presentation ?? null;
  const config = useMemo(() => resolveStorefrontConfig(presentation), [presentation]);
  const plans = useMemo(() => data?.plans ?? [], [data]);
  const addOns = useMemo(() => sortAddOns(data?.addOns ?? []), [data]);

  const professions = useMemo(() => {
    const set = new Set<string>(['all']);
    for (const plan of plans) set.add(plan.profession);
    return Array.from(set);
  }, [plans]);

  const filteredPlans = useMemo(() => {
    const needle = query.trim().toLowerCase();
    return plans.filter((plan) => {
      // A plan allocated to 'all' disciplines shows under every discipline tab (matches the
      // storefront's "apply for all professions" semantics, mirroring subscriptions-catalog).
      const professionMatch =
        activeProfession === 'all' || plan.profession === activeProfession || plan.profession === 'all';
      const queryMatch =
        needle.length === 0 ||
        plan.name.toLowerCase().includes(needle) ||
        (plan.description ?? '').toLowerCase().includes(needle);
      return professionMatch && queryMatch;
    });
  }, [plans, activeProfession, query]);

  const groups = useMemo(
    () => groupPlansByCategory(filteredPlans, config, presentation),
    [filteredPlans, config, presentation],
  );

  const ownedPlanCode = entitlement?.planCode ?? null;

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
      {config.sections.showCompareMatrix ? <CatalogCompareMatrix plans={filteredPlans} config={config} /> : null}

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
                  onSelect={setSelectedPlan}
                />
              ))}
            </div>
          </section>
        ))
      )}

      {config.sections.showAddOns ? <CatalogAddOnsSection addOns={addOns} /> : null}
      {config.sections.showCta ? <CatalogCta config={config} /> : null}

      <CatalogPlanDetailDrawer
        plan={selectedPlan}
        presentation={presentation}
        config={config}
        owned={ownedPlanCode != null && selectedPlan != null && ownedPlanCode === selectedPlan.code}
        variant={variant}
        onClose={() => setSelectedPlan(null)}
        onAddToCart={(plan) => {
          addToCart({ code: plan.code, kind: 'plan', name: plan.name, price: plan.price, currency: plan.currency });
          setSelectedPlan(null);
        }}
      />
    </div>
  );
}
