'use client';

import { useEffect, useState } from 'react';
import { Stethoscope, Lightbulb } from 'lucide-react';
import { Card } from '@/components/ui/card';
import { MotionSection } from '@/components/ui/motion-primitives';
import { getProfessionRemediationTips, type ProfessionRemediationTip } from '@/lib/writing-remediation-professions';
import { fetchUserProfile } from '@/lib/api';

export default function ProfessionRemediationCallout() {
  const [tips, setTips] = useState<ProfessionRemediationTip[]>([]);
  const [profession, setProfession] = useState<string>('');
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    fetchUserProfile()
      .then((profile) => {
        const prof = profile.profession || '';
        setProfession(prof);
        setTips(getProfessionRemediationTips(prof));
      })
      .catch(() => {
        setTips([]);
      })
      .finally(() => setLoading(false));
  }, []);

  if (loading || tips.length === 0) return null;

  return (
    <MotionSection delayIndex={4}>
      <Card className="border-border bg-surface p-6">
        <div className="flex items-center gap-2 mb-5">
          <div className="w-8 h-8 rounded-full bg-primary/10 flex items-center justify-center shrink-0">
            <Stethoscope className="w-4 h-4 text-primary" />
          </div>
          <h2 className="text-lg font-bold text-navy">{profession ? `${profession}-specific coaching` : 'Profession-specific coaching'}</h2>
        </div>
        <p className="text-sm text-muted mb-4">
          These tips are tailored to your profession. They highlight the most common writing gaps for your field and show how a strong response differs from a weak one.
        </p>
        <div className="space-y-4">
          {tips.map((tip) => (
            <div key={tip.criterionCode} className="rounded-xl border border-border bg-background-light p-4">
              <div className="flex items-center gap-2 mb-2">
                <Lightbulb className="w-4 h-4 text-primary shrink-0" />
                <h3 className="text-sm font-bold text-navy">{tip.title}</h3>
                <span className={`text-[10px] font-bold uppercase tracking-wider px-2 py-0.5 rounded-md ${
                  tip.priority === 'high' ? 'bg-danger/10 text-danger' : 'bg-warning/10 text-warning'
                }`}>
                  {tip.priority}
                </span>
              </div>
              <p className="text-xs text-muted mb-3 leading-relaxed">{tip.description}</p>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <div className="rounded-lg bg-danger/5 border border-danger/10 p-3">
                  <p className="text-[10px] font-bold uppercase tracking-wider text-danger mb-1">Weak example</p>
                  <p className="text-xs text-navy italic leading-relaxed">{tip.exampleWeak}</p>
                </div>
                <div className="rounded-lg bg-success/5 border border-success/10 p-3">
                  <p className="text-[10px] font-bold uppercase tracking-wider text-success mb-1">Strong example</p>
                  <p className="text-xs text-navy leading-relaxed">{tip.exampleStrong}</p>
                </div>
              </div>
            </div>
          ))}
        </div>
      </Card>
    </MotionSection>
  );
}
