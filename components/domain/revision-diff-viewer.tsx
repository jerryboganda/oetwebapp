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
        <div className="px-4 py-2 bg-red-50 border-b border-red-200 rounded-t">
          <h4 className="text-xs font-semibold text-red-700 uppercase tracking-wide">Original Submission</h4>
        </div>
        <div className="bg-white border border-red-100 rounded-b p-4 flex-1 overflow-y-auto">
          <div className="text-sm text-navy whitespace-pre-wrap leading-relaxed">{original}</div>
        </div>
      </div>

      {/* Revised */}
      <div className="flex flex-col">
        <div className="px-4 py-2 bg-emerald-50 border-b border-emerald-200 rounded-t">
          <h4 className="text-xs font-semibold text-emerald-700 uppercase tracking-wide">Revised Version</h4>
        </div>
        <div className="bg-white border border-emerald-100 rounded-b p-4 flex-1 overflow-y-auto">
          <div className="text-sm text-navy whitespace-pre-wrap leading-relaxed">{revised}</div>
        </div>
      </div>
    </div>
  );
}
