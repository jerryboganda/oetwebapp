import { cn } from '@/lib/utils';
import { AlertCircle, MinusCircle, Lightbulb } from 'lucide-react';

export type IssueType = 'omission' | 'unnecessary' | 'suggestion';

interface WritingIssue {
  id: string;
  type: IssueType;
  criterion: string;
  text: string;
  anchor?: string; // text to highlight in the original
}

interface WritingIssueListProps {
  issues: WritingIssue[];
  activeIssueId?: string;
  onIssueClick?: (issue: WritingIssue) => void;
  className?: string;
}

const issueConfig: Record<IssueType, { label: string; icon: typeof AlertCircle; color: string }> = {
  omission: { label: 'Omission', icon: MinusCircle, color: 'text-warning' },
  unnecessary: { label: 'Unnecessary Detail', icon: AlertCircle, color: 'text-danger' },
  suggestion: { label: 'Revision Suggestion', icon: Lightbulb, color: 'text-info' },
};

export function WritingIssueList({ issues, activeIssueId, onIssueClick, className }: WritingIssueListProps) {
  return (
    <div className={cn('flex flex-col gap-2', className)}>
      {issues.map((issue) => {
        const config = issueConfig[issue.type];
        const Icon = config.icon;
        const isActive = activeIssueId === issue.id;
        return (
          <button
            key={issue.id}
            type="button"
            onClick={() => onIssueClick?.(issue)}
            className={cn(
              'flex items-start gap-3 p-3 rounded border text-left transition-[color,background-color,border-color,box-shadow,transform,opacity,filter] duration-200',
              isActive ? 'border-primary bg-primary/5 ring-1 ring-primary' : 'border-border hover:border-border',
            )}
          >
            <Icon className={cn('w-5 h-5 mt-0.5 shrink-0', config.color)} aria-hidden="true" />
            <div className="flex-1 min-w-0">
              <div className="flex items-center gap-2 mb-0.5">
                <span className="text-xs font-semibold text-muted">{config.label}</span>
                <span className="text-xs text-muted">· {issue.criterion}</span>
              </div>
              <p className="text-sm text-navy">{issue.text}</p>
            </div>
          </button>
        );
      })}
    </div>
  );
}
