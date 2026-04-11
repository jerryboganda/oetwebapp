import { createLearnerMetaLabel, sanitizeLearnerSurfaceMetaItems } from '../learner-surface';

describe('sanitizeLearnerSurfaceMetaItems', () => {
  it('removes empty labels and limits the row to three items', () => {
    const result = sanitizeLearnerSurfaceMetaItems([
      { label: 'Timed flow' },
      { label: '  ' },
      { label: undefined as unknown as string },
      { label: 'Report included' },
      { label: 'Guided setup' },
      { label: 'Hidden extra' },
    ]);

    expect(result).toEqual([
      { label: 'Timed flow' },
      { label: 'Report included' },
      { label: 'Guided setup' },
    ]);
  });

  it('ignores items whose labels are not strings', () => {
    const result = sanitizeLearnerSurfaceMetaItems([
      { label: undefined as unknown as string },
      { label: null as unknown as string },
      { label: 'Valid label' },
    ]);

    expect(result).toEqual([
      { label: 'Valid label' },
    ]);
  });
});

describe('createLearnerMetaLabel', () => {
  it('falls back when the source value is empty', () => {
    expect(createLearnerMetaLabel('', 'Fallback label')).toBe('Fallback label');
    expect(createLearnerMetaLabel('  ', 'Fallback label')).toBe('Fallback label');
    expect(createLearnerMetaLabel(null, 'Fallback label')).toBe('Fallback label');
  });

  it('keeps the source value when it is meaningful', () => {
    expect(createLearnerMetaLabel('Timed flow', 'Fallback label')).toBe('Timed flow');
  });
});
