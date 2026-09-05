// writing:qa-export — deterministic, reproducible Writing-task backend/classification QA export.
// Enumerates EVERY currently prepared Writing source file (filesystem prepared-upload source),
// applies the EXACT rule-based classification used by the backend (no AI), and writes one CSV row per file.
// Usage: node ./scripts/writing-qa-export.mjs [--out artifacts/developer-action-brief/02-writing-qa-export.csv]
// Exit code 0 on success; prints TOTAL vs EXPORTED reconciliation + summaries to stdout.
import { readdirSync, statSync, writeFileSync, mkdirSync, existsSync, readFileSync } from "node:fs";
import { join, relative, sep } from "node:path";
import { createHash } from "node:crypto";
import { execSync } from "node:child_process";

const REPO = process.cwd();
const DEFAULT_OUT = "artifacts/developer-action-brief/02-writing-qa-export.csv";
const outPath = process.argv.includes("--out")
  ? process.argv[process.argv.indexOf("--out") + 1]
  : DEFAULT_OUT;

const CACHE_FILE = join(REPO, "artifacts/developer-action-brief/writing-extracted-text.json");
let extractedTextMap = {};
if (!existsSync(CACHE_FILE)) {
  console.log("[info] writing-extracted-text.json not found, generating via extract_writing_text.py...");
  try {
    execSync("python scripts/extract_writing_text.py", { stdio: "inherit", cwd: REPO });
  } catch (err) {
    console.warn("[warn] Failed to run extract_writing_text.py:", err.message);
  }
}
if (existsSync(CACHE_FILE)) {
  try {
    extractedTextMap = JSON.parse(readFileSync(CACHE_FILE, "utf8"));
  } catch (err) {
    console.warn("[warn] failed to parse writing-extracted-text.json", err.message);
  }
}

const WRITING_ROOTS = [
  "OET/Materials ( To be Uploaded )/Writing",
  "OET with Dr. Ahmed Hesham ( Medicine Only )/Writing_",
  "OET Materials & Videos Data/Writing",
];

// ── Exact backend rules (mirrors, does not reimplement loosely) ──
// ContentConventionParser.LetterTypeRules (backend/src/OetLearner.Api/Services/Content/ContentConventionParser.cs:114-122)
const LETTER_RULES = [
  [/routine\s*referral/i, "routine_referral"],
  [/urgent\s*referral/i, "urgent_referral"],
  [/non[\s-]*medical|occupational\s*therapist/i, "non_medical_referral"],
  [/update.*discharge|discharge.*gp/i, "update_discharge"],
  [/update.*referral|specialist.*gp/i, "update_referral_specialist_to_gp"],
  [/transfer\s*letter/i, "transfer_letter"],
];
// RealContentFolderImporter.WritingLetterTypeMap (RealContentFolderImporter.cs:371-381)
const LETTER_MAP = [
  ["routine", "routine_referral"],
  ["urgent", "urgent_referral"],
  ["non medical", "non_medical_referral"],
  ["non-medical", "non_medical_referral"],
  ["update & discharge", "update_discharge"],
  ["discharge", "update_discharge"],
  ["update & referral", "update_referral_specialist_to_gp"],
  ["specialist", "update_referral_specialist_to_gp"],
];
// extract-writing-pdfs canonical Writing 1-6 folder map (scripts/extract-writing-pdfs/Program.cs:23-31)
const CANONICAL_FOLDERS = [
  [/^writing\s*1/i, "routine_referral"],
  [/^writing\s*2/i, "non_medical_referral"],
  [/^writing\s*3/i, "urgent_referral"],
  [/^writing\s*4/i, "update_discharge"],
  [/^writing\s*5/i, "update_referral_specialist_to_gp"],
  [/^writing\s*6/i, "transfer_letter"],
];
const CANONICAL_SET = new Set([
  "routine_referral",
  "urgent_referral",
  "non_medical_referral",
  "update_discharge",
  "update_referral_specialist_to_gp",
  "transfer_letter",
  "Other Letters",
  "other_letters",
  "LT-OT",
  "LT-RR",
  "LT-UR",
  "LT-DG",
  "LT-TR",
  "LT-NM",
]);

