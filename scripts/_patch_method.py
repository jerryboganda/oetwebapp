import sys
p = r'c:\Users\Dr Faisal Maqsood PC\Desktop\New OET Web App\scripts\admin\republish-listening-drafts.mjs'
with open(p, 'rb') as f:
    t = f.read()
n = t.replace(b"method: 'PATCH'", b"method: 'PUT'")
assert n != t, 'no change'
with open(p, 'wb') as f:
    f.write(n)
print('OK')
