import { expect, test, type Page } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

const videoSummary = {
  id: 'video-1',
  title: 'Writing task planning',
  description: 'Plan a referral letter before writing.',
  durationSeconds: 900,
  thumbnailUrl: null,
  accessTier: 'free',
  isAccessible: true,
  requiresUpgrade: false,
  lockReason: null,
  subtestCode: 'writing',
  difficulty: 'core',
  tags: ['planning', 'referral'],
  isFeatured: true,
  publishedAt: '2026-06-20T00:00:00Z',
  viewCount: 42,
  progress: { positionSeconds: 120, percentComplete: 13, completed: false },
  bookmarked: false,
  categoryIds: ['cat-1'],
};

const secondVideo = {
  ...videoSummary,
  id: 'video-2',
  title: 'Speaking warm-up clinic',
  subtestCode: 'speaking',
  isFeatured: false,
  progress: null,
  categoryIds: ['cat-1'],
};

const libraryHome = {
  featured: [videoSummary],
  continueWatching: [videoSummary],
  categories: [
    {
      id: 'cat-1',
      title: 'Exam strategy',
      slug: 'exam-strategy',
      description: 'Plan-first techniques.',
      videos: [videoSummary, secondVideo],
    },
  ],
  uncategorized: [],
};

const videoDetail = {
  ...videoSummary,
  chapters: [{ timeSeconds: 120, title: 'Plan the answer' }],
  captions: [{ languageCode: 'en', label: 'English' }],
  attachments: [{ id: 'att-1', title: 'Planning checklist', url: '/v1/media/media-1/content' }],
  previousVideoId: null,
  nextVideoId: 'video-2',
};

async function mockVideoLibraryApis(page: Page) {
  await page.route(/\/v1\/features\/video_library$/, async (route) => {
    await route.fulfill({ json: { key: 'video_library', enabled: true } });
  });

  await page.route(/\/v1\/video-library$/, async (route) => {
    await route.fulfill({ json: libraryHome });
  });

  await page.route(/\/v1\/video-library\/videos\/video-1$/, async (route) => {
    await route.fulfill({ json: videoDetail });
  });
}

test.describe('Learner video library @learner @smoke', () => {
  test.beforeEach(async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-learner') {
      test.skip();
    }
    await mockVideoLibraryApis(page);
  });

  test('hub browses the catalog and search filters it', async ({ page }, testInfo) => {
    const diagnostics = observePage(page);

    await page.goto('/videos', { waitUntil: 'domcontentloaded' });
    await expect(page.getByRole('heading', { name: /Learn from expert-led OET video lessons/i })).toBeVisible();
    await expect(page.getByText('Writing task planning').first()).toBeVisible();

    await page.getByLabel('Search videos').fill('speaking warm-up');
    await expect(page.getByText('Speaking warm-up clinic')).toBeVisible();
    await expect(page.getByText('Writing task planning')).toHaveCount(0);

    expectNoSevereClientIssues(diagnostics, { allowNextDevNoise: true, allowNotificationReconnectNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('web browser gets the lock screen — no video element, no playback-session call', async ({ page }, testInfo) => {
    const diagnostics = observePage(page);
    const playbackSessionCalls: string[] = [];
    const cdnCalls: string[] = [];

    page.on('request', (request) => {
      const url = request.url();
      if (/playback-session/.test(url)) playbackSessionCalls.push(url);
      if (/\.b-cdn\.net/.test(url)) cdnCalls.push(url);
    });

    await page.goto('/videos/video-1', { waitUntil: 'domcontentloaded' });
    await expect(page.getByRole('heading', { name: /Writing task planning/i })).toBeVisible();

    // MISSION-CRITICAL: browsers never render a player or request playback.
    await expect(page.getByText(/Videos play only in the OET app/i)).toBeVisible();
    await expect(page.locator('video')).toHaveCount(0);
    expect(playbackSessionCalls).toHaveLength(0);
    expect(cdnCalls).toHaveLength(0);

    // The catalog metadata stays fully browsable (chapters, handouts, next step).
    await expect(page.getByText(/Plan the answer/i)).toBeVisible();
    await expect(page.getByText(/Planning checklist/i)).toBeVisible();

    // Download CTAs are present on the lock screen.
    await expect(page.getByRole('link', { name: /Desktop app/i })).toBeVisible();

    expectNoSevereClientIssues(diagnostics, { allowNextDevNoise: true, allowNotificationReconnectNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('legacy /lessons redirects to the video library', async ({ page }, testInfo) => {
    const diagnostics = observePage(page);

    await page.goto('/lessons', { waitUntil: 'domcontentloaded' });
    await expect(page).toHaveURL(/\/videos$/);

    expectNoSevereClientIssues(diagnostics, { allowNextDevNoise: true, allowNotificationReconnectNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
