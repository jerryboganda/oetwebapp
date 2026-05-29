import { cn } from '@/lib/utils';

interface RevisionDiffViewerProps {
  original: string;
  revised: string;
  className?: string;
}

export function RevisionDiffViewer({ original, revised, className }: RevisionDiffViewerProps) {
  return (
    <div className={cn('grid grid-cols-1 lg:grid-cols-2 gap-4', className)}>
      {/* Original */}
      <div className="flex flex-col">
        <div className="px-4 py-2 bg-danger/10 border-b border-danger/30 rounded-t">
          <h4 className="text-xs font-semibold text-danger uppercase tracking-wide">Original Submission</h4>
        </div>
        <div className="bg-surface border border-danger/20 rounded-b p-4 flex-1 overflow-y-auto">
          <div className="text-sm text-navy whitespace-pre-wrap leading-relaxed">{original}</div>
        </div>
      </div>

      {/* Revised */}
      <div className="flex flex-col">
        <div className="px-4 py-2 bg-success/10 border-b border-success/30 rounded-t">
          <h4 className="text-xs font-semibold text-success uppercase tracking-wide">Revised Version</h4>
        </div>
        <div className="bg-surface border border-success/20 rounded-b p-4 flex-1 overflow-y-auto">
          <div className="text-sm text-navy whitespace-pre-wrap leading-relaxed">{revised}</div>
        </div>
      </div>
    </div>
  );
}
