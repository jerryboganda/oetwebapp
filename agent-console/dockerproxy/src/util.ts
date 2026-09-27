// Small total helpers for walking untrusted JSON (request bodies, daemon replies).

export type JsonRecord = Record<string, unknown>;

export function asRecord(value: unknown): JsonRecord {
  return value !== null && typeof value === 'object' && !Array.isArray(value) ? (value as JsonRecord) : {};
}

export function asArray(value: unknown): unknown[] {
  return Array.isArray(value) ? value : [];
}

export function asStringArray(value: unknown): string[] {
  return asArray(value).filter((item): item is string => typeof item === 'string');
}

export function str(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

export function stripSlash(name: string): string {
  return name.startsWith('/') ? name.slice(1) : name;
}

export function truncate(text: string, max: number): string {
  return text.length <= max ? text : `${text.slice(0, max)}…`;
}
