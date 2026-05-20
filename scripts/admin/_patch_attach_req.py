#!/usr/bin/env python3
"""Add a console.log dumping the attach request body before the fetch."""
import pathlib, sys
p = pathlib.Path("/opt/oetwebapp/scripts/admin/retry-listening-tts.mjs")
s = p.read_text()
marker = "[DEBUG attach REQ]"
if marker in s:
    print("already patched"); sys.exit(0)
old = "  const r = await adminFetch(`/v1/admin/papers/${paperId}/assets`, { method: 'POST', body });\n"
if old not in s:
    print("REQ PATTERN NOT FOUND"); sys.exit(1)
new = (
    "  console.log(`    [DEBUG attach REQ] paper=${paperId} body=${JSON.stringify(body)}`);\n"
    "  const r = await adminFetch(`/v1/admin/papers/${paperId}/assets`, { method: 'POST', body });\n"
)
p.write_text(s.replace(old, new, 1))
print("PATCHED REQ")
