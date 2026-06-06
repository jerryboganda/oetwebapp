#!/usr/bin/env python3
"""Validate Codex skill folders generated for this project."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path


FRONTMATTER_RE = re.compile(r"(?s)^---\s*\n(.*?)\n---\s*\n")
NAME_RE = re.compile(r"^[a-z0-9][a-z0-9-]{0,62}$")
BAD_PATTERNS = [
    "Claude Code",
    "TodoWrite",
    "Task tool",
    "Copilot CLI",
    "$SKILL_ROOT",
    "${SKILL_ROOT}",
]


def parse_skill(path: Path) -> tuple[dict[str, str], str]:
    text = path.read_text(encoding="utf-8", errors="replace")
    match = FRONTMATTER_RE.match(text)
    if not match:
        return {}, text
    raw = match.group(1)
    body = text[match.end() :]
    fields: dict[str, str] = {}
    for line in raw.splitlines():
        if ":" not in line:
            continue
        key, value = line.split(":", 1)
        fields[key.strip()] = value.strip().strip("'\"")
    return fields, body


def validate_scope(root: Path) -> list[dict[str, object]]:
    results: list[dict[str, object]] = []
    if not root.exists():
        return [
            {
                "path": str(root),
                "name": None,
                "ok": False,
                "errors": ["skills root does not exist"],
                "warnings": [],
            }
        ]
    seen: dict[str, Path] = {}
    for skill_dir in sorted([p for p in root.iterdir() if p.is_dir() and not p.name.startswith(".")]):
        skill_path = skill_dir / "SKILL.md"
        errors: list[str] = []
        warnings: list[str] = []
        if not skill_path.exists():
            errors.append("missing SKILL.md")
            fields, body = {}, ""
        else:
            fields, body = parse_skill(skill_path)
        name = fields.get("name", "")
        description = fields.get("description", "")
        extra_fields = sorted(set(fields) - {"name", "description"})
        if extra_fields:
            errors.append(f"unsupported frontmatter fields: {', '.join(extra_fields)}")
        if not NAME_RE.match(name):
            errors.append("invalid or missing name")
        if name != skill_dir.name:
            errors.append(f"name '{name}' does not match folder '{skill_dir.name}'")
        if not description:
            errors.append("missing description")
        elif not description.lower().startswith("use when"):
            warnings.append("description does not start with 'Use when'")
        if name:
            if name in seen:
                errors.append(f"duplicate skill name also in {seen[name]}")
            seen[name] = skill_dir
        for pattern in BAD_PATTERNS:
            if pattern in body:
                warnings.append(f"body contains platform-specific reference: {pattern}")
        results.append(
            {
                "path": str(skill_dir),
                "name": name or None,
                "ok": not errors,
                "errors": errors,
                "warnings": warnings,
            }
        )
    return results


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--project-skills", default=".codex/skills")
    parser.add_argument("--user-skills", default=str(Path.home() / ".codex" / "skills"))
    parser.add_argument("--write-report", default=".codex/skills-validation.json")
    args = parser.parse_args()

    report = {
        "project": validate_scope(Path(args.project_skills)),
        "user": validate_scope(Path(args.user_skills)),
    }
    summary = {
        scope: {
            "total": len(items),
            "ok": sum(1 for item in items if item["ok"]),
            "errors": sum(len(item["errors"]) for item in items),
            "warnings": sum(len(item["warnings"]) for item in items),
        }
        for scope, items in report.items()
    }
    output = {"summary": summary, "report": report}
    Path(args.write_report).parent.mkdir(parents=True, exist_ok=True)
    Path(args.write_report).write_text(json.dumps(output, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(summary, indent=2))
    return 1 if any(value["errors"] for value in summary.values()) else 0


if __name__ == "__main__":
    raise SystemExit(main())
