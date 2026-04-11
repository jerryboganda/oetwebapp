// Debug script to trace the 'config' error
const fs = require('fs');
const path = require('path');

const testFile = path.resolve(__dirname, '..', 'app/achievements/page.debug.test.tsx');
const buf = fs.readFileSync(testFile);
console.log('File size:', buf.length);
console.log('First 3 bytes (hex):', buf.slice(0, 3).toString('hex'));
console.log('Has BOM:', buf[0] === 0xEF && buf[1] === 0xBB && buf[2] === 0xBF);
console.log('');

// Also check if the main page.tsx has any issues
const pageFile = path.resolve(__dirname, '..', 'app/achievements/page.tsx');
const pageSrc = fs.readFileSync(pageFile, 'utf8');

// Look for '.config' access in the page source
const configMatches = pageSrc.match(/\.\bconfig\b/g);
console.log('.config occurrences in page.tsx:', configMatches);

// Check for alertConfig at module level in alert.tsx
const alertFile = path.resolve(__dirname, '..', 'components/ui/alert.tsx');
const alertSrc = fs.readFileSync(alertFile, 'utf8');
const alertConfigLine = alertSrc.split('\n').find(l => l.includes('.config'));
console.log('alert.tsx .config line:', alertConfigLine);
