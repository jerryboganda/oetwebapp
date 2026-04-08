import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';
import {
  fetchNotificationFeedForRole,
  triggerAdminNotificationProof,
  triggerExpertAssignedNotificationProof,
  triggerLearnerNotificationProof,
} from '../fixtures/notification-proof';

function escapeRegex(value: string) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

async function openNotificationCenter(page: Parameters<typeof observePage>[0]) {
  await page.getByRole('button', { name: /notifications/i }).click();
}

function notificationCenterPanel(page: Parameters<typeof observePage>[0]) {
  return page.locator('[data-radix-popper-content-wrapper], [role="dialog"]').last();
}

function notificationItemButton(
  page: Parameters<typeof observePage>[0],
  proof: {
    title: string;
    body: string;
  },
) {
  return notificationCenterPanel(page)
    .locator('button')
    .filter({ hasText: new RegExp(`${escapeRegex(proof.title)}[\\s\\S]*${escapeRegex(proof.body)}`) })
    .first();
}

async function refreshNotificationCenter(page: Parameters<typeof observePage>[0]) {
  const refreshButton = notificationCenterPanel(page).getByRole('button', { name: /^refresh$/i });
  await expect(refreshButton).toBeVisible();
  await refreshButton.evaluate((element) => {
    (element as HTMLButtonElement).click();
  });
}

async function unreadCount(page: Parameters<typeof observePage>[0]) {
  const label = await page.getByRole('button', { name: /notifications/i }).getAttribute('aria-label');
  const match = label?.match(/\((\d+) unread\)/i);
  return match ? Number.parseInt(match[1]!, 10) : 0;
}

