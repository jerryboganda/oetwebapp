# Admin Assistant — codebase tools and what they can actually do

Owner directive 2026-10-09. Read this before asking the admin chatbot anything about this codebase,
and before changing any tool under `Services/AiAssistant/Tools/` or `Services/AiAssistant/Indexing/`.

## The four source tools, and the one hard boundary

`read_file`, `search_codebase`, `retrieve_codebase` and `list_directory` read this application's
source. They are **restricted to the admin assistant** (`ai_assistant.admin`) in two independent
places:

1. **Grants** — `AiToolCatalogSeederHostedService` seeds those tool codes for
   `AiFeatureCodes.AiAssistantAdmin` only.
2. **The tools themselves** — `AdminOnlyToolGuard.Refusal(ctx, toolCode)` refuses unless
   `ctx.IsAdmin` or the feature code is `ai_assistant.admin`.

The second exists because grants are seeded data a future edit could widen, while the source tree is
about to be mounted into the production API container for real search. Source on a learner-reachable
surface would be a disclosure incident, so the restriction is enforced in the tool as well.
`query_database` is read-only (rolled-back transaction + `SET TRANSACTION READ ONLY`) and is not
source-restricted, but it is still an admin grant.

## Source must be mounted; otherwise the tools refuse

In production the API image is a `dotnet publish` output — DLLs, no source, no `.git`. A single
resolver, `RepoRootResolver`, answers "where is the source" for both the filesystem tools and the
indexer, and it returns one of two things:

- `Root` + the list of source prefixes that actually exist there, or
- **null plus a reason**, when nothing containing project source could be resolved.

It does **not** fall back to the current directory. That fallback was the original bug: the tools
resolved `/app` and cheerfully listed 60 DLLs while the indexer returned null and never built, so
"no source" and "searched and found nothing" were indistinguishable. Both tools now refuse with
`codebase_source_unavailable` and the resolver's reason.

### How source gets there

`production-deploy.yml` runs `git archive` over the **exact released SHA** (not a clone — no `.git`,
no history, no remotes, byte-for-byte what was built), stages it with the rollout, and on the VPS
extracts it to `/opt/oetwebapp/source/<sha>`, repointing `/opt/oetwebapp/source/current` atomically
and keeping the two most recent SHAs for rollback. Compose bind-mounts that path **read-only** at
`/srv/source`, and `CODEBASE_INDEX_ROOT` defaults to it.

`.gitattributes` carries `export-ignore` rules so the archive excludes `.env*`, keys, `node_modules`
and build output. `.env` files are gitignored and therefore never tracked, so `git archive` cannot
pick them up; the rules are defence in depth.

To check whether source is mounted:

```
GET /v1/admin/ai/codebase/status
```

`sourceAvailable: false` means the tools will refuse. `summary` is the one-line answer.

### Re-indexing

```
POST /v1/admin/ai/codebase/reindex     # 409 with a reason when no source is mounted
GET  /v1/admin/ai/codebase/status      # progress, chunk count, last indexed
```

Indexing also runs from a hosted service 30 s after boot and every 6 h. Both endpoints exist because
`ICodebaseIndexer.TriggerReindex()` and `GetStatusAsync()` previously had **zero callers**: indexing
ran on a timer, and nothing anywhere reported whether it had worked.

## Embeddings are `vector(1536)`, and the migration guards its own data

`AiCodebaseChunks.Embedding` was created as a plain Postgres `real[]` while every sibling embedding
column in the model is `vector(1536)` and the provider has pgvector enabled. The pgvector distance
operator does not exist on `real[]`, so the exception was swallowed and `CodebaseRetriever` silently
degraded to keyword-only — the "hybrid" search was never hybrid.

`20270119100000_CodebaseEmbeddingToVector` fixes it by drop-and-re-add (a direct `ALTER ... TYPE` is
not supported for this conversion). That is destructive only if rows exist, so the migration
**asserts the table is empty and raises if it is not**, rather than assuming. The same migration adds
an index on `ContentHash`, which the indexer queries on every re-index to skip unchanged chunks and
which never existed.

## Grounding: enforced for learners and tutors, deliberately not for admin

`AiAssistantOrchestrator` computes `isError` for every tool result and — before this change — used it
for exactly one thing: the stream event. It was never branched on, so a turn whose tools all failed
still persisted whatever text the model produced. That is how an assistant answered a codebase
question with confidence after five consecutive empty searches. The model was not wrong to try; the
loop never told it nothing had been learned.

For **`ai_assistant.learner`** and **`ai_assistant.expert`** only, when every tool call in a turn
failed or returned nothing usable, the answer now carries a system note requiring it to state what it
could not find and not assert specifics it could not verify. A tool that "succeeded" while returning
an empty result set counts as failed for this purpose.

**The admin assistant is exempt, by owner decision on 2026-10-09.** It is the operator's own tool, and
the owner judged restricting it more harmful than an occasional ungrounded answer. The learner and
expert system prompts carry the rule; the admin prompt is unchanged. The consequence is accepted and
deliberate: the admin chatbot can still produce a confident answer built on tools that returned
nothing, which is exactly what motivated this work.

## `query_database` closes its connection

It opened the shared EF connection with `OpenAsync` and never closed it — no `finally`. Because
`LearnerDbContext` is scoped and shared with the tool invoker and the orchestrator, EF still believed
the connection was closed and re-opened it on the next `SaveChangesAsync`, producing
`Connection already open` for every remaining call in that turn. It now closes what it opened, and
only what it opened. `CodebaseRetriever` already did this correctly in both of its raw paths.

## Known limits

- **First-boot indexing cost.** Embedding a whole checkout takes time. The hosted service waits 30 s
  after boot before its first pass; if a full index is slow, that delay is the thing to raise.
- **Archive size.** `git archive` of the repository is roughly 50 MB, shipped once per release.
- **`list_directory` is capped** at 200 entries and says so when it truncates, because an
  uncapped listing previously dumped the entire publish directory into the prompt.
- **No automated tests exist** in this repo, so none cover these tools. Correctness evidence is
  compilation, the static guards, and operator use.
