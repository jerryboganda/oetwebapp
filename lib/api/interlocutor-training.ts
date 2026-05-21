/**
 * Typed API client for Interlocutor Training modules (Phase 6).
 *
 * Backs two surfaces:
 *   - Admin CRUD (list, create, update, publish, archive)
 *   - Tutor read + mark-complete
 */
import { apiClient } from '@/lib/api';

export type InterlocutorTrainingStage = 'Onboarding' | 'Refresher';
export type InterlocutorTrainingStatus = 'Draft' | 'Published' | 'Archived';

export interface InterlocutorTrainingModule {
  id: string;
  title: string;
  orderIndex: number;
  contentMarkdown: string;
  requiredForCalibration: boolean;
  stage: InterlocutorTrainingStage | string;
  status: InterlocutorTrainingStatus | string;
  createdAt: string;
  updatedAt: string;
  publishedAt: string | null;
}

export interface TutorInterlocutorTrainingModule
  extends Omit<InterlocutorTrainingModule, 'createdAt' | 'updatedAt'> {
  completedAt: string | null;
  quizScore: number | null;
  isCompleted: boolean;
}

export interface TutorInterlocutorTrainingState {
  modules: TutorInterlocutorTrainingModule[];
  isEligibleForLiveRooms: boolean;
}

// ── Admin ───────────────────────────────────────────────────────────────────

export interface ListModuleFilters {
  stage?: InterlocutorTrainingStage | string | null;
  status?: InterlocutorTrainingStatus | string | null;
}

export async function adminListInterlocutorModules(
  filters?: ListModuleFilters,
): Promise<InterlocutorTrainingModule[]> {
  const params = new URLSearchParams();
  if (filters?.stage) params.set('stage', String(filters.stage));
  if (filters?.status) params.set('status', String(filters.status));
  const query = params.toString();
  return apiClient.get<InterlocutorTrainingModule[]>(
    `/v1/admin/speaking/interlocutor-training/modules${query ? `?${query}` : ''}`,
  );
}

export async function adminGetInterlocutorModule(
  id: string,
): Promise<InterlocutorTrainingModule> {
  return apiClient.get<InterlocutorTrainingModule>(
    `/v1/admin/speaking/interlocutor-training/modules/${encodeURIComponent(id)}`,
  );
}

export interface CreateInterlocutorModuleInput {
  title: string;
  contentMarkdown?: string;
  stage?: InterlocutorTrainingStage | string;
  orderIndex: number;
  requiredForCalibration: boolean;
}

export async function adminCreateInterlocutorModule(
  body: CreateInterlocutorModuleInput,
): Promise<InterlocutorTrainingModule> {
  return apiClient.post<InterlocutorTrainingModule>(
    '/v1/admin/speaking/interlocutor-training/modules',
    body,
  );
}

export interface UpdateInterlocutorModuleInput {
  title?: string;
  contentMarkdown?: string;
  stage?: InterlocutorTrainingStage | string;
  orderIndex?: number;
  requiredForCalibration?: boolean;
}

export async function adminUpdateInterlocutorModule(
  id: string,
  body: UpdateInterlocutorModuleInput,
): Promise<InterlocutorTrainingModule> {
  return apiClient.patch<InterlocutorTrainingModule>(
    `/v1/admin/speaking/interlocutor-training/modules/${encodeURIComponent(id)}`,
    body,
  );
}

export async function adminPublishInterlocutorModule(
  id: string,
): Promise<InterlocutorTrainingModule> {
  return apiClient.post<InterlocutorTrainingModule>(
    `/v1/admin/speaking/interlocutor-training/modules/${encodeURIComponent(id)}/publish`,
    {},
  );
}

export async function adminArchiveInterlocutorModule(
  id: string,
): Promise<InterlocutorTrainingModule> {
  return apiClient.post<InterlocutorTrainingModule>(
    `/v1/admin/speaking/interlocutor-training/modules/${encodeURIComponent(id)}/archive`,
    {},
  );
}

// ── Tutor ───────────────────────────────────────────────────────────────────

export async function tutorListInterlocutorTraining(): Promise<TutorInterlocutorTrainingState> {
  return apiClient.get<TutorInterlocutorTrainingState>(
    '/v1/expert/speaking/interlocutor-training',
  );
}

export interface CompleteInterlocutorModuleInput {
  quizScore?: number | null;
}

export interface CompleteInterlocutorModuleResult {
  moduleId: string;
  completedAt: string | null;
  quizScore: number | null;
}

export async function tutorCompleteInterlocutorModule(
  id: string,
  body?: CompleteInterlocutorModuleInput,
): Promise<CompleteInterlocutorModuleResult> {
  return apiClient.post<CompleteInterlocutorModuleResult>(
    `/v1/expert/speaking/interlocutor-training/modules/${encodeURIComponent(id)}/complete`,
    body ?? {},
  );
}
