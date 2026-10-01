import type { Metadata } from 'next';
import Link from 'next/link';
import { notFound } from 'next/navigation';
import { ArrowLeft, ChevronRight, Dumbbell } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { CardLink } from '@/components/ui/card-link';
import { EmptyState } from '@/components/ui/empty-error';
import { MotionItem } from '@/components/ui/motion-primitives';
import { LearnerPageHero } from '@/components/domain/learner-surface';
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

function typeDescription(type: DrillType) {
  return `OET Writing practice: ${TYPE_TITLES[type].toLowerCase()} drills.`;
}

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
    description: typeDescription(parsed.data),
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
    <>
      {/* A server page: icons go to the client hero as elements, not components. */}
      <LearnerPageHero
        eyebrow="Writing Practice"
        icon={<Dumbbell />}
        accent="writing"
        title={TYPE_TITLES[type]}
        description={typeDescription(type)}
        aside={
          <Button asChild variant="outline" size="sm">
            <Link href="/writing/drills">
              <ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" /> All drill categories
            </Link>
          </Button>
        }
      />

      {drills.length === 0 ? (
        <EmptyState icon={<Dumbbell className="h-8 w-8" />} title="No drills available yet for this category." />
      ) : (
        <ul className="space-y-3">
          {drills.map((d, index) => (
            <li key={d.id}>
              <MotionItem delayIndex={Math.min(index, 5)}>
                <CardLink href={`/writing/drills/${type}/${d.id}`} className="group flex items-center gap-4">
                  <div className="min-w-0 flex-1">
                    <div className="mb-1 flex flex-wrap items-center gap-2">
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
                      <span className="text-xs tabular-nums text-muted">~{d.estimatedMinutes} min</span>
                    </div>
                    <h2 className="text-lg font-semibold text-navy transition-colors group-hover:text-primary">
                      {d.title}
                    </h2>
                    <p className="line-clamp-2 text-sm text-muted">{d.brief}</p>
                  </div>
                  <ChevronRight
                    className="h-5 w-5 shrink-0 text-muted group-hover:text-primary rtl:rotate-180"
                    aria-hidden
                  />
                </CardLink>
              </MotionItem>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}
