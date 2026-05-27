'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { ArrowLeft, CheckCircle2, Dumbbell, XCircle } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
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
    <LearnerDashboardShell pageTitle={drill?.title ?? 'Writing Drill'}>
      <div className="space-y-8">
        <Button asChild variant="ghost" size="sm"><Link href="/writing/drills"><ArrowLeft className="h-4 w-4" /> Drills</Link></Button>
        <LearnerPageHero
          eyebrow={drill ? drill.targetSubSkill : 'Writing Drill'}
          icon={Dumbbell}
          accent="amber"
          title={drill?.title ?? 'Writing drill'}
          description={drill ? writingSkillLabels[drill.targetSubSkill] ?? drill.targetSubSkill : 'Loading drill'}
          highlights={[{ icon: Dumbbell, label: 'Attempts', value: `${drill?.attemptCount ?? 0}` }]}
        />
        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
        {drill ? (
          <section className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <LearnerSurfaceSectionHeader eyebrow="Prompt" title="Complete the drill" className="mb-4" />
            <div className="space-y-3 text-sm leading-7 text-navy">
              {promptBlocks.map((block) => <p key={block}>{block}</p>)}
            </div>
            <textarea
              value={responseText}
              onChange={(event) => setResponseText(event.target.value)}
              className="mt-5 min-h-36 w-full rounded-xl border border-border bg-background p-3 text-sm text-navy outline-none focus:border-primary"
              placeholder="Type your answer"
            />
            <div className="mt-4 flex flex-wrap gap-3">
              <Button onClick={() => void submit()} loading={saving}>Submit attempt</Button>
              {result ? <Badge variant={result.isCorrect ? 'success' : 'danger'}>{result.isCorrect ? <CheckCircle2 className="h-3 w-3" /> : <XCircle className="h-3 w-3" />} {result.feedbackText}</Badge> : null}
            </div>
          </section>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}