'use client';

/**
 * Admin · Writing hub.
 *
 * Landing page for the OET Writing exam-closure admin surface. Surfaces the
 * authoring, analytics, and policy tools as navigation cards so each is
 * reachable in one click. The individual pages enforce their own permission
 * gates; this hub is intentionally a simple, link-only index.
 */

import Link from 'next/link';
import {
  ArrowRight,
  BarChart3,
  Cpu,
  Eye,
  FilePlus2,
  ListChecks,
  PenSquare,
  Sparkles,
} from 'lucide-react';
import {
  AdminSettingsLayout,
  SettingsSection,
} from '@/components/admin/layout/admin-settings-layout';
import { Card } from '@/components/admin/ui/card';

interface HubLink {
  href: string;
  title: string;
  description: string;
  icon: React.ReactNode;
}

const AUTHORING_LINKS: HubLink[] = [
  {
    href: '/admin/writing/tasks/new',
    title: 'Create task',
    description: 'Author a new OET Writing task with case notes, recipient, and checklist.',
    icon: <FilePlus2 className="h-5 w-5" />,
  },
  {
    href: '/admin/writing/tasks',
    title: 'Task library',
    description: 'Browse, edit, clone, publish, and export existing Writing tasks.',
    icon: <ListChecks className="h-5 w-5" />,
  },
];

const INSIGHTS_LINKS: HubLink[] = [
  {
    href: '/admin/writing/analytics/rule-violations',
    title: 'Analytics',
    description: 'Integrity telemetry — paste blocks, focus loss, and rule violations.',
    icon: <BarChart3 className="h-5 w-5" />,
  },
  {
    href: '/admin/writing/result-visibility',
    title: 'Result visibility',
    description: 'Control when graded results are released to candidates.',
    icon: <Eye className="h-5 w-5" />,
  },
];

const AI_LINKS: HubLink[] = [
  {
    href: '/admin/writing/options',
    title: 'AI options',
    description: 'Kill switches for AI grading and coach, plus free-tier entitlement.',
    icon: <Cpu className="h-5 w-5" />,
  },
  {
    href: '/admin/writing/ai-draft',
    title: 'AI draft',
    description: 'Configure the AI draft-generation pipeline for Writing.',
    icon: <Sparkles className="h-5 w-5" />,
  },
];

function HubCardGrid({ links }: { links: HubLink[] }) {
  return (
    <div className="grid gap-3 sm:grid-cols-2">
      {links.map((link) => (
        <Link
          key={link.href}
          href={link.href}
          className="group rounded-admin outline-none focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)]"
        >
          <Card
            surface="default"
            className="h-full p-4 transition-colors duration-150 group-hover:border-[var(--admin-primary)] motion-reduce:transition-none"
          >
            <div className="flex items-start gap-3">
              <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-admin bg-[var(--admin-primary-tint)] text-[var(--admin-primary)]">
                {link.icon}
              </div>
              <div className="min-w-0 flex-1">
                <div className="flex items-center justify-between gap-2">
                  <h3 className="text-sm font-semibold text-admin-fg-strong">{link.title}</h3>
                  <ArrowRight
                    className="h-4 w-4 shrink-0 text-admin-fg-muted transition-transform duration-150 group-hover:translate-x-0.5 group-hover:text-[var(--admin-primary)] motion-reduce:transition-none"
                    aria-hidden="true"
                  />
                </div>
                <p className="mt-1 text-xs text-admin-fg-muted">{link.description}</p>
              </div>
            </div>
          </Card>
        </Link>
      ))}
    </div>
  );
}

export default function AdminWritingHubPage() {
  return (
    <AdminSettingsLayout
      title="Writing"
      description="Author Writing tasks, review integrity analytics, and manage result-release policy."
      eyebrow="Writing"
      icon={<PenSquare className="h-5 w-5" />}
      breadcrumbs={[{ label: 'Admin', href: '/admin' }, { label: 'Writing' }]}
    >
      <SettingsSection title="Authoring" description="Create and manage Writing tasks.">
        <HubCardGrid links={AUTHORING_LINKS} />
      </SettingsSection>

      <SettingsSection
        title="Insights & policy"
        description="Monitor exam integrity and control how results reach candidates."
      >
        <HubCardGrid links={INSIGHTS_LINKS} />
      </SettingsSection>

      <SettingsSection title="AI" description="AI grading, coaching, and draft generation.">
        <HubCardGrid links={AI_LINKS} />
      </SettingsSection>
    </AdminSettingsLayout>
  );
}
