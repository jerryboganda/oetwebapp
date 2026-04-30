import type { Metadata } from 'next';
import Link from 'next/link';
import { notFound } from 'next/navigation';
import { ChevronRight } from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { LearnerDashboardShell } from '@/components/layout';
import { listDrills } from '@/lib/writing-drills/loader';
import { DrillTypeSchema, type DrillType } from '@/lib/writing-drills/types';

const TYPE_TITLES: Record<DrillType, string> = {
  relevance: 'Case-note selection',
  opening: 'Opening paragraphs',
  ordering: 'Paragraph ordering',
  expansion: 'Sentence expansion',
  tone: 'Formal tone',
  abbreviation: 'Abbreviations',
};

export async function generateMetadata({
  params,
}: {
  params: Promise<{ type: string }>;
}): Promise<Metadata> {
  const { type } = await params;
  const parsed = DrillTypeSchema.safeParse(type);
  if (!parsed.success) return { title: 'Writing drills' };
  return {
    title: `${TYPE_TITLES[parsed.data]} drills`,
    description: `OET Writing practice — ${TYPE_TITLES[parsed.data].toLowerCase()} drills.`,
  };
}

export default async function WritingDrillsTypeListPage({
  params,
}: {
  params: Promise<{ type: string }>;
}) {
  const { type: rawType } = await params;
  const parsed = DrillTypeSchema.safeParse(rawType);
  if (!parsed.success) notFound();
  const type = parsed.data;

  const drills = listDrills({ type });

  return (
    <LearnerDashboardShell pageTitle={`Writing — ${TYPE_TITLES[type]}`}>
      <header className="bg-navy text-white pt-10 pb-12 px-4 sm:px-6 lg:px-8">
        <Link
          href="/writing/drills"
          className="text-info text-sm hover:underline focus:outline-none focus-visible:ring-2 focus-visible:ring-info rounded"
        >
          ← All drill categories
        </Link>
        <h1 className="text-3xl sm:text-4xl font-bold mt-2">{TYPE_TITLES[type]}</h1>
      </header>

      <main className="-mt-6 relative z-10 px-4 sm:px-6 lg:px-8 pb-16">
        {drills.length === 0 ? (
          <Card>
            <CardContent className="p-8 text-center text-muted">
              No drills available yet for this category.
            </CardContent>
          </Card>
        ) : (
          <ul className="space-y-3">
            {drills.map((d) => (
              <li key={d.id}>
                <Link
                  href={`/writing/drills/${type}/${d.id}`}
                  className="block group focus:outline-none focus-visible:ring-2 focus-visible:ring-primary rounded-xl"
                >
                  <Card className="transition-shadow group-hover:shadow-md">
                    <CardContent className="p-5 flex items-center gap-4">
                      <div className="flex-1">
                        <div className="flex items-center gap-2 mb-1">
                          <Badge variant="muted" size="sm">
                            {d.profession}
                          </Badge>
                          {d.letterType && (
                            <Badge variant="info" size="sm">
                              {d.letterType.replaceAll('_', ' ')}
                            </Badge>
                          )}
                          <Badge variant="outline" size="sm">
                            {d.difficulty}
                          </Badge>
                          <span className="text-xs text-muted">~{d.estimatedMinutes} min</span>
                        </div>
                        <h2 className="text-lg font-semibold text-navy group-hover:text-primary transition-colors">
                          {d.title}
                        </h2>
                        <p className="text-sm text-muted line-clamp-2">{d.brief}</p>
                      </div>
                      <ChevronRight
                        className="w-5 h-5 text-muted group-hover:text-primary"
                        aria-hidden
                      />
                    </CardContent>
                  </Card>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </main>
    </LearnerDashboardShell>
  );
}
