import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';

// The production E2E workflow cannot be run from a PR, so the mistakes that broke it before are guarded here as text checks:
// a colon-space in a plain YAML scalar made a push run fail with 0 jobs, and a module the script imports but the workflow
// does not copy next to it fails at import, after the candidate tape has been built.
const workflow = readFileSync(resolve('.github/workflows/speaking-live-voice-prod-e2e.yml'), 'utf8');
const e2e = readFileSync(resolve('scripts/qa/speaking-live-voice-browser-e2e.mjs'), 'utf8');

describe('live voice E2E workflow', () => {
  it('copies every module the E2E script imports next to it', () => {
    const imported = [...e2e.matchAll(/from '\.\/([a-z0-9-]+\.mjs)'/g)].map((match) => match[1]);
    expect(imported.length).toBeGreaterThanOrEqual(3);
    const copyLine = workflow.split('\n').find((line) => line.includes('/tmp/pw/e2e.mjs')) ?? '';
    for (const file of imported) expect(copyLine, file).toContain(`scripts/qa/${file}`);
  });

  it('keeps every input description a plain YAML scalar without a colon-space or a comment marker', () => {
    const descriptions = workflow.split('\n').filter((line) => /^\s+description: /.test(line)).map((line) => line.replace(/^\s+description: /, ''));
    expect(descriptions.length).toBeGreaterThanOrEqual(13);
    for (const text of descriptions) {
      if (/^['"]/.test(text)) continue; // a quoted scalar may hold anything
      expect(text, text).not.toMatch(/: | #/);
    }
  });

  it('passes every environment variable the E2E script reads from the Run step', () => {
    const block = /const \{([^}]*)\} = process\.env;/.exec(e2e)?.[1] ?? '';
    const names = block.match(/[A-Z][A-Z0-9_]{2,}/g) ?? [];
    expect(names).toEqual(expect.arrayContaining(['MODE', 'FAULT_RELOAD_AT_S', 'VERIFY_CREDITS', 'GRADE_RETRY']));
    for (const name of names) expect(workflow, name).toMatch(new RegExp(`^\\s+${name}: `, 'm'));
  });

  it('offers only candidate scripts that exist', () => {
    const section = workflow.slice(workflow.indexOf('      script:'), workflow.indexOf('      voice:'));
    const options = (/options: \[([^\]]*)\]/.exec(section)?.[1] ?? '').split(',').map((name) => name.trim()).filter(Boolean);
    expect(options).toEqual(expect.arrayContaining(['smoke', 'smoke-A', 'smoke-B']));
    for (const name of options) expect(existsSync(resolve(`scripts/qa/speaking-candidate-scripts/${name}.txt`)), name).toBe(true);
  });

  it('keeps the artifact short-lived and refuses to run during a deploy', () => {
    expect(workflow).toMatch(/retention-days: 3\b/);
    expect(workflow).toMatch(/gh run list [^\n]*--workflow deploy\.yml/);
    expect(workflow).toMatch(/permissions:\s+actions: read\s+contents: read/);
  });
});
