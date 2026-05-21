#!/usr/bin/env python3
"""Audit ContentItem instantiations in SeedData for missing PublishedRevisionId."""
import re
import sys

path = sys.argv[1] if len(sys.argv) > 1 else 'backend/src/OetLearner.Api/Services/SeedData.cs'
content = open(path).read()

# Match `new ContentItem { ... }` blocks; balance braces.
results = []
i = 0
n = len(content)
key = 'new ContentItem'
while True:
    idx = content.find(key, i)
    if idx < 0:
        break
    # Find opening brace
    j = content.find('{', idx)
    if j < 0:
        break
    depth = 1
    k = j + 1
    while k < n and depth > 0:
        if content[k] == '{':
            depth += 1
        elif content[k] == '}':
            depth -= 1
        k += 1
    body = content[j:k]
    line_num = content.count('\n', 0, idx) + 1
    has_pub = 'PublishedRevisionId' in body
    idm = re.search(r'Id\s*=\s*"([^"]+)"', body)
    results.append((line_num, idm.group(1) if idm else '?', has_pub))
    i = k

total = len(results)
missing = [r for r in results if not r[2]]
print(f'Total ContentItem instantiations: {total}')
print(f'Missing PublishedRevisionId: {len(missing)}')
for line_num, id_, _ in missing:
    print(f'  line {line_num}: Id={id_}')
