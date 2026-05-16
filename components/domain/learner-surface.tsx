'use client';

import Link from 'next/link';
import { ArrowRight } from 'lucide-react';
import { isValidElement, type ElementType, type ReactNode } from 'react';
import { Badge, Button, Card } from '@/components/ui';
import { cn } from '@/lib/utils';
import {
  type LearnerPageHeroModel,
  type LearnerPageHeroHighlight,
  type LearnerSurfaceCardModel,
  type LearnerSurfaceMetaItem,
  sanitizeLearnerPageHeroHighlights,
  sanitizeLearnerSurfaceMetaItems,
} from '@/lib/learner-surface';

const accentTokens = {
  primary: {
    icon: 'bg-primary/10 text-primary',
    eyebrow: 'bg-primary/10 text-primary border-primary/20',
  },
  navy: {
    icon: 'bg-navy/10 text-navy',
    eyebrow: 'bg-navy/10 text-navy border-navy/20',
  },
  amber: {
    icon: 'bg-amber-50 text-amber-700',
    eyebrow: 'bg-amber-50 text-amber-700 border-amber-200',
  },
  blue: {
    icon: 'bg-blue-50 text-blue-700',
    eyebrow: 'bg-blue-50 text-blue-700 border-blue-200',
  },
  indigo: {
    icon: 'bg-indigo-50 text-indigo-700',
    eyebrow: 'bg-indigo-50 text-indigo-700 border-indigo-200',
  },
  purple: {
    icon: 'bg-purple-50 text-purple-700',
    eyebrow: 'bg-purple-50 text-purple-700 border-purple-200',
  },
  rose: {
    icon: 'bg-rose-50 text-rose-700',
    eyebrow: 'bg-rose-50 text-rose-700 border-rose-200',
  },
  emerald: {
    icon: 'bg-emerald-50 text-emerald-700',
    eyebrow: 'bg-emerald-50 text-emerald-700 border-emerald-200',
  },
  slate: {
    icon: 'bg-slate-100 text-slate-700',
    eyebrow: 'bg-slate-100 text-slate-700 border-slate-200',
  },
} as const;

const actionVariantStyles = {
  primary: 'bg-primary text-white hover:bg-primary/90 shadow-sm',
  secondary: 'bg-navy text-white hover:bg-navy/90 shadow-sm',
  outline: 'border border-border text-navy hover:bg-surface hover:border-border-hover',
  ghost: 'text-navy hover:bg-lavender/40 dark:hover:bg-white/5',
} as const;

function renderIcon(icon: ElementType | ReactNode | undefined, className?: string) {
  if (!icon) {
    return null;
  }

  if (isValidElement(icon)) {
    return icon;
  }

  const Icon = icon as ElementType;
  return <Icon className={className} />;
}

function renderAction(action: LearnerSurfaceCardModel['primaryAction'] | LearnerSurfaceCardModel['secondaryAction'], fullWidth = false) {
  if (!action) return null;

  const variant = action.variant ?? 'primary';

  if (action.href) {
    return (
      <Link
        href={action.href}
        className={cn(
          'inline-flex items-center justify-center gap-2 rounded-lg px-5 py-2 text-sm font-medium transition-all duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 active:scale-[0.98]',
          actionVariantStyles[variant],
          fullWidth && 'w-full',
        )}
      >
        {action.label}
        <ArrowRight className="w-4 h-4" />
      </Link>
    );
  }

  return (
    <Button fullWidth={fullWidth} variant={variant} onClick={action.onClick}>
      {action.label}
      <ArrowRight className="w-4 h-4" />
    </Button>
  );
}

export function LearnerSurfaceMetaRow({ items, size = 'normal', className }: { items?: LearnerSurfaceMetaItem[]; size?: 'normal' | 'compact'; className?: string }) {
  const safeItems = sanitizeLearnerSurfaceMetaItems(items);

  if (safeItems.length === 0) {
    return null;
  }

  return (
    <div className={cn(
      'flex flex-wrap items-center gap-x-4 gap-y-2',
      size === 'compact' ? 'text-xs text-muted' : 'text-sm font-semibold text-muted',
      className,
    )}>
      {safeItems.map((item) => {
        const Icon = item.icon;
        return (
          <span key={item.label} className="flex items-center gap-1.5">
            {Icon ? (
              <Icon className={size === 'compact' ? 'w-3 h-3' : 'w-4 h-4'} />
            ) : (
              // Visual-parity fallback: callers that omit `icon` would otherwise
              // render a bare label and look mis-aligned next to sibling cards
              // whose meta items have icons. A small filled bullet keeps the
              // icon-column footprint identical without inventing semantics.
              <span aria-hidden="true" className={cn('flex items-center justify-center', size === 'compact' ? 'h-3 w-3' : 'h-4 w-4')}>
                <span className="block h-1.5 w-1.5 rounded-full bg-current opacity-60" />
              </span>
            )}
            {item.label}
          </span>
        );
      })}
    </div>
  );
}

