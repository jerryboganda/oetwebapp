import { expect, test, type Locator, type Page } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from './fixtures/diagnostics';
import { installFakeRecordingMedia } from './fixtures/media';

// 23 Sep 2026 owner requirement (PDF §6/§7): every Speaking screen works at
// 360–430 CSS px in the phone browser / app webview — the primary control is
// inside the viewport and not covered by the learner bottom nav, nothing
// overflows horizontally, and the role card is readable without zoom.
//
// Hermetic: the Speaking API calls under test are route-mocked, so the spec
// needs no seeded session. Only the learner auth state comes from setup.

const VIEWPORTS = [
  { width: 360, height: 780 },
  { width: 390, height: 844 },
  { width: 430, height: 932 },
] as const;

const SESSION_ID = 'e2e-mobile-session';
const CARD_ID = 'rpc-e2e-mobile';

const CARD = {
  cardId: CARD_ID,
  professionId: 'medicine',
  scenarioTitle: 'Post-operative wound review with a worried patient who has many questions',
  setting: 'Surgical outpatient clinic',
  candidateRole: 'Doctor',
  interlocutorRole: 'Patient',
  patientName: 'Mr Alexander Fitzgerald-Montgomery',
  patientAge: '67',
  background: 'Two weeks after an elective hernia repair. The wound is slightly red and the patient is worried about infection and returning to work as a removals supervisor.',
  tasks: [
    'Find out what is worrying the patient about the wound',
    'Explain the findings and that this looks like normal healing',
    'Advise on warning signs, wound care and a gradual return to heavy lifting',
  ],
  allowedNotes: false,
  prepTimeSeconds: 180,
  rolePlayTimeSeconds: 300,
  difficulty: 'core',
  criteriaFocus: [],
  disclaimer: 'Practice estimate only. This is not an official OET score or result.',
};

async function mockSpeakingApi(page: Page) {
  await page.route(`**/v1/speaking/sessions/${SESSION_ID}`, (route) => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      sessionId: SESSION_ID,
      state: 'Active',
      mode: 'ai_self_practice',
      consentVersion: 'recording.v1',
      consentAccepted: true,
      liveVoiceAvailable: false,
      isFreeSample: false,
      prepStartedAt: new Date().toISOString(),
      prepEndsAt: new Date().toISOString(),
      rolePlayStartedAt: new Date().toISOString(),
      rolePlayEndsAt: new Date(Date.now() + 300_000).toISOString(),
      endedAt: null,
      submittedAt: null,
      card: CARD,
    }),
  }));
  await page.route(`**/v1/speaking/sessions/${SESSION_ID}/clock`, (route) => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({ stage: 'active', roleplayIndex: 0, serverNow: new Date().toISOString(), secondsRemaining: 290, expired: false, canAdvanceTo: ['finished'] }),
  }));
  await page.route(`**/v1/speaking/role-play-cards/${CARD_ID}`, (route) => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(CARD),
  }));
}

async function expectNoHorizontalOverflow(page: Page, label: string) {
  const overflow = await page.evaluate(() => ({
    documentWidth: document.documentElement.scrollWidth,
    viewportWidth: window.innerWidth,
  }));
  expect(overflow.documentWidth, `${label} overflows horizontally: ${JSON.stringify(overflow)}`)
    .toBeLessThanOrEqual(overflow.viewportWidth + 1);
}

/** The control is inside the viewport and is the top-most element at its centre (no nav/overlay on top). */
async function expectReachable(page: Page, control: Locator, label: string) {
  await control.scrollIntoViewIfNeeded();
  const box = await control.boundingBox();
  const viewport = page.viewportSize();
  expect(box, `${label} has no box`).not.toBeNull();
  expect(viewport).not.toBeNull();
  if (!box || !viewport) return;
  expect(box.x, `${label} starts off-screen`).toBeGreaterThanOrEqual(0);
  expect(box.x + box.width, `${label} ends off-screen`).toBeLessThanOrEqual(viewport.width + 1);
  expect(box.y + box.height, `${label} is below the fold`).toBeLessThanOrEqual(viewport.height + 1);
  const onTop = await control.evaluate((element) => {
    const rect = element.getBoundingClientRect();
    const hit = document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2);
    return Boolean(hit && (hit === element || element.contains(hit)));
  });
  expect(onTop, `${label} is covered by another element (bottom nav / overlay)`).toBe(true);
}

test.describe('Speaking mobile layout @learner @speaking @responsive', () => {
  for (const viewport of VIEWPORTS) {
    test(`active role-play fits ${viewport.width}px: card visible, one reachable Finish & submit`, async ({ page }, testInfo) => {
      if (!testInfo.project.name.includes('learner')) test.skip();

      const diagnostics = observePage(page);
      await page.setViewportSize(viewport);
      await installFakeRecordingMedia(page);
      await mockSpeakingApi(page);

      try {
        await page.goto(`/speaking/sessions/${SESSION_ID}`, { waitUntil: 'domcontentloaded' });

        const card = page.getByTestId('speaking-role-card');
        await expect(card).toBeVisible({ timeout: 30_000 });
        const cardBox = await card.boundingBox();
        expect(cardBox && cardBox.width).toBeLessThanOrEqual(viewport.width);

        await expect(page.getByTestId('speaking-mic-indicator')).toHaveCount(1);
        const finish = page.getByRole('button', { name: 'Finish & submit' });
        await expect(finish).toHaveCount(1);
        await expectReachable(page, finish, `Finish & submit @${viewport.width}`);
        await expectNoHorizontalOverflow(page, `active role-play @${viewport.width}`);

        expectNoSevereClientIssues(diagnostics, {
          allowNextDevNoise: true,
          allowAuthRedirectNoise: true,
          allowNotificationReconnectNoise: true,
          allowMockedBackendNoise: true,
        });
      } finally {
        diagnostics.detach();
        await attachDiagnostics(testInfo, diagnostics);
      }
    });

    test(`role-play entry fits ${viewport.width}px: rules + consent start control above the bottom nav`, async ({ page }, testInfo) => {
      if (!testInfo.project.name.includes('learner')) test.skip();

      const diagnostics = observePage(page);
      await page.setViewportSize(viewport);
      await mockSpeakingApi(page);

      try {
        await page.goto(`/speaking/roleplay/${CARD_ID}`, { waitUntil: 'domcontentloaded' });

        await expect(page.getByTestId('speaking-role-card')).toBeVisible({ timeout: 30_000 });
        await expect(page.getByTestId('speaking-rules-consent')).toBeVisible();
        await expect(page.getByRole('timer')).toHaveCount(0);

        await page.getByRole('checkbox', { name: /i have read the rules/i }).check();
        await expectReachable(page, page.getByRole('button', { name: 'Start preparation' }), `Start preparation @${viewport.width}`);
        await expectNoHorizontalOverflow(page, `role-play entry @${viewport.width}`);

        expectNoSevereClientIssues(diagnostics, {
          allowNextDevNoise: true,
          allowAuthRedirectNoise: true,
          allowNotificationReconnectNoise: true,
          allowMockedBackendNoise: true,
        });
      } finally {
        diagnostics.detach();
        await attachDiagnostics(testInfo, diagnostics);
      }
    });
  }
});
