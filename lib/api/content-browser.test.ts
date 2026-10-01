const { apiRequest } = vi.hoisted(() => ({ apiRequest: vi.fn() }));

vi.mock('./client', () => ({ apiRequest }));

import { fetchContentPackage, fetchContentPackages } from './content-browser';

describe('content packages client', () => {
  beforeEach(() => apiRequest.mockReset());

  it('turns the stored comparisonFeaturesJson column into the comparisonFeatures array', async () => {
    apiRequest.mockResolvedValue({
      items: [
        { id: 'p1', comparisonFeaturesJson: '["Full mocks","Tutor review"]' },
        { id: 'p2', comparisonFeaturesJson: 'not json' },
        { id: 'p3' },
      ],
      total: 3,
    });

    const response = await fetchContentPackages();

    const items = (response.items ?? []) as Array<{ comparisonFeatures: string[] }>;
    expect(items.map((pkg) => pkg.comparisonFeatures)).toEqual([
      ['Full mocks', 'Tutor review'],
      [],
      [],
    ]);
    expect(response.total).toBe(3);
  });

  it('keeps an array the API already sends, and parses a single package', async () => {
    apiRequest.mockResolvedValueOnce({ id: 'p1', comparisonFeatures: ['Ready'] });
    expect((await fetchContentPackage('p1'))?.comparisonFeatures).toEqual(['Ready']);

    apiRequest.mockResolvedValueOnce({ id: 'p2', comparisonFeaturesJson: '["Parsed"]' });
    expect((await fetchContentPackage('p2'))?.comparisonFeatures).toEqual(['Parsed']);
  });
});
