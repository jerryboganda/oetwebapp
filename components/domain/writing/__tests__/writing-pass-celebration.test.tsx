import { render } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AuthContext, type AuthContextValue } from '@/contexts/auth-context';
import { queryKeys } from '@/lib/query/keys';
import { WritingPassCelebration } from '../writing-pass-celebration';

vi.mock('@/lib/api', () => ({ fetchUserProfile: vi.fn() }));

const particles = () => document.querySelectorAll('.celebration-particle');

function renderFor(profile: { targetScores: { Writing: number | null }; targetCountry: string }, score: number) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } });
  queryClient.setQueryData(queryKeys.profile.self('learner-1'), profile);
  const auth = { user: { userId: 'learner-1' } } as unknown as AuthContextValue;
  return render(
    <QueryClientProvider client={queryClient}>
      <AuthContext.Provider value={auth}>
        <div className="relative">
          <WritingPassCelebration score={score} onceKey={`result-${Math.random()}`} />
        </div>
      </AuthContext.Provider>
    </QueryClientProvider>,
  );
}

describe('WritingPassCelebration', () => {
  beforeEach(() => window.sessionStorage.clear());

  it("celebrates against the learner's own Writing target first", () => {
    renderFor({ targetScores: { Writing: 380 }, targetCountry: 'US' }, 360);
    expect(particles()).toHaveLength(0); // above the US C+ pass mark, but below their own target

    renderFor({ targetScores: { Writing: 380 }, targetCountry: 'US' }, 390);
    expect(particles()).toHaveLength(20);
  });

  it("falls back to the destination country's pass mark (Writing is country-aware)", () => {
    renderFor({ targetScores: { Writing: null }, targetCountry: 'GB' }, 320);
    expect(particles()).toHaveLength(0); // GB needs Grade B (350)

    renderFor({ targetScores: { Writing: null }, targetCountry: 'US' }, 320);
    expect(particles()).toHaveLength(20); // US accepts C+ (300)
  });

  it('assumes nothing without a target or a resolvable country', () => {
    renderFor({ targetScores: { Writing: null }, targetCountry: '' }, 480);
    expect(particles()).toHaveLength(0);
  });
});
