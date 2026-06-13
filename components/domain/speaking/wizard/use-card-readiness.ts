/**
 * Publish-readiness for a role-play card, consolidating the rules that were
 * previously scattered across the candidate editor, the interlocutor editor,
 * and the card list page.
 *
 * The only HARD rule the backend enforces on publish is "an interlocutor
 * script exists" (AdminService.SpeakingRolePlayCards.PublishSpeakingRolePlayCardAsync).
 * The rest are SOFT quality checks surfaced as warnings so the operator knows
 * what to finish, without blocking a technically-valid publish.
 */

import {
  CARD_DRAFT_SEED_SETTING,
  CARD_DRAFT_SEED_ROLE,
  CARD_DRAFT_SEED_TITLE,
} from './card-wizard-config';
import type { RolePlayCardDetail } from '@/lib/api/speaking-role-play-cards';

export interface CardReadinessItem {
  label: string;
  ok: boolean;
  /** Hard rules block publish; soft rules are advisory. */
  hard: boolean;
}

export interface CardReadiness {
  items: CardReadinessItem[];
  /** True when every hard rule passes — i.e. the card can be published. */
  hardReady: boolean;
}

function filled(value: string | null | undefined, seed?: string): boolean {
  const v = (value ?? '').trim();
  if (!v) return false;
  if (seed && v === seed) return false;
  return true;
}

export function getCardReadiness(card: RolePlayCardDetail): CardReadiness {
  const taskCount = (card.tasks ?? []).filter((t) => t.trim().length > 0).length;

  const items: CardReadinessItem[] = [
    { label: 'Interlocutor (hidden) script added', ok: Boolean(card.hasInterlocutorScript), hard: true },
    { label: 'Scenario title set', ok: filled(card.scenarioTitle, CARD_DRAFT_SEED_TITLE), hard: false },
    { label: 'Setting set', ok: filled(card.setting, CARD_DRAFT_SEED_SETTING), hard: false },
    { label: 'Candidate role set', ok: filled(card.candidateRole, CARD_DRAFT_SEED_ROLE), hard: false },
    { label: 'Background written', ok: filled(card.background), hard: false },
    { label: 'At least 3 task bullets', ok: taskCount >= 3, hard: false },
    { label: 'At least one scoring criterion selected', ok: (card.criteriaFocus ?? []).length >= 1, hard: false },
  ];

  const hardReady = items.filter((i) => i.hard).every((i) => i.ok);
  return { items, hardReady };
}
