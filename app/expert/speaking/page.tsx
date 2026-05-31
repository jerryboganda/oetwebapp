'use client';

import Link from 'next/link';
import { BarChart3, ClipboardList, Eye, Mic, MessageSquareText } from 'lucide-react';
import { ExpertDashboardShell } from '@/components/layout/expert-dashboard-shell';
import { Card } from '@/components/ui/card';

type HubCard = {
  href: string;
  title: string;
  description: string;
  icon: React.ReactNode;
};

const hubCards: HubCard[] = [
  {
    href: '/expert/speaking/queue',
    title: 'Review queue',
    description: 'Pick up submitted Speaking sessions that are ready for expert review.',
    icon: <ClipboardList className="h-5 w-5 text-primary" />,
  },
  {
    href: '/expert/speaking/assess',
    title: 'Assess sessions',
    description: 'Open the scoring workspace for sessions you are actively reviewing.',
    icon: <Eye className="h-5 w-5 text-primary" />,
  },
  {
    href: '/expert/speaking/moderation',
    title: 'Moderation',
    description: 'Resolve double-marked Speaking sessions and request reattempts when needed.',
    icon: <BarChart3 className="h-5 w-5 text-primary" />,
  },
  {
    href: '/expert/speaking/live-room',
    title: 'Live room',
    description: 'Jump into the live-tutor room flow for real-time speaking support.',
    icon: <Mic className="h-5 w-5 text-primary" />,
  },
  {
    href: '/expert/speaking/queue',
    title: 'Speaking workflow',
    description: 'Follow the queue → assess → moderate flow without leaving the Speaking portal.',
    icon: <MessageSquareText className="h-5 w-5 text-primary" />,
  },
];

export default function ExpertSpeakingPage() {
  return (
    <ExpertDashboardShell pageTitle="Speaking" workspaceClassName="space-y-6">
      <header className="space-y-2">
        <p className="text-xs font-semibold uppercase tracking-widest text-primary">Expert speaking portal</p>
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-navy">Speaking workflow hub</h1>
          <p className="mt-1 max-w-3xl text-sm text-muted">
            Move from queue to assessment to moderation in one place, then open the live room when a learner needs real-time support.
          </p>
        </div>
      </header>

      <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        {hubCards.map((card) => (
          <Card key={card.href} className="h-full overflow-hidden">
            <Link href={card.href} className="flex h-full flex-col gap-3 p-5 hover:bg-primary/5">
              <span className="inline-flex h-10 w-10 items-center justify-center rounded-2xl bg-primary/10">
                {card.icon}
              </span>
              <div>
                <h2 className="text-base font-semibold text-navy">{card.title}</h2>
                <p className="mt-2 text-sm leading-relaxed text-muted">{card.description}</p>
              </div>
              <span className="text-sm font-semibold text-primary">Open section →</span>
            </Link>
          </Card>
        ))}
      </section>
    </ExpertDashboardShell>
  );
}