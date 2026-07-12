'use client';

import { Checkbox } from '@/components/ui/form-controls';
import { MODULE_KEYS, MODULE_LABELS, type ModuleKey, type UserAccessModuleOverride } from '@/lib/user-access';

interface ModuleTogglesProps {
  overrides: UserAccessModuleOverride[];
  onChange: (next: UserAccessModuleOverride[]) => void;
  disabled?: boolean;
}

function isEnabled(overrides: UserAccessModuleOverride[], moduleKey: ModuleKey): boolean {
  return overrides.find((o) => o.moduleKey === moduleKey)?.enabled ?? false;
}

/** Four checkboxes bound to `UserAccess.moduleOverrides` — Recalls, Materials Library, Videos, Mocks. */
export function ModuleToggles({ overrides, onChange, disabled }: ModuleTogglesProps) {
  function toggle(moduleKey: ModuleKey) {
    const enabled = !isEnabled(overrides, moduleKey);
    const others = overrides.filter((o) => o.moduleKey !== moduleKey);
    onChange([...others, { moduleKey, enabled }]);
  }

  return (
    <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
      {MODULE_KEYS.map((moduleKey) => (
        <Checkbox
          key={moduleKey}
          label={MODULE_LABELS[moduleKey]}
          checked={isEnabled(overrides, moduleKey)}
          onChange={() => toggle(moduleKey)}
          disabled={disabled}
        />
      ))}
    </div>
  );
}
