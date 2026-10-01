'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { ArrowLeft, Dumbbell } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { cardClassName } from '@/components/ui/card';
import { MotionSection } from '@/components/ui/motion-primitives';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { getWritingDrill, submitWritingDrillAttempt, type WritingDrillAttemptDto, type WritingDrillDetailDto, writingSkillLabels } from '@/lib/writing-pathway-api';

export default function WritingDrillPracticeDetailPage() {
  const params = useParams();
  const id = typeof params?.id === 'string' ? params.id : '';
  const [drill, setDrill] = useState<WritingDrillDetailDto | null>(null);
  const [responseText, setResponseText] = useState('');
  const [result, setResult] = useState<WritingDrillAttemptDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (!id) return;
    getWritingDrill(id).then(setDrill).catch(() => setError('Could not load this Writing drill.'));
  }, [id]);

  const promptBlocks = useMemo(() => (drill?.promptMarkdown ?? '').split('\n\n').filter(Boolean), [drill]);

  const submit = async () => {
    if (!drill) return;
    setSaving(true);
    setError(null);
    try {
      setResult(await submitWritingDrillAttempt(drill.id, responseText));
    } catch {
      setError('Could not score this drill attempt.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <>
      <LearnerPageHero
        eyebrow={drill ? drill.targetSubSkill : 'Writing Drill'}
        icon={Dumbbell}
        accent="writing"
        title={drill?.title ?? 'Writing drill'}
        description={drill ? writingSkillLabels[drill.targetSubSkill] ?? drill.targetSubSkill : 'Loading drill'}
        highlights={[{ icon: Dumbbell, label: 'Attempts', value: `${drill?.attemptCount ?? 0}` }]}
        aside={<Button asChild variant="outline" size="sm"><Link href="/writing/drills"><ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" /> Drills</Link></Button>}
      />
      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
      {!drill && !error ? <LearnerSkeleton variant="list" /> : null}
      {drill ? (
        <MotionSection>
          <section className={cardClassName({ padding: 'lg' })}>
            <LearnerSurfaceSectionHeader eyebrow="Prompt" title="Complete the drill" className="mb-4" />
            <div className="max-w-3xl space-y-3 text-sm leading-7 text-navy">
              {promptBlocks.map((block) => <p key={block}>{block}</p>)}
            </div>
            <textarea
              value={responseText}
              onChange={(event) => setResponseText(event.target.value)}
              aria-label="Type your answer"
              className="mt-5 min-h-36 w-full rounded-control border border-border bg-background p-3 text-sm text-navy outline-none focus:border-primary focus-visible:ring-2 focus-visible:ring-primary"
              placeholder="Type your answer"
            />
            <div className="mt-4">
              <Button onClick={() => void submit()} loading={saving}>Submit attempt</Button>
            </div>
            {result ? (
              <InlineAlert variant={result.isCorrect ? 'success' : 'error'} live="polite" className="mt-4">
                {result.feedbackText}
              </InlineAlert>
            ) : null}
          </section>
        </MotionSection>
      ) : null}
    </>
  );
}