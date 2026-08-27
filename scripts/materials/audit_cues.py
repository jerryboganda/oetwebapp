import json

data = json.load(open("scripts/materials/test_audio_silences.json"))

for item in data:
    tnum = item["test_num"]
    fname = item["filename"]
    dur = item["total_duration"]
    sils = item["silences"]
    print(f"\n=======================================================")
    print(f"TEST {tnum:02d}: {fname} (Total: {dur:.1f}s = {dur/60:.1f}min)")
    print(f"=======================================================")
    for i, s in enumerate(sils):
        st, en, d = s["start"], s["end"], s["dur"]
        # Print silences >= 4s or significant
        if d >= 3.0:
            print(f"  [{i:02d}] {st:6.1f}s ({st/60:4.1f}m) -> {en:6.1f}s ({en/60:4.1f}m) | dur: {d:5.1f}s")
