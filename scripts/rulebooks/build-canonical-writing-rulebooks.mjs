#!/usr/bin/env node
// Rebuilds rulebooks/writing/<profession>/rulebook.v1.json for the six
// professions with live Writing content, from the canonical
// docs/canonical-rules/OET_AI_Rules_Master.jsonl registry. See
// docs/canonical-rules/README.md for the severity-mapping rationale.
//
// Usage: node scripts/rulebooks/build-canonical-writing-rulebooks.mjs [--check]
//   --check   verify the working-tree files already match the build output
//             (exit 1 with a diff summary if not) instead of writing them.

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "..");
const REGISTRY_PATH = path.join(ROOT, "docs/canonical-rules/OET_AI_Rules_Master.jsonl");
const CHECK_ONLY = process.argv.includes("--check");

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

const CRITICAL_CLASSIFICATIONS = new Set(["Safety", "Hard Rule", "Hard Strategy", "Validated Override"]);

function severityOf(rule) {
  if (rule.authority === "OET_OFFICIAL") return "critical";
  if (CRITICAL_CLASSIFICATIONS.has(rule.classification)) return "critical";
  return "major";
}

function slugSection(title) {
  return title
    .toLowerCase()
    .replace(/&/g, "and")
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
}

function loadRegistry() {
  const lines = fs.readFileSync(REGISTRY_PATH, "utf8").split(/\r?\n/).filter(Boolean);
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
    };
    const exemplarPhrases = r.good_example && r.good_example.trim() ? [r.good_example.trim()] : undefined;
    return {
      id: r.id,
      section: r.section,
      severity: severityOf(r),
      title: r.title,
      body: r.canonical_rule,
      appliesTo: "all",
      enforcement: "ai-grounded",
      ...(exemplarPhrases ? { exemplarPhrases } : {}),
      params,
    };
  });

  return {
    version: "2.0.0-canonical",
    kind: "writing",
    profession: folder,
    publishedAt: "2026-08-31T00:00:00Z",
    authoritySource:
      "OET AI Rules Master — Canonical Registry v1.0 (2026-08-31), sha256:7f6446d3… " +
      "(supersedes the legacy 172-rule provenance-only set; see docs/canonical-rules/README.md)",
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
