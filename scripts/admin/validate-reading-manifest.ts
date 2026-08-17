#!/usr/bin/env node
/**
 * Offline dry-run for Reading import bundles.
 * Does not write to the API. Backend ValidatePaperAsync remains authoritative after a write.
 */
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import {
  validateReadingImportBundle,
  type ReadingValidationIssue,
} from '../../lib/reading-manifest-contract.ts';

const workspaceRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');

function parseArgs(argv: string[]): Record<string, string | boolean> {
  const result: Record<string, string | boolean> = {};
  for (let index = 0; index < argv.length; index += 1) {
    const item = argv[index];
    if (!item.startsWith('--')) continue;
    const key = item.slice(2);
    const next = argv[index + 1];
    if (!next || next.startsWith('--')) {
      result[key] = true;
      continue;
    }
    result[key] = next;
    index += 1;
  }
  return result;
}

async function loadBundle(manifestPath: string): Promise<unknown> {
  const resolvedPath = path.resolve(workspaceRoot, manifestPath);
  const extension = path.extname(resolvedPath).toLowerCase();
  if (extension === '.mjs' || extension === '.js') {
    const module = await import(pathToFileURL(resolvedPath).href);
    return module.default ?? module.bundle;
  }
  const raw = await fs.readFile(resolvedPath, 'utf8');
  return JSON.parse(raw.replace(/^\uFEFF/, ''));
}

function printIssues(label: string, issues: ReadingValidationIssue[]): void {
  if (issues.length === 0) {
    console.log(`  ${label}: none`);
    return;
  }
  for (const issue of issues) {
    console.log(`  [${issue.severity}] ${issue.code}: ${issue.message}`);
  }
}

async function main(): Promise<void> {
  const args = parseArgs(process.argv.slice(2));
  const manifestPath = typeof args.manifest === 'string' ? args.manifest : '';
  if (!manifestPath) {
    throw new Error('Usage: node --experimental-strip-types scripts/admin/validate-reading-manifest.ts --manifest <path>');
  }
  const requirePublishMetadata = args['allow-draft'] !== true;
  const bundle = await loadBundle(manifestPath);
  const result = validateReadingImportBundle(bundle, { requirePublishMetadata });

  console.log('WARNING: a write import posts replaceExisting=true on /reading/manifest and can delete QuestionPaper PDFs.');
  console.log('Re-attach Part A/B/C primary PDFs after a replace import, then re-validate.');
  console.log('WARNING: EvidenceSentence is not persisted by ImportManifestAsync. Dry-run publish-ready is not import publish-ready.');

  for (const paper of result.papers) {
    console.log(`\nPaper: ${paper.label}`);
    console.log(`  publishReady: ${paper.report.isPublishReady && paper.assetIssues.every((issue) => issue.severity !== 'error')}`);
    console.log(`  counts: ${JSON.stringify(paper.report.counts)}`);
    console.log(`  types: ${paper.report.detected.questionTypes.join(', ') || '(none)'}`);
    printIssues('structure', paper.report.issues);
    printIssues('assets', paper.assetIssues);
  }

  if (result.papers.length === 0) {
    throw new Error('No Reading papers or manifest parts found.');
  }
  if (!result.isPublishReady) {
    process.exitCode = 1;
  }
}

main().catch((error) => {
  console.error(error instanceof Error ? error.stack ?? error.message : error);
  process.exitCode = 1;
});
