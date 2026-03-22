---
name: contract-check
description: Check request, response, schema, and server-action contracts. Use when changing route handlers, server actions, shared types, payload shapes, validation, or any interface consumed by multiple layers.
---

# Contract Check

## Purpose
- Protect request and response shapes across route handlers, server actions, and shared schemas.
- Catch compatibility issues before a change reaches the UI or other consumers.

## Workflow
- Trace the producer and every known consumer.
- Compare input names, output shapes, defaults, validation, and error semantics.
- Note any compatibility break, migration need, or follow-up synchronization.
- Prefer exact file and symbol references over general descriptions.

## Repo context
- Route handlers live in `src/app/**/route.ts` and proxy external APIs.
- Server actions live in `src/lib/auth/action.ts` and a few `src/app/**/action.ts` files.
- Shared types live in `src/interface` and `src/types`.
- Menu and routing drift can appear in `src/Data/Sidebar/sidebar.ts`.
- Auth actions are mocked redirects, not real auth backends.

## Output
- State the contract that changed and who depends on it.
- Call out breaking and non-breaking differences explicitly.
- Recommend the smallest safe path when compatibility is at risk.
- Do not edit code unless the parent task asks for implementation.
