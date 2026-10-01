'use client';

import { Stethoscope, Lightbulb } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { getProfessionRemediationTips, type ProfessionRemediationTip } from '@/lib/writing-remediation-professions';

export default function ProfessionRemediationCallout({
  profession = '',
  tips,
}: {
  profession?: string;
  tips?: ProfessionRemediationTip[];
}) {
  const resolvedTips = tips ?? getProfessionRemediationTips(profession);
  if (resolvedTips.length === 0) return null;

  return (
    <MotionSection>
      <Card padding="lg">
        <div className="mb-5 flex items-center gap-2">
          <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-primary/10">
            <Stethoscope className="h-4 w-4 text-primary" aria-hidden="true" />
          </div>
          <h2 className="text-lg font-bold text-navy">{profession ? `${profession}-specific coaching` : 'Profession-specific coaching'}</h2>
        </div>
        <p className="mb-4 text-sm text-muted">
          These tips are tailored to your profession. They highlight the most common writing gaps for your field and show how a strong response differs from a weak one.
        </p>
        <div className="space-y-3">
          {resolvedTips.map((tip, index) => (
            <MotionItem key={tip.criterionCode} delayIndex={Math.min(index, 5)} className="rounded-xl bg-background-light p-4">
              <div className="mb-2 flex flex-wrap items-center gap-2">
                <Lightbulb className="h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
                <h3 className="min-w-0 text-sm font-bold text-navy">{tip.title}</h3>
                <Badge variant={tip.priority === 'high' ? 'danger' : 'warning'} size="sm" className="capitalize">
                  {tip.priority}
                </Badge>
              </div>
              <p className="mb-3 text-xs leading-relaxed text-muted">{tip.description}</p>
              <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                <div className="min-w-0 rounded-lg border border-danger/10 bg-danger/5 p-3">
                  <p className="eyebrow mb-1 text-danger-strong">Weak example</p>
                  <p className="text-xs italic leading-relaxed text-navy">{tip.exampleWeak}</p>
                </div>
                <div className="min-w-0 rounded-lg border border-success/10 bg-success/5 p-3">
                  <p className="eyebrow mb-1 text-success-strong">Strong example</p>
                  <p className="text-xs leading-relaxed text-navy">{tip.exampleStrong}</p>
                </div>
              </div>
            </MotionItem>
          ))}
        </div>
      </Card>
    </MotionSection>
  );
}
