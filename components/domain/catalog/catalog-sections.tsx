'use client';

import Link from 'next/link';
import { ArrowRight, Search, Sparkles, CheckCircle2 } from 'lucide-react';
import { Card } from '@/components/ui';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { cn } from '@/lib/utils';
import type { MyEntitlementSnapshot } from '@/lib/api';
import type { PublicCatalogAddOnRow } from '@/lib/types/admin';
import {
  type CatalogStorefrontConfig,
  resolveCatalogIcon,
  normalizeAccent,
  professionLabel,
  formatPrice,
} from '@/lib/catalog-presentation';

export function CatalogHero({ config }: { config: CatalogStorefrontConfig }) {
  const accent = normalizeAccent(config.hero.accent, config.accent);
  const HeroIcon = resolveCatalogIcon(config.hero.highlights[0]?.iconKey) ?? Sparkles;
  const highlights = config.hero.highlights.map((item) => ({
    label: item.label,
    value: item.value,
    icon: resolveCatalogIcon(item.iconKey),
  }));

  return (
    <LearnerPageHero
      eyebrow={config.hero.eyebrow}
      title={config.hero.title}
      description={config.hero.subtitle}
      icon={HeroIcon}
      accent={accent}
      highlights={highlights}
    />
  );
}

export function CatalogCta({ config }: { config: CatalogStorefrontConfig }) {
  const { cta } = config;
  return (
    <section className="rounded-2xl border border-border bg-surface px-6 py-10 text-center shadow-sm">
      <h2 className="text-2xl font-bold text-navy">{cta.title}</h2>
      <p className="mx-auto mt-2 max-w-xl text-sm text-muted">{cta.subtitle}</p>
      <div className="mt-6 flex flex-wrap items-center justify-center gap-3">
        <Link
          href={cta.primaryHref}
          className="inline-flex min-h-11 items-center justify-center gap-2 rounded-lg bg-primary px-5 py-2.5 text-sm font-semibold text-white shadow-sm transition-[background-color,transform] duration-200 hover:bg-primary/90 active:scale-[0.98] motion-reduce:active:scale-100"
        >
          {cta.primaryLabel} <ArrowRight className="h-4 w-4" />
        </Link>
        {cta.secondaryLabel ? (
          <Link
            href={cta.secondaryHref}
            className="inline-flex min-h-11 items-center justify-center gap-2 rounded-lg border border-border bg-surface px-5 py-2.5 text-sm font-semibold text-navy transition-colors hover:bg-background-light"
          >
            {cta.secondaryLabel}
          </Link>
        ) : null}
      </div>
    </section>
  );
}

export interface CatalogFiltersProps {
  professions: string[];
  activeProfession: string;
  onProfessionChange: (profession: string) => void;
  query: string;
  onQueryChange: (query: string) => void;
  config: CatalogStorefrontConfig;
}

export function CatalogFilters({
  professions,
  activeProfession,
  onProfessionChange,
  query,
  onQueryChange,
  config,
}: CatalogFiltersProps) {
  return (
    <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
      <div className="flex flex-wrap gap-1.5">
        {professions.map((profession) => {
          const active = profession === activeProfession;
          return (
            <button
              key={profession}
              type="button"
              onClick={() => onProfessionChange(profession)}
              aria-pressed={active}
              className={cn(
                'rounded-full px-3.5 py-1.5 text-sm font-semibold transition-colors',
                active ? 'bg-primary text-white' : 'border border-border bg-surface text-muted hover:text-navy',
              )}
            >
              {professionLabel(config, profession)}
            </button>
          );
        })}
      </div>
      <div className="relative sm:w-64">
        <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" />
        <input
          type="search"
          value={query}
          onChange={(event) => onQueryChange(event.target.value)}
          placeholder="Search packages"
          aria-label="Search packages"
          className="w-full rounded-lg border border-border bg-surface py-2 pl-9 pr-3 text-sm text-navy placeholder:text-muted focus:outline-none focus:ring-2 focus:ring-primary"
        />
      </div>
    </div>
  );
}

export function CatalogAddOnsSection({ addOns }: { addOns: PublicCatalogAddOnRow[] }) {
  if (addOns.length === 0) return null;
  return (
    <section className="space-y-4">
      <LearnerSurfaceSectionHeader
        eyebrow="Add-ons"
        title="Add-ons reference"
        description="Add-ons attach to an eligible parent course. Each card shows the eligibility flag the parent must have."
      />
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {addOns.map((addon) => (
          <Card key={addon.code} padding="md" className="flex h-full flex-col">
            <div className="flex items-start justify-between gap-3">
              <div className="min-w-0">
                <h4 className="font-bold text-navy">{addon.name}</h4>
                <p className="mt-1 text-[11px] uppercase tracking-wider text-muted">
                  requires {addon.eligibilityFlag || 'n/a'}
                </p>
              </div>
              <div className="text-right">
                <div className="text-lg font-bold text-navy">{formatPrice(addon.price, addon.currency)}</div>
                {addon.originalPrice != null && addon.originalPrice > addon.price ? (
                  <div className="text-xs text-muted line-through">was {formatPrice(addon.originalPrice, addon.currency)}</div>
                ) : null}
              </div>
            </div>
            {addon.description ? <p className="mt-3 text-sm text-muted">{addon.description}</p> : null}
          </Card>
        ))}
      </div>
    </section>
  );
}

export function CatalogEntitlementSummary({ snapshot }: { snapshot: MyEntitlementSnapshot }) {
  if (!snapshot.hasEligibleSubscription) return null;

  const stats: Array<{ label: string; value: string }> = [];
  if (snapshot.writingAssessmentsRemaining > 0) stats.push({ label: 'Writing left', value: String(snapshot.writingAssessmentsRemaining) });
  if (snapshot.speakingSessionsRemaining > 0) stats.push({ label: 'Speaking left', value: String(snapshot.speakingSessionsRemaining) });
  if (snapshot.aiCreditsRemaining > 0) stats.push({ label: 'AI credits', value: String(snapshot.aiCreditsRemaining) });

  const expiry = snapshot.expiresAt
    ? new Date(snapshot.expiresAt).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' })
    : null;

  return (
    <Card padding="md" className="border-primary/20 bg-primary/5">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-start gap-3">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-2xl bg-primary/10 text-primary">
            <CheckCircle2 className="h-5 w-5" />
          </div>
          <div>
            <p className="text-[11px] font-bold uppercase tracking-[0.16em] text-primary">Your current plan</p>
            <p className="text-base font-bold text-navy">{snapshot.planCode ?? snapshot.tier}</p>
            {expiry ? <p className="text-sm text-muted">Access until {expiry}</p> : null}
          </div>
        </div>
        {stats.length > 0 ? (
          <div className="flex flex-wrap gap-2">
            {stats.map((stat) => (
              <div key={stat.label} className="rounded-xl border border-border bg-surface px-3 py-2 text-center">
                <p className="text-base font-bold text-navy">{stat.value}</p>
                <p className="text-[11px] uppercase tracking-wider text-muted">{stat.label}</p>
              </div>
            ))}
          </div>
        ) : null}
      </div>
    </Card>
  );
}
