// Script to remove 'import { ... } from "vitest"' lines from all test files
// Since vitest.config.ts has globals: true, all vitest APIs are available globally
const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');

const root = path.resolve(__dirname, '..');

// Find all .test.ts and .test.tsx files
const files = execSync(
  'git ls-files "*.test.ts" "*.test.tsx"',
  { cwd: root, encoding: 'utf8' }
).trim().split('\n').filter(Boolean);

let fixed = 0;
let skipped = 0;

for (const relPath of files) {
  const fullPath = path.join(root, relPath);
  if (!fs.existsSync(fullPath)) continue;
  
  const content = fs.readFileSync(fullPath, 'utf8');
  
  // Match: import { ... } from 'vitest'; or import { ... } from "vitest";
  const pattern = /^import\s+\{[^}]*\}\s+from\s+['"]vitest['"];?\s*\n/gm;
  
  if (pattern.test(content)) {
    const newContent = content.replace(pattern, '');
    fs.writeFileSync(fullPath, newContent, 'utf8');
    fixed++;
    console.log('FIXED:', relPath);
  } else {
    skipped++;
  }
}

console.log(`\nDone: ${fixed} files fixed, ${skipped} files skipped`);
