'use client';

/**
 * Pricing page hero — the gold-on-navy gradient banner used by both
 * `/catalog` and `/pricing`. Extracted from `app/catalog/page.tsx` so the
 * landing surfaces can be re-composed without duplicating markup.
 */

export interface PricingHeroBadge {
  label: string;
  tooltip: string;
}

export interface PricingHeroProps {
  eyebrow?: string;
  title?: string;
  subtitle?: string;
  badges?: PricingHeroBadge[];
}

const DEFAULT_BADGES: PricingHeroBadge[] = [
  { label: 'W', tooltip: 'Writing letter assessment add-ons' },
  { label: 'S', tooltip: 'Extra private Speaking sessions' },
  { label: 'TB GBP32', tooltip: 'Discounted GBP 32 Tutor Book add-on' },
];

export function Hero({
  eyebrow = 'OET with Dr. Ahmed Hesham, 2026 Portfolio',
  title = 'The complete OET 2026 catalogue',
  subtitle = 'Recorded courses, writing letter assessments, private speaking sessions and The Tutor Book. Every SKU from the 2026 portfolio, with current and original pricing.',
  badges = DEFAULT_BADGES,
}: PricingHeroProps) {
  return (
    <section className="relative overflow-hidden bg-navy px-4 pb-20 pt-24 text-white">
      <div className="mx-auto max-w-5xl text-center">
        <p className="mb-2 text-xs font-semibold uppercase tracking-[0.3em] text-[#D4A44F]">{eyebrow}</p>
        <h1 className="text-4xl font-bold tracking-tight sm:text-5xl">{title}</h1>
        <p className="mx-auto mt-4 max-w-2xl text-lg text-white/80">{subtitle}</p>
        <div className="mt-6 flex flex-wrap items-center justify-center gap-2 text-sm">
          {badges.map((badge) => (
            <FlagBadge key={badge.label} label={badge.label} tooltip={badge.tooltip} />
          ))}
        </div>
      </div>
    </section>
  );
}

function FlagBadge({ label, tooltip }: PricingHeroBadge) {
  return (
    <span
      title={tooltip}
      className="inline-flex items-center gap-1.5 rounded-full bg-white/10 px-3 py-1 text-xs font-medium text-white"
    >
      <span className="inline-block h-2 w-2 rounded-full bg-[#D4A44F]" /> {label}
    </span>
  );
}
