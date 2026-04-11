const fs = require('fs');
const buf = fs.readFileSync(process.argv[2]);
// Strip ALL ANSI escape sequences (ESC[...X, ESC]...ST, etc.)
const clean = buf.toString()
  .replace(/\x1b\[[\d;]*[a-zA-Z]/g, '')
  .replace(/\x1b\][\d;]*[^\x07]*\x07/g, '')
  .replace(/[\x00-\x08\x0b\x0c\x0e-\x1f]/g, '');

// Print lines around "Failed" or errors
const lines = clean.split('\n');
let inFailSection = false;
let count = 0;
for (const line of lines) {
  const t = line.trim();
  if (/Failed Test|FAIL /.test(t)) { inFailSection = true; count = 0; }
  if (inFailSection && count < 30) { console.log(t); count++; }
  if (/Test Files/.test(t) && inFailSection) break;
}

if (!inFailSection) {
  console.log('No "Failed Test" section found. Searching for error indicators...');
  for (const line of lines) {
    const t = line.trim();
    if (/Error:|TypeError|findByText|Unable to find|Timed out|invariant|assert/i.test(t)) {
      console.log(t);
    }
  }
}
