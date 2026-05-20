#!/usr/bin/env python3
"""Idempotently patch retry-listening-tts.mjs to log full attach error body."""
import sys, pathlib
p = pathlib.Path("/opt/oetwebapp/scripts/admin/retry-listening-tts.mjs")
s = p.read_text()
marker = "[DEBUG attach"
if marker in s:
    print("already patched")
    sys.exit(0)
old = "  throw new Error(`attach ${partCode} failed: ${r.status} ${bodyStr.slice(0, 300)}`);\n"
if old not in s:
    print("PATTERN NOT FOUND")
    sys.exit(1)
new = (
    "  const _hdrs = r.headers ? JSON.stringify(r.headers).slice(0,300) : '';\n"
    "  console.log(`    [DEBUG attach ${partCode}] status=${r.status} bodyLen=${bodyStr.length} body=${bodyStr.slice(0,500)} headers=${_hdrs}`);\n"
    "  throw new Error(`attach ${partCode} failed: ${r.status} ${bodyStr.slice(0, 300)}`);\n"
)
p.write_text(s.replace(old, new, 1))
print("PATCHED")