export function LearnerSurfaceSectionHeader({
  eyebrow,
  icon,
  title,
  description,
  action,
  className,
}: {
  eyebrow?: string;
  icon?: ElementType | ReactNode;
  title: string;
  description?: string;
  action?: ReactNode;
  className?: string;
}) {
  return (
    <div className={cn('flex flex-col sm:flex-row sm:items-end justify-between gap-4', className)}>
      <div>
        {eyebrow ? (
          <p className="text-xs font-bold text-muted uppercase tracking-wider mb-1.5">{eyebrow}</p>
        ) : null}
        <div className="flex items-center gap-2">
          {icon ? <span className="text-primary">{renderIcon(icon, 'h-4 w-4')}</span> : null}
          <h2 className="text-xl font-bold text-navy">{title}</h2>
        </div>
        {description ? <p className="text-sm text-muted mt-1">{description}</p> : null}
      </div>
      {action}
    </div>
  );
}

export function LearnerPageHero({
  title,
  description,
  eyebrow,
  icon: Icon,
  accent = 'primary',
  highlights: rawHighlights,
  aside,
}: LearnerPageHeroModel) {
  const palette = accentTokens[accent];
  const highlights = sanitizeLearnerPageHeroHighlights(rawHighlights);

  const renderHighlight = (item: LearnerPageHeroHighlight) => {
    const HighlightIcon = item.icon;

    return (
      <div
        key={`${item.label}-${item.value}`}
        className="inline-flex min-w-0 flex-1 basis-[140px] items-center gap-2 rounded-2xl border border-border bg-background-light px-3 py-2"
      >
        {HighlightIcon ? (
          <div className={cn('flex h-8 w-8 shrink-0 items-center justify-center rounded-xl', palette.icon)}>
            <HighlightIcon className="h-4 w-4" />
          </div>
        ) : null}
        <div className="min-w-0">
          <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-muted">{item.label}</p>
          <p className="text-sm font-semibold text-navy break-words">{item.value}</p>
        </div>
      </div>
    );
  };

  return (
    <section className="rounded-2xl border border-border bg-surface px-4 py-4 shadow-sm sm:px-6 sm:py-6">
      <div className="flex flex-col gap-5 lg:flex-row lg:items-start lg:justify-between">
        <div className="min-w-0 flex-1">
          <div className="flex items-start gap-3 sm:gap-4">
          {Icon ? (
              <div className={cn('mt-0.5 flex h-10 w-10 shrink-0 items-center justify-center rounded-xl sm:h-12 sm:w-12 sm:rounded-2xl', palette.icon)}>
                {renderIcon(Icon, 'h-5 w-5 sm:h-6 sm:w-6')}
            </div>
          ) : null}
            <div className="min-w-0">
              {eyebrow ? <p className="mb-1.5 text-[11px] font-semibold uppercase tracking-[0.18em] text-muted">{eyebrow}</p> : null}
              <h1 className="text-xl font-bold tracking-tight text-navy sm:text-[1.75rem]">{title}</h1>
              <p className="mt-2 max-w-3xl text-sm leading-6 text-muted">{description}</p>
            </div>
          </div>

          {highlights.length > 0 ? (
            <div className="mt-4 flex flex-wrap gap-2.5">
              {highlights.map(renderHighlight)}
            </div>
          ) : null}
        </div>
        {aside ? <div className="shrink-0 lg:max-w-sm">{aside}</div> : null}
      </div>
    </section>
  );
}

export function LearnerSurfaceCard({
  card,
  children,
  footer,
  className,
}: {
  card: LearnerSurfaceCardModel;
  children?: ReactNode;
  footer?: ReactNode;
  className?: string;
}) {
  const palette = accentTokens[card.accent ?? 'primary'];
  const EyebrowIcon = card.eyebrowIcon;

  return (
    <Card className={cn('h-full', className)}>
      <div className="flex h-full flex-col justify-between gap-6">
        <div>
          <div className="flex items-start justify-between gap-3">
            <div>
              {card.eyebrow ? (
                <div className={cn('inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-xs font-bold uppercase tracking-wider', palette.eyebrow)}>
                  {EyebrowIcon ? <EyebrowIcon className="w-3.5 h-3.5" /> : null}
                  {card.eyebrow}
                </div>
              ) : null}
            </div>
            {card.statusLabel ? <Badge variant="muted">{card.statusLabel}</Badge> : null}
          </div>
          <h3 className="mt-4 text-xl font-bold text-navy">{card.title}</h3>
          <p className="mt-2 text-sm leading-relaxed text-muted">{card.description}</p>
          <LearnerSurfaceMetaRow items={card.metaItems} className="mt-4" />
          {children ? <div className="mt-5">{children}</div> : null}
        </div>
        <div className="space-y-3">
          {footer}
          {card.primaryAction ? renderAction(card.primaryAction, true) : null}
          {card.secondaryAction ? renderAction(card.secondaryAction, true) : null}
        </div>
      </div>
    </Card>
  );
}
