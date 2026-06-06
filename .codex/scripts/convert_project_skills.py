#!/usr/bin/env python3
"""Convert repository skill material into Codex-usable skill folders."""

from __future__ import annotations

import argparse
import json
import re
import shutil
from dataclasses import dataclass
from pathlib import Path


ROOT_SKILLS = Path(".github/skills")
VENDORED_PLUGINS = Path(".github/awesome-copilot/plugins")
PROJECT_CODEX_SKILLS = Path(".codex/skills")
PROJECT_REFERENCES = PROJECT_CODEX_SKILLS / "oet-agent-references" / "references"
INDEX_PATH = Path(".codex/skills-index.json")

OET_AGENT_NAMES = {
    "oet-explorer": "OET Explorer",
    "oet-implementer": "OET Implementer",
    "oet-planner": "OET Planner",
    "oet-qa-validator": "OET QA Validator",
    "oet-reviewer": "OET Reviewer",
    "oet-security-reviewer": "OET Security Reviewer",
    "oet-ralph-coordinator": "OET Ralph Coordinator",
    "oet-omo-orchestrator": "OET OMO Orchestrator",
}

PROJECT_SKILL_DESCRIPTIONS = {
    "oet-agent-references": (
        "Use when working in the OET Prep Platform and needing to adapt the "
        "repo's local agent, prompt, or instruction material for Codex without "
        "loading the whole catalog."
    ),
    "oet-planner": (
        "Use when planning complex OET platform features, refactors, risky "
        "sequencing, PRD-to-implementation breakdowns, or broad app edits."
    ),
    "oet-implementer": (
        "Use when implementing focused code changes, bug fixes, tests, docs "
        "updates, or refactors in the OET app after context has been gathered."
    ),
    "oet-reviewer": (
        "Use when reviewing OET app changes for regressions, contract drift, "
        "missing tests, risky abstractions, or code quality issues."
    ),
    "oet-qa-validator": (
        "Use when selecting or running OET validation commands, debugging "
        "failing checks, Playwright smoke tests, lint, type, or build results."
    ),
    "oet-security-reviewer": (
        "Use when reviewing OET auth, AI gateway, uploads, scoring, rulebooks, "
        "runtime settings, secrets handling, or security-sensitive changes."
    ),
    "oet-explorer": (
        "Use when mapping unfamiliar OET code paths, docs, tests, ownership "
        "boundaries, or implementation patterns before making a change."
    ),
    "oet-ralph-coordinator": (
        "Use when coordinating multi-step OET work, handoffs, QA loops, and "
        "continuity state across agents or long-running tasks."
    ),
    "oet-omo-orchestrator": (
        "Use when coordinating the OET OMO agent reference set for planning, "
        "implementation, QA, security, visual, librarian, and oracle roles."
    ),
}


@dataclass
class SkillSource:
    name: str
    slug: str
    description: str
    path: Path
    all_paths: list[Path]
    chosen_reason: str
    warnings: list[str]


def repo_rel(path: Path) -> str:
    return path.as_posix()


def slugify(value: str) -> str:
    value = value.strip().lower()
    value = re.sub(r"[^a-z0-9]+", "-", value)
    value = re.sub(r"-+", "-", value).strip("-")
    return value[:63].strip("-") or "skill"


def parse_frontmatter(text: str) -> tuple[dict[str, str], str]:
    if not text.startswith("---"):
        return {}, text
    match = re.match(r"(?s)^---\s*\n(.*?)\n---\s*\n?", text)
    if not match:
        return {}, text
    raw = match.group(1)
    body = text[match.end() :]
    data: dict[str, str] = {}
    for key in ("name", "description"):
        key_match = re.search(rf"(?m)^{key}:\s*(.+)$", raw)
        if key_match:
            value = key_match.group(1).strip().strip("'\"")
            data[key] = value
    return data, body


