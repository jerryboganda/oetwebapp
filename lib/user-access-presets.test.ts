import { describe, expect, it } from 'vitest';
import { MODULE_KEYS } from './user-access';
import { ACCESS_PRESETS, buildModuleOverrides } from './user-access-presets';

describe('ACCESS_PRESETS', () => {
  it('defines exactly the four expected presets', () => {
    expect(ACCESS_PRESETS.map((preset) => preset.id).sort()).toEqual(
      ['fullAccess', 'materialsOnly', 'recallsOnly', 'videosOnly'].sort(),
    );
  });

  it('sets a boolean for every module key on every preset', () => {
    for (const preset of ACCESS_PRESETS) {
      expect(Object.keys(preset.moduleOverrides).sort()).toEqual([...MODULE_KEYS].sort());
      for (const key of MODULE_KEYS) {
        expect(typeof preset.moduleOverrides[key]).toBe('boolean');
      }
    }
  });

  it('recallsOnly enables only Recalls and scopes by recall set', () => {
    const preset = ACCESS_PRESETS.find((p) => p.id === 'recallsOnly')!;
    expect(preset.moduleOverrides).toEqual({
      Recalls: true,
      MaterialsLibrary: false,
      VideoLibrary: false,
      Mocks: false,
    });
    expect(preset.scopeField).toBe('recallSetCodes');
  });

  it('materialsOnly enables only MaterialsLibrary and scopes by folder', () => {
    const preset = ACCESS_PRESETS.find((p) => p.id === 'materialsOnly')!;
    expect(preset.moduleOverrides).toEqual({
      Recalls: false,
      MaterialsLibrary: true,
      VideoLibrary: false,
      Mocks: false,
    });
    expect(preset.scopeField).toBe('materialFolderIds');
  });

  it('videosOnly enables only VideoLibrary and scopes by video', () => {
    const preset = ACCESS_PRESETS.find((p) => p.id === 'videosOnly')!;
    expect(preset.moduleOverrides).toEqual({
      Recalls: false,
      MaterialsLibrary: false,
      VideoLibrary: true,
      Mocks: false,
    });
    expect(preset.scopeField).toBe('videoIds');
  });

  it('fullAccess enables every module and has no scope field', () => {
    const preset = ACCESS_PRESETS.find((p) => p.id === 'fullAccess')!;
    expect(preset.moduleOverrides).toEqual({
      Recalls: true,
      MaterialsLibrary: true,
      VideoLibrary: true,
      Mocks: true,
    });
    expect(preset.scopeField).toBeNull();
  });
});

describe('buildModuleOverrides', () => {
  it('expands a preset into the explicit 4-key API shape', () => {
    const preset = ACCESS_PRESETS.find((p) => p.id === 'recallsOnly')!;
    const overrides = buildModuleOverrides(preset);
    expect(overrides).toHaveLength(MODULE_KEYS.length);
    expect(overrides).toEqual(
      expect.arrayContaining([
        { moduleKey: 'Recalls', enabled: true },
        { moduleKey: 'MaterialsLibrary', enabled: false },
        { moduleKey: 'VideoLibrary', enabled: false },
        { moduleKey: 'Mocks', enabled: false },
      ]),
    );
  });
});
