'use client';

import Link from 'next/link';
import { ArrowRight, CheckCircle2 } from 'lucide-react';

import type { PublicCatalogPlanRow } from '@/lib/types/admin';

/**
 * Single OET 2026 plan card — extracted from `app/catalog/page.tsx` so
 * the matrix view, the bundle grid, and the marketplace can all render
 * identical cards.
 */

const PROFESSION_LABEL: Record<string, string> = {
  all: 'All disciplines',
  medicine: 'Medicine',
  nursing: 'Nursing',
  pharmacy: 'Pharmacy',
};

export interface PackageCardProps {
  plan: PublicCatalogPlanRow;
  /** Link href override. Defaults to `/marketplace/packages/{code}`. */
  detailsHref?: string;
}

export function PackageCard({ plan, detailsHref }: PackageCardProps) {
  const href = detailsHref ?? `/marketplace/packages/${encodeURIComponent(plan.code)}`;

  return (
    <article className="flex h-full flex-col rounded-2xl border border-border bg-background p-6 shadow-sm">
      <div className="flex items-start justify-between gap-3">
        <h4 className="text-lg font-bold leading-snug">{plan.name}</h4>
        <PriceCell price={plan.price} originalPrice={plan.originalPrice ?? null} />
      </div>
      <div className="mt-3 flex flex-wrap gap-1.5">
        <Tag label={PROFESSION_LABEL[plan.profession] ?? plan.profession} />
        <Tag label={formatAccess(plan.accessDurationDays)} />
        {plan.writingAddonsEnabled ? <Tag label="W add-ons" gold /> : null}
        {plan.speakingAddonsEnabled ? <Tag label="S add-ons" gold /> : null}
        {plan.tutorBookDiscountEnabled ? <Tag label="TB GBP32" gold /> : null}
      </div>
      {plan.description ? (
        <p className="mt-3 text-sm text-muted line-clamp-4">{plan.description}</p>
      ) : null}
      {(plan.bundledWritingAssessments > 0 ||
        plan.bundledSpeakingSessions > 0 ||
        plan.bundledAiCredits > 0 ||
        plan.bundledTutorBook ||
        plan.bundledBasicEnglish) && (
        <ul className="mt-4 space-y-1.5 text-sm">
          {plan.bundledWritingAssessments > 0 ? (
            <li className="flex items-center gap-2">
              <CheckCircle2 className="h-4 w-4 flex-none text-emerald-600" />
              {plan.bundledWritingAssessments} bundled writing assessment
              {plan.bundledWritingAssessments === 1 ? '' : 's'}
            </li>
          ) : null}
          {plan.bundledSpeakingSessions > 0 ? (
            <li className="flex items-center gap-2">
              <CheckCircle2 className="h-4 w-4 flex-none text-emerald-600" />
              {plan.bundledSpeakingSessions} bundled private speaking session
              {plan.bundledSpeakingSessions === 1 ? '' : 's'}
            </li>
          ) : null}
          {plan.bundledAiCredits > 0 ? (
            <li className="flex items-center gap-2">
              <CheckCircle2 className="h-4 w-4 flex-none text-emerald-600" />
              {plan.bundledAiCredits} AI practice credits
            </li>
          ) : null}
          {plan.bundledTutorBook ? (
            <li className="flex items-center gap-2">
              <CheckCircle2 className="h-4 w-4 flex-none text-emerald-600" />
              The Tutor Book - First Edition 2026 (PDF + Telegram)
            </li>
          ) : null}
          {plan.bundledBasicEnglish ? (
            <li className="flex items-center gap-2">
              <CheckCircle2 className="h-4 w-4 flex-none text-emerald-600" />
              Basic English foundation course
            </li>
          ) : null}
        </ul>
      )}
      <div className="mt-auto pt-5">
        <Link
          href={href}
          className="inline-flex w-full items-center justify-center gap-2 rounded-lg border border-border bg-background-light px-4 py-2 text-sm font-medium text-navy transition-colors hover:bg-surface"
        >
          View details <ArrowRight className="h-4 w-4" />
        </Link>
      </div>
    </article>
  );
}

export function PriceCell({
  price,
  originalPrice,
  compact = false,
}: {
  price: number;
  originalPrice: number | null;
  compact?: boolean;
}) {
  return (
    <div className="text-right">
      <div className={`font-bold ${compact ? 'text-base' : 'text-xl'}`}>GBP {price.toFixed(0)}</div>
      {originalPrice !== null && originalPrice > price ? (
        <div className="text-xs text-muted line-through">was GBP {originalPrice.toFixed(0)}</div>
      ) : null}
    </div>
  );
}

function Tag({ label, gold = false }: { label: string; gold?: boolean }) {
  return (
    <span
      className={`inline-flex items-center rounded-full px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider ${
        gold ? 'bg-[#D4A44F]/15 text-[#996F1F]' : 'bg-background-light text-muted'
      }`}
    >
      {label}
    </span>
  );
}

function formatAccess(days: number): string {
  if (days >= 9000) return 'Permanent';
  if (days >= 365) return `${Math.round(days / 365)} year${days >= 730 ? 's' : ''}`;
  if (days >= 30) return `${Math.round(days / 30)} months`;
  return `${days} days`;
}
