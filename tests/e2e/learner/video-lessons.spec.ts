import { expect, test, type Page } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

const listLessons = [
  {
    id: 'video-lesson-1',
    source: 'content_hierarchy',
    examTypeCode: 'oet',
    subtestCode: 'writing',
    title: 'Writing task planning',
    description: 'Plan a referral letter before writing.',
    durationSeconds: 900,
    thumbnailUrl: null,
    category: 'strategy',
    instructorName: null,
    difficultyLevel: 'intermediate',
    isAccessible: true,
    isPreviewEligible: false,
    requiresUpgrade: false,
    progress: { watchedSeconds: 120, completed: false, percentComplete: 13, lastWatchedAt: '2026-04-01T00:00:00Z' },
    programId: 'program-1',
    moduleId: 'module-1',
    sortOrder: 1,
  },
  {
    id: 'video-lesson-2',
    source: 'content_hierarchy',
    examTypeCode: 'oet',
    subtestCode: 'speaking',
    title: 'Speaking preview clinic',
    description: 'Preview a speaking strategy lesson.',
    durationSeconds: 600,
    thumbnailUrl: null,
    category: 'preview',
    instructorName: null,
    difficultyLevel: 'beginner',
    isAccessible: true,
    isPreviewEligible: true,
    requiresUpgrade: false,
    progress: null,
    programId: 'program-1',
    moduleId: 'module-1',
    sortOrder: 2,
  },
];

const lessonDetail = {
  ...listLessons[0],
  videoUrl: null,
  captionUrl: null,
  transcriptUrl: null,
  accessReason: 'entitled',
  mediaAssetId: 'media-1',
  programTitle: 'OET Video Course',
  trackId: 'track-1',
  trackTitle: 'Writing Track',
  moduleTitle: 'Planning Module',
  previousLessonId: null,
  nextLessonId: 'video-lesson-2',
  chapters: [{ timeSeconds: 120, title: 'Plan the answer' }],
  resources: [{ title: 'Planning checklist', url: '/resources/planning.pdf', type: 'pdf' }],
};

const programOutline = {
  id: 'program-1',
  title: 'OET Video Course',
  description: 'Structured OET video lessons.',
  examTypeCode: 'oet',
  thumbnailUrl: null,
  isAccessible: true,
  tracks: [
    {
      id: 'track-1',
      title: 'Writing Track',
      description: 'Writing lessons.',
      subtestCode: 'writing',
      modules: [
        {
          id: 'module-1',
          title: 'Planning Module',
          description: 'Plan before writing.',
          estimatedDurationMinutes: 25,
          lessons: listLessons,
        },
      ],
    },
  ],
};

async function mockVideoLessonApis(page: Page) {
  await page.route(/\/v1\/features\/video_lessons$/, async (route) => {
    await route.fulfill({ json: { key: 'video_lessons', enabled: true } });
  });

  await page.route(/\/v1\/lessons(?:\?.*)?$/, async (route) => {
    const url = new URL(route.request().url());
    const subtest = url.searchParams.get('subtestCode');
    const items = subtest ? listLessons.filter((lesson) => lesson.subtestCode === subtest) : listLessons;
    await route.fulfill({ json: items });
  });

  await page.route(/\/v1\/lessons\/video-lesson-1$/, async (route) => {
    await route.fulfill({ json: lessonDetail });
  });

  await page.route(/\/v1\/lessons\/programs\/program-1$/, async (route) => {
    await route.fulfill({ json: programOutline });
  });

  await page.route(/\/v1\/programs-browser(?:\?.*)?$/, async (route) => {
    await route.fulfill({
      json: {
        items: [
          {
            id: 'program-1',
            title: 'OET Video Course',
            description: 'Structured OET video lessons.',
            programType: 'full_course',
            isAccessible: true,
            trackCount: 1,
            estimatedDurationMinutes: 150,
          },
        ],
        total: 1,
        page: 1,
        pageSize: 20,
      },
    });
  });
}

test.describe('Learner video lessons @learner @smoke', () => {
  test.beforeEach(async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-learner') {
      test.skip();
    }
    await mockVideoLessonApis(page);
  });

  test('hub filters lessons and opens the first lesson detail', async ({ page }, testInfo) => {
    const diagnostics = observePage(page);

    await page.goto('/lessons', { waitUntil: 'domcontentloaded' });
    await expect(page.getByRole('heading', { name: /^Video Lessons$/i })).toBeVisible();
    await expect(page.getByText('Writing task planning')).toBeVisible();

    const subtestSelect = page.locator('select').first();
    await subtestSelect.selectOption('speaking');
    await expect(page.getByText('Speaking preview clinic')).toBeVisible();
    await expect(page.getByText('Writing task planning')).toHaveCount(0);

    await subtestSelect.selectOption('');
    await page.getByRole('link', { name: /Writing task planning/i }).click();
    await expect(page).toHaveURL(/\/lessons\/video-lesson-1$/);
    await expect(page.getByRole('heading', { name: /Writing task planning/i })).toBeVisible();
    await expect(page.getByText(/Planning checklist/i)).toBeVisible();

    expectNoSevereClientIssues(diagnostics, { allowNextDevNoise: true, allowNotificationReconnectNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('program card opens the video program outline', async ({ page }, testInfo) => {
    const diagnostics = observePage(page);

    await page.goto('/lessons/programs', { waitUntil: 'domcontentloaded' });
    await page.getByRole('link', { name: /OET Video Course/i }).click();

    await expect(page).toHaveURL(/\/lessons\/programs\/program-1$/);
    await expect(page.getByRole('heading', { name: /OET Video Course/i })).toBeVisible();
    await expect(page.getByRole('link', { name: /Writing task planning/i })).toHaveAttribute('href', '/lessons/video-lesson-1');

    expectNoSevereClientIssues(diagnostics, { allowNextDevNoise: true, allowNotificationReconnectNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('discover uses canonical video lesson links and hides invalid player links after filtering', async ({ page }, testInfo) => {
    const diagnostics = observePage(page);

    await page.goto('/lessons/discover', { waitUntil: 'domcontentloaded' });
    await expect(page.getByRole('heading', { name: /Discover Video Lessons/i })).toBeVisible();
    await expect(page.getByRole('link', { name: /Writing task planning/i })).toHaveAttribute('href', '/lessons/video-lesson-1');

    await page.getByPlaceholder('Search video lessons, topics, or categories...').fill('invalid content item');
    await expect(page.getByText('No video lessons found')).toBeVisible();
    await expect(page.getByRole('link', { name: /Writing task planning/i })).toHaveCount(0);

    expectNoSevereClientIssues(diagnostics, { allowNextDevNoise: true, allowNotificationReconnectNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
