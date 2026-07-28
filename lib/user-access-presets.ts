/**
 * Quick Grant preset definitions — pure data consumed by QuickGrantModal.
 * See docs/superpowers/specs/2026-07-29-quick-grant-access-presets-design.md.
 */

import { BookOpen, FolderOpen, Video, Sparkles } from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import { MODULE_KEYS, type ModuleKey, type UserAccessModuleOverride } from './user-access';

export type PresetId = 'recallsOnly' | 'materialsOnly' | 'videosOnly' | 'fullAccess';

export type PresetScopeField = 'recallSetCodes' | 'materialFolderIds' | 'videoIds' | null;

export interface AccessPreset {
  id: PresetId;
  label: string;
  icon: LucideIcon;
  /** One-line summary shown on the Quick Grant confirm step. */
  description: string;
  moduleOverrides: Record<ModuleKey, boolean>;
  scopeField: PresetScopeField;
}

export const ACCESS_PRESETS: AccessPreset[] = [
  {
    id: 'recallsOnly',
    label: 'Recalls Only',
    icon: BookOpen,
    description: 'Recalls only — every other module is turned off for this learner.',
    moduleOverrides: { Recalls: true, MaterialsLibrary: false, VideoLibrary: false, Mocks: false },
    scopeField: 'recallSetCodes',
  },
  {
    id: 'materialsOnly',
    label: 'Materials Only',
    icon: FolderOpen,
    description: 'Materials Library only — every other module is turned off for this learner.',
    moduleOverrides: { Recalls: false, MaterialsLibrary: true, VideoLibrary: false, Mocks: false },
    scopeField: 'materialFolderIds',
  },
  {
    id: 'videosOnly',
    label: 'Videos Only',
    icon: Video,
    description: 'Video Library only — every other module is turned off for this learner.',
    moduleOverrides: { Recalls: false, MaterialsLibrary: false, VideoLibrary: true, Mocks: false },
    scopeField: 'videoIds',
  },
  {
    id: 'fullAccess',
    label: 'Full Access',
    icon: Sparkles,
    description: 'Every module turned on for this learner, regardless of what their plan alone grants.',
    moduleOverrides: { Recalls: true, MaterialsLibrary: true, VideoLibrary: true, Mocks: true },
    scopeField: null,
  },
];

/** Expands a preset's module map into the explicit 4-key override array the API expects. */
export function buildModuleOverrides(preset: AccessPreset): UserAccessModuleOverride[] {
  return MODULE_KEYS.map((moduleKey) => ({
    moduleKey,
    enabled: preset.moduleOverrides[moduleKey],
  }));
}
