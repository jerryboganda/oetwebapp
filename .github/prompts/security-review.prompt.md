---
name: "security-review"
description: "Run a security and AI-grounding review."
agent: "OET Security Reviewer"
argument-hint: "Security-sensitive area or diff"
tools: ["read", "search", "web"]
---

Security-review: `${input:target:current security-sensitive work}`.

Focus on auth, authorization, secrets, uploads, AI grounding, prompt injection, runtime settings, billing, admin access, and auditability. Do not edit.