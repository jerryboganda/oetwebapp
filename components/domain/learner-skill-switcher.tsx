'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { BookOpen, FilePenLine, FileQuestion, Headphones, MessageSquare, Mic, Stethoscope } from 'lucide-react';
import { cn } from '@/lib/utils';

const learnerSkillModules = [
  { href: '/writing', label: 'Writing', shortLabel: 'Writing', icon: FilePenLine, description: 'Letters and case notes' },
  { href: '/speaking', label: 'Speaking', shortLabel: 'Speaking', icon: Mic, description: 'Roleplay and fluency' },
  { href: '/reading', label: 'Reading', shortLabel: 'Reading', icon: BookOpen, description: 'Parts A, B, and C' },
  { href: '/listening', label: 'Listening', shortLabel: 'Listening', icon: Headphones, description: 'Audio, notes, and review' },
  { href: '/mocks', label: 'Mocks', shortLabel: 'Mocks', icon: FileQuestion, description: 'Timed transfer practice' },
  { href: '/diagnostic', label: 'Diagnostic', shortLabel: 'Diagnostic', icon: Stethoscope, description: 'Baseline and triage' },
  { href: '/conversation', label: 'AI Conversation', shortLabel: 'Conversation', icon: MessageSquare, description: 'Interactive scenarios' },
] as const;

function isActive(pathname: string | null, href: string) {
  if (!pathname) return false;
  return pathname === href || pathname.startsWith(`${href}/`);
}

export function LearnerSkillSwitcher({
  className,
  compact = false,
  title = 'Switch practice focus',
}: {
  className?: string;
  compact?: boolean;
  title?: string;
}) {
  const pathname = usePathname();

  return (
    <nav
      className={cn('rounded-[20px] border border-border bg-surface p-3 shadow-sm', className)}
      aria-label="Skill modules"
    >
      <div className="mb-3 flex items-center justify-between gap-3">
        <p className="text-xs font-black uppercase tracking-[0.18em] text-muted">{title}</p>
      </div>
      <div className={cn('grid gap-2', compact ? 'grid-cols-2 sm:grid-cols-4 lg:grid-cols-7' : 'grid-cols-1 sm:grid-cols-2 lg:grid-cols-4')}>
        {learnerSkillModules.map((module) => {
          const active = isActive(pathname, module.href);
          const Icon = module.icon;
          return (
            <Link
              key={module.href}
              href={module.href}
              aria-current={active ? 'page' : undefined}
              className={cn(
                'pressable group flex min-h-11 items-center gap-2 rounded-2xl border px-3 py-2 text-left transition-[background-color,border-color,box-shadow,color] duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2',
                active
                  ? 'border-primary/30 bg-primary/10 text-primary-dark shadow-sm'
                  : 'border-border/70 bg-background-light text-navy hover:border-border-hover hover:bg-white',
              )}
            >
              <span className={cn('flex h-8 w-8 shrink-0 items-center justify-center rounded-xl', active ? 'bg-primary text-white' : 'bg-white text-muted ring-1 ring-border/70')}>
                <Icon className="h-4 w-4" aria-hidden="true" />
              </span>
              <span className="min-w-0">
                <span className="block truncate text-sm font-bold">{compact ? module.shortLabel : module.label}</span>
                {!compact ? <span className="mt-0.5 block truncate text-xs text-muted">{module.description}</span> : null}
              </span>
            </Link>
          );
        })}
      </div>
    </nav>
  );
}
