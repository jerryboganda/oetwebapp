/**
 * @vitest-environment jsdom
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';

const { apiRequestMock } = vi.hoisted(() => ({ apiRequestMock: vi.fn() }));

vi.mock('../api', () => ({
  apiRequest: apiRequestMock,
}));

import * as adminApi from '../study-planner-admin-api';

describe('study-planner-admin-api', () => {
  beforeEach(() => {
    apiRequestMock.mockReset();
  });

  describe('task templates', () => {
    it('listTaskTemplates builds query string with filters', async () => {
      apiRequestMock.mockResolvedValue([]);
      await adminApi.listTaskTemplates({ subtest: 'writing', search: 'ref', includeArchived: true });
      expect(apiRequestMock).toHaveBeenCalledWith(
        expect.stringContaining('/v1/admin/study-planner/task-templates')
      );
      const url = apiRequestMock.mock.calls[0][0] as string;
      expect(url).toContain('subtest=writing');
      expect(url).toContain('search=ref');
      expect(url).toContain('includeArchived=true');
    });

    it('listTaskTemplates omits params when none given', async () => {
      apiRequestMock.mockResolvedValue([]);
      await adminApi.listTaskTemplates();
      expect(apiRequestMock).toHaveBeenCalledWith('/v1/admin/study-planner/task-templates');
    });

    it('createTaskTemplate POSTs with correct body', async () => {
      apiRequestMock.mockResolvedValue({ id: 't1' });
      await adminApi.createTaskTemplate({
        slug: 'abc', title: 'A', subtestCode: 'writing',
        itemType: 'practice', durationMinutes: 30, rationaleMarkdown: 'x',
      });
      const call = apiRequestMock.mock.calls[0];
      expect(call[0]).toBe('/v1/admin/study-planner/task-templates');
      expect(call[1].method).toBe('POST');
      expect(JSON.parse(call[1].body)).toMatchObject({ slug: 'abc', title: 'A' });
    });

    it('archiveTaskTemplate sends DELETE', async () => {
      apiRequestMock.mockResolvedValue(undefined);
      await adminApi.archiveTaskTemplate('t1');
      expect(apiRequestMock).toHaveBeenCalledWith(
        '/v1/admin/study-planner/task-templates/t1',
        { method: 'DELETE' }
      );
    });
  });

  describe('plan templates', () => {
    it('listPlanTemplates hides archived by default', async () => {
      apiRequestMock.mockResolvedValue([]);
      await adminApi.listPlanTemplates();
      expect(apiRequestMock).toHaveBeenCalledWith('/v1/admin/study-planner/plan-templates');
    });

    it('listPlanTemplates with includeArchived sets query', async () => {
      apiRequestMock.mockResolvedValue([]);
      await adminApi.listPlanTemplates(true);
      expect(apiRequestMock).toHaveBeenCalledWith('/v1/admin/study-planner/plan-templates?includeArchived=true');
    });

    it('replacePlanTemplateItems normalises payload', async () => {
      apiRequestMock.mockResolvedValue(undefined);
      await adminApi.replacePlanTemplateItems('p1', [
        { taskTemplateId: 't1', weekOffset: 0, dayOffsetWithinWeek: 0, section: 'today', priority: 50, isMandatory: true, prerequisiteItemTemplateId: null, ordering: 0 },
      ]);
      const call = apiRequestMock.mock.calls[0];
      const body = JSON.parse(call[1].body);
      expect(body.items[0].taskTemplateId).toBe('t1');
      expect(body.items[0].section).toBe('today');
    });
  });

  describe('rules', () => {
    it('previewRuleMatch posts learner context JSON', async () => {
      apiRequestMock.mockResolvedValue({ templateId: 'tpl', matchedRuleIds: [], consideredRuleIds: [], reason: 'ok' });
      await adminApi.previewRuleMatch({
        userId: 'u', examFamilyCode: 'oet', weakSubtests: ['writing'],
      });
      const call = apiRequestMock.mock.calls[0];
      expect(call[0]).toBe('/v1/admin/study-planner/rules/preview');
      expect(call[1].method).toBe('POST');
      expect(JSON.parse(call[1].body)).toMatchObject({ examFamilyCode: 'oet' });
    });
  });

  describe('drift policy', () => {
    it('getDriftPolicy defaults to oet exam family', async () => {
      apiRequestMock.mockResolvedValue({});
      await adminApi.getDriftPolicy();
      expect(apiRequestMock).toHaveBeenCalledWith('/v1/admin/study-planner/drift-policies/oet');
    });

    it('updateDriftPolicy PUTs with body', async () => {
      apiRequestMock.mockResolvedValue({});
      await adminApi.updateDriftPolicy('oet', { mildDays: 3, moderateDays: 7, severeDays: 14 });
      const call = apiRequestMock.mock.calls[0];
      expect(call[1].method).toBe('PUT');
    });
  });

  describe('insights', () => {
    it('getStudyPlannerInsights hits insights endpoint', async () => {
      apiRequestMock.mockResolvedValue({ totalPlans: 1 });
      await adminApi.getStudyPlannerInsights();
      expect(apiRequestMock).toHaveBeenCalledWith('/v1/admin/study-planner/insights');
    });

    it('regenerateLearnerPlan POSTs to nested route', async () => {
      apiRequestMock.mockResolvedValue({ id: 'p1', version: 2, state: 'completed' });
      await adminApi.regenerateLearnerPlan('u-1');
      expect(apiRequestMock).toHaveBeenCalledWith(
        '/v1/admin/study-planner/users/u-1/plan/regenerate',
        { method: 'POST' }
      );
    });
  });
});
