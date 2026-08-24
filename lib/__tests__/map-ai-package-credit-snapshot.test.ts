import { mapAiPackageCreditSnapshot } from '../map-ai-package-credit-snapshot';

describe('mapAiPackageCreditSnapshot', () => {
  it('keeps OET Mastery writing and speaking unlimited flags from the API', () => {
    const snapshot = mapAiPackageCreditSnapshot({
      userId: 'learner-1',
      sharedCredits: 0,
      flexibleCredits: 0,
      writingOnlyCredits: 0,
      speakingOnlyCredits: 0,
      listeningTestsRemaining: null,
      readingTestsRemaining: null,
      mockExamsRemaining: 0,
      expiredBecausePassed: false,
      transactions: [],
      creditsGranted: 0,
      creditsUsed: 0,
      creditsRemaining: 0,
      writingUnlimited: true,
      speakingUnlimited: true,
    });

    expect(snapshot.writingUnlimited).toBe(true);
    expect(snapshot.speakingUnlimited).toBe(true);
    expect(snapshot.listeningTestsRemaining).toBeNull();
    expect(snapshot.readingTestsRemaining).toBeNull();
  });
});
