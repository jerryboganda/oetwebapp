/**
 * Fixes test files that use vi.mock('next/navigation', ...) by:
 * 1. Removing the vi.mock('next/navigation', ...) block
 * 2. Adding import { renderWithRouter } from '@/tests/test-utils'
 * 3. Replacing render( with renderWithRouter(
 * 4. Removing render from @testing-library/react import
 */
const fs = require('fs');
const path = require('path');

const files = [
  'lib/__tests__/use-expert-auth.test.tsx',
  'app/mocks/report/[id]/page.test.tsx',
  'app/mocks/player/[id]/page.test.tsx',
  'components/layout/__tests__/notification-center.test.tsx',
  'components/layout/__tests__/app-shell.test.tsx',
  'components/auth/__tests__/sign-in-form.test.tsx',
  'components/auth/__tests__/register-form.test.tsx',
  'app/billing/page.test.tsx',
  'app/expert/page.test.tsx',
  'app/expert/non-review-pages.test.tsx',
  'app/writing/player/page.test.tsx',
  'app/expert/layout.test.tsx',
  'app/diagnostic/reading/page.test.tsx',
  'app/settings/[section]/page.test.tsx',
  'app/settings/page.test.tsx',
  'app/submissions/page.test.tsx',
  'app/(auth)/sign-in/page.test.tsx',
  'app/admin/non-editor-pages.test.tsx',
  'app/admin/layout.test.tsx',
  'app/(auth)/forgot-password/verify/page.test.tsx',
  'app/(auth)/reset-password/success/page.test.tsx',
  'app/(auth)/auth/callback/[provider]/page.test.tsx',
  'app/(auth)/register/page.test.tsx',
  'app/(auth)/register/success/page.test.tsx',
];

const root = path.resolve(__dirname, '..');
let fixed = 0;
let skipped = 0;

for (const rel of files) {
  const fp = path.join(root, rel);
  if (!fs.existsSync(fp)) {
    console.log(`SKIP (not found): ${rel}`);
    skipped++;
    continue;
  }

  let src = fs.readFileSync(fp, 'utf8');
  const orig = src;

  // 1. Remove vi.mock('next/navigation', ...) block
  // Match: vi.mock('next/navigation', () => ({ ... }));
  // Could span multiple lines
  const mockPatterns = [
    // Single-line: vi.mock('next/navigation', () => ({ ... }));
    /vi\.mock\(\s*['"]next\/navigation['"].*?\)\s*;?\s*\n/gs,
  ];

  for (const pat of mockPatterns) {
    src = src.replace(pat, '');
  }

  // If multi-line mock wasn't caught, try a more aggressive approach
  if (src.includes("vi.mock('next/navigation'") || src.includes('vi.mock("next/navigation"')) {
    // Find the start and balance braces to the end
    const idx = src.indexOf("vi.mock('next/navigation'") !== -1
      ? src.indexOf("vi.mock('next/navigation'")
      : src.indexOf('vi.mock("next/navigation"');
    if (idx >= 0) {
      let depth = 0;
      let end = idx;
      let started = false;
      for (let i = idx; i < src.length; i++) {
        if (src[i] === '(') { depth++; started = true; }
        if (src[i] === ')') { depth--; }
        if (started && depth === 0) {
          end = i + 1;
          // Skip trailing semicolons and newlines
          while (end < src.length && (src[end] === ';' || src[end] === '\n' || src[end] === '\r')) end++;
          break;
        }
      }
      src = src.substring(0, idx) + src.substring(end);
    }
  }

  // 2. Add renderWithRouter import if not already present
  if (!src.includes('renderWithRouter')) {
    // Add after existing imports
    const lastImportIdx = src.lastIndexOf('\nimport ');
    if (lastImportIdx >= 0) {
      const lineEnd = src.indexOf('\n', lastImportIdx + 1);
      src = src.substring(0, lineEnd + 1) + "import { renderWithRouter } from '@/tests/test-utils';\n" + src.substring(lineEnd + 1);
    } else {
      src = "import { renderWithRouter } from '@/tests/test-utils';\n" + src;
    }
  }

  // 3. Replace render( with renderWithRouter( — but only standalone render() calls
  // Match: render(<...>) but not renderWithRouter(
  src = src.replace(/(?<!\w)render\s*\(/g, 'renderWithRouter(');

  // 4. Remove render from @testing-library/react import if renderWithRouter is the only render used
  // Change: import { render, screen } to import { screen }
  // Change: import { render, screen, ... } to import { screen, ... }
  src = src.replace(
    /import\s*\{([^}]*)\brender\b([^}]*)\}\s*from\s*['"]@testing-library\/react['"]/,
    (match, before, after) => {
      let names = (before + 'render' + after).split(',').map(s => s.trim()).filter(Boolean);
      names = names.filter(n => n !== 'render');
      if (names.length === 0) return ''; // Remove the entire import if empty
      return `import { ${names.join(', ')} } from '@testing-library/react'`;
    }
  );

  if (src !== orig) {
    fs.writeFileSync(fp, src, 'utf8');
    console.log(`FIXED: ${rel}`);
    fixed++;
  } else {
    console.log(`NO CHANGE: ${rel}`);
    skipped++;
  }
}

console.log(`\nDone: ${fixed} fixed, ${skipped} skipped`);
