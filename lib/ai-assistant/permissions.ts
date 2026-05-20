// Client-side permission helpers for the admin-only AI Assistant.
// Mirrors backend AiAssistantAuthorizationService — keys must match the
// constants in lib/admin-permissions.ts.

import { AdminPermission, hasPermission } from '@/lib/admin-permissions';

// Minimal shape — narrowed at call sites to avoid coupling here.
export interface AiAssistantCurrentUser {
  userId?: string;
  role?: string; // 'learner' | 'expert' | 'admin' | 'sponsor'
  permissions?: ReadonlyArray<string>;
}

function isAdmin(user: AiAssistantCurrentUser | null | undefined): boolean {
  return user?.role === 'admin';
}

export function hasAiAssistantAccess(user: AiAssistantCurrentUser | null | undefined): boolean {
  if (!isAdmin(user)) return false;
  return hasPermission(user!.permissions ? [...user!.permissions] : null, AdminPermission.UseAiAssistant);
}

export function hasManageAiAssistant(user: AiAssistantCurrentUser | null | undefined): boolean {
  if (!isAdmin(user)) return false;
  return hasPermission(user!.permissions ? [...user!.permissions] : null, AdminPermission.ManageAiAssistant);
}

export function hasUseUnrestricted(user: AiAssistantCurrentUser | null | undefined): boolean {
  if (!isAdmin(user)) return false;
  return hasPermission(user!.permissions ? [...user!.permissions] : null, AdminPermission.UseAiAssistantUnrestricted);
}
