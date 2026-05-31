'use client';

import Link from 'next/link';
import { BarChart3, Eye, FileSearch, Mic, MessageSquareText, NotebookPen, ClipboardList } from 'lucide-react';
import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/admin/ui/card';

type HubCard = {
  href: string;
  title: string;
  description: string;
  icon: React.ReactNode;
  badge: string;
};

const hubCards: HubCard[] = [
  {
    href: '/admin/speaking/result-visibility',
    title: 'Result visibility',
    description: 'Choose exactly which Speaking result fields learners can see, from submission receipt to drills and reattempts.',
    icon: <Eye className="h-5 w-5" />,
    badge: 'Policy',
  },
  {
    href: '/admin/analytics/speaking',
    title: 'Speaking analytics',
    description: 'Review class, tutor-consistency, and content-difficulty metrics for the Speaking module.',
    icon: <BarChart3 className="h-5 w-5" />,
    badge: 'Insights',
  },
  {
    href: '/admin/speaking/recordings/audit',
    title: 'Recording audit',
    description: 'Inspect access patterns and audit trails for learner speaking recordings.',
    icon: <FileSearch className="h-5 w-5" />,
    badge: 'Audit',
  },
  {
    href: '/admin/content/papers?subtest=speaking',
    title: 'Speaking authoring',
    description: 'Open the role-play card authoring surface for candidate, interlocutor, and source assets.',
    icon: <Mic className="h-5 w-5" />,
    badge: 'Content',
  },
  {
    href: '/admin/content/speaking/mock-sets',
    title: 'Mock sets',
    description: 'Bundle published role-plays into the official Speaking mock shape.',
    icon: <MessageSquareText className="h-5 w-5" />,
    badge: 'OET',
  },
  {
    href: '/admin/content/speaking/shared-resources',
    title: 'Shared resources',
    description: 'Manage shared warm-up prompts and assessment-criteria PDFs used across Speaking cards.',
    icon: <NotebookPen className="h-5 w-5" />,
    badge: 'Assets',
  },
];

export default function AdminSpeakingPage() {
  return (
    <AdminCatalogLayout
      title="Speaking"
      description="Operational hub for Speaking result visibility, analytics, recording audits, and the content surfaces that feed the learner experience."
      eyebrow="Admin"
      hideViewModeToggle
    >
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        {hubCards.map((card) => (
          <Card asChild interactive key={card.href} className="h-full">
            <Link href={card.href} className="flex h-full flex-col p-5">
              <div className="flex items-start justify-between gap-3">
                <span className="inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-admin-lg bg-[var(--admin-primary-tint)] text-[var(--admin-primary)]">
                  {card.icon}
                </span>
                <Badge variant="info" size="sm">
                  {card.badge}
                </Badge>
              </div>
              <CardHeader className="px-0 pt-4 pb-0">
                <CardTitle>{card.title}</CardTitle>
                <CardDescription className="mt-2">{card.description}</CardDescription>
              </CardHeader>
              <CardContent className="px-0 pt-4 pb-0">
                <span className="text-sm font-medium text-admin-primary">Open section →</span>
              </CardContent>
            </Link>
          </Card>
        ))}
      </div>
    </AdminCatalogLayout>
  );
}