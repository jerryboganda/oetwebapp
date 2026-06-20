'use client';

import { ArrowRight, CheckCircle2, Sparkles } from 'lucide-react';
import { Card } from '@/components/ui';
import { cn } from '@/lib/utils';
import type { PublicCatalogPlanRow } from '@/lib/types/admin';
import {
  type CatalogCardPresentation,
  type CatalogStorefrontConfig,
  resolveCatalogIcon,
  defaultIconForCategory,
  normalizeAccent,
  planFeatureBullets,
  addOnEnabledFlags,
  professionLabel,
  formatAccessDuration,
  formatPrice,
} from '@/lib/catalog-presentation';

export const CATALOG_ACCENT_TILE: Record<string, string> = {
  primary: 'bg-primary/10 text-primary',
  navy: 'bg-navy/10 text-navy',
  amber: 'bg-amber-50 text-amber-700',
  blue: 'bg-blue-50 text-blue-700',
  indigo: 'bg-indigo-50 text-indigo-700',
  purple: 'bg-purple-50 text-purple-700',
  rose: 'bg-rose-50 text-rose-700',
  emerald: 'bg-emerald-50 text-emerald-700',
  slate: 'bg-slate-100 text-slate-700',
};

export interface CatalogPlanCardProps {
  plan: PublicCatalogPlanRow;
  presentation: CatalogCardPresentation;
  config: CatalogStorefrontConfig;
  owned?: boolean;
  onSelect: (plan: PublicCatalogPlanRow) => void;
}

export function CatalogPlanCard({ plan, presentation, config, owned, onSelect }: CatalogPlanCardProps) {
  const Icon = resolveCatalogIcon(presentation.iconKey) ?? defaultIconForCategory(plan.productCategory);
  const accent = normalizeAccent(presentation.accent, config.accent);
  const tile = CATALOG_ACCENT_TILE[accent] ?? CATALOG_ACCENT_TILE.primary;
  const bullets = planFeatureBullets(plan, presentation).slice(0, 3);
  const flags = addOnEnabledFlags(plan);
  const tagline = presentation.tagline ?? plan.description ?? '';
  const hasDiscount = plan.originalPrice != null && plan.originalPrice > plan.price;

  return (
    <Card
      hoverable
      padding="none"
      className={cn('flex h-full flex-col overflow-hidden', presentation.featured && 'ring-2 ring-primary/40 shadow-clinical')}
    >
      {presentation.featured ? (
        <div className="flex items-center justify-center gap-1.5 bg-primary px-3 py-1.5 text-xs font-bold uppercase tracking-wider text-white">
          <Sparkles className="h-3.5 w-3.5" /> {presentation.badgeLabel || 'Most popular'}
        </div>
      ) : null}
      <div className="flex h-full flex-col gap-4 p-5">
        <div className="flex items-start justify-between gap-3">
          <div className={cn('flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl', tile)}>
            <Icon className="h-5 w-5" />
          </div>
          <div className="text-right">
            <div className="text-2xl font-bold text-navy">{formatPrice(plan.price, plan.currency)}</div>
            {hasDiscount ? (
              <div className="text-xs text-muted line-through">was {formatPrice(plan.originalPrice as number, plan.currency)}</div>
            ) : null}
          </div>
        </div>
        <div>
          <h3 className="text-lg font-bold leading-snug text-navy">{plan.name}</h3>
          <div className="mt-1.5 flex flex-wrap items-center gap-2 text-[11px] font-semibold uppercase tracking-wide text-muted">
            <span>{professionLabel(config, plan.profession)}</span>
            <span aria-hidden="true">·</span>
            <span>{formatAccessDuration(plan.accessDurationDays)}</span>
          </div>
          {tagline ? <p className="mt-3 text-sm leading-relaxed text-muted line-clamp-3">{tagline}</p> : null}
        </div>
        {bullets.length > 0 ? (
          <ul className="space-y-1.5 text-sm text-navy">
            {bullets.map((bullet) => (
              <li key={bullet} className="flex items-start gap-2">
                <CheckCircle2 className="mt-0.5 h-4 w-4 flex-none text-success" />
                <span>{bullet}</span>
              </li>
            ))}
          </ul>
        ) : null}
        {flags.length > 0 ? (
          <div className="flex flex-wrap gap-1.5">
            {flags.map((flag) => (
              <span key={flag.key} className="inline-flex items-center rounded-full bg-primary/10 px-2.5 py-0.5 text-[11px] font-semibold text-primary">
                {flag.label}
              </span>
            ))}
          </div>
        ) : null}
        <div className="mt-auto pt-1">
          {owned ? (
            <span className="inline-flex w-full items-center justify-center gap-2 rounded-lg bg-success/10 px-4 py-2.5 text-sm font-semibold text-success">
              <CheckCircle2 className="h-4 w-4" /> Active on your account
            </span>
          ) : (
            <button
              type="button"
              onClick={() => onSelect(plan)}
              className="inline-flex w-full items-center justify-center gap-2 rounded-lg bg-primary px-4 py-2.5 text-sm font-semibold text-white transition-[background-color,transform] duration-200 hover:bg-primary/90 active:scale-[0.98] motion-reduce:active:scale-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
            >
              View details <ArrowRight className="h-4 w-4" />
            </button>
          )}
        </div>
      </div>
    </Card>
  );
}