def normalize_description(description: str, fallback_name: str) -> str:
    description = " ".join(description.split())
    description = re.sub(r"(?i)^use\s+when:\s*", "Use when ", description)
    description = re.sub(r"(?i)^use\s+this\s+skill\s+when\s+", "Use when ", description)
    description = re.sub(r"(?i)^use\s+when\s+use\s+this\s+skill\s+when\s+", "Use when ", description)
    if not description:
        description = f"Use when working with {fallback_name.replace('-', ' ')} workflows."
    if not description.lower().startswith("use when"):
        description = f"Use when {description[0].lower()}{description[1:]}"
    description = re.sub(r"(?i)^use\s+when\s+use\s+when\s+", "Use when ", description)
    description = description.replace("Use when  ", "Use when ")
    return description


def normalize_body(body: str) -> str:
    replacements = {
        "Claude Code": "Codex",
        "Claude": "Codex",
        "Copilot CLI": "Codex",
        "GitHub Copilot": "Codex",
        "TodoWrite": "Codex todo/progress list",
        "Task tool": "Codex multi-agent tools when available",
        "$SKILL_ROOT/": "<path-to-this-skill>/",
        "${SKILL_ROOT}/": "<path-to-this-skill>/",
        "$SKILL_ROOT": "<path-to-this-skill>",
        "${SKILL_ROOT}": "<path-to-this-skill>",
    }
    for old, new in replacements.items():
        body = body.replace(old, new)
    body = body.replace("python3 ", "python ")
    return body.strip() + "\n"


def read_skill(path: Path) -> tuple[str, str, str, list[str]]:
    text = path.read_text(encoding="utf-8", errors="replace")
    frontmatter, body = parse_frontmatter(text)
    name = frontmatter.get("name") or path.parent.name
    slug = slugify(name)
    description = normalize_description(frontmatter.get("description", ""), slug)
    warnings: list[str] = []
    if slug != name.strip().lower():
        warnings.append(f"normalized name '{name}' to '{slug}'")
    if not frontmatter.get("description"):
        warnings.append("missing source description; generated fallback")
    return name, slug, description, normalize_body(body), warnings


def collect_sources() -> list[SkillSource]:
    by_slug: dict[str, list[Path]] = {}
    for root in (ROOT_SKILLS, VENDORED_PLUGINS):
        if not root.exists():
            continue
        for path in root.rglob("SKILL.md"):
            _, slug, _, _, _ = read_skill(path)
            by_slug.setdefault(slug, []).append(path)

    sources: list[SkillSource] = []
    for slug, paths in sorted(by_slug.items()):
        root_paths = [p for p in paths if p.parts[:2] == (".github", "skills")]
        chosen = root_paths[0] if root_paths else sorted(paths, key=lambda p: repo_rel(p))[0]
        name, chosen_slug, description, _, warnings = read_skill(chosen)
        reason = "preferred root .github/skills copy" if root_paths else "no root copy; used vendored plugin copy"
        if len(paths) > 1:
            warnings.append(f"deduplicated {len(paths)} source copies")
        sources.append(
            SkillSource(
                name=name,
                slug=chosen_slug,
                description=description,
                path=chosen,
                all_paths=sorted(paths, key=lambda p: repo_rel(p)),
                chosen_reason=reason,
                warnings=warnings,
            )
        )
    return sources


def clean_dir(path: Path) -> None:
    if path.exists():
        shutil.rmtree(path)
    path.mkdir(parents=True, exist_ok=True)


def copy_supporting_files(source_dir: Path, target_dir: Path) -> list[str]:
    copied: list[str] = []
    for child in source_dir.iterdir():
        if child.name == "SKILL.md":
            continue
        destination = target_dir / child.name
        if child.is_dir():
            shutil.copytree(child, destination, ignore=shutil.ignore_patterns(".git", "node_modules"))
        else:
            shutil.copy2(child, destination)
        copied.append(child.name)
    return sorted(copied)