test.describe('Notification center coverage', () => {
  test('learner desktop bell opens a populated notification center and deep links correctly', async ({ page, request }, testInfo) => {
    if (!testInfo.project.name.includes('learner') || testInfo.project.name.includes('mobile')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    const message = `QA learner notification proof ${Date.now()}`;

    await page.goto('/');
    await expect(page.getByRole('heading', { name: /keep today'?s priorities and exam signals in view/i })).toBeVisible();
    const unreadBefore = await unreadCount(page);

    const proof = await triggerLearnerNotificationProof(request, 'LearnerReviewReworkRequested', {
      attemptId: 'wa-001',
      message,
    });

    await expect.poll(async () => unreadCount(page)).toBeGreaterThan(unreadBefore);

    await openNotificationCenter(page);
    await expect.poll(async () => await notificationCenterPanel(page).textContent() ?? '').toContain(proof.body);

    await page.getByLabel('Category').selectOption('reviews');
    await page.getByLabel('Channel').selectOption('in_app');

    const notificationButton = notificationItemButton(page, proof);
    await expect(notificationButton).toBeVisible();
    await expect(notificationButton).toContainText(proof.title);
    await expect(notificationButton).toContainText(proof.body);
    await notificationButton.scrollIntoViewIfNeeded();
    await notificationButton.evaluate((element) => {
      (element as HTMLButtonElement).click();
    });

    await expect(page).toHaveURL(/\/submissions\/wa-001$/);

    await openNotificationCenter(page);
    const notificationCard = notificationItemButton(page, proof);
    await expect(notificationCard).toContainText(/read/i);
    await expect(page.getByRole('button', { name: /mark all read/i })).toBeVisible();

    expect(proof.processedImmediately).toBeTruthy();
    expectNoSevereClientIssues(diagnostics, { allowNotificationReconnectNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('learner mobile bell opens the drawer and shows notification content', async ({ page, request }, testInfo) => {
    if (!testInfo.project.name.includes('mobile') || !testInfo.project.name.includes('learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    const message = `QA mobile learner notification proof ${Date.now()}`;

    await page.goto('/');
    await expect(page.getByRole('heading', { name: /keep today'?s priorities and exam signals in view/i })).toBeVisible();

    await triggerLearnerNotificationProof(request, 'LearnerReviewReworkRequested', {
      attemptId: 'wa-001',
      message,
    });

    await openNotificationCenter(page);
    await expect(page.getByRole('heading', { name: /notification center/i })).toBeVisible();
    await expect.poll(async () => await notificationCenterPanel(page).textContent() ?? '').toContain(message);

    expectNoSevereClientIssues(diagnostics, { allowNotificationReconnectNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('expert desktop receives a notification and deep links into the assigned review', async ({ page, request }, testInfo) => {
    if (!testInfo.project.name.includes('expert') || testInfo.project.name.includes('mobile')) {
      test.skip();
    }

    const diagnostics = observePage(page);

    await page.goto('/expert/queue');
    await expect(page.getByRole('main').getByText(/review queue|queue/i).first()).toBeVisible();
    const unreadBefore = await unreadCount(page);

    const { proof, message, reviewRequestId } = await triggerExpertAssignedNotificationProof(request);

    await expect.poll(async () => unreadCount(page)).toBeGreaterThan(unreadBefore);
    await expect
      .poll(async () => {
        const feed = await fetchNotificationFeedForRole(request, 'expert');
        return feed.items.some((item) => item.body === proof.body && item.actionUrl === proof.actionUrl);
      })
      .toBeTruthy();

    await openNotificationCenter(page);
    await refreshNotificationCenter(page);
    await expect.poll(async () => await notificationCenterPanel(page).textContent() ?? '').toContain(reviewRequestId);

    await expect(notificationCenterPanel(page).getByText(message)).toHaveCount(0);
    const expertNotificationButton = notificationItemButton(page, proof);
    await expect(expertNotificationButton).toBeVisible();
    await expect(expertNotificationButton).toContainText(proof.title);
    await expect(expertNotificationButton).toContainText(proof.body);
    await expertNotificationButton.scrollIntoViewIfNeeded();
    await expertNotificationButton.evaluate((element) => {
      (element as HTMLButtonElement).click();
    });
    await expect(page).toHaveURL(new RegExp(`/expert/review/${reviewRequestId}$`));

    expectNoSevereClientIssues(diagnostics, { allowNotificationReconnectNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('admin notification operations page shows governance panels and recent delivery visibility', async ({ page, request }, testInfo) => {
    if (!testInfo.project.name.includes('admin') || testInfo.project.name.includes('mobile')) {
      test.skip();
    }

    const diagnostics = observePage(page);

    await page.goto('/admin/notifications');
    await expect(page.getByRole('heading', { name: /^notifications$/i })).toBeVisible();
    await expect(page.getByText(/global email governance/i)).toBeVisible();
    await expect(page.getByText(/email preview & test send/i)).toBeVisible();
    await expect(page.getByRole('heading', { name: /delivery health/i })).toBeVisible();
    await expect(page.getByText(/policy change audit trail/i)).toBeVisible();

    const { proof, message } = await triggerAdminNotificationProof(request);
    await expect
      .poll(async () => {
        const feed = await fetchNotificationFeedForRole(request, 'admin');
        return feed.items.some((item) => item.body === proof.body && item.actionUrl === proof.actionUrl);
      })
      .toBeTruthy();

    await openNotificationCenter(page);
    await refreshNotificationCenter(page);
    await expect.poll(async () => await notificationCenterPanel(page).textContent() ?? '').toContain(proof.body);
    const adminNotificationButton = notificationItemButton(page, proof);
    await expect(adminNotificationButton).toBeVisible();
    await expect(adminNotificationButton).toContainText(proof.title);
    await expect(adminNotificationButton).toContainText(message);
    await page.getByRole('button', { name: /notifications/i }).click();

    await page.locator('#main-content').getByRole('button', { name: /^refresh$/i }).click();
    await expect(page.locator('#main-content tbody tr').filter({ hasText: proof.eventKey }).first()).toBeVisible();

    expectNoSevereClientIssues(diagnostics, { allowNotificationReconnectNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
