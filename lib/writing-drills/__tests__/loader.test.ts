/**
 * Loader integration test — exercises zod validation against the seed JSON.
 */

import { describe, it, expect } from 'vitest';
import { listDrills, getDrill, getDrillsByType, DrillNotFoundError } from '../loader';

describe('writing-drills loader', () => {
  it('loads and validates all seed drills', () => {
    const all = listDrills();
    expect(all.length).toBeGreaterThanOrEqual(6);
    const types = new Set(all.map((d) => d.type));
    expect(types.size).toBeGreaterThanOrEqual(6);
  });

  it('filters by type', () => {
    const relevance = listDrills({ type: 'relevance' });
    expect(relevance.length).toBeGreaterThan(0);
    expect(relevance.every((d) => d.type === 'relevance')).toBe(true);
  });

  it('getDrill returns the full drill', () => {
    const drill = getDrill('writing-drill-relevance-001');
    expect(drill.type).toBe('relevance');
    expect(drill.id).toBe('writing-drill-relevance-001');
  });

  it('getDrill throws DrillNotFoundError for unknown id', () => {
    expect(() => getDrill('nope')).toThrow(DrillNotFoundError);
  });

  it('getDrillsByType returns full drill objects', () => {
    const opening = getDrillsByType('opening', 'medicine');
    expect(opening.length).toBeGreaterThan(0);
    expect(opening[0].type).toBe('opening');
  });
});
