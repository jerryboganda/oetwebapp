import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { resolveApiMediaUrl } from '../media-url';

const ORIGINAL = process.env.NEXT_PUBLIC_API_BASE_URL;

describe('resolveApiMediaUrl', () => {
  beforeEach(() => {
    process.env.NEXT_PUBLIC_API_BASE_URL = 'https://api.example.com';
  });

  afterEach(() => {
    if (ORIGINAL === undefined) {
      delete process.env.NEXT_PUBLIC_API_BASE_URL;
    } else {
      process.env.NEXT_PUBLIC_API_BASE_URL = ORIGINAL;
    }
  });

  it('returns null for null/undefined/empty input', () => {
    expect(resolveApiMediaUrl(null)).toBeNull();
    expect(resolveApiMediaUrl(undefined)).toBeNull();
    expect(resolveApiMediaUrl('')).toBeNull();
  });

  it('returns absolute https URLs unchanged', () => {
    expect(resolveApiMediaUrl('https://cdn.example.com/foo.png')).toBe('https://cdn.example.com/foo.png');
  });

  it('returns absolute http URLs unchanged', () => {
    expect(resolveApiMediaUrl('http://cdn.example.com/foo.png')).toBe('http://cdn.example.com/foo.png');
  });

  it('treats absolute URL match case-insensitively', () => {
    expect(resolveApiMediaUrl('HTTPS://cdn.example.com/foo.png')).toBe('HTTPS://cdn.example.com/foo.png');
  });

  it('prepends the API base for relative paths starting with /', () => {
    expect(resolveApiMediaUrl('/media/abc.png')).toBe('https://api.example.com/media/abc.png');
  });

  it('inserts a / when the path does not start with one', () => {
    expect(resolveApiMediaUrl('media/abc.png')).toBe('https://api.example.com/media/abc.png');
  });

  it('strips a trailing / on the API base', () => {
    process.env.NEXT_PUBLIC_API_BASE_URL = 'https://api.example.com/';
    expect(resolveApiMediaUrl('/media/abc.png')).toBe('https://api.example.com/media/abc.png');
  });

  it('returns the path unchanged when the base is missing', () => {
    delete process.env.NEXT_PUBLIC_API_BASE_URL;
    expect(resolveApiMediaUrl('/media/abc.png')).toBe('/media/abc.png');
  });

  it('returns the path unchanged when the base is whitespace-only', () => {
    process.env.NEXT_PUBLIC_API_BASE_URL = '   ';
    expect(resolveApiMediaUrl('/media/abc.png')).toBe('/media/abc.png');
  });
});
