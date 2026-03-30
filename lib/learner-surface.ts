import type { ElementType, ReactNode } from 'react';

export type LearnerSurfaceCardKind =
  | 'task'
  | 'navigation'
  | 'setup'
  | 'evidence'
  | 'status'
  | 'insight';

export type LearnerSurfaceSourceType =
  | 'backend_task'
  | 'backend_summary'
  | 'frontend_navigation'
  | 'frontend_setup'
  | 'frontend_status'
  | 'frontend_insight';

export type LearnerSurfaceAccent =
  | 'primary'
  | 'navy'
  | 'amber'
  | 'blue'
  | 'indigo'
  | 'purple'
  | 'rose'
  | 'emerald'
  | 'slate';

export interface LearnerSurfaceMetaItem {
  label: string;
  icon?: ElementType;
}

export interface LearnerSurfaceAction {
  label: string;
  href?: string;
  onClick?: () => void;
  variant?: 'primary' | 'secondary' | 'outline' | 'ghost';
}

export interface LearnerSurfaceCardModel {
  kind: LearnerSurfaceCardKind;
  sourceType: LearnerSurfaceSourceType;
  title: string;
  description: string;
  eyebrow?: string;
  eyebrowIcon?: ElementType;
  accent?: LearnerSurfaceAccent;
  metaItems?: LearnerSurfaceMetaItem[];
  primaryAction?: LearnerSurfaceAction;
  secondaryAction?: LearnerSurfaceAction;
  statusLabel?: string;
}

export interface LearnerPageHeroModel {
  title: string;
  description: string;
  eyebrow?: string;
  icon?: ElementType;
  accent?: LearnerSurfaceAccent;
  highlights?: LearnerPageHeroHighlight[];
  aside?: ReactNode;
}

export interface LearnerPageHeroHighlight {
  label: string;
  value: string;
  icon?: ElementType;
}

export function sanitizeLearnerSurfaceMetaItems(items?: LearnerSurfaceMetaItem[]) {
  return (items ?? [])
    .filter((item): item is LearnerSurfaceMetaItem => typeof item?.label === 'string' && item.label.trim().length > 0)
    .slice(0, 3);
}

export function sanitizeLearnerPageHeroHighlights(items?: LearnerPageHeroHighlight[]) {
  return (items ?? [])
    .filter((item): item is LearnerPageHeroHighlight => (
      typeof item?.label === 'string'
      && item.label.trim().length > 0
      && typeof item.value === 'string'
      && item.value.trim().length > 0
    ))
    .slice(0, 3);
}

export function createLearnerMetaLabel(value: string | null | undefined, fallback: string) {
  const normalized = value?.trim();
  return normalized && normalized.length > 0 ? normalized : fallback;
}
