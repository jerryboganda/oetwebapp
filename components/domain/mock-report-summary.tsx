import { cn } from '@/lib/utils';
import { Card } from '@/components/ui/card';
import { ScoreRangeBadge, Badge, StatusBadge, type StatusType } from '@/components/ui/badge';
import { type SubTest } from './subtest-switcher';

interface SubtestResult {
  subtest: SubTest;
  scoreLow: number;
  scoreHigh: number;
  grade: string;
  confidence: 'high' | 'medium' | 'low';
}

interface MockReportSummaryProps {
  title: string;
  date: string;
  status: StatusType;
  overallScore?: { low: number; high: number };
  overallGrade?: string;
  subtests?: SubtestResult[];
  priorComparison?: { change: number; label: string };
  className?: string;
}

export function MockReportSummary({
  title, date, status, overallScore, overallGrade, subtests, priorComparison, className,
}: MockReportSummaryProps) {
  return (
    <Card className={cn('', className)}>
      <div className="flex items-center justify-between mb-4">
        <div>
          <h3 className="text-lg font-bold text-navy">{title}</h3>
          <p className="text-xs text-muted">{date}</p>
        </div>
        <StatusBadge status={status} />
      </div>

      {overallScore && (
        <div className="flex items-center gap-3 mb-4">
          <ScoreRangeBadge low={overallScore.low} high={overallScore.high} label="Overall" />
          {overallGrade && <Badge size="md">{overallGrade}</Badge>}
          {priorComparison && (
            <span className={cn('text-xs font-semibold', priorComparison.change >= 0 ? 'text-emerald-600' : 'text-red-600')}>
              {priorComparison.change >= 0 ? '+' : ''}{priorComparison.change} {priorComparison.label}
            </span>
          )}
        </div>
      )}

      {subtests && subtests.length > 0 && (
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
          {subtests.map((st) => (
            <div key={st.subtest} className="text-center p-3 bg-gray-50 rounded">
              <p className="text-xs font-semibold text-muted capitalize mb-1">{st.subtest}</p>
              <p className="text-sm font-bold text-navy">{st.scoreLow}–{st.scoreHigh}</p>
              <Badge size="sm" variant={st.confidence === 'high' ? 'success' : st.confidence === 'low' ? 'danger' : 'warning'}>{st.grade}</Badge>
            </div>
          ))}
        </div>
      )}
    </Card>
  );
}
