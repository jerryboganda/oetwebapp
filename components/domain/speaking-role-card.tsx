import { cn } from '@/lib/utils';
import { Card } from '@/components/ui/card';

interface SpeakingRoleCardProps {
  role: string;
  setting: string;
  patient: string;
  task: string;
  background?: string;
  tasks?: string[];
  patientEmotion?: string;
  communicationGoal?: string;
  clinicalTopic?: string;
  prepTimeSeconds?: number;
  roleplayTimeSeconds?: number;
  disclaimer?: string;
  className?: string;
}

function formatSeconds(seconds?: number) {
  if (!seconds) return null;
  const minutes = Math.floor(seconds / 60);
  const remainder = seconds % 60;
  return remainder === 0 ? `${minutes} min` : `${minutes}m ${remainder}s`;
}

export function SpeakingRoleCard({
  role,
  setting,
  patient,
  task,
  background,
  tasks = [],
  patientEmotion,
  communicationGoal,
  clinicalTopic,
  prepTimeSeconds,
  roleplayTimeSeconds,
  disclaimer,
  className,
}: SpeakingRoleCardProps) {
  const prepLabel = formatSeconds(prepTimeSeconds);
  const roleplayLabel = formatSeconds(roleplayTimeSeconds);

  return (
    <Card className={cn('bg-lavender/30 border-primary/20', className)} role="region" aria-label="Role card details">
      <div className="space-y-3">
        {(prepLabel || roleplayLabel) && (
          <div className="flex flex-wrap gap-2">
            {prepLabel && <span className="rounded-full bg-white/80 px-3 py-1 text-[11px] font-bold text-primary">Prep: {prepLabel}</span>}
            {roleplayLabel && <span className="rounded-full bg-white/80 px-3 py-1 text-[11px] font-bold text-primary">Role-play: {roleplayLabel}</span>}
          </div>
        )}
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
        {tasks.length > 0 && (
          <div>
            <p className="text-xs font-semibold text-primary uppercase tracking-wide mb-1">Role objectives</p>
            <ul className="space-y-2">
              {tasks.map((item, index) => (
                <li key={`${item}-${index}`} className="flex gap-2 text-sm leading-relaxed text-navy">
                  <span className="font-bold text-primary">{index + 1}.</span>
                  <span>{item}</span>
                </li>
              ))}
            </ul>
          </div>
        )}
        {background && (
          <div>
            <p className="text-xs font-semibold text-primary uppercase tracking-wide mb-1">Background</p>
            <p className="text-sm text-navy">{background}</p>
          </div>
        )}
        {(patientEmotion || communicationGoal || clinicalTopic) && (
          <div className="grid gap-2 rounded-2xl border border-primary/15 bg-white/60 p-3 text-sm text-navy sm:grid-cols-3">
            {patientEmotion && (
              <div>
                <p className="text-[10px] font-bold uppercase tracking-widest text-muted">Emotion</p>
                <p className="font-semibold">{patientEmotion}</p>
              </div>
            )}
            {communicationGoal && (
              <div>
                <p className="text-[10px] font-bold uppercase tracking-widest text-muted">Goal</p>
                <p className="font-semibold">{communicationGoal}</p>
              </div>
            )}
            {clinicalTopic && (
              <div>
                <p className="text-[10px] font-bold uppercase tracking-widest text-muted">Topic</p>
                <p className="font-semibold">{clinicalTopic}</p>
              </div>
            )}
          </div>
        )}
        {disclaimer && (
          <p className="rounded-xl bg-white/70 px-3 py-2 text-xs font-semibold leading-relaxed text-muted">
            {disclaimer}
          </p>
        )}
      </div>
    </Card>
  );
}
