# agy 1.2.5 - Verified Protocol Notes

Durable record of protocol facts established by direct measurement while hardening this bridge.
Every claim below was observed in raw command output on this machine, not inferred from docs.

## 1. One-shot JSON envelope (--output-format json)

Invocation: agy -p <PROMPT> --output-format json --model <M> --effort <E> --print-timeout <N>s

Raw stdout:

    "conversation_id":"508d3fc6-2b0b-40e0-a0bf-83f6e3986ada",
    "status":"SUCCESS","response":"OK\n","duration_seconds":3.83,"num_turns":1,
    "usage":{"input_tokens":23802,"output_tokens":61,"thinking_tokens":60,
             "cache_read_tokens":0,"total_tokens":23863}

conversation_id is TOP-LEVEL. Reuse it with --conversation <id> to resume.

## 2. CRITICAL: -p consumes the NEXT argument as its prompt

Running:  agy -p --input-format stream-json ...
Produces: Error: -p took "--input-format" as its prompt, so the intended prompt was left
          as an argument and ignored. Attach the prompt to the flag (-p=...) and move
          --input-format elsewhere on the command line.

So -p is NOT a boolean. Always write -p <PROMPT> or -p=<PROMPT>, and keep other flags
before or after it but never immediately after -p.

## 3. stream-json output frames (--output-format stream-json)

Frame 1 - init:

    {"event":"init","conversation_id":"...","init":{"model":"gemini-3.8-flash-low",
     "cwd":"C:\\Users\\...","tools":[... 57 tool names ...],"permission_mode":"always-proceed"}}

Frame 2 - step progress:

    {"event":"step_update","step_update":{"conversation_id":"...","step_index":0,"state":...}}

KEY BEHAVIOURAL FACT: in stream-json mode the process STAYS ALIVE waiting for more input
(it behaves as a multi-turn session), and --print-timeout ends the wait with the message
"print timeout after 30s with turn in progress; returning partial output".
This is why naive probes appear to hang. Any streaming implementation MUST terminate the
child explicitly after the terminal event rather than waiting for process exit.

The NDJSON INPUT schema for --input-format stream-json remains UNDOCUMENTED and was not
confirmed; multi-turn was therefore implemented via --conversation resume instead.

## 4. Token cost baseline - plan budgets around this

A trivial one-line prompt costs about 23,800 input tokens (measured 23802 / 23796 / 23799
across three runs) because agy ships a very large system prompt and tool catalog.
Each resumed turn re-sends accumulated trajectory context, so long conversations grow.

## 5. Named agents

`agy agent` returns EMPTY output on this machine - there are no named agents available.
The --agent flag exists and is supported by the bridge config, but has no usable values.
Do NOT run `agy agent` in automation: it blocks.

## 6. Tools available inside agy (from the init frame)

ask_custom_permission, ask_permission, ask_question, browser_* (many), call_mcp_tool,
command_status, define_subagent, delete_knowledge, find_by_name, finish, generate_image,
grep_search, invoke_subagent, list_dir, list_permissions, list_resources, manage_inbox,
manage_subagents, manage_task, multi_replace_file_content, notebook_edit, read_resource,
read_url_content, replace_file_content, run_command, schedule, search_web, sed_file,
send_command_input, send_message, view_file, wait, write_to_file

## 7. Hooks decision protocol (PreToolUse)

hooks.json - global at ~/.gemini/config/hooks.json, workspace at <ws>/.agents/hooks.json.
Events: PreToolUse, PostToolUse, PreInvocation, PostInvocation, Stop.
A PreToolUse hook reads the tool call as JSON on STDIN and writes JSON on STDOUT:

    {"decision":"deny","reason":"..."}   blocks      {"decision":"allow"}   permits

Other decisions: ask, force_ask. EXIT CODE IS NOT THE BLOCKING MECHANISM.
Handlers run via cmd /c on Windows and are SYNCHRONOUS, so they block every command -
keep them small. Handler fields: command (required), type (optional), timeout (seconds).

## 8. Permissions model

settings.json (~/.gemini/antigravity-cli/settings.json):
  toolPermission:        always-proceed | request-review | strict | proceed-in-sandbox
  artifactReviewPolicy:  always-proceed | agent-decides | asks-for-review
  permissions.allow / .deny / .ask accept grants like command(...), read_file(...),
  write_file(...), mcp(...).

## 9. MCP servers registered

chrome-devtools-mcp (stdio, enabled), gemini-bridge (stdio, enabled).
Adding more costs tokens on EVERY turn (tool definitions are injected into the prompt),
so expand MCP reach deliberately on an already ~24k-token baseline.

## 10. Hardening commits on this bridge

  2e010d18  baseline as-found
  ad532bad  timeout authority + --print-timeout safety margin
  68b4ebf2  transient retry with jittered backoff + write-role side-effect guard
  2adc3b49  real git worktree isolation for write roles
  43176181  opt-in session continuity + ag_sessions tool
  d1b97324  opt-in budget ceilings enforced at attempt boundaries
  9b7b000f  durable run ledger + ag_runs tool
  07ff6968  opt-in per-role model/effort routing
  bbc8437a  declare bridge as an ES module package

