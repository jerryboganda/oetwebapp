'use client';

import Link from 'next/link';
import { ArrowRight, CheckCircle2, Clock, Layers, Tag } from 'lucide-react';
import { Drawer } from '@/components/ui';
import { cn } from '@/lib/utils';
import type { PublicCatalogPlanRow } from '@/lib/types/admin';
import {
  type CatalogPresentation,
  type CatalogStorefrontConfig,
  resolveCardPresentation,
  resolveCatalogIcon,
  defaultIconForCategory,
  normalizeAccent,
  planFeatureBullets,
  addOnEnabledFlags,
  professionLabel,
  categoryLabel,
  formatAccessDuration,
  formatPrice,
} from '@/lib/catalog-presentation';
import { CATALOG_ACCENT_TILE } from './catalog-plan-card';

export interface CatalogPlanDetailDrawerProps {
  plan: PublicCatalogPlanRow | null;
  presentation?: CatalogPresentation | null;
  config: CatalogStorefrontConfig;
  owned?: boolean;
  variant: 'dashboard' | 'public';
  onClose: () => void;
}

interface BundledStat {
  label: string;
  value: string;
}

function bundledStats(plan: PublicCatalogPlanRow): BundledStat[] {
  const stats: BundledStat[] = [{ label: 'Access', value: formatAccessDuration(plan.accessDurationDays) }];
  if (plan.bundledWritingAssessments > 0) stats.push({ label: 'Writing assessments', value: String(plan.bundledWritingAssessments) });
  if (plan.bundledSpeakingSessions > 0) stats.push({ label: 'Speaking sessions', value: String(plan.bundledSpeakingSessions) });
  if (plan.bundledAiCredits > 0) stats.push({ label: 'AI credits', value: String(plan.bundledAiCredits) });
  if (plan.bundledTutorBook) stats.push({ label: 'Tutor Book', value: 'Included' });
  if (plan.bundledBasicEnglish) stats.push({ label: 'Basic English', value: 'Included' });
  return stats;
}

export function CatalogPlanDetailDrawer({ plan, presentation, config, owned, variant, onClose }: CatalogPlanDetailDrawerProps) {
  const card = plan ? resolveCardPresentation(plan.code, presentation) : {};
  const Icon = plan ? (resolveCatalogIcon(card.iconKey) ?? defaultIconForCategory(plan.productCategory)) : Layers;
  const accent = normalizeAccent(card.accent, config.accent);
  const tile = CATALOG_ACCENT_TILE[accent] ?? CATALOG_ACCENT_TILE.primary;
  const bullets = plan ? planFeatureBullets(plan, card) : [];
  const flags = plan ? addOnEnabledFlags(plan) : [];
  const stats = plan ? bundledStats(plan) : [];
  const hasDiscount = plan?.originalPrice != null && plan.originalPrice > plan.price;
  const buyHref = plan
    ? `/checkout/review?productType=plan_purchase&priceId=${encodeURIComponent(plan.code)}&quantity=1`
    : '#';

  return (
    <Drawer open={plan != null} onClose={onClose} title={plan?.name ?? 'Package details'}>
      {plan ? (
        <div className="space-y-6">
          <div className="flex items-start gap-4">
            <div className={cn('flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl', tile)}>
              <Icon className="h-6 w-6" />
            </div>
            <div className="min-w-0">
              <div className="flex flex-wrap items-center gap-2 text-[11px] font-semibold uppercase tracking-wide text-muted">
                <span>{professionLabel(config, plan.profession)}</span>
                <span aria-hidden="true">·</span>
                <span>{categoryLabel(config, plan.productCategory)}</span>
              </div>
              <div className="mt-2 flex items-baseline gap-2">
                <span className="text-3xl font-bold text-navy">{formatPrice(plan.price, plan.currency)}</span>
                {hasDiscount ? (
                  <span className="text-sm text-muted line-through">was {formatPrice(plan.originalPrice as number, plan.currency)}</span>
                ) : null}
              </div>
            </div>
          </div>

          {card.tagline || plan.description ? (
            <p className="text-sm leading-relaxed text-muted">{card.tagline ?? plan.description}</p>
          ) : null}

          {stats.length > 0 ? (
            <div className="grid grid-cols-2 gap-2">
              {stats.map((stat) => (
                <div key={stat.label} className="rounded-xl border border-border bg-background-light px-3 py-2">
                  <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-muted">{stat.label}</p>
                  <p className="text-sm font-semibold text-navy">{stat.value}</p>
                </div>
              ))}
            </div>
          ) : null}

          {bullets.length > 0 ? (
            <div>
              <p className="mb-2 text-xs font-bold uppercase tracking-wider text-muted">What you get</p>
              <ul className="space-y-2 text-sm text-navy">
                {bullets.map((bullet) => (
                  <li key={bullet} className="flex items-start gap-2">
                    <CheckCircle2 className="mt-0.5 h-4 w-4 flex-none text-success" />
                    <span>{bullet}</span>
                  </li>
                ))}
              </ul>
            </div>
          ) : null}

          {flags.length > 0 ? (
            <div>
              <p className="mb-2 text-xs font-bold uppercase tracking-wider text-muted">Available add-ons</p>
              <div className="flex flex-wrap gap-1.5">
                {flags.map((flag) => (
                  <span key={flag.key} className="inline-flex items-center gap-1 rounded-full bg-primary/10 px-2.5 py-1 text-xs font-semibold text-primary">
                    <Tag className="h-3 w-3" /> {flag.label}
                  </span>
                ))}
              </div>
            </div>
          ) : null}

          <div className="border-t border-border pt-4">
            {owned ? (
              <div className="flex items-center justify-center gap-2 rounded-lg bg-success/10 px-4 py-3 text-sm font-semibold text-success">
                <CheckCircle2 className="h-4 w-4" /> This package is active on your account
              </div>
            ) : (
              <Link
                href={buyHref}
                className="inline-flex w-full items-center justify-center gap-2 rounded-lg bg-primary px-5 py-3 text-sm font-semibold text-white transition-[background-color,transform] duration-200 hover:bg-primary/90 active:scale-[0.98] motion-reduce:active:scale-100"
              >
                {variant === 'dashboard' ? 'Continue to purchase' : 'Get this package'} <ArrowRight className="h-4 w-4" />
              </Link>
            )}
            <p className="mt-2 text-center text-xs text-muted">
              <Clock className="mr-1 inline h-3 w-3" />
              Secure checkout · {formatAccessDuration(plan.accessDurationDays)}
            </p>
          </div>
        </div>
      ) : null}
    </Drawer>
  );
}
