/**
 * Typed API helpers for the Study Plan template admin surface.
 * Wraps the apiClient with the right paths, types, and error normalisation
 * so the admin pages can stay focused on presentation.
 */
import { apiClient } from './api';

export type StudyPlanSlotKind =
  | 'next-unattempted-paper'
  | 'drill-by-tag'
  | 'spaced-rep-review'
  | 'weak-skill-focus'
  | 'full-mock'
  | 'mini-mock'
  | 'expert-review-submission'
  | 'pronunciation-drill'
  | 'vocabulary-flashcards'
  | 'custom-content';

export interface StudyPlanTemplateSlot {
  subtest: string;
  kind: StudyPlanSlotKind;
  minutes: number;
  tags?: string[];
  rationaleHint?: string;
  contentId?: string;
}

export interface StudyPlanTemplateDay {
  dayOfWeek: string;
  slots: StudyPlanTemplateSlot[];
}

export interface StudyPlanTemplateWeek {
  weekIndex: number;
  label?: string;
  days: StudyPlanTemplateDay[];
}

export interface StudyPlanTemplateCheckpoint {
  afterWeek: number;
  kind: string;
  subtests: string[];
}

export interface StudyPlanTemplateBody {
  weeks: StudyPlanTemplateWeek[];
  checkpoints: StudyPlanTemplateCheckpoint[];
}

export interface StudyPlanTemplateListItem {
  id: string;
  slug: string;
  name: string;
  description: string | null;
  examTypeCode: string;
  minWeeks: number;
  maxWeeks: number;
  targetBand: string | null;
  professionId: string | null;
  focusTags: string[];
  defaultMinutesPerDay: number;
  isActive: boolean;
  version: number;
  tierCodes: string[];
  updatedAt: string;
}

export interface StudyPlanTemplateDetail extends StudyPlanTemplateListItem {
  body: StudyPlanTemplateBody;
}

export interface StudyPlanTemplateUpsertRequest {
  slug: string;
  name: string;
  description?: string | null;
  examTypeCode?: string;
  minWeeks: number;
  maxWeeks: number;
  targetBand?: string | null;
  professionId?: string | null;
  focusTags?: string[];
  defaultMinutesPerDay: number;
  isActive: boolean;
  tierCodes?: string[];
  body?: StudyPlanTemplateBody;
}

export interface StudyPlanTemplatePreview {
  templateId: string;
  slug: string;
  days: Array<{
    weekIndex: number;
    dayOfWeek: string;
    slots: Array<{
      subtest: string;
      kind: string;
      minutes: number;
      title: string;
      contentId: string | null;
      route: string;
    }>;
  }>;
}

export async function listStudyPlanTemplates(filters?: {
  tier?: string;
  profession?: string;
  active?: boolean;
}): Promise<StudyPlanTemplateListItem[]> {
  const params = new URLSearchParams();
  if (filters?.tier) params.set('tier', filters.tier);
  if (filters?.profession) params.set('profession', filters.profession);
  if (filters?.active !== undefined) params.set('active', String(filters.active));
  const qs = params.toString();
  return apiClient.get<StudyPlanTemplateListItem[]>(
    `/v1/admin/study-plan-templates/${qs ? `?${qs}` : ''}`,
  );
}

export async function getStudyPlanTemplate(id: string): Promise<StudyPlanTemplateDetail> {
  return apiClient.get<StudyPlanTemplateDetail>(`/v1/admin/study-plan-templates/${id}`);
}

export async function createStudyPlanTemplate(
  request: StudyPlanTemplateUpsertRequest,
): Promise<StudyPlanTemplateDetail> {
  return apiClient.post<StudyPlanTemplateDetail>('/v1/admin/study-plan-templates/', request);
}

export async function updateStudyPlanTemplate(
  id: string,
  request: StudyPlanTemplateUpsertRequest,
): Promise<StudyPlanTemplateDetail> {
  return apiClient.put<StudyPlanTemplateDetail>(`/v1/admin/study-plan-templates/${id}`, request);
}

export async function softDeleteStudyPlanTemplate(id: string): Promise<void> {
  await apiClient.delete(`/v1/admin/study-plan-templates/${id}`);
}

export async function duplicateStudyPlanTemplate(id: string): Promise<StudyPlanTemplateDetail> {
  return apiClient.post<StudyPlanTemplateDetail>(
    `/v1/admin/study-plan-templates/${id}/duplicate`,
  );
}

export async function validateStudyPlanTemplate(
  id: string,
): Promise<{ isValid: boolean; errors: string[] }> {
  return apiClient.post(`/v1/admin/study-plan-templates/${id}/validate`);
}

export async function previewStudyPlanTemplate(
  id: string,
  request: { professionId?: string | null; targetBand?: string | null; weeksToPreview: number },
): Promise<StudyPlanTemplatePreview> {
  return apiClient.post<StudyPlanTemplatePreview>(
    `/v1/admin/study-plan-templates/${id}/preview`,
    request,
  );
}

export async function bulkStudyPlanTemplateAction(
  action: 'activate' | 'deactivate' | 'duplicate' | 'soft-delete',
  templateIds: string[],
): Promise<{ processed: number }> {
  return apiClient.post('/v1/admin/study-plan-templates/bulk', { action, templateIds });
}

export async function setStudyPlanTemplateTiers(
  id: string,
  tierCodes: string[],
): Promise<string[]> {
  return apiClient.put<string[]>(`/v1/admin/study-plan-templates/${id}/tiers`, { tierCodes });
}

export async function forceRegenerateLearnerStudyPlan(userId: string): Promise<unknown> {
  return apiClient.post(`/v1/admin/study-plan/${userId}/regenerate`);
}

export function emptyTemplateBody(): StudyPlanTemplateBody {
  return { weeks: [{ weekIndex: 0, label: 'Week 1', days: [] }], checkpoints: [] };
}
