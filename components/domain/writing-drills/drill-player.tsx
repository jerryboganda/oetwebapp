'use client';

import { useCallback } from 'react';
import { analytics } from '@/lib/analytics';
import type { Drill, DrillGradeResult } from '@/lib/writing-drills/types';
import { RelevanceDrillComponent } from './relevance-drill';
import { OpeningDrillComponent } from './opening-drill';
import { OrderingDrillComponent } from './ordering-drill';
import { ExpansionDrillComponent } from './expansion-drill';
import { ToneDrillComponent } from './tone-drill';
import { AbbreviationDrillComponent } from './abbreviation-drill';

interface DrillPlayerProps {
  drill: Drill;
}

export function DrillPlayer({ drill }: DrillPlayerProps) {
  const onGraded = useCallback(
    (result: DrillGradeResult) => {
      analytics.track('writing_drill_graded', {
        drillId: drill.id,
        type: drill.type,
        profession: drill.profession,
        scorePercent: result.scorePercent,
        passed: result.passed,
        errorTags: result.errorTags.join(',') || null,
      });
    },
    [drill.id, drill.type, drill.profession],
  );

  switch (drill.type) {
    case 'relevance':
      return <RelevanceDrillComponent drill={drill} onGraded={onGraded} />;
    case 'opening':
      return <OpeningDrillComponent drill={drill} onGraded={onGraded} />;
    case 'ordering':
      return <OrderingDrillComponent drill={drill} onGraded={onGraded} />;
    case 'expansion':
      return <ExpansionDrillComponent drill={drill} onGraded={onGraded} />;
    case 'tone':
      return <ToneDrillComponent drill={drill} onGraded={onGraded} />;
    case 'abbreviation':
      return <AbbreviationDrillComponent drill={drill} onGraded={onGraded} />;
    default: {
      const _exhaustive: never = drill;
      throw new Error(`Unhandled drill type: ${JSON.stringify(_exhaustive)}`);
    }
  }
}
