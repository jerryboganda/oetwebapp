import {
  isExamOrLiveRoute,
  isImmersiveLearnerRoute,
  isLearnerWorkspaceRoute,
  resolveLearnerChrome,
  shouldShowLearnerBreadcrumbs,
} from '../learner-dashboard-route-policy';

describe('learner dashboard route policy', () => {
  it('identifies learner workspace routes without including unrelated portals', () => {
    expect(isLearnerWorkspaceRoute('/writing/result')).toBe(true);
    expect(isLearnerWorkspaceRoute('/progress')).toBe(true);
    expect(isLearnerWorkspaceRoute('/admin/content')).toBe(false);
    expect(isLearnerWorkspaceRoute('/expert/queue')).toBe(false);
  });

  it('suppresses breadcrumbs on dashboard roots and immersive player routes', () => {
    expect(shouldShowLearnerBreadcrumbs('/')).toBe(false);
    expect(shouldShowLearnerBreadcrumbs('/dashboard')).toBe(false);
    expect(shouldShowLearnerBreadcrumbs('/listening/player/attempt-1')).toBe(false);
    expect(isImmersiveLearnerRoute('/mocks/player/mock-1')).toBe(true);
  });

  it('shows breadcrumbs on oriented deep learner pages', () => {
    expect(shouldShowLearnerBreadcrumbs('/mocks/report/mock-1')).toBe(true);
    expect(shouldShowLearnerBreadcrumbs('/speaking/results/attempt-1')).toBe(true);
    expect(shouldShowLearnerBreadcrumbs('/settings/profile')).toBe(true);
  });

  it('classifies exam and live routes segment-exactly', () => {
    expect(isExamOrLiveRoute('/reading/paper/p1')).toBe(true);
    expect(isExamOrLiveRoute('/listening/paper/p1/')).toBe(true);
    expect(isExamOrLiveRoute('/mocks/player/m1')).toBe(true);
    expect(isExamOrLiveRoute('/mocks/speaking-room/b1')).toBe(true);
    expect(isExamOrLiveRoute('/writing/paper/session/s1')).toBe(true);
    expect(isExamOrLiveRoute('/mocks/writing/a1')).toBe(true);
    expect(isExamOrLiveRoute('/speaking/sessions/s1/live-tutor')).toBe(true);
    expect(isExamOrLiveRoute('/speaking/sessions/s1')).toBe(false);
    expect(isExamOrLiveRoute('/listening/player/attempt-1')).toBe(true);
    expect(isExamOrLiveRoute('/expert/speaking-room/b1')).toBe(true);
    expect(isExamOrLiveRoute('/expert/speaking/live-room/s1')).toBe(true);
    expect(isExamOrLiveRoute('/expert/speaking/exam/e1?tab=notes')).toBe(true);
    expect(isExamOrLiveRoute('/reading/paper/p1/results')).toBe(false);
    expect(isExamOrLiveRoute('/reading/exam')).toBe(false);
    expect(isExamOrLiveRoute('/reading')).toBe(false);
    expect(isExamOrLiveRoute('/expert/speaking')).toBe(false);
    expect(isExamOrLiveRoute('/admin/users')).toBe(false);
  });

  it('resolves learner chrome: workspace by default, exam players keep the full shell', () => {
    expect(resolveLearnerChrome('/dashboard')).toEqual({ mode: 'workspace', requireAuth: true, examOrLive: false });
    expect(resolveLearnerChrome(null)).toEqual({ mode: 'workspace', requireAuth: true, examOrLive: false });
    expect(resolveLearnerChrome('/reading/paper/p1')).toEqual({ mode: 'workspace', requireAuth: true, examOrLive: true });
    expect(resolveLearnerChrome('/mocks/player/m1').mode).toBe('workspace');
    expect(resolveLearnerChrome('/speaking/sessions/s1/results').mode).toBe('workspace');
  });

  it('resolves self-chromed learner routes to no chrome', () => {
    expect(resolveLearnerChrome('/listening/player/a1')).toEqual({ mode: 'none', requireAuth: true, examOrLive: true });
    expect(resolveLearnerChrome('/speaking/sessions/s1/live-tutor').mode).toBe('none');
    expect(resolveLearnerChrome('/speaking/sessions/s1').mode).toBe('none');
    expect(resolveLearnerChrome('/billing/payment-return?status=ok').mode).toBe('none');
  });

  it('keeps content, hub and results pages in the workspace chrome so navigation never disappears', () => {
    for (const route of ['/listening/strategies/', '/listening/lessons/l1', '/listening/stats', '/listening/mocks/m1/results', '/speaking/exam', '/speaking/exam/e1/results', '/speaking/mocks']) {
      expect(resolveLearnerChrome(route).mode, route).toBe('workspace');
    }
  });

  it('resolves focus routes with the page title copy or i18n key', () => {
    expect(resolveLearnerChrome('/onboarding')).toEqual({
      mode: 'focus', requireAuth: true, title: 'Getting Started', examOrLive: false,
    });
    expect(resolveLearnerChrome('/placement-test')).toMatchObject({ mode: 'focus', title: 'Placement Test' });
    expect(resolveLearnerChrome('/placement-test/history').mode).toBe('workspace');
    expect(resolveLearnerChrome('/writing/paper/session/s1')).toEqual({
      mode: 'focus', requireAuth: true, titleKey: 'writing.paper.pageTitle', examOrLive: true,
    });
    expect(resolveLearnerChrome('/writing/submissions/w1/revise')).toMatchObject({
      mode: 'focus', titleKey: 'writing.submissions.revise.pageTitle',
    });
  });

  it('keeps the public speaking reference pages outside the auth gate', () => {
    expect(resolveLearnerChrome('/speaking/assessment-criteria')).toEqual({ mode: 'workspace', requireAuth: false, examOrLive: false });
    expect(resolveLearnerChrome('/speaking/intro-questions').requireAuth).toBe(false);
    expect(resolveLearnerChrome('/speaking').requireAuth).toBe(true);
  });
});
