// Minimal static feed server for the updater round-trip test (spike c).
// Serves latest.json (advertising v0.1.1) + the v0.1.1 NSIS artifact + its .sig
// so a running v0.1.0 build checks, downloads, verifies the minisign signature,
// and installs. Usage: node updater-feed-server.mjs <bundleDir> <port>
import { createServer } from 'node:http';
import { readFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';

const bundleDir = process.argv[2];
const port = Number(process.argv[3] || 8765);
if (!bundleDir) { console.error('usage: node updater-feed-server.mjs <bundleDir> <port>'); process.exit(1); }

// Locate the NSIS updater artifact (*-setup.exe) + its .sig.
import { readdirSync } from 'node:fs';
const files = readdirSync(bundleDir);
const setup = files.find((f) => f.endsWith('-setup.exe'));
const sigFile = files.find((f) => f.endsWith('-setup.exe.sig'));
if (!setup || !sigFile) { console.error('missing -setup.exe or .sig in', bundleDir, '\nfound:', files); process.exit(1); }
const signature = readFileSync(join(bundleDir, sigFile), 'utf8').trim();

const latest = {
  version: '0.1.1',
  notes: 'Updater round-trip test build',
  pub_date: '2026-06-12T00:00:00Z',
  platforms: {
    'windows-x86_64': {
      signature,
      url: `http://127.0.0.1:${port}/download/${encodeURIComponent(setup)}`,
    },
  },
};

createServer((req, res) => {
  if (req.url === '/latest.json') {
    res.writeHead(200, { 'content-type': 'application/json' });
    res.end(JSON.stringify(latest));
    console.log('served latest.json (-> v0.1.1)');
  } else if (req.url?.startsWith('/download/')) {
    const path = join(bundleDir, setup);
    if (!existsSync(path)) { res.writeHead(404); res.end('not found'); return; }
    const buf = readFileSync(path);
    res.writeHead(200, { 'content-type': 'application/octet-stream', 'content-length': buf.length });
    res.end(buf);
    console.log(`served artifact ${setup} (${buf.length} bytes)`);
  } else {
    res.writeHead(404);
    res.end('not found');
  }
}).listen(port, '127.0.0.1', () => console.log(`updater feed on http://127.0.0.1:${port} advertising v0.1.1 (${setup})`));
