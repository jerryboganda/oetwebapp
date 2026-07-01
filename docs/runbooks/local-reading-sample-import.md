# Local Reading Sample Import Runbook

> Audience: local developer or content operator preparing owner-supplied OET Reading samples.
> Scope: localhost Docker only. Do not use this runbook against staging, production, or the `185.252.233.186` VPS.

This runbook imports structured Reading papers through the real admin API and
publishes them as active learner-visible papers. PDFs alone are not enough: the
Reading publish gate requires uploaded primary assets and a canonical structured
manifest with Parts A, B, and C.

## 1. Prerequisites

1. Start the local Docker stack and confirm these services are healthy:
   - API: `http://localhost:8080`
   - Web: `http://localhost:3000`
   - Containers: `oet-local-api`, `oet-local-web`, `oet-local-postgres`
2. Confirm the local admin account can sign in.
3. Keep source PDFs, rendered pages, OCR/transcription scratch files, and local
   manifest bundles under `output/` or another ignored path unless the content
   owner explicitly approves committing them.

## 2. Manifest Bundle

The importer reads a JavaScript or JSON manifest bundle. The local bundle used
for the initial Reading samples lives at:

```text
output/reading-import-manifests/reading-samples.local.mjs
```

Each paper entry must provide:

- `paper`: content paper metadata, including `subtestCode: "reading"`, title,
  slug, duration, provenance, and profession scope.
- `assets`: uploaded source files with roles such as `QuestionPaper` and
  `AnswerKey`. Reading publish requires the role-specific primary assets.
- `manifest`: the canonical Reading structure.

Canonical Reading shape:

| Part | Texts | Questions | Time |
| ---- | ----- | --------- | ---- |
| A | 4 | 20 | 15 minutes |
| B | 6 | 6 | 45 minutes |
| C | 2 | 16 | 45 minutes |

Part A question types are position-sensitive: Q1-Q7 are matching text reference,
Q8-Q14 are short answer, and Q15-Q20 are sentence completion. Part B uses
three-option multiple choice. Part C uses four-option multiple choice.

## 3. Import Command

Run from the repository root in PowerShell. Set the admin credentials in the
terminal session only; do not write them to a tracked file.

```powershell
Set-Location "C:\Users\Dr Faisal Maqsood PC\Desktop\New OET Web App"
$env:OET_ADMIN_EMAIL = "<local-admin-email>"
$env:OET_ADMIN_PASSWORD = "<local-admin-password>"
node scripts/admin/import-reading-manifests-local.mjs --manifest output/reading-import-manifests/reading-samples.local.mjs --replace-existing
```

The script refuses non-local API hosts. `--replace-existing` updates an existing
paper with the same slug, replaces same-role assets, imports the manifest, runs
backend Reading validation, and publishes the paper.

## 4. Validation

After import, validate through the API before checking the UI:

1. Admin paper list returns each expected slug from `/v1/admin/papers?subtest=reading`.
2. Admin structure endpoints return question counts `20/6/16`, text counts
   `4/6/2`, total points `42`, and `ready: true`.
3. Learner Reading home returns the published papers.
4. Learner structure endpoints return the same counts and do not serialize
   answer, explanation, or accepted synonym fields.
5. Open `http://localhost:3000/admin/content/reading` and confirm the imported
   papers appear with published status.

## 5. Troubleshooting

| Symptom | Likely cause | Fix |
| ------- | ------------ | --- |
| Publish fails with missing assets | Required Reading asset roles are absent or not primary. | Upload and attach the role-specific primary assets, then rerun with `--replace-existing`. |
| Manifest import fails for Part A matching answers | Q1-Q7 answers are not one of `A`, `B`, `C`, or `D`. | Correct the manifest answer payloads for the matching section. |
| Upload commit rejects a chunk part count | Client chunk size does not match the server-advertised size. | Let the importer use `chunkSizeBytes` from `/v1/admin/uploads`. |
| API response terminates during asset or structure operations | Endpoint returned EF navigation graphs instead of DTO projections. | Check `ContentPapersAdminEndpoints` and `ReadingAuthoringAdminEndpoints` projection helpers. |
| Admin UI shows only legacy seed content | The Reading list is still using the legacy content library endpoint. | Rebuild local web and confirm it calls `/v1/admin/papers?subtest=reading`. |

## 6. Operational Notes

- This workflow is for local data loading and QA. Production content promotion
  needs the normal release/deployment evidence path.
- Do not commit ignored `output/reading-import-extract/**` scratch files.
- Do not commit admin passwords, rendered scanned pages, or transcriptions
  unless the owner explicitly approves adding the content payload to the repo.
