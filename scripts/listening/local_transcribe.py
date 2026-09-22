#!/usr/bin/env python3
"""
local_transcribe.py — local faster-whisper transcription for the Listening
audio-boundary pipeline, in the SAME JSON SCHEMA as the QA transcribe endpoint
(POST /v1/admin/listening/qa/transcribe) so every downstream consumer
(plan phase, post-cut verify, content verifier) works unchanged:

    {"provider": "faster-whisper-local", "language": "en", "model": "small",
     "wordCount": <int>, "meanConfidence": <float>,
     "segments": [{"speaker": "candidate", "startMs": <int>, "endMs": <int>,
                   "text": "...", "confidence": <float|null>,
                   "words": [{"text": "...", "startMs": <int>, "endMs": <int>}, ...]}]}

Timings are RELATIVE TO THE WINDOW START (the callers add their own manifest
offsets). faster-whisper gives real word timestamps — strictly better than the
endpoint's segment-spread fallback.

Usage:
    python scripts/listening/local_transcribe.py --audio FILE [--model small] [--language en]

Writes nothing itself; prints the JSON document to stdout. Requires
faster-whisper (pip) + ffmpeg on PATH.
"""

import argparse
import json
import os
import subprocess
import sys
import tempfile


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--audio", required=True)
    ap.add_argument("--model", default="small")
    ap.add_argument("--language", default="en")
    args = ap.parse_args()

    if not os.path.isfile(args.audio):
        print(json.dumps({"error": f"file not found: {args.audio}"}))
        return 2

    ff = ["ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
          "-i", args.audio, "-ar", "16000", "-ac", "1"]
    tmp = tempfile.NamedTemporaryFile(suffix=".wav", delete=False)
    tmp.close()
    ff += [tmp.name]
    try:
        r = subprocess.run(ff, capture_output=True, text=True)
        if r.returncode != 0:
            print(json.dumps({"error": f"ffmpeg failed: {r.stderr[-400:]}"}))
            return 2

        from faster_whisper import WhisperModel

        model = WhisperModel(args.model, device="cpu", compute_type="int8", cpu_threads=8)
        # NO VAD, no batched pipeline: both remove silence and remap word
        # timestamps — a ~5s drift was observed around a 10s pre-cue silence,
        # which shifts the Extract Two cut point. whisper_cue_scan.py (the
        # proven local path) uses vad_filter=False for the same reason.
        segments, info = model.transcribe(
            tmp.name,
            language=args.language,
            word_timestamps=True,
            vad_filter=False,
        )

        out_segments = []
        texts = []
        confs = []
        total_words = 0
        for seg in segments:
            text = (seg.text or "").strip()
            texts.append(text)
            seg_conf = None
            if getattr(seg, "avg_logprob", None) is not None:
                import math
                seg_conf = max(0.0, min(1.0, math.exp(seg.avg_logprob)))
                confs.append(seg_conf)
            words = []
            for w in (seg.words or []):
                token = (w.word or "").strip()
                if not token or w.start is None:
                    continue
                words.append({
                    "text": token,
                    "startMs": int(round(w.start * 1000)),
                    "endMs": int(round((w.end if w.end is not None else w.start) * 1000)),
                })
                total_words += 1
            out_segments.append({
                "speaker": "candidate",
                "startMs": int(round((seg.start or 0) * 1000)),
                "endMs": int(round((seg.end or 0) * 1000)),
                "text": text,
                "confidence": seg_conf,
                "words": words,
            })

        print(json.dumps({
            "provider": "faster-whisper-local",
            "language": getattr(info, "language", None) or args.language,
            "model": args.model,
            "wordCount": total_words,
            "meanConfidence": round(sum(confs) / len(confs), 4) if confs else 0.85,
            "segments": out_segments,
        }))
        return 0
    finally:
        try:
            os.unlink(tmp.name)
        except OSError:
            pass


if __name__ == "__main__":
    sys.exit(main())
