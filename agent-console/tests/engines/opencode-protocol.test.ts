import { describe, expect, it } from 'vitest';
import { mapOpenCodeModels, mapOpenCodePermission, mapOpenCodeProviders, parseOpenCodeUserCode } from '../../src/engines/opencode-protocol.js';

const cwd = '/workspace/sessions/01J9ZQ4X7V3N8K2M5P6R7S8T9V';

describe('OpenCode permission mapping', () => {
  it('maps a native bash permission to the exact command before Guard approval', () => {
    expect(mapOpenCodePermission({
      id: 'per_1',
      callID: 'call_1',
      sessionID: 'ses_1',
      type: 'bash',
      title: 'Run a shell command',
      metadata: { command: 'git status', cwd },
    }, cwd)).toEqual({
      toolCallId: 'call_1',
      name: 'Bash',
      input: { command: 'git status', cwd },
      command: 'git status',
      cwd,
    });
  });

  it('maps a native edit permission to a guarded write path', () => {
    expect(mapOpenCodePermission({
      id: 'per_2',
      callID: 'call_2',
      sessionID: 'ses_1',
      type: 'edit',
      title: 'Edit a file',
      metadata: { filePath: 'src/example.ts' },
    }, cwd)).toEqual({
      toolCallId: 'call_2',
      name: 'Edit',
      input: { filePath: 'src/example.ts', file_path: 'src/example.ts' },
      cwd,
      writePaths: ['src/example.ts'],
    });
  });

  it('rejects permissions without an exact command or path', () => {
    expect(mapOpenCodePermission({
      id: 'per_3',
      callID: 'call_3',
      sessionID: 'ses_1',
      type: 'bash',
      title: 'Run a shell command',
      metadata: {},
    }, cwd)).toBeNull();
  });

  it('rejects permissions belonging to another OpenCode session', () => {
    expect(mapOpenCodePermission({
      id: 'per_4',
      callID: 'call_4',
      sessionID: 'ses_other',
      type: 'bash',
      title: 'Run a shell command',
      metadata: { command: 'git status' },
    }, cwd, 'ses_1')).toBeNull();
  });

  it('preserves opaque model ids, exposes OAuth methods only and never returns credentials', () => {
    const providers = mapOpenCodeProviders({
      all: [{
        id: 'custom-provider',
        name: 'Custom Provider',
        key: 'must-not-escape',
        models: {
          'model/variant-family': {
            id: 'model/variant-family',
            name: 'Model One',
            variants: { low: {}, high: {} },
            defaultVariant: 'low',
          },
        },
      }],
      connected: ['custom-provider'],
    }, {
      'custom-provider': [{ type: 'api', label: 'API key' }, { type: 'oauth', label: 'Sign in' }],
    });

    expect(providers).toEqual([{
      id: 'custom-provider',
      name: 'Custom Provider',
      connected: true,
      oauthMethods: [{ index: 1, label: 'Sign in' }],
      apiMethods: [{ index: 0, label: 'API key' }],
    }]);
    expect(JSON.stringify(providers)).not.toContain('must-not-escape');
    expect(mapOpenCodeModels({
      all: [{
        id: 'custom-provider',
        name: 'Custom Provider',
        key: 'must-not-escape',
        models: {
          'model/variant-family': {
            id: 'model/variant-family',
            name: 'Model One',
            variants: { low: {}, high: {} },
            defaultVariant: 'low',
          },
        },
      }],
      connected: ['custom-provider'],
    })).toEqual([{
      value: 'custom-provider/model/variant-family',
      displayName: 'Custom Provider · Model One',
      supportsEffort: false,
      efforts: [],
    }]);
  });

  it('extracts only explicit device codes for headless OAuth methods', () => {
    expect(parseOpenCodeUserCode('Open the verification page. Enter code: ABCD-EFGH')).toBe('ABCD-EFGH');
    expect(parseOpenCodeUserCode('Complete authorization in your browser.')).toBeNull();
  });
});