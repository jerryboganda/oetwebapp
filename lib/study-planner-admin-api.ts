import { apiRequest } from './api';

export interface TaskTemplateDto {
  id: string;
  slug: string;
  title: string;
  subtestCode: string;
  itemType: string;
  durationMinutes: number;
  rationaleMarkdown: string;
  professionScopeJson: string;
  examFamilyCode: string;
  targetCountriesJson: string;
  difficultyMin: number;
  difficultyMax: number;
  defaultSection: string;
  defaultContentPaperId: string | null;
  tagsCsv: string;
  isArchived: boolean;
  createdByAdminId: string;
  createdAt: string;
  updatedAt: string;
}

export interface PlanTemplateDto {
  id: string;
  slug: string;
  name: string;
  description: string;
  durationWeeks: number;
  defaultHoursPerWeek: number;
  examFamilyCode: string;
  isArchived: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface PlanTemplateItemDto {
  id: string;
  planTemplateId: string;
  taskTemplateId: string;
  weekOffset: number;
  dayOffsetWithinWeek: number;
  section: string;
  priority: number;
  isMandatory: boolean;
  prerequisiteItemTemplateId: string | null;
  ordering: number;
}

export interface AssignmentRuleDto {
  id: string;
  name: string;
  examFamilyCode: string;
  priority: number;
  weight: number;
  conditionJson: string;
  targetTemplateId: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface DriftPolicyDto {
  id: string;
  examFamilyCode: string;
  mildDays: number;
  moderateDays: number;
  severeDays: number;
  mildCopy: string;
  moderateCopy: string;
  severeCopy: string;
  onTrackCopy: string;
  autoRegenerateOnModerate: boolean;
  autoRegenerateOnSevere: boolean;
  updatedAt: string;
}

export interface StudyPlannerInsights {
  totalPlans: number;
  totalItems: number;
  completedItems: number;
  overdueItems: number;
  completionRate: number;
  regenLast7d: number;
}

// ── Task Templates ──────────────────────────────────────────────────────────

export function listTaskTemplates(params?: {
  subtest?: string;
  examFamily?: string;
  includeArchived?: boolean;
  search?: string;
}): Promise<TaskTemplateDto[]> {
  const q = new URLSearchParams();
  if (params?.subtest) q.set('subtest', params.subtest);
  if (params?.examFamily) q.set('examFamily', params.examFamily);
  if (params?.includeArchived) q.set('includeArchived', 'true');
  if (params?.search) q.set('search', params.search);
  const query = q.toString();
  return apiRequest(`/v1/admin/study-planner/task-templates${query ? '?' + query : ''}`);
}

export function getTaskTemplate(id: string): Promise<TaskTemplateDto> {
  return apiRequest(`/v1/admin/study-planner/task-templates/${id}`);
}

export interface TaskTemplateCreatePayload {
  slug: string;
  title: string;
  subtestCode: string;
  itemType: string;
  durationMinutes: number;
  rationaleMarkdown: string;
  professionScope?: string[];
  examFamilyCode?: string;
  targetCountries?: string[];
  difficultyMin?: number;
  difficultyMax?: number;
  defaultSection?: string;
  defaultContentPaperId?: string | null;
  tagsCsv?: string;
}

export function createTaskTemplate(dto: TaskTemplateCreatePayload): Promise<TaskTemplateDto> {
  return apiRequest('/v1/admin/study-planner/task-templates', {
    method: 'POST', body: JSON.stringify(dto),
  });
}

export function updateTaskTemplate(id: string, dto: Partial<TaskTemplateCreatePayload> & { isArchived?: boolean }): Promise<TaskTemplateDto> {
  return apiRequest(`/v1/admin/study-planner/task-templates/${id}`, {
    method: 'PUT', body: JSON.stringify(dto),
  });
}

export function archiveTaskTemplate(id: string): Promise<void> {
  return apiRequest(`/v1/admin/study-planner/task-templates/${id}`, { method: 'DELETE' });
}

// ── Plan Templates ──────────────────────────────────────────────────────────

export function listPlanTemplates(includeArchived = false): Promise<PlanTemplateDto[]> {
  return apiRequest(`/v1/admin/study-planner/plan-templates${includeArchived ? '?includeArchived=true' : ''}`);
}

export function getPlanTemplateDetail(id: string): Promise<{ template: PlanTemplateDto; items: PlanTemplateItemDto[] }> {
  return apiRequest(`/v1/admin/study-planner/plan-templates/${id}`);
}

export function createPlanTemplate(dto: { slug: string; name: string; description?: string; durationWeeks?: number; defaultHoursPerWeek?: number; examFamilyCode?: string }): Promise<PlanTemplateDto> {
  return apiRequest('/v1/admin/study-planner/plan-templates', {
    method: 'POST', body: JSON.stringify(dto),
  });
}

export function updatePlanTemplate(id: string, dto: { name?: string; description?: string; durationWeeks?: number; defaultHoursPerWeek?: number; isArchived?: boolean }): Promise<PlanTemplateDto> {
  return apiRequest(`/v1/admin/study-planner/plan-templates/${id}`, {
    method: 'PUT', body: JSON.stringify(dto),
  });
}

export function archivePlanTemplate(id: string): Promise<void> {
  return apiRequest(`/v1/admin/study-planner/plan-templates/${id}`, { method: 'DELETE' });
}

export function replacePlanTemplateItems(id: string, items: Omit<PlanTemplateItemDto, 'id' | 'planTemplateId'>[]): Promise<void> {
  return apiRequest(`/v1/admin/study-planner/plan-templates/${id}/items`, {
    method: 'PUT',
    body: JSON.stringify({ items: items.map((it) => ({
      id: null,
      taskTemplateId: it.taskTemplateId,
      weekOffset: it.weekOffset,
      dayOffsetWithinWeek: it.dayOffsetWithinWeek,
      section: it.section,
      priority: it.priority,
      isMandatory: it.isMandatory,
      prerequisiteItemTemplateId: it.prerequisiteItemTemplateId,
      ordering: it.ordering,
    })) }),
  });
}

// ── Rules ────────────────────────────────────────────────────────────────────

export function listAssignmentRules(includeInactive = false): Promise<AssignmentRuleDto[]> {
  return apiRequest(`/v1/admin/study-planner/rules${includeInactive ? '?includeInactive=true' : ''}`);
}

export function createAssignmentRule(dto: { name: string; examFamilyCode?: string; priority?: number; weight?: number; conditionJson: string; targetTemplateId: string; isActive?: boolean }): Promise<AssignmentRuleDto> {
  return apiRequest('/v1/admin/study-planner/rules', { method: 'POST', body: JSON.stringify(dto) });
}

export function updateAssignmentRule(id: string, dto: { name?: string; priority?: number; weight?: number; conditionJson?: string; targetTemplateId?: string; isActive?: boolean }): Promise<AssignmentRuleDto> {
  return apiRequest(`/v1/admin/study-planner/rules/${id}`, { method: 'PUT', body: JSON.stringify(dto) });
}

export function deleteAssignmentRule(id: string): Promise<void> {
  return apiRequest(`/v1/admin/study-planner/rules/${id}`, { method: 'DELETE' });
}

export interface LearnerPlanContextPreview {
  userId: string;
  professionId?: string;
  examFamilyCode: string;
  targetCountry?: string;
  weeksToExam?: number;
  hoursPerWeek?: number;
  targetWritingScore?: number;
  targetSpeakingScore?: number;
  targetReadingScore?: number;
  targetListeningScore?: number;
  weakSubtests: string[];
}

export function previewRuleMatch(ctx: LearnerPlanContextPreview): Promise<{ templateId: string | null; matchedRuleIds: string[]; consideredRuleIds: string[]; reason: string }> {
  return apiRequest('/v1/admin/study-planner/rules/preview', { method: 'POST', body: JSON.stringify(ctx) });
}

// ── Drift Policy ────────────────────────────────────────────────────────────

export function getDriftPolicy(examFamilyCode = 'oet'): Promise<DriftPolicyDto> {
  return apiRequest(`/v1/admin/study-planner/drift-policies/${examFamilyCode}`);
}

export function updateDriftPolicy(examFamilyCode: string, dto: Partial<Omit<DriftPolicyDto, 'id' | 'examFamilyCode' | 'updatedAt'>>): Promise<DriftPolicyDto> {
  return apiRequest(`/v1/admin/study-planner/drift-policies/${examFamilyCode}`, {
    method: 'PUT', body: JSON.stringify(dto),
  });
}

// ── Insights + per-learner ──────────────────────────────────────────────────

export function getStudyPlannerInsights(): Promise<StudyPlannerInsights> {
  return apiRequest('/v1/admin/study-planner/insights');
}

export function getLearnerPlan(userId: string): Promise<{ plan: unknown; items: unknown[] }> {
  return apiRequest(`/v1/admin/study-planner/users/${userId}/plan`);
}

export function regenerateLearnerPlan(userId: string): Promise<{ id: string; version: number; state: string }> {
  return apiRequest(`/v1/admin/study-planner/users/${userId}/plan/regenerate`, { method: 'POST' });
}
