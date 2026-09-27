import { badRequest } from './errors.js';

// Tiny request-body validators that produce CONTRACT.md error envelopes
// (400 + a stable code) instead of framework-specific validation errors.

export type Json = Record<string, unknown>;

export function asObject(body: unknown, allowEmpty = true): Json {
  if (body === undefined || body === null || body === '') {
    if (allowEmpty) return {};
    throw badRequest('bad_request', 'A JSON object body is required.');
  }
  if (typeof body !== 'object' || Array.isArray(body)) throw badRequest('bad_request', 'Body must be a JSON object.');
  return body as Json;
}

export function optString(obj: Json, key: string, max: number, options: { allowEmpty?: boolean } = {}): string | undefined {
  const value = obj[key];
  if (value === undefined || value === null) return undefined;
  if (typeof value !== 'string') throw badRequest('bad_request', `${key} must be a string.`);
  if (!options.allowEmpty && value.trim() === '') throw badRequest('bad_request', `${key} must not be empty.`);
  if (value.length > max) throw badRequest('bad_request', `${key} is longer than ${max} characters.`);
  return value;
}

export function reqString(obj: Json, key: string, max: number): string {
  const value = optString(obj, key, max);
  if (value === undefined) throw badRequest('bad_request', `${key} is required.`);
  return value;
}

export function optBoolean(obj: Json, key: string): boolean | undefined {
  const value = obj[key];
  if (value === undefined || value === null) return undefined;
  if (typeof value !== 'boolean') throw badRequest('bad_request', `${key} must be a boolean.`);
  return value;
}

export function optEnum<T extends string>(obj: Json, key: string, allowed: readonly T[]): T | undefined {
  const value = obj[key];
  if (value === undefined || value === null) return undefined;
  if (typeof value !== 'string' || !(allowed as readonly string[]).includes(value)) {
    throw badRequest('bad_request', `${key} must be one of: ${allowed.join(', ')}.`);
  }
  return value as T;
}

export function reqEnum<T extends string>(obj: Json, key: string, allowed: readonly T[]): T {
  const value = optEnum(obj, key, allowed);
  if (value === undefined) throw badRequest('bad_request', `${key} is required (one of: ${allowed.join(', ')}).`);
  return value;
}

/** Model / effort ids are opaque engine strings; only shape is checked here. */
export function optOpaqueId(obj: Json, key: string): string | undefined {
  const value = optString(obj, key, 200);
  if (value !== undefined && !/^[\x21-\x7e]+$/.test(value)) throw badRequest('bad_request', `${key} contains invalid characters.`);
  return value;
}

export function reqOpaqueId(obj: Json, key: string): string {
  const value = optOpaqueId(obj, key);
  if (value === undefined) throw badRequest('bad_request', `${key} is required.`);
  return value;
}
