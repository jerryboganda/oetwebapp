import {
  isExamOrLiveRoute,
  isImmersiveLearnerRoute,
  isLearnerWorkspaceRoute,
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
});
