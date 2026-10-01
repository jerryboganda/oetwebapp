import { renderHook } from '@testing-library/react';

// The hook caches each key's baseline per page session, so every test loads a fresh module.
async function loadHook() {
  vi.resetModules();
  return (await import('../use-increase-since-last-visit')).useIncreaseSinceLastVisit;
}

describe('useIncreaseSinceLastVisit', () => {
  beforeEach(() => window.localStorage.clear());

  it('never claims an increase on a first visit, and records the value', async () => {
    const useIncrease = await loadHook();
    const { result } = renderHook(() => useIncrease('streak', 4));

    expect(result.current).toBe(false);
    expect(window.localStorage.getItem('streak')).toBe('4');
  });

  it('reports a real increase over the stored value, but not an equal or lower one', async () => {
    window.localStorage.setItem('streak', '3');
    let useIncrease = await loadHook();
    expect(renderHook(() => useIncrease('streak', 4)).result.current).toBe(true);

    window.localStorage.setItem('streak', '4');
    useIncrease = await loadHook();
    expect(renderHook(() => useIncrease('streak', 4)).result.current).toBe(false);
    expect(renderHook(() => useIncrease('streak', 2)).result.current).toBe(false);
  });

  it('gives a later consumer the same session baseline after an earlier one recorded the new value', async () => {
    window.localStorage.setItem('streak', '3');
    const useIncrease = await loadHook();

    renderHook(() => useIncrease('streak', 5)); // e.g. the header chip, mounted first
    expect(window.localStorage.getItem('streak')).toBe('5');
    expect(renderHook(() => useIncrease('streak', 5)).result.current).toBe(true); // the dashboard card, later
  });

  it('waits for a real value before recording anything', async () => {
    const useIncrease = await loadHook();
    const { result } = renderHook(() => useIncrease('level', null));

    expect(result.current).toBe(false);
    expect(window.localStorage.getItem('level')).toBeNull();
  });
});
