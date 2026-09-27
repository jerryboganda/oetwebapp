import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const { mockGetMe } = vi.hoisted(() => ({ mockGetMe: vi.fn() }));

vi.mock('../api', () => ({
  getMe: (...args: unknown[]) => mockGetMe(...args),
}));

import { adminNavItems } from '@/lib/admin-navigation';
import { fetchIsOwnerAgentOwner, filterOwnerOnlyNavItems, resetOwnerFlagCacheForTests } from '../owner-flag';

describe('owner-agent owner flag', () => {
  beforeEach(() => {
    mockGetMe.mockReset();
    resetOwnerFlagCacheForTests();
  });

  afterEach(() => {
    resetOwnerFlagCacheForTests();
  });

  it('hides owner-only nav entries unless the viewer is the owner', () => {
    const hidden = filterOwnerOnlyNavItems(adminNavItems, false).map((item) => item.href);
    const shown = filterOwnerOnlyNavItems(adminNavItems, true).map((item) => item.href);
    expect(hidden).not.toContain('/admin/agent-console');
    expect(shown).toContain('/admin/agent-console');
    expect(shown).toHaveLength(adminNavItems.length);
  });

  it('asks /me once per user and caches the answer in memory', async () => {
    mockGetMe.mockResolvedValue({ isOwner: true, unlocked: false, featureEnabled: true });
    await expect(fetchIsOwnerAgentOwner('owner-1')).resolves.toBe(true);
    await expect(fetchIsOwnerAgentOwner('owner-1')).resolves.toBe(true);
    expect(mockGetMe).toHaveBeenCalledTimes(1);
  });

  it('fails closed and retries later when /me errors', async () => {
    mockGetMe.mockRejectedValueOnce(new Error('503'));
    await expect(fetchIsOwnerAgentOwner('admin-2')).resolves.toBe(false);
    mockGetMe.mockResolvedValueOnce({ isOwner: false });
    await expect(fetchIsOwnerAgentOwner('admin-2')).resolves.toBe(false);
    expect(mockGetMe).toHaveBeenCalledTimes(2);
  });
});
