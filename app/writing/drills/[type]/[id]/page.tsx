import type { Metadata } from 'next';
import Link from 'next/link';
import { notFound } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { Card, CardContent } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
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
      title: `${drill.title} — Writing drill`,
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
    <LearnerDashboardShell pageTitle={drill.title}>
      <header className="bg-navy text-white pt-10 pb-12 px-4 sm:px-6 lg:px-8">
        <Link
          href={`/writing/drills/${drill.type}`}
          className="text-info text-sm hover:underline focus:outline-none focus-visible:ring-2 focus-visible:ring-info rounded"
        >
          ← Back to {drill.type.replaceAll('_', ' ')} drills
        </Link>
        <div className="flex items-center gap-2 mt-2 flex-wrap">
          <Badge variant="muted" size="sm">
            {drill.profession}
          </Badge>
          {drill.letterType && (
            <Badge variant="info" size="sm">
              {drill.letterType.replaceAll('_', ' ')}
            </Badge>
          )}
          <Badge variant="outline" size="sm">
            {drill.difficulty}
          </Badge>
          <span className="text-xs text-white/60">~{drill.estimatedMinutes} min</span>
        </div>
        <h1 className="text-3xl sm:text-4xl font-bold mt-2">{drill.title}</h1>
        <p className="text-white/70 mt-2 max-w-3xl">{drill.brief}</p>
      </header>

      <main className="-mt-6 relative z-10 px-4 sm:px-6 lg:px-8 pb-16 space-y-4">
        <Card className="border-amber-200 bg-amber-50">
          <CardContent className="p-4 text-xs text-amber-900">
            <strong>Practice mode.</strong> This drill is graded automatically against an authored
            answer key — it is not a substitute for teacher correction or the AI Writing Coach.
          </CardContent>
        </Card>
        <DrillPlayer drill={drill} />
      </main>
    </LearnerDashboardShell>
  );
}
