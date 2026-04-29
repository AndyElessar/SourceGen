---
description: "Memory protocol for agent workflows that store goal and plan state in /memories/session/."
applyTo: "**"
---

# Memory Policy

## Tool Usage

For `/memories/session/*`:

- **Read / verify**: `memory` with `view`.
- **Write**: `memory` with `create` / `str_replace` / `insert`.
- **Forbidden**: `read_file`, `grep_search`, `run_in_terminal`, `cat`, shell redirection, pipes. Same rule applies to subagents.
- `resolveMemoryFileUri` is informational only — never pass its URI to another tool.

## Memory Rules

Three core principles:

1. **Single writer** — each file has exactly one owner (see table). Non-owners may only read.
2. **Single command per operation** — one `memory` call per intent. Do not combine `create` with `str_replace` / `insert` on the same file in a single turn. A write followed by a verifying `view` is allowed.
3. **Memory tool only** — see the rule above. Each `memory.view` call returns the latest committed content, so concurrent readers always see the most recent verified write.

Allowed commands: `view`, `create`, `str_replace`, `insert`. Parents MUST NOT dispatch two writers for the same file in parallel.

### Session Files And Ownership

| Path | Writer | Readers | Purpose |
|------|--------|---------|---------|
| `goal.md` | Parent (Orchestrator / Doc / DevOps) | All | Requirement goal |
| `plan.md` | Parent | All | Approved plan |
| `plan-review.md` | PlanReview | Parent | Plan review report |
| `changes.md` | Implement / Doc / DevOps (single-step plans only) | Parent, Review | Implementation summary |
| `changes-step-{n}.md` | Implement / Doc / DevOps (multi-step plans, one writer per step) | Parent, Review | Per-step implementation summary |
| `review.md` | Review | Parent | Code review report |

## Parent Workflow

Parents are Orchestrator, DevOps, and Doc.

### Happy Path

1. `create` `goal.md` with a concise distilled goal — before any research.
2. Dispatch `Explore` as the first subagent call, UNLESS the task is trivially scoped (single-file edit, typo fix, or user-supplied exact instructions). When in doubt, dispatch `Explore`.
3. Call `askQuestions` if **any one** of the following is true (OR, not AND):
   - `goal.md` lacks explicit acceptance criteria.
   - Two or more mutually exclusive implementation approaches exist and existing rules do not specify priority.
   - The change touches a public API, a package dependency, or `.github/workflows/*`.
4. Draft `plan.md` (goal, approach/steps, scope/files, acceptance criteria) → save with `create` (new) or `str_replace` (update) → `view` to verify → present the plan inline → wait for explicit user approval.
5. Dispatch non-Explore subagents only after `plan.md` is verified AND approved.

### Error Handling

- Save or verify of `plan.md` fails → return `BLOCKED_NO_PLAN_MEMORY_WRITE`.
- Child returns any `BLOCKED_*` → resolve at parent level → update `plan.md` if scope changed → re-dispatch.
- Scope changes mid-task → overwrite and re-verify `plan.md` before the next dispatch.

## Child Workflow

Children are Implement, Review, DocReview, Spec, PlanReview.

### Happy Path

1. Sequentially call:
   ```
   memory({ command: "view", path: "/memories/session/goal.md" })
   memory({ command: "view", path: "/memories/session/plan.md" })
   ```
2. If both files are present and non-empty → perform the assigned task → report using the agent's defined Output Format.

### Error Handling

| Condition | Action |
|---|---|
| Either file missing or empty | Return `BLOCKED_NEEDS_PARENT_PLAN` |
| Plan ambiguous or design decision needed | Return `BLOCKED_NEEDS_PARENT_DECISION` with the exact clarification needed |
| `memory` call returned an actual error | Return `BLOCKED_NO_PLAN_MEMORY` with the verbatim error |

Children MUST NOT request plan content or approval from the user — route through the parent. `vscode/memory` is granted in every child's frontmatter `tools:`; you MUST attempt the call before claiming it is unavailable, and MUST NOT substitute another tool.

## Parent → Child Delegation Template

```
Your frontmatter (tools:) grants #tool:vscode/memory. Follow these steps:

1. First action — call:
   memory({ command: "view", path: "/memories/session/goal.md" })
2. Second action — call:
   memory({ command: "view", path: "/memories/session/plan.md" })
3. Then perform: <task>
4. Report using: <agent's defined Output Format>

Follow the Memory Rules above. If the memory tool genuinely fails, return BLOCKED_NO_PLAN_MEMORY with the verbatim error message.
```

## BLOCKED Response Codes

| Code | Returned by | Resolved by |
|------|-------------|-------------|
| `BLOCKED_NEEDS_PARENT_PLAN` | Child | Parent |
| `BLOCKED_NEEDS_PARENT_DECISION` | Child | Parent |
| `BLOCKED_NO_PLAN_MEMORY` | Any | User / system |
| `BLOCKED_NO_PLAN_MEMORY_WRITE` | Parent | User |

Surface any `BLOCKED_*` state clearly in the agent's report.
