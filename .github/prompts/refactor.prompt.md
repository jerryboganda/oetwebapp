---
name: "refactor"
description: "Clean up OET code without changing behavior — dead code, duplication, structure."
agent: "OET Implementer"
argument-hint: "Files or area to refactor"
tools: ["read", "search", "edit", "execute", "todo"]
---

Refactor: `${input:target:Describe the area}`.

Improve internal structure without changing observable behavior. Remove dead code and duplication, simplify nested logic, and align naming with OET domain language. Run the smallest relevant test/lint/type-check after each meaningful change. Do not mix refactoring with feature work.
