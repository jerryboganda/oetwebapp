const fs = require('fs');
const be = fs.readFileSync('.audit-tmp/be_fullpaths.txt', 'utf8').split('\n').filter(Boolean);
const bePaths = be.map(l => { const sp = l.indexOf(' '); return { method: l.slice(0, sp), path: l.slice(sp + 1) }; });

function escRe(s) { return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'); }

// Convert a backend path with {param} into a regex source, {param} => one segment [^/]+
function beToRegexSource(p) {
  const parts = p.split(/(\{[^}]+\})/g);
  return parts.map(seg => seg.startsWith('{') ? '[^/]+' : escRe(seg)).join('');
}
const beRe = bePaths.map(b => ({ ...b, src: beToRegexSource(b.path) }));

const fe = fs.readFileSync('.audit-tmp/fe_paths.txt', 'utf8').split('\n').filter(Boolean);

function feMatches(fp) {
  const fpTrim = fp.replace(/\/$/, '');
  for (const b of beRe) {
    // full match (FE has a real value where BE has {param})
    if (new RegExp('^' + b.src + '/?$').test(fp)) return true;
    // FE is a prefix of BE path because the template literal was truncated mid-URL.
    // Compare literal prefix: strip {param} from BE, see if BE literal-prefix startsWith FE literal.
    const beLiteralPrefix = b.path.split('{')[0].replace(/\/$/, '');
    if (beLiteralPrefix.startsWith(fpTrim) && fpTrim.length > 0) return true;
    // Or FE (with its own sample dynamic segments) is a prefix when BE regex matches the FE start.
    if (new RegExp('^' + b.src).test(fpTrim)) return true;
  }
  return false;
}

const missing = fe.filter(fp => !feMatches(fp));
fs.writeFileSync('.audit-tmp/fe_missing.txt', missing.join('\n'));
console.log('FE paths:', fe.length, '  no backend match:', missing.length);
