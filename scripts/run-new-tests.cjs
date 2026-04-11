const { execSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const cwd = __dirname.replace(/[\\/]scripts$/, '');

// Run ALL tests with JSON reporter, capture structured results
const cmd = `node node_modules/vitest/vitest.mjs run --exclude "**/bare.test.ts" --exclude "**/sanity.test.ts" --exclude "**/*.debug.test.*" --reporter=json`;
try {
  const out = execSync(cmd, { cwd, env: { ...process.env, NO_COLOR: '1' }, timeout: 180000, maxBuffer: 100 * 1024 * 1024 });
  parseAndPrint(out.toString());
} catch (e) {
  if (e.stdout) parseAndPrint(e.stdout.toString());
  else console.log('Error:', e.message);
}

function parseAndPrint(text) {
  // Find the JSON object in the output (skip any non-JSON preamble)
  const start = text.indexOf('{"numTotalTestSuites"');
  if (start < 0) {
    console.log('No JSON found in output. First 500 chars:', text.substring(0, 500));
    return;
  }
  const j = JSON.parse(text.substring(start));
  console.log(`Test Files: ${j.numPassedTestSuites} passed, ${j.numFailedTestSuites} failed (${j.numTotalTestSuites} total)`);
  console.log(`Tests: ${j.numPassedTests} passed, ${j.numFailedTests} failed (${j.numTotalTests} total)`);
  
  // Show each file status
  j.testResults.forEach(s => {
    const n = (s.name.split('oetwebapp/').pop() || s.name.split('oetwebapp\\').pop() || s.name);
    const ok = s.assertionResults.every(t => t.status === 'passed');
    if (!ok) console.log(`  FAIL: ${n}`);
  });
  
  // Show failure details
  j.testResults.forEach(s => {
    s.assertionResults.forEach(t => {
      if (t.status === 'failed') {
        const msg = (t.failureMessages[0] || '').split('\n')[0];
        console.log(`  FAILED: ${t.fullName} -> ${msg.substring(0, 150)}`);
      }
    });
  });
}