def write_skill(target_dir: Path, slug: str, description: str, body: str) -> None:
    target_dir.mkdir(parents=True, exist_ok=True)
    text = f"---\nname: {slug}\ndescription: {json.dumps(description)[1:-1]}\n---\n\n{body}"
    (target_dir / "SKILL.md").write_text(text, encoding="utf-8")


def convert_user_skills(sources: list[SkillSource], user_skills: Path) -> list[dict[str, object]]:
    user_skills.mkdir(parents=True, exist_ok=True)
    records: list[dict[str, object]] = []
    for source in sources:
        target_dir = user_skills / source.slug
        clean_dir(target_dir)
        _, _, description, body, warnings = read_skill(source.path)
        copied = copy_supporting_files(source.path.parent, target_dir)
        write_skill(target_dir, source.slug, description, body)
        records.append(
            {
                "name": source.slug,
                "scope": "user-wide",
                "sourcePath": repo_rel(source.path),
                "allSourcePaths": [repo_rel(p) for p in source.all_paths],
                "targetPath": str(target_dir),
                "chosenReason": source.chosen_reason,
                "supportingFiles": copied,
                "warnings": sorted(set(source.warnings + warnings)),
                "validation": "pending",
            }
        )
    return records


def read_agent_body(agent_path: Path) -> tuple[str, str]:
    text = agent_path.read_text(encoding="utf-8", errors="replace")
    frontmatter, body = parse_frontmatter(text)
    return frontmatter.get("description", ""), normalize_body(body)


