import type { Metadata } from 'next';
import Link from 'next/link';
import { notFound } from 'next/navigation';
import { ArrowLeft, Clock, Dumbbell, FileText, Gauge, Stethoscope } from 'lucide-react';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { DrillPlayer } from '@/components/domain/writing-drills/drill-player';
import { DrillNotFoundError, getDrill } from '@/lib/writing-drills/loader';
import { DrillTypeSchema } from '@/lib/writing-drills/types';

export async function generateMetadata({
  params,
}: {
  params: Promise<{ type: string; id: string }>;
}): Promise<Metadata> {
  const { id } = await params;
  try {
    const drill = getDrill(id);
    return {
      title: `${drill.title} | Writing drill`,
      description: drill.brief,
    };
  } catch {
    return { title: 'Writing drill' };
  }
}

export default async function WritingDrillPlayerPage({
  params,
}: {
  params: Promise<{ type: string; id: string }>;
}) {
  const { type: rawType, id } = await params;
  const typeResult = DrillTypeSchema.safeParse(rawType);
  if (!typeResult.success) notFound();

  let drill;
  try {
    drill = getDrill(id);
  } catch (error) {
    if (error instanceof DrillNotFoundError) notFound();
    throw error;
  }

  // The route's [type] must match the drill's actual type — guards against
  // mismatched URLs that would render the wrong UI.
  if (drill.type !== typeResult.data) notFound();

  return (
    <>
      {/* A server page: icons go to the client hero as elements, not components. */}
      <LearnerPageHero
        eyebrow="Writing Drill"
        icon={<Dumbbell />}
        accent="writing"
        title={drill.title}
        description={drill.brief}
        highlights={[
          { icon: <Stethoscope />, label: 'Profession', value: drill.profession },
          ...(drill.letterType ? [{ icon: <FileText />, label: 'Letter type', value: drill.letterType.replaceAll('_', ' ') }] : []),
          { icon: <Gauge />, label: 'Difficulty', value: drill.difficulty },
          { icon: <Clock />, label: 'Time', value: `~${drill.estimatedMinutes} min` },
        ]}
        aside={
          <Button asChild variant="outline" size="sm">
            <Link href={`/writing/drills/${drill.type}`}>
              <ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" /> Back to {drill.type.replaceAll('_', ' ')} drills
            </Link>
          </Button>
        }
      />

      <InlineAlert variant="warning" live="polite">
        <strong>Practice mode.</strong> This drill is graded automatically against an authored
        answer key. It is not a substitute for teacher correction or the AI Writing Coach.
      </InlineAlert>

      <DrillPlayer drill={drill} />
    </>
  );
}
