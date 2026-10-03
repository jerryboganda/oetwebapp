import type { Permission as NativeOpenCodePermission } from '@opencode-ai/sdk/client';
import type { EngineProvider, ModelInfo, ToolCallRequest } from './types.js';

export type OpenCodePermission = NativeOpenCodePermission;

export function parseOpenCodeUserCode(instructions: string): string | null {
  const match = /\b(?:enter|confirmation|verification)\s+code\s*:\s*([A-Za-z0-9-]{4,32})\b/i.exec(instructions);
  return match?.[1] ?? null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function nonEmptyString(...values: unknown[]): string | undefined {
  for (const value of values) {
    if (typeof value === 'string' && value.trim()) return value;
  }
  return undefined;
}

/** Converts only permissions with enough native input for the Guard to classify. */
export function mapOpenCodePermission(
  permission: OpenCodePermission,
  cwd: string,
  sessionId?: string,
): ToolCallRequest | null {
  const permissionId = nonEmptyString(permission.id);
  const toolCallId = nonEmptyString(permission.callID, permission.id);
  const type = nonEmptyString(permission.type)?.toLowerCase();
  const metadata = isRecord(permission.metadata) ? permission.metadata : {};
  if (!permissionId || !toolCallId || !type) return null;
  if (sessionId && permission.sessionID !== sessionId) return null;

  const request: ToolCallRequest = {
    toolCallId,
    name: '',
    input: metadata,
    cwd: nonEmptyString(metadata['cwd']) ?? cwd,
  };

  if (type === 'bash') {
    const command = nonEmptyString(metadata['command'], metadata['cmd']);
    if (!command) return null;
    request.name = 'Bash';
    request.input = { ...metadata, command };
    request.command = command;
    return request;
  }

  if (type === 'read' || type === 'edit' || type === 'write' || type === 'patch') {
    const filePath = nonEmptyString(metadata['filePath'], metadata['file_path'], metadata['path']);
    if (!filePath) return null;
    request.name = type === 'read' ? 'Read' : type === 'patch' ? 'apply_patch' : type === 'edit' ? 'Edit' : 'Write';
    request.input = { ...metadata, file_path: filePath };
    if (type !== 'read') request.writePaths = [filePath];
    return request;
  }

  if (type === 'glob' || type === 'grep' || type === 'webfetch' || type === 'websearch') {
    request.name = type === 'webfetch' ? 'WebFetch' : type === 'websearch' ? 'WebSearch' : type === 'glob' ? 'Glob' : 'Grep';
    return request;
  }

  return null;
}

/** Projects only safe provider metadata; native credential fields never leave OpenCode. */
export function mapOpenCodeProviders(providerList: unknown, providerAuth: unknown): EngineProvider[] {
  if (!isRecord(providerList) || !Array.isArray(providerList['all'])) return [];
  const connected = new Set(
    Array.isArray(providerList['connected'])
      ? providerList['connected'].filter((value): value is string => typeof value === 'string')
      : [],
  );
  const methodsByProvider = isRecord(providerAuth) ? providerAuth : {};

  return providerList['all'].flatMap((value) => {
    if (!isRecord(value)) return [];
    const id = nonEmptyString(value['id']);
    if (!id) return [];
    const methods = Array.isArray(methodsByProvider[id]) ? methodsByProvider[id] as unknown[] : [];
    return [{
      id,
      name: nonEmptyString(value['name']) ?? id,
      connected: connected.has(id),
      oauthMethods: methods.flatMap((method, index) => {
        if (!isRecord(method) || method['type'] !== 'oauth') return [];
        const label = nonEmptyString(method['label']);
        return label ? [{ index, label }] : [];
      }),
    }];
  });
}

/** Maps only connected providers and treats provider/model IDs as opaque values. */
export function mapOpenCodeModels(providerList: unknown): ModelInfo[] {
  if (!isRecord(providerList) || !Array.isArray(providerList['all'])) return [];
  const connected = new Set(
    Array.isArray(providerList['connected'])
      ? providerList['connected'].filter((value): value is string => typeof value === 'string')
      : [],
  );
  const models: ModelInfo[] = [];

  for (const providerValue of providerList['all']) {
    if (!isRecord(providerValue)) continue;
    const providerId = nonEmptyString(providerValue['id']);
    const providerName = nonEmptyString(providerValue['name']) ?? providerId;
    const providerModels = providerValue['models'];
    if (!providerId || !providerName || !connected.has(providerId) || !isRecord(providerModels)) continue;

    for (const [key, modelValue] of Object.entries(providerModels)) {
      if (!isRecord(modelValue)) continue;
      const modelId = nonEmptyString(modelValue['id']) ?? key;
      const model: ModelInfo = {
        value: `${providerId}/${modelId}`,
        displayName: `${providerName} · ${nonEmptyString(modelValue['name']) ?? modelId}`,
        supportsEffort: false,
        efforts: [],
      };
      const description = nonEmptyString(modelValue['description']);
      if (description) model.description = description;
      models.push(model);
    }
  }

  return models;
}