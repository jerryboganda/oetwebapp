import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

// egress/ and dockerproxy/ are separate Docker build contexts with zero runtime
// deps, so each carries its own copy of these modules. approvals.ts is the
// fail-closed approval callback: a fix applied to one copy only would leave the
// other proxy on the old behaviour. (log.ts and grants.ts differ on purpose.)
const consoleRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (proxy: string, file: string): string => readFileSync(path.join(consoleRoot, proxy, 'src', file), 'utf8');

describe('proxy shared modules', () => {
  it.each(['approvals.ts', 'secrets.ts'])('egress and dockerproxy copies of %s are identical', (file) => {
    expect(read('dockerproxy', file)).toBe(read('egress', file));
  });
});
