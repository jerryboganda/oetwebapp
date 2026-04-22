#!/usr/bin/env node

import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync, statSync } from 'node:fs';
import { basename, extname } from 'node:path';

const textExtensions = new Set([
  '.cs',
  '.csproj',
  '.css',
  '.cjs',
  '.html',
  '.js',
  '.json',
  '.md',
  '.mdx',
  '.mjs',
  '.ps1',
  '.sh',
  '.sln',
  '.sql',
  '.svg',
  '.toml',
  '.ts',
  '.tsx',
  '.txt',
  '.xml',
  '.yaml',
  '.yml',
]);

const textFilenames = new Set([
  '.dockerignore',
  '.editorconfig',
  '.env.example',
  '.gitignore',
  'Dockerfile',
]);

const excludedSegments = new Set([
  '.git',
  '.next',
  '.tools',
  '.turbo',
  'bin',
  'build',
  'coverage',
  'dist',
  'node_modules',
  'obj',
  'output',
  'playwright-report',
]);

const excludedFilePatterns = [
  /(^|\/).*\.log$/i,
  /(^|\/)(backend_test_full|build_check|dotnet_check|lint_check|test_check)\.txt$/i,
  /(^|\/)tsconfig\.tsbuildinfo$/i,
];

const sentinelCodepoints = new Set([
  0x00c2,
  0x00c3,
  0x00c9,
  0x00ce,
  0x00d8,
  0x00d9,
  0x00e2,
  0x00ef,
  0xfffd,
]);

function trackedAndUnignoredFiles() {
  return execFileSync('git', ['ls-files', '--cached', '--others', '--exclude-standard'], {
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'pipe'],
  })
    .split(/\r?\n/)
    .filter(Boolean);
}

function isCandidateFile(filePath) {
  const normalized = filePath.replaceAll('\\', '/');
  const parts = normalized.split('/');
  if (parts.some((part) => excludedSegments.has(part))) {
    return false;
  }

  if (excludedFilePatterns.some((pattern) => pattern.test(normalized))) {
    return false;
  }

  return textExtensions.has(extname(normalized).toLowerCase()) || textFilenames.has(basename(normalized));
}

function hasMojibakeSentinel(line) {
  for (const char of line) {
    const codepoint = char.codePointAt(0);
    if (codepoint === undefined) {
      continue;
    }

    if (sentinelCodepoints.has(codepoint) || (codepoint >= 0x80 && codepoint <= 0x9f)) {
      return true;
    }
  }

  return false;
}

const findings = [];

for (const filePath of trackedAndUnignoredFiles()) {
  if (!isCandidateFile(filePath) || !existsSync(filePath)) {
    continue;
  }

  const stats = statSync(filePath);
  if (!stats.isFile() || stats.size > 2_000_000) {
    continue;
  }

  const content = readFileSync(filePath, 'utf8');
  if (content.includes('\0')) {
    continue;
  }

  const lines = content.split(/\r?\n/);
  lines.forEach((line, index) => {
    if (hasMojibakeSentinel(line)) {
      findings.push(`${filePath}:${index + 1}: ${line.trim().slice(0, 180)}`);
    }
  });
}

if (findings.length > 0) {
  console.error(`Potential mojibake artifacts found in ${findings.length} line(s):`);
  for (const finding of findings.slice(0, 80)) {
    console.error(finding);
  }

  if (findings.length > 80) {
    console.error(`...and ${findings.length - 80} more.`);
  }

  process.exit(1);
}

console.log('No mojibake sentinels found in scanned text files.');
