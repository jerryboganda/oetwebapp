'use client';

import Link from 'next/link';
import { ArrowRight } from 'lucide-react';
import type { ReactNode } from 'react';

import { cn } from '@/lib/utils';
import { Badge, type BadgeProps } from './badge';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from './card';

export type AdminHubLink = {
  href: string;
  title: string;
  description: string;
  icon: ReactNode;
  badge?: string;
  badgeVariant?: BadgeProps['variant'];
};

export type AdminHubSectionProps = {
  title: string;
  description?: string;
  links: AdminHubLink[];
  columns?: 'two' | 'three' | 'five';
  className?: string;
};

const columnsClass = {
  two: 'grid-cols-1 md:grid-cols-2',
  three: 'grid-cols-1 md:grid-cols-2 xl:grid-cols-3',
  five: 'grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 2xl:grid-cols-5',
} as const;

export function AdminHubLinkCard({ link }: { link: AdminHubLink }) {
  return (
    <Card asChild interactive className="h-full">
      <Link
        href={link.href}
        aria-label={`${link.title} Open workspace`}
        className="group flex h-full flex-col p-4"
      >
        <div className="flex items-start justify-between gap-3">
          <span className="inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-admin-lg bg-[var(--admin-primary-tint)] text-[var(--admin-primary)]">
            {link.icon}
          </span>
          {link.badge ? (
            <Badge variant={link.badgeVariant ?? 'info'} size="sm">
              {link.badge}
            </Badge>
          ) : null}
        </div>
        <div className="mt-4 min-w-0 flex-1">
          <div className="flex items-start justify-between gap-3">
            <CardTitle className="text-base leading-snug">{link.title}</CardTitle>
            <ArrowRight
              className="mt-0.5 h-4 w-4 shrink-0 text-admin-fg-muted transition-transform duration-150 group-hover:translate-x-0.5 group-hover:text-[var(--admin-primary)] motion-reduce:transition-none"
              aria-hidden="true"
            />
          </div>
          <CardDescription className="mt-2 leading-relaxed">{link.description}</CardDescription>
        </div>
        <span className="mt-4 text-sm font-medium text-admin-primary">Open workspace</span>
      </Link>
    </Card>
  );
}

export function AdminHubSection({
  title,
  description,
  links,
  columns = 'three',
  className,
}: AdminHubSectionProps) {
  if (links.length === 0) return null;

  return (
    <section aria-labelledby={`${title.toLowerCase().replace(/[^a-z0-9]+/g, '-')}-heading`} className={className}>
      <Card>
        <CardHeader className="flex-col items-start gap-1">
          <CardTitle id={`${title.toLowerCase().replace(/[^a-z0-9]+/g, '-')}-heading`}>{title}</CardTitle>
          {description ? <CardDescription>{description}</CardDescription> : null}
        </CardHeader>
        <CardContent>
          <div className={cn('grid gap-3', columnsClass[columns])}>
            {links.map((link) => (
              <AdminHubLinkCard key={link.href} link={link} />
            ))}
          </div>
        </CardContent>
      </Card>
    </section>
  );
}