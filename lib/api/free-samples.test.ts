const { mockGet } = vi.hoisted(() => ({ mockGet: vi.fn() }));

vi.mock('@/lib/api', () => ({ apiClient: { get: mockGet } }));

import { listFreeSamples } from './free-samples';

describe('listFreeSamples', () => {
  beforeEach(() => vi.clearAllMocks());

  it('asks the server what is on offer for the subtest — a plain GET, no free flag anywhere', async () => {
    const rows = [{
      professionId: 'medicine',
      contentId: 'c1',
      state: 'retry_available',
      route: '/writing/submissions/sub-1/revise',
      limit: 2,
      successfulCount: 1,
      remaining: 1,
      lastResultRoute: '/writing/submissions/sub-1/results',
      lastSubmissionId: 'sub-1',
    }];
    mockGet.mockResolvedValue(rows);

    await expect(listFreeSamples('writing')).resolves.toEqual(rows);
    expect(mockGet).toHaveBeenCalledTimes(1);
    expect(mockGet).toHaveBeenCalledWith('/v1/free-samples/writing');

    await listFreeSamples('speaking');
    expect(mockGet).toHaveBeenLastCalledWith('/v1/free-samples/speaking');
  });
});
