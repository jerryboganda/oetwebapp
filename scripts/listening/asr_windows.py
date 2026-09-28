#!/usr/bin/env python3
"""Speech-to-text for Listening QA windows. Runs ONLY on GitHub Actions (repo compute policy).

usage: asr_windows.py <manifest.json> <out.json> [model]

manifest: [{"key": "...", "file": "path.mp3", "offset": 0.0}]
out:      {key: {"offset": .., "duration": .., "segments": [{"start": abs_s, "end": abs_s, "text": ".."}]}}

Never prints transcript text (job logs of a public repo are public); it prints keys and timings only.
"""
import json
import sys
import time

from faster_whisper import WhisperModel

manifest = json.load(open(sys.argv[1], encoding="utf-8"))
out_path = sys.argv[2]
model_name = sys.argv[3] if len(sys.argv) > 3 else "small.en"

model = WhisperModel(model_name, device="cpu", compute_type="int8", cpu_threads=4)
results = {}
for i, m in enumerate(manifest, 1):
    t0 = time.time()
    segments, info = model.transcribe(
        m["file"],
        language="en",
        beam_size=1,
        vad_filter=False,
        condition_on_previous_text=False,
    )
    segs = [
        {"start": round(s.start + m["offset"], 2), "end": round(s.end + m["offset"], 2), "text": s.text.strip()}
        for s in segments
    ]
    results[m["key"]] = {"offset": m["offset"], "duration": round(info.duration, 2), "segments": segs}
    print(f"[{i}/{len(manifest)}] {m['key']} audio={info.duration:.0f}s took={time.time() - t0:.0f}s segments={len(segs)}", flush=True)

json.dump(results, open(out_path, "w", encoding="utf-8"))