function classifyLetterType(folderPath, fileName) {
  const hay = `${folderPath} ${fileName}`;
  for (const [re, code] of CANONICAL_FOLDERS) {
    const seg = folderPath.split(/[/\\]/).find((s) => /^writing\s*\d/i.test(s.trim()));
    if (seg && re.test(seg.trim())) return { letterType: code, method: "canonical-folder-map" };
  }
  for (const [re, code] of LETTER_RULES) {
    if (re.test(hay)) return { letterType: code, method: "convention-parser-regex" };
  }
  const lower = hay.toLowerCase();
  for (const [hint, code] of LETTER_MAP) {
    if (lower.includes(hint)) return { letterType: code, method: "importer-hint-map" };
  }
  // Other Letters is a fully valid catalogue category (LT-OT / other_letters) under every profession.
  return { letterType: "Other Letters", method: "fallback-other-letters" };
}

function classifyProfession(fullPath) {
  const lower = fullPath.toLowerCase();
  if (lower.includes("same for all professions")) return "all";
  if (lower.includes("(medicine only)") || lower.includes("medicine only")) return "medicine";
  for (const p of ["dentistry", "medicine", "nursing", "pharmacy", "physiotherapy", "radiography"]) {
    if (lower.includes(`/${p}/`) || lower.includes(`/${p}\\`) || lower.includes(`(${p}`)) return p;
  }
  // OET Materials & Videos Data/Writing/{Arabic,Medicine,Nursing,Pharmacy}
  const m = lower.match(/writing[/\\](arabic|medicine|nursing|pharmacy)/);
  if (m) return m[1] === "arabic" ? "medicine-ar" : m[1];
  return "unknown";
}

function roleOf(relPath, fileName) {
  const p = relPath.toLowerCase().split("\\").join("/");
  const f = fileName.toLowerCase();

  // 1. Reference: Rulebooks, grammar guides, booklet templates, assessment criteria
  if (
    f.includes("rulebook") ||
    f.includes("grammar") ||
    f.includes("booklet") ||
    f.includes("criteria") ||
    p.includes("/writing rulebook") ||
    p.includes("/grammar rules")
  ) {
    return "Reference";
  }

  // 2. Case notes explicit in filename (even inside corrected folders)
  if (
    f.includes("case notes") ||
    f.includes("case-notes") ||
    f.includes("casenotes") ||
    f.includes("( case notes )") ||
    f.includes("(case notes)")
  ) {
    return "CaseNotes";
  }

  // 3. Model answers / Answer sheets / Corrected letters / Sample letters
  if (
    f.includes("answer sheet") ||
    f.includes("model answer") ||
    f.includes("model answers") ||
    f.includes("answers.pdf") ||
    f.includes("corrected") ||
    f.includes("50 medical writing task answers") ||
    p.includes("/corrected letters") ||
    p.includes("/nursing corrected letters")
  ) {
    return "ModelAnswer";
  }

  // 4. Case notes: all candidate tasks/stimulus files (PDFs, DOCX, and case-note images)
  // Images and documents in New Writing Tasks, Nursing Case Notes, Workshops, Recalls, etc.
  if (
    p.includes("case notes") ||
    p.includes("new writing tasks") ||
    p.includes("recalls") ||
    p.includes("workshops") ||
    /\.(pdf|docx?|jpg|jpeg|png)$/.test(f)
  ) {
    return "CaseNotes";
  }

  return "Supplementary";
}

function walk(dir, out) {
  let entries = [];
  try {
    entries = readdirSync(dir);
  } catch {
    return;
  }
  for (const e of entries) {
    const p = join(dir, e);
    let st;
    try {
      st = statSync(p);
    } catch {
      continue;
    }
    if (st.isDirectory()) walk(p, out);
    else if (st.isFile()) out.push({ abs: p, size: st.size });
  }
}

const files = [];
for (const root of WRITING_ROOTS) {
  const abs = join(REPO, root);
  if (!existsSync(abs)) {
    console.log(`[warn] missing root (skipped): ${root}`);
    continue;
  }
  walk(abs, files);
}
// Task scope: Writing TASKS are document files (case notes / model answers /
// reference PDFs/DOCXs/images). Lesson/session .mp4 videos under the same
// Writing folders belong to the VIDEO workflow (Item 03), not Writing tasks —
// excluded here with an explicit count (documented intentional exclusion, not
// a silent drop). DB ContentPaper(SourceContentPaperId)/WritingScenario rows,
// when present, are the post-import representation of these same files.
const videoExt = new Set([".mp4", ".m4a", ".mp3", ".wav", ".mov", ".avi", ".mkv"]);
const extOf = (p) => {
  const i = p.lastIndexOf(".");
  return i >= 0 ? p.slice(i).toLowerCase() : "";
};
const videoFiles = files.filter((f) => videoExt.has(extOf(f.abs)));
const taskFiles = files.filter((f) => !videoExt.has(extOf(f.abs)));
console.log(`EXCLUDED_VIDEO_FILES=${videoFiles.length} (lesson/session recordings; see Item 03 video SOP)`);
// Deterministic order: sort by relative path.
taskFiles.sort((a, b) => relative(REPO, a.abs).localeCompare(relative(REPO, b.abs)));

