import { cn } from '@/lib/utils';

export type FlagType = 'pronunciation' | 'fluency' | 'grammar' | 'vocabulary' | 'empathy' | 'structure';

interface TranscriptFlag {
  id: string;
  type: FlagType;
  text: string;
  suggestion?: string;
  startTime?: string;
}

interface TranscriptFlagListProps {
  flags: TranscriptFlag[];
  activeFlagId?: string;
  onFlagClick?: (flag: TranscriptFlag) => void;
  className?: string;
}

const flagConfig: Record<FlagType, { label: string; color: string }> = {
  pronunciation: { label: 'Pronunciation', color: 'bg-red-50 text-red-700 border-red-200' },
  fluency: { label: 'Fluency', color: 'bg-amber-50 text-amber-700 border-amber-200' },
  grammar: { label: 'Grammar', color: 'bg-orange-50 text-orange-700 border-orange-200' },
  vocabulary: { label: 'Vocabulary', color: 'bg-blue-50 text-blue-700 border-blue-200' },
  empathy: { label: 'Empathy', color: 'bg-purple-50 text-purple-700 border-purple-200' },
  structure: { label: 'Structure', color: 'bg-emerald-50 text-emerald-700 border-emerald-200' },
};

export function TranscriptFlagList({ flags, activeFlagId, onFlagClick, className }: TranscriptFlagListProps) {
  return (
    <div className={cn('flex flex-col gap-2', className)}>
      {flags.map((flag) => {
        const config = flagConfig[flag.type];
        const isActive = activeFlagId === flag.id;
        return (
          <button
            key={flag.id}
            type="button"
            onClick={() => onFlagClick?.(flag)}
            className={cn(
              'flex items-start gap-3 p-3 rounded border text-left transition-all',
              isActive ? 'border-primary bg-primary/5 ring-1 ring-primary' : 'border-border hover:border-border',
            )}
          >
            <div className="flex-1 min-w-0">
              <div className="flex items-center gap-2 mb-1">
                <span className={cn('px-2 py-0.5 rounded text-xs font-semibold border', config.color)}>{config.label}</span>
                {flag.startTime && <span className="text-xs text-muted">{flag.startTime}</span>}
              </div>
              <p className="text-sm text-navy">{flag.text}</p>
              {flag.suggestion && <p className="text-xs text-primary mt-1">→ {flag.suggestion}</p>}
            </div>
          </button>
        );
      })}
    </div>
  );
}

/* ─── Flag Legend ─── */
export function TranscriptFlagLegend({ className }: { className?: string }) {
  return (
    <div className={cn('flex flex-wrap gap-2', className)}>
      {(Object.entries(flagConfig) as [FlagType, typeof flagConfig[FlagType]][]).map(([key, config]) => (
        <span key={key} className={cn('px-2 py-0.5 rounded text-xs font-semibold border', config.color)}>
          {config.label}
        </span>
      ))}
    </div>
  );
}
