import { cn } from '@/lib/utils';
import { Card } from '@/components/ui/card';

interface SpeakingRoleCardProps {
  role: string;
  setting: string;
  patient: string;
  task: string;
  background?: string;
  className?: string;
}

export function SpeakingRoleCard({ role, setting, patient, task, background, className }: SpeakingRoleCardProps) {
  return (
    <Card className={cn('bg-lavender/30 border-primary/20', className)} role="region" aria-label="Role card details">
      <div className="space-y-3">
        <div>
          <p className="text-xs font-semibold text-primary uppercase tracking-wide mb-1">Your Role</p>
          <p className="text-sm font-bold text-navy">{role}</p>
        </div>
        <div>
          <p className="text-xs font-semibold text-primary uppercase tracking-wide mb-1">Setting</p>
          <p className="text-sm text-navy">{setting}</p>
        </div>
        <div>
          <p className="text-xs font-semibold text-primary uppercase tracking-wide mb-1">Patient / Client</p>
          <p className="text-sm text-navy">{patient}</p>
        </div>
        <div>
          <p className="text-xs font-semibold text-primary uppercase tracking-wide mb-1">Task</p>
          <p className="text-sm text-navy">{task}</p>
        </div>
        {background && (
          <div>
            <p className="text-xs font-semibold text-primary uppercase tracking-wide mb-1">Background</p>
            <p className="text-sm text-navy">{background}</p>
          </div>
        )}
      </div>
    </Card>
  );
}
