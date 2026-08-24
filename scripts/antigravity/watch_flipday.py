"""Flip-day watcher: is Mode C available yet?

Checks (monthly, or before each release):
  1. Latest google-antigravity version on PyPI vs the pin in pyproject.toml.
  2. State of google-antigravity/antigravity-sdk-python issue #20 (SDK OAuth).

Stdlib only. Network optional: failures print as UNKNOWN, never raise.

Usage:
  agent-gateway\\.venv\\Scripts\\python.exe scripts\\antigravity\\watch_flipday.py
"""
from __future__ import annotations

import json
import re
import sys
import urllib.request
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
PYPROJECT = REPO_ROOT / "agent-gateway" / "pyproject.toml"
ISSUE_URL = "https://api.github.com/repos/google-antigravity/antigravity-sdk-python/issues/20"
PYPINP_URL = "https://pypi.org/pypi/google-antigravity/json"


def _get_json(url: str) -> object | None:
    try:
        req = urllib.request.Request(url, headers={"User-Agent": "oet-flipday-watch", "Accept": "application/vnd.github+json"})
        with urllib.request.urlopen(req, timeout=15) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except Exception as exc:  # noqa: BLE001 - diagnostics must not crash
        print(f"  fetch failed ({type(exc).__name__})")
        return None


def pinned_version() -> str:
    text = PYPROJECT.read_text(encoding="utf-8")
    m = re.search(r"google-antigravity>=([\w.]+)", text)
    return m.group(1) if m else "?"


def main() -> int:
    print("== Antigravity flip-day status ==")
    pin = pinned_version()
    data = _get_json(PYPINP_URL)
    if isinstance(data, dict):
        latest = data.get("info", {}).get("version", "?")
        flag = "" if latest == pin else "  <-- UPDATE AVAILABLE"
        print(f"PyPI latest : {latest}{flag}")
        print(f"Pinned      : {pin}")
        releases = data.get("releases", {})
        recent = sorted(releases)[-5:]
        print(f"Recent tags : {', '.join(recent)}")
    else:
        print("PyPI latest : UNKNOWN")
        print(f"Pinned      : {pin}")

    issue = _get_json(ISSUE_URL)
    if isinstance(issue, dict):
        state = issue.get("state", "?")
        closed = issue.get("closed_at")
        title = issue.get("title", "?")
        answer = "SDK OAuth SHIPPED — run the flip-day checklist!" if state == "closed" and closed else (
            "still open" if state == "open" else f"state={state}"
        )
        print(f"Issue #20   : {answer}  ({title[:70]})")
        flip_ready = state == "closed" and bool(closed)
    else:
        print("Issue #20   : UNKNOWN (check github.com manually)")
        flip_ready = False

    print()
    print(f"FLIP-DAY READY: {'YES - follow docs/antigravity/flip-day-checklist.md' if flip_ready else 'NOT YET'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
