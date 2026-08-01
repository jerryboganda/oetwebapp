/**
 * Shared helpers for deriving display titles / unique slugs from filenames
 * when an admin batch-uploads multiple files at once (each becoming its own
 * record) instead of typing metadata per file.
 */

/** Strip the extension from a filename to use as a display title. */
export function titleFromFilename(filename: string): string {
  const withoutExt = filename.replace(/\.[^./\\]+$/, '');
  return withoutExt.trim() || filename;
}

/** Lowercase, hyphenated slug derived from a filename (extension stripped). */
export function slugifyFilename(filename: string): string {
  const withoutExt = filename.replace(/\.[^./\\]+$/, '');
  const slug = withoutExt
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
  return slug || 'item';
}

/** Returns a slug unique within `used`, appending -2, -3, ... on collision, and records it in `used`. */
export function uniqueSlug(base: string, used: Set<string>): string {
  let key = base;
  let n = 2;
  while (used.has(key)) {
    key = `${base}-${n}`;
    n += 1;
  }
  used.add(key);
  return key;
}