def convert_project_oet_skills() -> list[dict[str, object]]:
    PROJECT_CODEX_SKILLS.mkdir(parents=True, exist_ok=True)
    records: list[dict[str, object]] = []

    reference_dir = PROJECT_CODEX_SKILLS / "oet-agent-references"
    clean_dir(reference_dir)
    PROJECT_REFERENCES.mkdir(parents=True, exist_ok=True)
    for source_root in (Path(".github/agents"), Path(".github/prompts"), Path(".github/instructions")):
        if not source_root.exists():
            continue
        copied_root = PROJECT_REFERENCES / source_root.name
        shutil.copytree(source_root, copied_root)
    write_skill(
        reference_dir,
        "oet-agent-references",
        PROJECT_SKILL_DESCRIPTIONS["oet-agent-references"],
        (
            "# OET Agent References\n\n"
            "Use this as the navigation skill for repo-local agent, prompt, and instruction material.\n\n"
            "## Workflow\n\n"
            "1. Treat `AGENTS.md` and `.github/copilot-instructions.md` as higher priority than imported references.\n"
            "2. Load only the specific reference under `references/agents`, `references/prompts`, or `references/instructions` needed for the task.\n"
            "3. Adapt role or prompt wording to Codex tools before acting; do not follow Copilot-only tool names literally.\n"
            "4. Keep OET domain docs authoritative for scoring, rulebooks, AI usage, uploads, result cards, runtime settings, admin UI, and deployment.\n"
        ),
    )
    records.append(
        {
            "name": "oet-agent-references",
            "scope": "project-local",
            "sourcePath": ".github/agents + .github/prompts + .github/instructions",
            "targetPath": repo_rel(reference_dir),
            "chosenReason": "reference bundle for non-skill local agent material",
            "supportingFiles": ["references"],
            "warnings": [],
            "validation": "pending",
        }
    )

    for slug, title in OET_AGENT_NAMES.items():
        agent_path = Path(".github/agents") / f"{slug}.agent.md"
        if not agent_path.exists():
            continue
        target_dir = PROJECT_CODEX_SKILLS / slug
        clean_dir(target_dir)
        _, body = read_agent_body(agent_path)
        if slug == "oet-qa-validator":
            body = (
                "You verify changes with the lightest sufficient host-side checks.\n\n"
                "## Constraints\n\n"
                "- Do not edit files unless explicitly asked to switch into implementation.\n"
                "- Do not hide failing checks.\n"
                "- Do not run production deploy commands.\n"
                "- Run validation directly on the Windows host via PowerShell or `cmd`, following `AGENTS.md` and `.github/instructions/validation.instructions.md`.\n"
                "- Never run validation on the production VPS.\n\n"
                "## Validation Ladder\n\n"
                "1. Parse config or schema touched by the change.\n"
                "2. Run focused unit tests for changed behavior.\n"
                "3. Run `pnpm exec tsc --noEmit` for TypeScript surface changes.\n"
                "4. Run `pnpm run lint` for frontend/shared code changes.\n"
                "5. Run `pnpm test` when shared logic or broad UI behavior changed.\n"
                "6. Run `pnpm run backend:build` and `pnpm run backend:test` for backend changes.\n"
                "7. Run Playwright smoke/E2E only when runtime user flows are affected.\n\n"
                "## Output\n\n"
                "Return commands run, pass/fail results, and the smallest next validation if more confidence is needed.\n"
            )
        body = (
            f"# {title}\n\n"
            "This is a Codex-compatible conversion of the repo-local agent role. "
            "Apply it only after reading the current repo instructions and relevant docs.\n\n"
            + re.sub(r"(?s)^# .+?\n", "", body, count=1).strip()
            + "\n"
        )
        body = body.replace("local Docker containers", "the Windows host validation ladder from AGENTS.md")
        body = body.replace("using local Docker for heavy checks", "using the validation ladder from AGENTS.md")
        write_skill(target_dir, slug, PROJECT_SKILL_DESCRIPTIONS[slug], body)
        records.append(
            {
                "name": slug,
                "scope": "project-local",
                "sourcePath": repo_rel(agent_path),
                "targetPath": repo_rel(target_dir),
                "chosenReason": "converted durable OET agent role into Codex skill",
                "supportingFiles": [],
                "warnings": [],
                "validation": "pending",
            }
        )
    return records


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--user-skills",
        default=str(Path.home() / ".codex" / "skills"),
        help="User-wide Codex skills directory.",
    )
    parser.add_argument("--inventory-only", action="store_true")
    args = parser.parse_args()

    sources = collect_sources()
    duplicate_groups = [s for s in sources if len(s.all_paths) > 1]
    inventory = {
        "totalSkillFiles": sum(1 for _ in ROOT_SKILLS.rglob("SKILL.md"))
        + (sum(1 for _ in VENDORED_PLUGINS.rglob("SKILL.md")) if VENDORED_PLUGINS.exists() else 0),
        "uniqueSkillNames": len(sources),
        "duplicateNames": len(duplicate_groups),
        "projectLocalAgentFiles": len(list(Path(".github/agents").glob("*.agent.md"))) if Path(".github/agents").exists() else 0,
        "promptFiles": len(list(Path(".github/prompts").glob("*.prompt.md"))) if Path(".github/prompts").exists() else 0,
        "instructionFiles": len(list(Path(".github/instructions").glob("*.instructions.md"))) if Path(".github/instructions").exists() else 0,
    }

    if args.inventory_only:
        print(json.dumps(inventory, indent=2))
        return 0

    PROJECT_CODEX_SKILLS.mkdir(parents=True, exist_ok=True)
    records = convert_project_oet_skills()
    records.extend(convert_user_skills(sources, Path(args.user_skills)))

    INDEX_PATH.parent.mkdir(parents=True, exist_ok=True)
    INDEX_PATH.write_text(
        json.dumps(
            {
                "generatedBy": ".codex/scripts/convert_project_skills.py",
                "inventory": inventory,
                "rules": {
                    "dedupe": "prefer .github/skills over vendored plugin copies",
                    "frontmatter": "Codex name and description only",
                    "projectLocal": "OET role and reference skills",
                    "userWide": "curated unique generic skills",
                },
                "records": records,
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )

    print(json.dumps({"inventory": inventory, "installed": len(records)}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
