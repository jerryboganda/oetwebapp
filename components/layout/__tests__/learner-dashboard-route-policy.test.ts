import {
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
});
