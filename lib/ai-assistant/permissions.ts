/**
 * AI Assistant permission checks — role-based access for AI chat features.
 */

import type { UserRole } from '@/lib/types/auth';
import type { AiAssistantAccess, AssistantRole } from './types';

// ─── Role Mapping ───────────────────────────────────────────────────────────

/**
 * Maps a platform user role to the assistant's capability tier.
 * - admin → full access (all tools, configuration)
 * - expert → limited (review-related tools only)
 * - learner / sponsor → basic (study help only, no mutations)
 */
export function getAssistantRole(userRole: UserRole | null | undefined): AssistantRole {
  switch (userRole) {
    case 'admin':
      return 'admin';
    case 'expert':
      return 'expert';
    case 'learner':
    case 'sponsor':
    default:
      return 'learner';
  }
}

// ─── Access Checks ──────────────────────────────────────────────────────────

/**
 * Returns whether a given user role is permitted to access the AI assistant.
 */
export function canAccessAssistant(role: UserRole | null | undefined): boolean {
  if (!role) return false;
  return role === 'admin' || role === 'expert' || role === 'learner';
}

/** @deprecated Use canAccessAssistant */
export function canAccessAiAssistant(role: UserRole | null | undefined): boolean {
  return canAccessAssistant(role);
}

/**
 * Determine AI Assistant access given a user role.
 * Returns a structured object describing what the user can do.
 */
export function getAiAssistantAccess(role: UserRole | null | undefined): AiAssistantAccess {
  if (!role) {
    return {
      canChat: false,
      canListThreads: false,
      canUseTools: false,
      canConfigureAssistant: false,
    };
  }

  switch (role) {
    case 'admin':
      return {
        canChat: true,
        canListThreads: true,
        canUseTools: true,
        canConfigureAssistant: true,
      };
    case 'expert':
      return {
        canChat: true,
        canListThreads: true,
        canUseTools: true,
        canConfigureAssistant: false,
      };
    case 'learner':
      return {
        canChat: true,
        canListThreads: true,
        canUseTools: false,
        canConfigureAssistant: false,
      };
    case 'sponsor':
    default:
      return {
        canChat: false,
        canListThreads: false,
        canUseTools: false,
        canConfigureAssistant: false,
      };
  }
}

/**
 * Returns the list of tool categories available for the assistant role.
 */
export function getAllowedToolCategories(role: AssistantRole): string[] {
  switch (role) {
    case 'admin':
      return ['study-help', 'review', 'content-management', 'analytics', 'configuration'];
    case 'expert':
      return ['study-help', 'review'];
    case 'learner':
    default:
      return ['study-help'];
  }
}
