#!/usr/bin/env node
// Rebuilds rulebooks/writing/<profession>/rulebook.v1.json for the six
// professions with live Writing content, from the canonical
// docs/canonical-rules/OET_AI_Rules_Master.jsonl registry. See
// docs/canonical-rules/README.md for the severity-mapping rationale and the
// optional owner Rev8 row fields (severity, applies_to, check_ids).
//
// Usage: node scripts/rulebooks/build-canonical-writing-rulebooks.mjs [--check]
//   --check   verify the working-tree files already match the build output
//             (exit 1 with a diff summary if not) instead of writing them.

import { createHash } from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "..");
const REGISTRY_PATH = path.join(ROOT, "docs/canonical-rules/OET_AI_Rules_Master.jsonl");
const CHECK_ONLY = process.argv.includes("--check");

// SHA-256 of the exact registry bytes this pack version was reviewed against
// (v1.1-rev8). The build refuses any other registry content, so an edited
// registry can never ship under a stale authoritySource hash again (the v1.0
// hash went stale silently after the 10 Sep 2026 G-W-116 amendment). After
// editing the registry: update this constant, the version fields in
// buildRulebook() and docs/canonical-rules/README.md, then rebuild.
const REGISTRY_SHA256 = "f0ff3ce01873ceb5d53c6736fed658fca5bab47b5d2bf9753a7d73cb3d153d84";

// Registry `profession` value -> repo folder name. Only these six have live
// Writing tasks today; every other registry profession is intentionally
// dropped here (see README "What this is for").
const SUPPORTED_PROFESSIONS = {
  Medicine: "medicine",
  Nursing: "nursing",
  Dentistry: "dentistry",
  Pharmacy: "pharmacy",
  Physiotherapy: "physiotherapy",
  Radiography: "radiography",
};

// Owner Rev8 (11 Sep 2026) classifications: "Owner Override" is critical;
// "Owner Clarification", "Exception", "Avoid", "Acceptable Alternative" and
// "Preferred Style" fall through to major. An explicit row `severity` wins.
const CRITICAL_CLASSIFICATIONS = new Set(["Safety", "Hard Rule", "Hard Strategy", "Validated Override", "Owner Override"]);
// Canonical packs have no minor/info tier (README "Severity mapping").
const ROW_SEVERITIES = new Set(["critical", "major"]);

function severityOf(rule) {
  if (rule.severity !== undefined) {
    if (!ROW_SEVERITIES.has(rule.severity)) {
      throw new Error(`${rule.profession} ${rule.id}: severity must be "critical" or "major", got ${JSON.stringify(rule.severity)}`);
    }
    return rule.severity;
  }
  if (rule.authority === "OET_OFFICIAL") return "critical";
  if (CRITICAL_CLASSIFICATIONS.has(rule.classification)) return "critical";
  return "major";
}

// Optional row `applies_to`: "all" (default) or a non-empty array of the legacy
// letter-type tokens WritingLetterTypeTaxonomy.ToLegacyLetterType produces.
function appliesToOf(rule) {
  const value = rule.applies_to;
  if (value === undefined || value === "all") return "all";
  if (Array.isArray(value) && value.length > 0 && value.every((t) => typeof t === "string" && t.length > 0)) return value;
  throw new Error(`${rule.profession} ${rule.id}: applies_to must be "all" or a non-empty string array, got ${JSON.stringify(value)}`);
}

// Optional row `check_ids`: the WritingRuleEngine detectors that enforce the
// rule. Carried as params.checkIds for audit only — never a top-level checkId,
// so those detectors keep running as the always-on BUILTIN battery and their
// BUILTIN.<checkId> finding ids stay stable.
function checkIdsOf(rule) {
  const value = rule.check_ids;
  if (value === undefined) return undefined;
  if (!Array.isArray(value) || !value.every((t) => typeof t === "string" && t.length > 0)) {
    throw new Error(`${rule.profession} ${rule.id}: check_ids must be a string array, got ${JSON.stringify(value)}`);
  }
  return value.length ? value : undefined;
}

function slugSection(title) {
  return title
    .toLowerCase()
    .replace(/&/g, "and")
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
}

