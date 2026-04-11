// Debug script to find the actual error source
const { execSync } = require('child_process');
const path = require('path');

const cwd = path.resolve(__dirname, '..');

try {
  const result = execSync(
    'node --stack-trace-limit=50 node_modules/vitest/vitest.mjs run --config vitest.debug.config.ts sanity.test.ts',
    { cwd, encoding: 'utf8', env: { ...process.env, DEBUG: '*' }, stdio: ['pipe', 'pipe', 'pipe'], timeout: 30000 }
  );
  console.log('STDOUT:', result);
} catch (e) {
  console.log('STDOUT:', e.stdout);
  console.log('STDERR:', e.stderr?.substring(0, 5000));
}
