'use client';

import { Calendar, AlertTriangle, Archive, RefreshCw } from 'lucide-react';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import type { ContentStalenessAssessment } from '@/lib/content-provenance';

interface ContentStalenessCardProps {
  assessment: ContentStalenessAssessment;
  onRefresh?: () => void;
  onArchive?: () => void;
}

export default function ContentStalenessCard({ assessment, onRefresh, onArchive }: ContentStalenessCardProps) {
  const actionColor =
    assessment.recommendedAction === 'archive'
      ? 'danger'
      : assessment.recommendedAction === 'major_revision'
        ? 'warning'
        : assessment.recommendedAction === 'minor_refresh'
          ? 'info'
          : 'success';

  const actionLabel: Record<ContentStalenessAssessment['recommendedAction'], string> = {
    no_action: 'Fresh',
    minor_refresh: 'Minor refresh suggested',
    major_revision: 'Major revision needed',
    archive: 'Archive recommended',
  };

  return (
    <Card className="border-border bg-surface p-5">
      <div className="flex items-start justify-between gap-4">
        <div>
          <h3 className="text-sm font-bold text-navy">{assessment.title}</h3>
          <p className="text-xs text-muted mt-1">
            Last published {assessment.daysSinceLastEdit} days ago
            {assessment.daysSinceLastUsage !== null && (
              <> · Last used {assessment.daysSinceLastUsage} days ago</>
            )}
          </p>
        </div>
        <Badge variant={actionColor as 'success' | 'warning' | 'danger' | 'info'} size="sm">
          {actionLabel[assessment.recommendedAction]}
        </Badge>
      </div>

      {assessment.isStale && (
        <div className="mt-3 rounded-lg bg-warning/5 border border-warning/10 p-3">
          <div className="flex items-start gap-2">
            <AlertTriangle className="w-4 h-4 text-warning shrink-0 mt-0.5" />
            <p className="text-xs text-warning leading-relaxed">{assessment.stalenessReason}</p>
          </div>
        </div>
      )}

      <div className="mt-3 flex flex-wrap gap-2 text-xs text-muted">
        <span className="inline-flex items-center gap-1">
          <Calendar className="w-3 h-3" />
          {assessment.usageCountLast90Days} uses (90d)
        </span>
        <span>·</span>
        <span>Rubric coverage {assessment.rubricCoveragePercent}%</span>
        {assessment.missingRubricCriteria.length > 0 && (
          <span className="text-danger">({assessment.missingRubricCriteria.length} gaps)</span>
        )}
      </div>

      <div className="mt-4 flex gap-2">
        {assessment.recommendedAction !== 'no_action' && onRefresh && (
          <Button variant="outline" size="sm" onClick={onRefresh} className="gap-1">
            <RefreshCw className="w-3.5 h-3.5" />
            Refresh
          </Button>
        )}
        {assessment.recommendedAction === 'archive' && onArchive && (
          <Button variant="ghost" size="sm" className="text-danger gap-1" onClick={onArchive}>
            <Archive className="w-3.5 h-3.5" />
            Archive
          </Button>
        )}
      </div>
    </Card>
  );
}
