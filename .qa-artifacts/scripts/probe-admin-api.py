#!/usr/bin/env python3
"""
Live probe of admin API endpoints. Reads /workspace/.qa-artifacts/reports/admin-endpoints.json,
GETs every readonly admin endpoint with the admin JWT, records:
  - status code, ms, content-length, content-type
  - body keys (first 2 levels) for response-shape drift detection
  - error envelope shape for non-2xx
Skips POST/PUT/PATCH/DELETE unless QA_PROBE_MUTATIONS=1.
"""
import json, os, subprocess, time, urllib.request, urllib.error, ssl
from concurrent.futures import ThreadPoolExecutor, as_completed

API = os.environ.get('QA_API', 'https://api.oetwithdrhesham.co.uk')
ORIGIN = 'https://app.oetwithdrhesham.co.uk'
PROBE_MUT = os.environ.get('QA_PROBE_MUTATIONS') == '1'

def fresh_token():
    out = subprocess.check_output(['bash', '/workspace/.qa-artifacts/scripts/get-admin-token.sh']).decode().strip()
    return out

TOKEN = fresh_token()
print(f'[probe] got fresh token (len={len(TOKEN)})')

ctx = ssl.create_default_context()

def fill_params(route):
    # Replace {param} with dummy id "QA-PROBE"; mark as such
    return route.replace('{paperId}','QA-PROBE').replace('{id}','QA-PROBE').replace('{userId}','QA-PROBE').replace('{contentId}','QA-PROBE').replace('{drillId}','QA-PROBE').replace('{lessonId}','QA-PROBE').replace('{bundleId}','QA-PROBE').replace('{transactionId}','QA-PROBE').replace('{sessionId}','QA-PROBE')

def probe(ep):
    method = ep['method']
    route = fill_params(ep['route'])
    if method != 'GET' and not PROBE_MUT:
        return {**ep, 'probeStatus': 'skipped-mutation'}
    url = API + route
    req = urllib.request.Request(url, method=method)
    req.add_header('Authorization', f'Bearer {TOKEN}')
    req.add_header('Origin', ORIGIN)
    req.add_header('Accept', 'application/json')
    t0 = time.time()
    try:
        with urllib.request.urlopen(req, timeout=15, context=ctx) as r:
            body = r.read(8192)
            code = r.status
            ct = r.headers.get('Content-Type','')
            cl = len(body)
            corr = r.headers.get('X-Correlation-Id','')
            keys = []
            if 'application/json' in ct:
                try:
                    d = json.loads(body.decode('utf-8','replace'))
                    if isinstance(d, dict): keys = list(d.keys())[:12]
                    elif isinstance(d, list): keys = ['[list]', f'len={len(d)}']
                except Exception:
                    keys = ['<unparseable-json>']
    except urllib.error.HTTPError as e:
        code = e.code; ct = e.headers.get('Content-Type','') if e.headers else ''
        body = e.read(2048) if e else b''
        cl = len(body); corr = (e.headers.get('X-Correlation-Id','') if e.headers else '')
        keys = []
        if 'application/json' in ct:
            try:
                d = json.loads(body.decode('utf-8','replace'))
                if isinstance(d, dict): keys = list(d.keys())[:12]
            except Exception: pass
    except Exception as e:
        return {**ep, 'probeStatus': 'error', 'probeError': str(e)[:200], 'probeMs': int((time.time()-t0)*1000)}
    return {**ep,
            'probeStatus': code,
            'probeMs': int((time.time()-t0)*1000),
            'probeContentType': ct,
            'probeBytes': cl,
            'probeKeys': keys,
            'probeCorrelationId': corr,
            'probedUrl': url}

src = json.load(open('/workspace/.qa-artifacts/reports/admin-endpoints.json'))
endpoints = src['endpoints']
print(f'[probe] {len(endpoints)} endpoints loaded. mutations={"on" if PROBE_MUT else "off"}.')

results = []
with ThreadPoolExecutor(max_workers=8) as pool:
    futs = {pool.submit(probe, ep): ep for ep in endpoints}
    for i, f in enumerate(as_completed(futs)):
        results.append(f.result())
        if (i+1) % 20 == 0: print(f'[probe] {i+1}/{len(endpoints)}')

# Sort and write
results.sort(key=lambda r: (str(r.get('probeStatus','zzz')), r['route']))
out = {
    'api': API,
    'tokenSubject': 'manwara575@gmail.com',
    'probedAt': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()),
    'mutationsProbed': PROBE_MUT,
    'results': results,
    'summary': {
        'total': len(results),
        'byStatus': {},
    }
}
from collections import Counter
out['summary']['byStatus'] = dict(Counter(str(r.get('probeStatus','')) for r in results).most_common(20))
json.dump(out, open('/workspace/.qa-artifacts/reports/admin-api-probe.json','w'), indent=2)
print(json.dumps(out['summary'], indent=2))
