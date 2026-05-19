#!/usr/bin/env python3
import sys
p = sys.argv[1]
with open(p, 'r', encoding='utf-8') as f:
    s = f.read()
old1 = "    retries = 3,\n    format = 'mp3',\n  } = opts;\n  if (!CONFIG.ai.apiKey) throw new Error('AI__ApiKey not set');\n  if (!text || !text.trim()) throw new Error('aiTts: empty text');\n\n  const body = { model, input: text, voice, response_format: format };"
new1 = "    retries = 3,\n    format = 'mp3',\n    instructions = 'A calm, clear, professional native English voice suitable for OET listening practice.',\n  } = opts;\n  if (!CONFIG.ai.apiKey) throw new Error('AI__ApiKey not set');\n  if (!text || !text.trim()) throw new Error('aiTts: empty text');\n\n  // Qwen3 voice-design accepts ONLY {model, input, voice, instructions}.\n  // Adding response_format/speed/stream -> HTTP 400. Missing instructions -> 400.\n  // Output is always WAV PCM 16-bit mono 24kHz; orchestrator sniffs container.\n  const body = { model, input: text, voice, instructions };\n  void format;"
if old1 not in s:
    print('OLD_NOT_FOUND', file=sys.stderr); sys.exit(2)
s2 = s.replace(old1, new1, 1)
with open(p, 'w', encoding='utf-8') as f:
    f.write(s2)
print('OK')
