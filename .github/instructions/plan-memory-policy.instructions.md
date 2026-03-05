---
description: "Use when executing agent workflows that coordinate through /memories/session/plan.md. Defines the plan memory protocol for parent and child agents."
applyTo: ".github/agents/**"
---

# Plan Memory Policy

All agents that participate in the plan→approve→implement→review workflow MUST follow this protocol for `/memories/session/plan.md`.

## Memory Access Rules

- Use #tool:vscode/memory (runtime: `memory`) to read and write `/memories/session/plan.md`.
- Do NOT use #tool:read for `/memories/session/plan.md`; this path is memory-only.
- Do NOT use #tool:edit for `/memories/session/plan.md`; this path is memory-only.

## Parent Agent Protocol

Parent agents (Orchestrator, DevOps, Doc) create, save, and maintain the plan:

1. **Explore First** — The first subagent call in every task MUST be `Explore` to gather context.
2. **Create Plan** — Immediately after `Explore` returns, create `plan.md` with Goal, Scope, Approach, and Acceptance Criteria.
3. **Save Plan** — Before delegating to any subagent after the initial `Explore` call, save the current plan to `/memories/session/plan.md` via #tool:vscode/memory .
4. **Verify Plan** — After saving, read back `/memories/session/plan.md` via #tool:vscode/memory and confirm the content is complete and current.
5. **Gate on Failure** — If memory write or verification fails, use #tool:vscode/askQuestions to request correction, then stop and return `BLOCKED_NO_PLAN_MEMORY_WRITE`.
6. **Handle Blocked Subagents** — If a subagent returns `BLOCKED_NEEDS_PARENT_PLAN` or `BLOCKED_NEEDS_PARENT_DECISION`, resolve at parent level, re-save/verify plan if needed, then re-dispatch the same subagent.
7. **Re-save on Scope Change** — If plan scope changes during execution, overwrite `/memories/session/plan.md` and verify again before any subsequent subagent delegation.

## Child Agent Protocol

Child agents (Implement, Review, DocReview, Spec) load and validate the plan:

1. **Load Plan** — Load `/memories/session/plan.md` via #tool:vscode/memory before any work.
2. **Validate Content** — Confirm the plan content is present and non-empty.
3. **Block if Missing** — If memory read fails or plan is missing/empty, stop and return `BLOCKED_NEEDS_PARENT_PLAN` with a brief reason requesting the parent agent to save a complete plan.
4. **Block on Tool Failure** — If memory is unavailable due to tool/runtime issues, stop and return `BLOCKED_NO_PLAN_MEMORY`.
5. **Block on Ambiguity** — If anything in the plan is unclear or a design decision is needed, return `BLOCKED_NEEDS_PARENT_DECISION` with the exact clarification needed.
6. **Never Ask User** — Never request plan content or approvals directly from the user; all requests go through the parent agent.

## BLOCKED Response Codes

| Code | Meaning | Who Returns | Who Resolves |
|------|---------|-------------|--------------|
| `BLOCKED_NEEDS_PARENT_PLAN` | Plan missing or empty in memory | Child agent | Parent agent saves plan, re-dispatches |
| `BLOCKED_NEEDS_PARENT_DECISION` | Plan ambiguity or design decision needed | Child agent | Parent agent clarifies, re-dispatches |
| `BLOCKED_NO_PLAN_MEMORY` | Memory tool unavailable | Any agent | User/system resolves tool issue |
| `BLOCKED_NO_PLAN_MEMORY_WRITE` | Memory write or verification failed | Parent agent | User resolves, parent retries |

## Output Preconditions Template

All agents using this policy MUST include these preconditions in their output report:

```
#### Preconditions
- MemoryPlanLoaded: true | false
- MemoryPlanSaved: true | false (parent agents only)
- MemoryPlanVerified: true | false (parent agents only)
- MemoryPath: /memories/session/plan.md
- Blocker: (empty or BLOCKED_* code with reason)
```

## Boundaries

- ✅ **Always:** Access `/memories/session/plan.md` exclusively via #tool:vscode/memory
- ✅ **Always:** Verify plan content after every save operation
- ✅ **Always:** Handle all `BLOCKED_*` responses at the appropriate level
- 🚫 **Never:** Use #tool:read for `/memories/session/plan.md`
- 🚫 **Never:** Use #tool:edit for `/memories/session/plan.md`
- 🚫 **Never:** Delegate to any subagent (after initial Explore) before saving and verifying plan
- 🚫 **Never:** Have child agents ask users directly for plan content or approvals
