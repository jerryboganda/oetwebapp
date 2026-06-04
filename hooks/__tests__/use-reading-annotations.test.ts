import { renderHook, act } from '@testing-library/react';
import { useReadingAnnotations } from '../use-reading-annotations';

const saveAnnotations = vi.fn().mockResolvedValue(undefined);
const getAnnotations = vi.fn().mockResolvedValue({ annotationsJson: null });

vi.mock('@/lib/reading-authoring-api', () => ({
  readingAnnotationsApi: {
    saveAnnotations: (...args: unknown[]) => saveAnnotations(...args),
    getAnnotations: (...args: unknown[]) => getAnnotations(...args),
  },
}));

describe('useReadingAnnotations', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('debounces a strikethrough edit and PUTs it once via the reading adapter', async () => {
    const { result } = renderHook(() =>
      useReadingAnnotations({ attemptId: 'att-r', initialAnnotationsJson: null }),
    );

    act(() => {
      result.current.update('q-9', (current) => ({ ...current, struckOptions: ['A'] }));
    });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(500);
    });

    expect(saveAnnotations).toHaveBeenCalledTimes(1);
    const [attemptId, payload] = saveAnnotations.mock.calls[0]!;
    expect(attemptId).toBe('att-r');
    expect(JSON.parse(payload as string).byQuestion['q-9'].struckOptions).toEqual(['A']);
  });

  it('hydrates rule-out marks from the initial attempt payload', () => {
    const initial = JSON.stringify({ byQuestion: { 'q-2': { struckOptions: ['B', 'C'] } } });
    const { result } = renderHook(() =>
      useReadingAnnotations({ attemptId: 'att-r', initialAnnotationsJson: initial }),
    );

    expect(result.current.state.byQuestion['q-2']?.struckOptions).toEqual(['B', 'C']);
  });
});
