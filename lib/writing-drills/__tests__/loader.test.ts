/**
 * Loader integration test — exercises zod validation against the seed JSON.
 */

import { describe, it, expect } from 'vitest';
import { listDrills, getDrill, getDrillsByType, DrillNotFoundError } from '../loader';
import type { DrillType, Profession } from '../types';

const PROFESSIONS: Profession[] = [
  'medicine',
  'nursing',
  'pharmacy',
  'physiotherapy',
  'dentistry',
  'occupational_therapy',
  'radiography',
  'podiatry',
  'dietetics',
  'optometry',
  'speech_pathology',
  'veterinary',
];

const DRILL_TYPES: DrillType[] = ['relevance', 'opening', 'ordering', 'expansion', 'tone', 'abbreviation'];

describe('writing-drills loader', () => {
  it('loads and validates all seed drills', () => {
    const all = listDrills();
    expect(all.length).toBeGreaterThanOrEqual(PROFESSIONS.length * DRILL_TYPES.length);
    const types = new Set(all.map((d) => d.type));
    expect(types.size).toBe(DRILL_TYPES.length);
    for (const profession of PROFESSIONS) {
      for (const type of DRILL_TYPES) {
        expect(all.some((drill) => drill.profession === profession && drill.type === type), `${profession}/${type}`).toBe(true);
      }
    }
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
