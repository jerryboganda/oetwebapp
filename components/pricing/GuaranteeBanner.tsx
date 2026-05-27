'use client';

import { ShieldCheck } from 'lucide-react';

/**
 * Single-row guarantee banner — used to anchor the marketing pricing
 * page with the score-guarantee promise (see /billing/score-guarantee
 * for the redemption flow).
 */

export interface GuaranteeBannerProps {
  title?: string;
  body?: string;
  ctaLabel?: string;
  ctaHref?: string;
}

export function GuaranteeBanner({
  title = 'Score guarantee',
  body = 'Hit the target band on your real OET attempt or we extend access until you do. Eligible OET 2026 plans only.',
  ctaLabel = 'Read the guarantee terms',
  ctaHref = '/billing/score-guarantee',
}: GuaranteeBannerProps) {
  return (
    <section className="border-t border-border bg-[#0E2841] px-4 py-12 text-white">
      <div className="mx-auto flex max-w-5xl flex-col items-start gap-4 sm:flex-row sm:items-center">
        <span className="inline-flex h-12 w-12 shrink-0 items-center justify-center rounded-full bg-[#D4A44F]/15 text-[#D4A44F]">
          <ShieldCheck className="h-6 w-6" aria-hidden="true" />
        </span>
        <div className="flex-1">
          <h3 className="text-xl font-bold">{title}</h3>
          <p className="mt-1 text-sm text-white/80">{body}</p>
        </div>
        <a
          href={ctaHref}
          className="inline-flex min-h-11 items-center justify-center rounded-lg border border-white/30 px-5 py-2.5 text-sm font-medium text-white transition-colors hover:bg-white/10"
        >
          {ctaLabel}
        </a>
      </div>
    </section>
  );
}
