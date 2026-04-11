// Fix remaining test files not tracked by git
const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '..');

function walkDir(dir, ext, results = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory() && entry.name !== 'node_modules' && entry.name !== '.next') {
      walkDir(full, ext, results);
    } else if (entry.isFile() && (entry.name.endsWith('.test.ts') || entry.name.endsWith('.test.tsx'))) {
      results.push(full);
    }
  }
  return results;
}

const files = walkDir(root);
const pattern = /^import\s+\{[^}]*\}\s+from\s+['"]vitest['"];?\s*\n/gm;

let fixed = 0;
for (const fullPath of files) {
  const content = fs.readFileSync(fullPath, 'utf8');
  if (pattern.test(content)) {
    // Reset regex lastIndex
    pattern.lastIndex = 0;
    const newContent = content.replace(pattern, '');
    fs.writeFileSync(fullPath, newContent, 'utf8');
    fixed++;
    console.log('FIXED:', path.relative(root, fullPath));
  }
}

console.log(`\nDone: ${fixed} additional files fixed`);
