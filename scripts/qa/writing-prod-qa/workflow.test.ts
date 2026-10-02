import { existsSync, readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { resolve } from 'node:path';

// The production QA workflow can only be dispatched once it is on main, so its structure is proven here.
// js-yaml is not a direct dependency: it is reached through eslint -> @eslint/eslintrc -> js-yaml (declared
// dependency edges, so it does not rely on pnpm hoisting).
const fromEslint = createRequire(createRequire(import.meta.url).resolve('eslint'));
const yaml = createRequire(fromEslint.resolve('@eslint/eslintrc'))('js-yaml') as { load(text: string): any };

const text = readFileSync(resolve('.github/workflows/writing-prod-qa.yml'), 'utf8');
const wf = yaml.load(text);
const run = readFileSync(resolve('scripts/qa/writing-prod-qa/run.mjs'), 'utf8');
const lib = readFileSync(resolve('scripts/qa/writing-prod-qa/lib.mjs'), 'utf8');
const steps = (job: string): any[] => wf.jobs[job].steps;
const step = (job: string, name: RegExp) => steps(job).find((s) => name.test(String(s.name ?? s.uses ?? '')));

const INPUTS = ['suite', 'professions', 'categories', 'reading_window', 'concurrency', 'pace_seconds', 'fault_mode',
  'verify_credits', 'require_l2_disabled', 'preflight_repair', 'browsers', 'cleanup'];

describe('writing-prod-qa.yml', () => {
  it('is inert: manual dispatch only, read-only token, one run at a time', () => {
    expect(Object.keys(wf.on)).toEqual(['workflow_dispatch']);
    expect(wf.permissions).toEqual({ contents: 'read' });
    expect(wf.concurrency).toEqual({ group: 'writing-prod-qa', 'cancel-in-progress': false });
    expect(text).not.toMatch(/^\s*(schedule|push|pull_request|pull_request_target|workflow_run):/m);
  });

  it('offers exactly the plan inputs (no provider-mode flip) with safe defaults, each passed as env', () => {
    const inputs = wf.on.workflow_dispatch.inputs;
    expect(Object.keys(inputs)).toEqual(INPUTS);
    expect(Object.keys(inputs).length).toBeLessThanOrEqual(25);
    expect(inputs.suite).toMatchObject({ type: 'choice', default: 'discover' });
    expect(inputs.suite.options).toEqual(['discover', 'matrix', 'acceptance', 'ui', 'all']);
    expect(inputs.fault_mode.options).toEqual(['flag', 'client', 'none']);
    expect(inputs.cleanup).toMatchObject({ default: 'on_success' });
    expect(inputs.preflight_repair.default).toBe(false);
    expect(text).not.toMatch(/codex_probe|mode:\s*codex/);
    for (const name of INPUTS) {
      const envName = name.toUpperCase();
      expect(wf.env[envName], envName).toBe(`\${{ inputs.${name} }}`);
      expect(lib, envName).toContain(envName);
    }
  });

  it('chains guard -> unit -> live and refuses to start during a deploy', () => {
    expect(wf.jobs.guard.needs).toBeUndefined();
    expect(wf.jobs.unit.needs).toBe('guard');
    expect(wf.jobs.live.needs).toEqual(['guard', 'unit']);
    expect(String(step('guard', /deploy/).run)).toMatch(/gh run list [^\n]*--workflow deploy\.yml/);
    expect(step('guard', /Validate/).run).toBe('node scripts/qa/writing-prod-qa/run.mjs check-inputs');
    for (const job of ['guard', 'live']) expect(wf.jobs[job].permissions).toEqual({ actions: 'read', contents: 'read' });
    expect(wf.jobs.unit.permissions).toBeUndefined();
    expect(wf.jobs.live['timeout-minutes']).toBeLessThanOrEqual(360);
  });

  it('gates live on the vitest suites and the hermetic detector spec', () => {
    expect(steps('unit').map((s) => s.run).join('\n')).toContain('pnpm exec vitest run scripts/qa/writing-prod-qa');
    expect(steps('unit').map((s) => s.run).join('\n')).toContain('playwright test tests/e2e/learner/writing-qa-detectors.spec.ts --project chromium-unauth');
    expect(existsSync(resolve('tests/e2e/learner/writing-qa-detectors.spec.ts'))).toBe(true);
  });

  it('uses only the production admin secrets, never in the guard or unit jobs', () => {
    expect([...new Set([...text.matchAll(/secrets\.([A-Z0-9_]+)/g)].map((m) => m[1]))].sort()).toEqual(['OET_ADMIN_EMAIL', 'OET_ADMIN_PASSWORD']);
    expect(JSON.stringify(wf.jobs.guard)).not.toMatch(/secrets\./);
    expect(JSON.stringify(wf.jobs.unit)).not.toMatch(/secrets\./);
  });

  it('stages every module the harness imports next to it and runs the always() safety net', () => {
    const imported = [...run.matchAll(/(?:from|import\()\s*'\.\/([a-z0-9-]+\.mjs)'/g)].map((m) => m[1]);
    expect(imported).toEqual(expect.arrayContaining(['api.mjs', 'contract.mjs', 'lib.mjs', 'browser.mjs']));
    for (const file of imported) expect(existsSync(resolve('scripts/qa/writing-prod-qa', file)), file).toBe(true);
    expect(step('live', /Stage the harness/).run).toContain('cp scripts/qa/writing-prod-qa/*.mjs scripts/qa/writing-prod-qa/scripts.json');
    expect(step('live', /Install isolated Playwright/).if).toContain("inputs.suite != 'discover'");
    expect(step('live', /Install isolated Playwright/).run).toContain('playwright@1.58.2');
    const net = step('live', /Safety net/);
    expect(net.if).toContain('always()');
    expect(net.run).toContain('run.mjs" safety-net');
    expect(step('live', /Run the harness/).env).toMatchObject({ OET_ADMIN_EMAIL: '${{ secrets.OET_ADMIN_EMAIL }}', GH_TOKEN: '${{ github.token }}' });
  });

  it('keeps public artifacts short-lived: media 3 days, evidence 14 days, uploaded even on failure', () => {
    const uploads = steps('live').filter((s) => String(s.uses).startsWith('actions/upload-artifact'));
    expect(uploads.map((s) => [s.with.name, s.with['retention-days'], s.if])).toEqual([
      ['writing-prod-qa-evidence', 14, '${{ always() }}'],
      ['writing-prod-qa-media', 3, '${{ always() }}'],
    ]);
  });
});