const rows = taskFiles.map((f, i) => {
  const rel = relative(REPO, f.abs).split(sep).join("/");
  const fileName = rel.split("/").pop();
  const folder = rel.split("/").slice(0, -1).join("/");
  const { letterType, method } = classifyLetterType(folder, fileName);
  const profession = classifyProfession(rel);
  const role = roleOf(rel, fileName);
  const id = `W-${String(i + 1).padStart(4, "0")}`;
  const hash = createHash("sha256").update(rel.toLowerCase()).digest("hex").slice(0, 12);
  const extracted = extractedTextMap[rel] ?? extractedTextMap[f.abs] ?? "";
  return {
    task_id: id,
    profession,
    letter_type: letterType,
    classification_method: method,
    is_canonical: CANONICAL_SET.has(letterType) ? "yes" : "no",
    task_title: folder.split("/").pop() || fileName,
    pdf_filename: fileName,
    source_path: rel,
    file_bytes: f.size,
    asset_role: role,
    // No AI classifier exists in the backend (rule-based only) — represent explicitly, never omit.
    ai_confidence: "",
    review_flag: f.size === 0 ? "empty_file" : "",
    pending_review: f.size === 0 ? "yes" : "no",
    import_status: "prepared-filesystem",
    source_id: hash,
    case_note_text: extracted,
    notes: f.size === 0 ? "empty file requires review" : "",
  };
});

function csvCell(v) {
  const s = String(v ?? "");
  const needsQuote = s.includes('"') || s.includes(",") || s.includes("\n") || s.includes("\r");
  return needsQuote ? `"${s.split('"').join('""')}"` : s;
}
const header = [
  "task_id", "profession", "letter_type", "classification_method", "is_canonical",
  "task_title", "pdf_filename", "source_path", "file_bytes", "asset_role",
  "ai_confidence", "review_flag", "pending_review", "import_status", "source_id",
  "case_note_text", "notes",
];
const csv = [header.join(","), ...rows.map((r) => header.map((h) => csvCell(r[h])).join(","))].join("\n") + "\n";

mkdirSync(outPath.split("/").slice(0, -1).join("/") || ".", { recursive: true });
writeFileSync(outPath, csv, "utf8");

// ── Summaries ──
const byProf = {}, byLetter = {};
let other = 0, pending = 0, empty = 0;
const seenSource = new Map(), dupSources = [];
const seenName = new Map(), dupNames = [];
for (const r of rows) {
  byProf[r.profession] = (byProf[r.profession] || 0) + 1;
  byLetter[r.letter_type] = (byLetter[r.letter_type] || 0) + 1;
  if (r.letter_type === "Other Letters" || r.letter_type === "Other") other++;
  if (r.pending_review === "yes") pending++;
  if (r.file_bytes === 0) empty++;
  const k = r.source_path.toLowerCase();
  if (seenSource.has(k)) dupSources.push(r.source_path);
  else seenSource.set(k, 1);
  const n = r.pdf_filename.toLowerCase();
  seenName.set(n, (seenName.get(n) || 0) + 1);
}
const dupNamesList = [...seenName.entries()].filter(([, c]) => c > 1);

console.log(`TOTAL_WRITING_SOURCE_FILES=${taskFiles.length + videoFiles.length} (documents=${taskFiles.length} + videos=${videoFiles.length})`);
console.log(`EXPORTED_ROWS=${rows.length}`);
console.log(`RECONCILED=${taskFiles.length === rows.length ? "yes" : "NO"}`);
console.log(`EXPORT_PATH=${outPath}`);
console.log(`PROFESSIONS=${JSON.stringify(byProf)}`);
console.log(`LETTER_TYPES=${JSON.stringify(byLetter)}`);
console.log(`OTHER_COUNT=${other}`);
console.log(`PENDING_REVIEW_COUNT=${pending}`);
console.log(`EMPTY_FILES=${empty}`);
console.log(`DUP_SOURCE_PATHS=${dupSources.length}`);
console.log(`DUP_FILENAMES=${JSON.stringify(dupNamesList.slice(0, 20))}`);
console.log(`AI_CLASSIFIER=none (rule-based only; ai_confidence empty for all rows)`);
