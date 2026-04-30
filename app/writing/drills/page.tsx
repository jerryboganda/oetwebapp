import type { Metadata } from 'next';
import Link from 'next/link';
import { ListChecks, MessageSquareQuote, ArrowDownUp, FileText, Sparkles, Hash } from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { LearnerDashboardShell } from '@/components/layout';
import { listDrills } from '@/lib/writing-drills/loader';
import type { DrillType } from '@/lib/writing-drills/types';

export const metadata: Metadata = {
  title: 'Writing Drills',
  description:
    'Build the underlying writing skills tested by OET — content selection, opening purpose, paragraph order, sentence expansion, formal tone, and abbreviations.',
};

interface DrillCategory {
  type: DrillType;
  title: string;
  description: string;
  Icon: typeof ListChecks;
  accent: string;
}

const CATEGORIES: DrillCategory[] = [
  {
    type: 'relevance',
    title: 'Case-note selection',
    description: 'Decide which case notes the recipient actually needs.',
    Icon: ListChecks,
    accent: 'bg-emerald-50 text-emerald-700',
  },
  {
    type: 'opening',
    title: 'Opening paragraphs',
    description: 'Choose the strongest purpose-first opening sentence.',
    Icon: MessageSquareQuote,
    accent: 'bg-blue-50 text-blue-700',
  },
  {
    type: 'ordering',
    title: 'Paragraph ordering',
    description: 'Sequence paragraphs the way the reader needs them.',
    Icon: ArrowDownUp,
    accent: 'bg-violet-50 text-violet-700',
  },
  {
    type: 'expansion',
    title: 'Sentence expansion',
    description: 'Convert note-form lines into complete clinical sentences.',
    Icon: FileText,
    accent: 'bg-amber-50 text-amber-800',
  },
  {
    type: 'tone',
    title: 'Formal tone',
    description: 'Lift casual phrasing into a professional clinical register.',
    Icon: Sparkles,
    accent: 'bg-rose-50 text-rose-700',
  },
  {
    type: 'abbreviation',
    title: 'Abbreviations',
    description: 'Decide when to expand and when to keep abbreviations.',
    Icon: Hash,
    accent: 'bg-slate-100 text-slate-700',
  },
];

export default function WritingDrillsIndexPage() {
  const all = listDrills();
  const countByType = new Map<DrillType, number>();
  for (const d of all) countByType.set(d.type, (countByType.get(d.type) ?? 0) + 1);

  return (
    <LearnerDashboardShell pageTitle="Writing Drills">
      <header className="bg-navy text-white pt-10 pb-12 px-4 sm:px-6 lg:px-8">
        <div className="flex items-center gap-3 mb-2">
          <div className="w-10 h-10 rounded-lg bg-white/10 flex items-center justify-center">
            <ListChecks className="w-5 h-5 text-info" />
          </div>
          <h1 className="text-3xl sm:text-4xl font-bold">Writing Drills</h1>
        </div>
        <p className="text-white/70 text-lg max-w-2xl">
          Bite-sized practice for the underlying skills tested in the OET Writing letter.
          Drills are graded instantly against authored answer keys — they do not call AI and
          never substitute for teacher correction.
        </p>
      </header>

      <main className="-mt-6 relative z-10 px-4 sm:px-6 lg:px-8 pb-16">
        <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
          {CATEGORIES.map(({ type, title, description, Icon, accent }) => {
            const count = countByType.get(type) ?? 0;
            return (
              <Link
                key={type}
                href={`/writing/drills/${type}`}
                aria-label={`${title} drills — ${count} available`}
                className="group focus:outline-none focus-visible:ring-2 focus-visible:ring-primary rounded-xl"
              >
                <Card className="h-full transition-shadow group-hover:shadow-md">
                  <CardContent className="p-5 space-y-3">
                    <div className="flex items-start justify-between gap-3">
                      <div
                        className={`inline-flex items-center justify-center w-10 h-10 rounded-lg ${accent}`}
                      >
                        <Icon className="w-5 h-5" />
                      </div>
                      <Badge variant={count > 0 ? 'info' : 'muted'} size="sm">
                        {count} drill{count === 1 ? '' : 's'}
                      </Badge>
                    </div>
                    <div>
                      <h2 className="text-lg font-semibold text-navy group-hover:text-primary transition-colors">
                        {title}
                      </h2>
                      <p className="text-sm text-muted">{description}</p>
                    </div>
                  </CardContent>
                </Card>
              </Link>
            );
          })}
        </div>
      </main>
    </LearnerDashboardShell>
  );
}
