import { renderHook, act } from '@testing-library/react';
import { useListeningAnnotations } from '../use-listening-annotations';

const saveAnnotations = vi.fn().mockResolvedValue(undefined);
const getAnnotations = vi.fn().mockResolvedValue({ annotationsJson: null });

vi.mock('@/lib/listening/v2-api', () => ({
  listeningV2Api: {
    saveAnnotations: (...args: unknown[]) => saveAnnotations(...args),
    getAnnotations: (...args: unknown[]) => getAnnotations(...args),
  },
}));

describe('useListeningAnnotations', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('hydrates from initial payload', () => {
    const initial = JSON.stringify({
      byQuestion: { 'q-1': { stemHighlighted: true, struckOptions: ['A'] } },
    });
    const { result } = renderHook(() =>
      useListeningAnnotations({ attemptId: 'att-1', initialAnnotationsJson: initial }),
    );

    expect(result.current.state.byQuestion['q-1']?.stemHighlighted).toBe(true);
    expect(result.current.state.byQuestion['q-1']?.struckOptions).toEqual(['A']);
    expect(saveAnnotations).not.toHaveBeenCalled();
  });

  it('debounces edits and saves once', async () => {
    const { result } = renderHook(() =>
      useListeningAnnotations({ attemptId: 'att-1', initialAnnotationsJson: null }),
    );

    act(() => {
      result.current.update('q-1', (current) => ({ ...current, stemHighlighted: true }));
    });
    act(() => {
      result.current.update('q-1', (current) => ({ ...current, struckOptions: ['B'] }));
    });

    // Before debounce fires, no save.
    expect(saveAnnotations).not.toHaveBeenCalled();

    // Advance past the 400 ms debounce window.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(500);
    });

    expect(saveAnnotations).toHaveBeenCalledTimes(1);
    const [attemptId, payload] = saveAnnotations.mock.calls[0]!;
    expect(attemptId).toBe('att-1');
    const parsed = JSON.parse(payload as string);
    expect(parsed.byQuestion['q-1'].stemHighlighted).toBe(true);
    expect(parsed.byQuestion['q-1'].struckOptions).toEqual(['B']);
  });

  it('skips save when disabled', async () => {
    const { result } = renderHook(() =>
      useListeningAnnotations({ attemptId: 'att-1', disabled: true }),
    );

    act(() => {
      result.current.update('q-1', (current) => ({ ...current, stemHighlighted: true }));
    });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(500);
    });

    expect(saveAnnotations).not.toHaveBeenCalled();
  });

  it('flushes pending edits on demand', async () => {
    const { result } = renderHook(() =>
      useListeningAnnotations({ attemptId: 'att-1', initialAnnotationsJson: null }),
    );

    act(() => {
      result.current.update('q-1', (current) => ({ ...current, stemHighlighted: true }));
    });

    await act(async () => {
      await result.current.flush();
    });

    expect(saveAnnotations).toHaveBeenCalledTimes(1);
  });

  it('reloads server state on reload()', async () => {
    getAnnotations.mockResolvedValueOnce({
      annotationsJson: JSON.stringify({
        byQuestion: { 'q-7': { struckOptions: ['C'] } },
      }),
    });
    const { result } = renderHook(() =>
      useListeningAnnotations({ attemptId: 'att-1', initialAnnotationsJson: null }),
    );

    await act(async () => {
      await result.current.reload();
    });

    expect(getAnnotations).toHaveBeenCalledWith('att-1');
    expect(result.current.state.byQuestion['q-7']?.struckOptions).toEqual(['C']);
  });

  it('removes question entry when annotation becomes empty', async () => {
    const initial = JSON.stringify({
      byQuestion: { 'q-1': { stemHighlighted: true } },
    });
    const { result } = renderHook(() =>
      useListeningAnnotations({ attemptId: 'att-1', initialAnnotationsJson: initial }),
    );

    act(() => {
      result.current.update('q-1', () => ({ stemHighlighted: false }));
    });

    expect(result.current.state.byQuestion['q-1']).toBeUndefined();
  });

  it('flags oversized payloads locally before calling the server', async () => {
    const { result } = renderHook(() =>
      useListeningAnnotations({ attemptId: 'att-1', initialAnnotationsJson: null }),
    );

    act(() => {
      result.current.update('q-1', () => ({
        highlights: [new Array(80_000).fill('x').join('')],
      }));
    });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(500);
    });

    expect(result.current.tooLarge).toBe(true);
    expect(saveAnnotations).not.toHaveBeenCalled();
  });
});
