import { cn } from '@/lib/utils';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { ArrowRight, Volume2, RotateCcw } from 'lucide-react';

interface BetterPhraseCardProps {
  original: string;
  issue: string;
  alternative: string;
  criterion?: string;
  onRepeatDrill?: () => void;
  onPlayOriginal?: () => void;
  onPlayAlternative?: () => void;
  className?: string;
}

export function BetterPhraseCard({
  original, issue, alternative, criterion, onRepeatDrill, onPlayOriginal, onPlayAlternative, className,
}: BetterPhraseCardProps) {
  return (
    <Card className={cn('', className)}>
      <div className="flex flex-col gap-3">
        {criterion && <span className="text-xs font-bold text-primary uppercase tracking-wide">{criterion}</span>}

        {/* Original */}
        <div className="p-3 bg-danger/10 border border-danger/30 rounded">
          <p className="text-xs font-bold text-danger mb-1">Original phrase</p>
          <div className="flex items-start gap-2">
            <p className="text-sm text-navy flex-1">&ldquo;{original}&rdquo;</p>
            {onPlayOriginal && (
              <button onClick={onPlayOriginal} className="p-2.5 -m-1 text-danger hover:text-danger/80" aria-label="Play original">
                <Volume2 className="w-4 h-4" />
              </button>
            )}
          </div>
        </div>

        {/* Issue */}
        <div className="flex items-center gap-2 text-xs text-warning">
          <ArrowRight className="w-3.5 h-3.5" aria-hidden="true" />
          <span className="font-bold">Issue:</span> {issue}
        </div>

        {/* Alternative */}
        <div className="p-3 bg-success/10 border border-success/30 rounded">
          <p className="text-xs font-bold text-success mb-1">Stronger alternative</p>
          <div className="flex items-start gap-2">
            <p className="text-sm text-navy flex-1">&ldquo;{alternative}&rdquo;</p>
            {onPlayAlternative && (
              <button onClick={onPlayAlternative} className="p-2.5 -m-1 text-success hover:text-success/80" aria-label="Play alternative">
                <Volume2 className="w-4 h-4" />
              </button>
            )}
          </div>
        </div>

        {/* Repeat drill */}
        {onRepeatDrill && (
          <Button size="sm" variant="secondary" onClick={onRepeatDrill} className="self-start">
            <RotateCcw className="w-3.5 h-3.5" /> Repeat Drill
          </Button>
        )}
      </div>
    </Card>
  );
}
