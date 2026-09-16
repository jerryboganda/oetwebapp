#!/usr/bin/env python3
"""
whisper_cue_scan.py — word-timestamped transcription window for Listening
audio-boundary repair.

Transcribes a window of an audio file with faster-whisper (word timestamps,
CPU int8) and prints JSON to stdout:

    {"offset": <sec>, "duration": <sec>, "model": "small",
     "words": [{"w": "extract", "s": 123.45, "e": 123.78}, ...],
     "text": "..."}

Word times are RELATIVE TO THE FULL FILE (window offset added), 3-decimal
seconds — precise enough to cut MP3 at the Extract Two transition cue.

Usage:
    python scripts/listening/whisper_cue_scan.py --audio FILE --offset 300 --duration 240 [--model small]

Requires: faster-whisper (pip), ffmpeg on PATH.
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
    ap.add_argument("--offset", type=float, default=0.0)
    ap.add_argument("--duration", type=float, default=0.0, help="0 = to end of file")
    ap.add_argument("--model", default="small")
    ap.add_argument("--language", default="en")
    args = ap.parse_args()

    if not os.path.isfile(args.audio):
        print(json.dumps({"error": f"file not found: {args.audio}"}))
        return 2

    # 16k mono window for whisper. Input seek (-ss before -i) is fine here:
    # MP3 frame precision (~26ms) is well inside the cue-cut tolerance.
    ff = ["ffmpeg", "-hide_banner", "-loglevel", "error", "-y"]
    if args.offset > 0:
        ff += ["-ss", f"{args.offset:.3f}"]
    ff += ["-i", args.audio]
    if args.duration > 0:
        ff += ["-t", f"{args.duration:.3f}"]
    ff += ["-ar", "16000", "-ac", "1"]
    tmp = tempfile.NamedTemporaryFile(suffix=".wav", delete=False)
    tmp.close()
    ff += [tmp.name]
    try:
        r = subprocess.run(ff, capture_output=True, text=True)
        if r.returncode != 0:
            print(json.dumps({"error": f"ffmpeg failed: {r.stderr[-400:]}"}))
            return 2

        from faster_whisper import WhisperModel

        model = WhisperModel(args.model, device="cpu", compute_type="int8")
        segments, _info = model.transcribe(
            tmp.name,
            language=args.language,
            word_timestamps=True,
            vad_filter=False,
        )
        words = []
        texts = []
        for seg in segments:
            texts.append(seg.text.strip())
            for w in (seg.words or []):
                if w.start is None:
                    continue
                words.append({
                    "w": (w.word or "").strip(),
                    "s": round(args.offset + w.start, 3),
                    "e": round(args.offset + (w.end if w.end is not None else w.start), 3),
                })
        print(json.dumps({
            "offset": args.offset,
            "duration": args.duration,
            "model": args.model,
            "words": words,
            "text": " ".join(texts),
        }))
        return 0
    finally:
        try:
            os.unlink(tmp.name)
        except OSError:
            pass


if __name__ == "__main__":
    sys.exit(main())
