---
description: "Use when executing agent workflows that coordinate through /memories/session/plan.md. Defines the plan memory protocol for parent and child agents."
applyTo: "src/**"
---

# Plan Memory Policy

All agents that participate in the plan→approve→implement→review workflow MUST follow this protocol for `/memories/session/plan.md`.

## Memory Access Rules

- **ONLY** use #tool:vscode/memory (the `memory` tool) to read and write `/memories/session/plan.md`.
- Do NOT use #tool:read (`read_file`) for `/memories/session/plan.md`; this path is memory-only.
- Do NOT use #tool:edit (`replace_string_in_file`) for `/memories/session/plan.md`; this path is memory-only.

### Exact Tool Call Syntax

**Reading the plan** — use the `view` command:
```
memory({ command: "view", path: "/memories/session/plan.md" })
```

**Saving the plan** — use the `create` command (for new) or `str_replace` command (for updates):
```
memory({ command: "create", path: "/memories/session/plan.md", file_text: "<plan content>" })
```

## Parent Agent Protocol

Parent agents (Orchestrator, DevOps, Doc) create, save, and maintain the plan:

1. **Explore First** — The first subagent call in every task MUST be `Explore` to gather context.
2. **Clarify if Needed** — After `Explore` returns, resolve any material ambiguity before finalizing the plan. Use #tool:vscode/askQuestions when requirements are incomplete, multiple valid approaches exist, public API or dependency changes are involved, or a user decision is required. Do not ask questions that can be answered from the codebase.
3. **Create Plan** — After `Explore` and any necessary clarification, create `plan.md` using the format defined by the active parent agent. The plan MUST be structured, complete, and current, and MUST include the equivalent of: goal/outcome, implementation approach or steps, scope or relevant files, and acceptance criteria or verification.
4. **Save Draft Plan** — After drafting the plan, and before delegating to any subagent after the initial `Explore` call, save the current plan to `/memories/session/plan.md` via #tool:vscode/memory or an update command if the file already exists.
5. **Present & Approve** — Present the plan inline to the user. The memory file is for persistence, not a substitute for showing the plan in conversation. Do not delegate execution subagents until the user explicitly approves.
6. **Verify Plan** — After every save, read back via #tool:vscode/memory and confirm the content is complete and current.
7. **Gate on Failure** — If memory write or verification fails, stop and return `BLOCKED_NO_PLAN_MEMORY_WRITE`. If user/system action may resolve the problem, use #tool:vscode/askQuestions to request correction before stopping.
8. **Handle Blocked Subagents** — If a subagent returns `BLOCKED_NEEDS_PARENT_PLAN` or `BLOCKED_NEEDS_PARENT_DECISION`, resolve at parent level, re-save/verify plan if needed, then re-dispatch the same subagent.
9. **Re-save on Scope Change** — If plan scope changes during refinement or execution, overwrite `/memories/session/plan.md` and verify again before any subsequent subagent delegation.

## Child Agent Protocol

**CRITICAL**: The VERY FIRST action of any child agent MUST be to load and validate the plan. Do NOT skip this step. Do NOT proceed to any other work until the plan is loaded and confirmed non-empty.

Child agents (Implement, Review, DocReview, Spec) load and validate the plan:

1. **Load Plan (FIRST ACTION — mandatory, non-skippable)** — Call #tool:vscode/memory as your very first tool call. No other tool call may precede this.
2. **Validate Content** — Confirm the plan content is present and non-empty. If valid, proceed to work.
3. **Block if Missing** — If memory read fails or plan is missing/empty, stop immediately and return `BLOCKED_NEEDS_PARENT_PLAN` with a brief reason requesting the parent agent to save a complete plan. Do NOT attempt to guess the plan or proceed without it.
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

## Reporting Guidance

If an agent definition requires a structured completion report, include plan-memory status in that report. A recommended template is:

```
#### Preconditions
- MemoryPlanLoaded: true | false
- MemoryPlanSaved: true | false (parent agents only)
- MemoryPlanVerified: true | false (parent agents only)
- MemoryPath: /memories/session/plan.md
- Blocker: (empty or BLOCKED_* code with reason)
```

If the active agent definition does not require a structured preconditions block, at minimum report any `BLOCKED_*` state clearly.

## Boundaries

- ✅ **Always:** Access `/memories/session/plan.md` exclusively via #tool:vscode/memory
- ✅ **Always:** Verify plan content after every save operation
- ✅ **Always:** Handle all `BLOCKED_*` responses at the appropriate level
- 🚫 **Never:** Use #tool:read for `/memories/session/plan.md`
- 🚫 **Never:** Use #tool:edit for `/memories/session/plan.md`
- 🚫 **Never:** Delegate to any subagent (after initial Explore) before saving and verifying plan
- 🚫 **Never:** Have child agents ask users directly for plan content or approvals
