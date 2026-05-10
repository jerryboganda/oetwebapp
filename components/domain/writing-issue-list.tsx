import { cn } from '@/lib/utils';
import { AlertCircle, MinusCircle, Lightbulb, Sparkles, ScrollText } from 'lucide-react';

export type IssueType = 'omission' | 'unnecessary' | 'suggestion';

/**
 * Where a writing issue came from.
 * - `rule`: deterministic rulebook detector (e.g. R03.4 smoking/drinking).
 * - `ai`:   AI examiner / human grader feedback.
 *
 * Optional. Defaults to undefined which renders without a source chip — the
 * existing aggregated "All Issues Summary" surface keeps working unchanged.
 */
export type IssueSource = 'rule' | 'ai';

interface WritingIssue {
  id: string;
  type: IssueType;
  criterion: string;
  text: string;
  anchor?: string; // text to highlight in the original
  /** Optional rule id (e.g. "R03.4") rendered as a small badge when present. */
  ruleId?: string;
  /** Provenance of this finding. Drives the source chip. */
  source?: IssueSource;
}

interface WritingIssueListProps {
  issues: WritingIssue[];
  activeIssueId?: string;
  onIssueClick?: (issue: WritingIssue) => void;
  className?: string;
}

const issueConfig: Record<IssueType, { label: string; icon: typeof AlertCircle; color: string }> = {
  omission: { label: 'Omission', icon: MinusCircle, color: 'text-amber-600 bg-amber-50' },
  unnecessary: { label: 'Unnecessary Detail', icon: AlertCircle, color: 'text-red-600 bg-red-50' },
  suggestion: { label: 'Revision Suggestion', icon: Lightbulb, color: 'text-blue-600 bg-blue-50' },
};

const sourceConfig: Record<IssueSource, { label: string; icon: typeof AlertCircle; chipClass: string }> = {
  rule: { label: 'Rule', icon: ScrollText, chipClass: 'bg-slate-100 text-slate-700 border border-slate-200' },
  ai: { label: 'AI', icon: Sparkles, chipClass: 'bg-violet-50 text-violet-700 border border-violet-200' },
};

export function WritingIssueList({ issues, activeIssueId, onIssueClick, className }: WritingIssueListProps) {
  return (
    <div className={cn('flex flex-col gap-2', className)}>
      {issues.map((issue) => {
        const config = issueConfig[issue.type];
        const Icon = config.icon;
        const isActive = activeIssueId === issue.id;
        const source = issue.source ? sourceConfig[issue.source] : null;
        const SourceIcon = source?.icon;
        return (
          <button
            key={issue.id}
            type="button"
            onClick={() => onIssueClick?.(issue)}
            className={cn(
              'flex items-start gap-3 p-3 rounded border text-left transition-all',
              isActive ? 'border-primary bg-primary/5 ring-1 ring-primary' : 'border-border hover:border-border',
            )}
          >
            <div className={cn('w-8 h-8 rounded-full flex items-center justify-center shrink-0', config.color)}>
              <Icon className="w-4 h-4" />
            </div>
            <div className="flex-1 min-w-0">
              <div className="flex flex-wrap items-center gap-2 mb-0.5">
                <span className="text-xs font-semibold text-muted">{config.label}</span>
                <span className="text-xs text-muted">· {issue.criterion}</span>
                {issue.ruleId ? (
                  <span className="rounded border border-slate-200 bg-slate-50 px-1.5 py-0.5 font-mono text-[10px] text-slate-700">
                    {issue.ruleId}
                  </span>
                ) : null}
                {source && SourceIcon ? (
                  <span className={cn('inline-flex items-center gap-1 rounded px-1.5 py-0.5 text-[10px] font-medium', source.chipClass)}>
                    <SourceIcon className="h-3 w-3" />
                    {source.label}
                  </span>
                ) : null}
              </div>
              <p className="text-sm text-navy">{issue.text}</p>
            </div>
          </button>
        );
      })}
    </div>
  );
}
