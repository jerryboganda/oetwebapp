import { getAiAssistantAccess, canAccessAiAssistant } from '../permissions';
import type { UserRole } from '@/lib/types/auth';

describe('AI Assistant permissions', () => {
  describe('Admin role', () => {
    it('has full access to all AI assistant features', () => {
      const access = getAiAssistantAccess('admin');
      expect(access.canChat).toBe(true);
      expect(access.canListThreads).toBe(true);
      expect(access.canUseTools).toBe(true);
      expect(access.canConfigureAssistant).toBe(true);
    });

    it('canAccessAiAssistant returns true', () => {
      expect(canAccessAiAssistant('admin')).toBe(true);
    });
  });

  describe('Expert role', () => {
    it('has chat, threads, and tools but not configuration', () => {
      const access = getAiAssistantAccess('expert');
      expect(access.canChat).toBe(true);
      expect(access.canListThreads).toBe(true);
      expect(access.canUseTools).toBe(true);
      expect(access.canConfigureAssistant).toBe(false);
    });

    it('canAccessAiAssistant returns true', () => {
      expect(canAccessAiAssistant('expert')).toBe(true);
    });
  });

  describe('Learner role', () => {
    it('has basic chat and threads but no tools or config', () => {
      const access = getAiAssistantAccess('learner');
      expect(access.canChat).toBe(true);
      expect(access.canListThreads).toBe(true);
      expect(access.canUseTools).toBe(false);
      expect(access.canConfigureAssistant).toBe(false);
    });

    it('canAccessAiAssistant returns true', () => {
      expect(canAccessAiAssistant('learner')).toBe(true);
    });
  });

  describe('Sponsor role', () => {
    it('has no AI assistant access', () => {
      const access = getAiAssistantAccess('sponsor');
      expect(access.canChat).toBe(false);
      expect(access.canListThreads).toBe(false);
      expect(access.canUseTools).toBe(false);
      expect(access.canConfigureAssistant).toBe(false);
    });

    it('canAccessAiAssistant returns false', () => {
      expect(canAccessAiAssistant('sponsor')).toBe(false);
    });
  });

  describe('Unauthenticated / null role', () => {
    it('returns no access for null role', () => {
      const access = getAiAssistantAccess(null);
      expect(access.canChat).toBe(false);
      expect(access.canListThreads).toBe(false);
      expect(access.canUseTools).toBe(false);
      expect(access.canConfigureAssistant).toBe(false);
    });

    it('returns no access for undefined role', () => {
      const access = getAiAssistantAccess(undefined);
      expect(access.canChat).toBe(false);
      expect(access.canConfigureAssistant).toBe(false);
    });

    it('canAccessAiAssistant returns false for null', () => {
      expect(canAccessAiAssistant(null)).toBe(false);
    });

    it('canAccessAiAssistant returns false for undefined', () => {
      expect(canAccessAiAssistant(undefined)).toBe(false);
    });
  });

  describe('Edge cases', () => {
    it('all valid roles return a complete access object', () => {
      const roles: UserRole[] = ['admin', 'expert', 'learner', 'sponsor'];
      for (const role of roles) {
        const access = getAiAssistantAccess(role);
        expect(access).toHaveProperty('canChat');
        expect(access).toHaveProperty('canListThreads');
        expect(access).toHaveProperty('canUseTools');
        expect(access).toHaveProperty('canConfigureAssistant');
      }
    });

    it('only admin can configure the assistant', () => {
      const roles: UserRole[] = ['admin', 'expert', 'learner', 'sponsor'];
      const configRoles = roles.filter((r) => getAiAssistantAccess(r).canConfigureAssistant);
      expect(configRoles).toEqual(['admin']);
    });
  });
});
