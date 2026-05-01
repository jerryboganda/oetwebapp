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
    <Card className={cn('bg-primary/5 hover:bg-primary/[0.07] transition-colors border border-primary/20 rounded-2xl shadow-sm p-6 sm:p-8', className)} role="region" aria-label="Role card details">
      <div className="space-y-6">
        {(prepLabel || roleplayLabel) && (
          <div className="flex flex-wrap gap-2 pb-2">
            {prepLabel && <span className="rounded-full bg-white shadow-sm border border-primary/10 px-4 py-1.5 text-[11px] font-black tracking-wide text-primary">PREP: {prepLabel}</span>}
            {roleplayLabel && <span className="rounded-full bg-white shadow-sm border border-primary/10 px-4 py-1.5 text-[11px] font-black tracking-wide text-primary">ROLE-PLAY: {roleplayLabel}</span>}
          </div>
        )}
        <div className="grid gap-6 md:grid-cols-2">
          <div>
            <p className="text-[10px] font-black text-primary/70 uppercase tracking-widest mb-1.5 flex items-center gap-1.5">
              <span className="w-1 h-3 rounded-full bg-primary/40 inline-block" />
              Your Role
            </p>
            <p className="text-base font-bold text-navy leading-snug">{role}</p>
          </div>
          <div>
            <p className="text-[10px] font-black text-primary/70 uppercase tracking-widest mb-1.5 flex items-center gap-1.5">
              <span className="w-1 h-3 rounded-full bg-primary/40 inline-block" />
              Setting
            </p>
            <p className="text-sm font-medium text-navy/90 leading-snug">{setting}</p>
          </div>
        </div>
        
        <div className="h-px w-full bg-gradient-to-r from-border/80 via-border to-transparent" />

        <div className="grid gap-6 md:grid-cols-2">
          <div>
            <p className="text-[10px] font-black text-primary/70 uppercase tracking-widest mb-1.5 flex items-center gap-1.5">
              <span className="w-1 h-3 rounded-full bg-primary/40 inline-block" />
              Patient / Client
            </p>
            <p className="text-sm font-medium text-navy/90 leading-relaxed">{patient}</p>
          </div>
          <div>
            <p className="text-[10px] font-black text-primary/70 uppercase tracking-widest mb-1.5 flex items-center gap-1.5">
              <span className="w-1 h-3 rounded-full bg-primary/40 inline-block" />
              Task
            </p>
            <p className="text-sm font-medium text-navy/90 leading-relaxed">{task}</p>
          </div>
        </div>

        {background && (
          <>
            <div className="h-px w-full bg-gradient-to-r from-border/80 via-border to-transparent" />
            <div className="bg-white/60 p-4 rounded-xl border border-primary/10">
              <p className="text-[10px] font-black text-primary/70 uppercase tracking-widest mb-2 flex items-center gap-1.5">
                <span className="w-1 h-3 rounded-full bg-primary/40 inline-block" />
                Background
              </p>
              <p className="text-sm font-medium text-navy/90 leading-relaxed italic">&quot;{background}&quot;</p>
            </div>
          </>
        )}

        {tasks.length > 0 && (
          <div className="pt-2">
            <p className="text-[10px] font-black text-primary/70 uppercase tracking-widest mb-3 flex items-center gap-1.5">
              <span className="w-1 h-3 rounded-full bg-primary/40 inline-block" />
              Role Objectives
            </p>
            <ul className="grid gap-3">
              {tasks.map((item, index) => (
                <li key={`${item}-${index}`} className="flex items-start gap-3 bg-white/50 p-3 rounded-xl border border-primary/10">
                  <span className="flex items-center justify-center w-6 h-6 rounded-full bg-primary text-white text-xs font-black shrink-0 mt-0.5 shadow-sm">
                    {index + 1}
                  </span>
                  <span className="text-sm font-medium text-navy/90 leading-relaxed">{item}</span>
                </li>
              ))}
            </ul>
          </div>
        )}
        
        {(patientEmotion || communicationGoal || clinicalTopic) && (
          <div className="grid gap-3 rounded-xl border border-primary/20 bg-white/80 p-4 shrink-0 sm:grid-cols-3 mt-4 shadow-sm">
            {patientEmotion && (
              <div>
                <p className="text-[9px] font-black uppercase tracking-[0.2em] text-muted mb-1">Emotion</p>
                <p className="font-bold text-navy text-sm">{patientEmotion}</p>
              </div>
            )}
            {communicationGoal && (
              <div>
                <p className="text-[9px] font-black uppercase tracking-[0.2em] text-muted mb-1">Goal</p>
                <p className="font-bold text-navy text-sm">{communicationGoal}</p>
              </div>
            )}
            {clinicalTopic && (
              <div>
                <p className="text-[9px] font-black uppercase tracking-[0.2em] text-muted mb-1">Topic</p>
                <p className="font-bold text-navy text-sm">{clinicalTopic}</p>
              </div>
            )}
          </div>
        )}
        {disclaimer && (
          <p className="rounded-xl bg-background-light px-4 py-3 text-xs font-bold leading-relaxed text-muted border border-border/60 text-center">
            {disclaimer}
          </p>
        )}
      </div>
    </Card>
  );
}
