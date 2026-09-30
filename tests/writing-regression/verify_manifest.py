"""CI verifier: the recorded regression manifest must match the CURRENT prompt source
and report a passing run. Fails (exit 1) when the prompt/rules/model changed without a
fresh passing regression, so a Writing prompt change can never silently deploy.

Run from tests/writing-regression:
  python verify_manifest.py
"""
import json
import os
import sys

import prompt_builder as pb

HERE = os.path.dirname(os.path.abspath(__file__))
MANIFEST = os.path.join(HERE, "manifest.json")


def main():
    if not os.path.exists(MANIFEST):
        print("FAIL: no regression manifest at %s. Run the regression and stamp it." % MANIFEST)
        sys.exit(1)
    m = json.loads(open(MANIFEST, encoding="utf-8").read())

    current_version = pb.HOUSE_VERSION
    problems = []

    if m.get("houseStyleVersion") != current_version:
        problems.append("house-style version changed: manifest=%s current=%s"
                        % (m.get("houseStyleVersion"), current_version))

    if m.get("runsPass") is not True:
        problems.append("manifest does not record a passing run (runsPass=%s)" % m.get("runsPass"))

    recorded = m.get("promptSha256") or {}
    for key, recorded_sha in recorded.items():
        prof, lt = key.split("|")
        current_sha = pb.prompt_sha256(prof, lt)
        if current_sha != recorded_sha:
            problems.append("prompt hash mismatch for %s: manifest=%s current=%s"
                            % (key, recorded_sha[:12], current_sha[:12]))

    if problems:
        print("FAIL — Writing regression gate:")
        for p in problems:
            print("  - " + p)
        print("\nThe Writing prompt/rules/model changed without a fresh passing regression.")
        print("Run locally (owner subscription): run_regression.py --execute, check_regression.py,")
        print("stamp_manifest.py --report-last-run, then commit the updated manifest.")
        sys.exit(1)

    print("PASS — regression manifest matches the current prompt (house %s, model %s/%s)."
          % (current_version, m.get("model"), m.get("effort")))
    sys.exit(0)


if __name__ == "__main__":
    main()
