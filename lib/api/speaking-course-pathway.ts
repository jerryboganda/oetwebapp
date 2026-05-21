/**
 * Typed API client for the 16-stage Speaking course pathway (Phase 6).
 *
 * Backs `/app/speaking/course-pathway/page.tsx`.
 */
import { apiClient } from '@/lib/api';

export type SpeakingPathwayStageState = 'locked' | 'in_progress' | 'completed';
export type SpeakingPathwayActivityKind =
  | 'OrientationVideo'
  | 'GuidedReading'
  | 'Drill'
  | 'RolePlay'
  | 'Mock';

export interface SpeakingPathwayStage {
  code: string;
  orderIndex: number;
  title: string;
  description: string;
  activityKind: SpeakingPathwayActivityKind | string;
  state: SpeakingPathwayStageState;
}

export interface SpeakingPathway {
  pathwayCode: string;
  title: string;
  totalStages: number;
  completedStageCount: number;
  progressPercent: number;
  stages: SpeakingPathwayStage[];
  nextStage: SpeakingPathwayStage | null;
}

export async function fetchSpeakingCoursePathway(): Promise<SpeakingPathway> {
  return apiClient.get<SpeakingPathway>('/v1/speaking/course-pathway');
}