function loadRegistry() {
  const raw = fs.readFileSync(REGISTRY_PATH);
  const sha256 = createHash("sha256").update(raw).digest("hex");
  if (sha256 !== REGISTRY_SHA256) {
    throw new Error(
      `Registry sha256 ${sha256} != REGISTRY_SHA256 ${REGISTRY_SHA256}: the registry was edited. ` +
        "Update REGISTRY_SHA256, the version fields and docs/canonical-rules/README.md, then rebuild.",
    );
  }
  const lines = raw.toString("utf8").split(/\r?\n/).filter(Boolean);
  return lines.map((line) => JSON.parse(line));
}

function buildRulebook(profession, folder, rules, existing) {
  const sections = [];
  const seenSection = new Set();
  for (const r of rules) {
    if (!seenSection.has(r.section)) {
      seenSection.add(r.section);
      sections.push({ id: slugSection(r.section), title: r.section, order: sections.length + 1 });
    }
  }

  const mappedRules = rules.map((r) => {
    const params = {
      classification: r.classification,
      authority: r.authority,
      evidence: r.evidence || undefined,
      aiGradingLogic: r.ai_grading_logic || undefined,
      sourceRefs: r.source_refs && r.source_refs.length ? r.source_refs : undefined,
      notes: r.notes || undefined,
      checkIds: checkIdsOf(r),
    };
    const exemplarPhrases = r.good_example && r.good_example.trim() ? [r.good_example.trim()] : undefined;
    return {
      id: r.id,
      section: r.section,
      severity: severityOf(r),
      title: r.title,
      body: r.canonical_rule,
      appliesTo: appliesToOf(r),
      enforcement: "ai-grounded",
      ...(exemplarPhrases ? { exemplarPhrases } : {}),
      params,
    };
  });

  return {
    version: "2.1.0-canonical-rev8",
    kind: "writing",
    profession: folder,
    publishedAt: "2026-09-11T00:00:00Z",
    authoritySource:
      "OET AI Rules Master — Canonical Registry v1.1-rev8 (2026-09-11: v1.0 of 2026-08-31 + owner Rev8 " +
      `additions OWN-W-001..038 and supersession amendments), sha256:${REGISTRY_SHA256}`,
    sections,
    rules: mappedRules,
    // Layout/structural tables and profession-specific display metadata are not
    // modelled in the canonical registry — carry the existing file's copies
    // forward unchanged so nothing that other code/tests read is silently lost.
    ...(existing && existing.tables !== undefined ? { tables: existing.tables } : {}),
    ...(existing && existing.professionSpecific !== undefined ? { professionSpecific: existing.professionSpecific } : {}),
  };
}

function main() {
  const registry = loadRegistry();
  const activeWriting = registry.filter((r) => r.skill === "Writing" && r.active_for_ai === true);
  if (activeWriting.length === 0) throw new Error("No active Writing rules found in registry — refusing to write empty rulebooks.");

  let failures = 0;
  for (const [regProfession, folder] of Object.entries(SUPPORTED_PROFESSIONS)) {
    const rules = activeWriting.filter((r) => r.profession === regProfession);
    if (rules.length === 0) {
      console.error(`FATAL: zero active Writing rules for ${regProfession} — refusing to overwrite ${folder} with an empty set.`);
      failures++;
      continue;
    }

    const targetPath = path.join(ROOT, "rulebooks/writing", folder, "rulebook.v1.json");
    const existing = fs.existsSync(targetPath) ? JSON.parse(fs.readFileSync(targetPath, "utf8")) : null;
    const book = buildRulebook(regProfession, folder, rules, existing);
    const serialized = JSON.stringify(book, null, 2) + "\n";

    if (CHECK_ONLY) {
      const current = existing ? fs.readFileSync(targetPath, "utf8") : null;
      if (current !== serialized) {
        console.error(`STALE: ${folder} — working-tree file does not match canonical build output. Re-run without --check.`);
        failures++;
      } else {
        console.log(`OK: ${folder} (${rules.length} active rules, up to date)`);
      }
      continue;
    }

    fs.writeFileSync(targetPath, serialized);
    console.log(`wrote ${targetPath} (${rules.length} active rules)`);
  }

  if (failures > 0) {
    console.error(`${failures} profession(s) failed.`);
    process.exit(1);
  }
}

main();
