---
description: "Use when executing agent workflows that coordinate through /memories/session/. Defines the memory protocol for parent and child agents."
---

# Memory Policy

All agents that participate in the plan→approve→implement→review workflow MUST follow this protocol for `/memories/session/` paths.

## Session Memory Paths

| Path | Owner | Purpose |
|------|-------|---------|
| `/memories/session/goal.md` | Parent agents (Orchestrator, Doc, DevOps) | Requirement goal — created before Discovery, read by all subagents |
| `/memories/session/plan.md` | Parent agents (Orchestrator, Doc, DevOps) | Approved plan — read by all child agents |
| `/memories/session/plan-review.md` | PlanReview agent | Structured plan review report — read by parent agent before presenting plan |
| `/memories/session/changes.md` | Implement agent | Changed files, decisions, issues, concerns from implementation |
| `/memories/session/review.md` | Review agent | Structured review report |

## Memory Access Rules

`/memories/session/` is a **memory-only namespace**. Treat it as an abstract store, not a filesystem.

### Allowed

- ✅ #tool:vscode/memory — the **only** tool permitted to read or write content under `/memories/session/`. Use the `view`, `create`, `str_replace`, and `insert` commands as documented in the tool's schema.
- ✅ #tool:vscode/resolveMemoryFileUri — may be used to obtain a file URI for a memory path **only** when another tool's schema strictly requires a URI argument (e.g., to display the path). The returned URI is informational; it MUST NOT be passed to any read/write/execute tool to bypass the memory abstraction.

### Forbidden

- 🚫 Do NOT use any tool other than #tool:vscode/memory to read or write `/memories/session/` paths. This includes (non-exhaustive):
  - #tool:read / file viewers
  - #tool:edit
  - #tool:execute / any terminal command (`cat`, `less`, `sed`, `awk`, `grep`, `ls`, `head`, `tail`, `echo >`, `tee`, redirection, pipes, etc.)
  - #tool:search scoped at memory paths
  - Any other tool that performs filesystem I/O on the resolved URI
- 🚫 Do NOT obtain the URI via #tool:vscode/resolveMemoryFileUri and then read/edit/execute against that URI with another tool. This is the same prohibition stated above and applies regardless of how the path was obtained.
- 🚫 Do NOT instruct a subagent (via prompt) to bypass these rules.

### Exact Tool Call Syntax

**Reading the plan** — use the `view` command:
```
memory({ command: "view", path: "/memories/session/plan.md" })
```

**Saving the plan** — use the `create` command (for new) or `str_replace` command (for updates):
```
memory({ command: "create", path: "/memories/session/plan.md", file_text: "<plan content>" })
```

```
memory({ command: "str_replace", path: "/memories/session/plan.md", old_str: "<existing text>", new_str: "<replacement text>" })
```

## Parent Agent Protocol

Parent agents (Orchestrator, DevOps, Doc) create, save, and maintain the goal and plan:

0. **Capture Goal** — Before any research, distill the user's request into a concise goal statement and save it to `/memories/session/goal.md` via #tool:vscode/memory. This file is the single source of truth for *what* we are trying to achieve. Include it (or reference it) when delegating to every subagent.
1. **Explore First** — The first subagent call in every task MUST be `Explore` to gather context. Provide the goal from `goal.md` alongside the research question.
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

Child agents (Implement, Review, DocReview, Spec, PlanReview) load and validate the plan:

1. **Load Goal and Plan (FIRST ACTION — mandatory, non-skippable)** — Call #tool:vscode/memory to read `/memories/session/goal.md` first, then `/memories/session/plan.md`, as your very first tool calls. No other tool call may precede these.
2. **Validate Content** — Confirm both goal and plan content are present and non-empty. If valid, proceed to work.
3. **Block if Missing** — If memory read fails or plan is missing/empty, stop immediately and return `BLOCKED_NEEDS_PARENT_PLAN` with a brief reason requesting the parent agent to save a complete plan. Do NOT attempt to guess the plan or proceed without it.
4. **Block on Tool Failure** — If memory is unavailable due to tool/runtime issues, stop and return `BLOCKED_NO_PLAN_MEMORY`. **You MUST attempt the tool call at least once before claiming unavailability** — see "Tool Availability Self-Check" below.
5. **Block on Ambiguity** — If anything in the plan is unclear or a design decision is needed, return `BLOCKED_NEEDS_PARENT_DECISION` with the exact clarification needed.
6. **Never Ask User** — Never request plan content or approvals directly from the user; all requests go through the parent agent.

### Tool Availability Self-Check

`vscode/memory` is granted in the `tools:` frontmatter of every child agent definition under `.github/agents/`. **You MUST NOT pre-emptively declare it unavailable**. Empirically observed failure mode: agents sometimes claim "memory tool not available" without ever invoking it, then fall back to `read_file`/`grep` (which violates the memory policy) or refuse to proceed.

Required behavior:

1. **Always attempt the call first.** Issue the exact tool call:
   `memory({ command: "view", path: "/memories/session/goal.md" })`
2. **Only after the tool call returns an actual error** may you return `BLOCKED_NO_PLAN_MEMORY`. Include the verbatim error message in your report.
3. **Never substitute** `read_file`, `grep_search`, `run_in_terminal`, or any other tool to access `/memories/session/*` paths — even if you believe `memory` is unavailable. If `memory` truly fails, the correct response is to STOP and return the BLOCKED code, not to bypass the policy.

## Delegation Prompt Pattern (Parent → Child)

When a parent agent delegates to a child agent and expects it to use #tool:vscode/memory, the prompt MUST be explicit. Empirical testing shows that vague prompts like "please use the memory tool to read plan.md" fail roughly half the time with GPT-5.x child agents — the model claims the tool is unavailable and either gives up or substitutes `read_file`. Explicit prompts with command + parameters + an anti-decline directive succeed reliably.

### Required prompt elements

1. **Name the tool explicitly** — write `#tool:vscode/memory` or "memory tool", not just "memory".
2. **Specify the command and parameters** — e.g., `command="view"`, `path="/memories/session/plan.md"`.
3. **Confirm authorization** — note that the tool is granted in the agent's frontmatter (`tools:` field of `.github/agents/<Agent>.agent.md`).
4. **Block the decline path** — explicitly forbid "tool unavailable" responses without an actual call attempt; explicitly forbid substituting `read_file`/`grep_search`/`run_in_terminal`.
5. **Specify the report shape** — what the child should return after a successful read.

### Recommended template

```
Your frontmatter (tools:) grants #tool:vscode/memory. Follow these steps:

1. As your first action, call memory with parameters:
   { "command": "view", "path": "/memories/session/goal.md" }
2. As your second action, call memory with parameters:
   { "command": "view", "path": "/memories/session/plan.md" }
3. After both calls succeed, perform: <the agent's primary task>
4. When complete, report using the agent's defined Output Format.

Prohibited:
- Do NOT use read_file, grep_search, run_in_terminal, or any other tool to access /memories/session/* paths
- Do NOT claim "the memory tool is unavailable" without first attempting the call. If the call genuinely fails, return BLOCKED_NO_PLAN_MEMORY with the verbatim error message.
```

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
- MemoryGoalLoaded: true | false
- MemoryPlanLoaded: true | false
- MemoryPlanSaved: true | false (parent agents only)
- MemoryPlanVerified: true | false (parent agents only)
- MemoryPath: /memories/session/goal.md, /memories/session/plan.md
- Blocker: (empty or BLOCKED_* code with reason)
```

If the active agent definition does not require a structured preconditions block, at minimum report any `BLOCKED_*` state clearly.

## Boundaries

- ✅ **Always:** Access `/memories/session/` paths exclusively via #tool:vscode/memory
- ✅ **Always:** Treat URIs returned by #tool:vscode/resolveMemoryFileUri as informational only
- ✅ **Always:** Verify plan content after every save operation
- ✅ **Always:** Handle all `BLOCKED_*` responses at the appropriate level
- ✅ **Always:** Attempt the `memory` tool call at least once before returning `BLOCKED_NO_PLAN_MEMORY`; never pre-emptively declare the tool unavailable
- ✅ **Always (parents):** When delegating to a child agent, write explicit prompts naming `#tool:vscode/memory`, the command (`view`), the exact path, and forbidding substitutes — see "Delegation Prompt Pattern (Parent → Child)"
- 🚫 **Never:** Use #tool:read, #tool:edit, #tool:execute, search/grep tools, or any other non-memory tool against `/memories/session/` paths or their resolved URIs
- 🚫 **Never:** Pipe, redirect, or shell-out to read or write `/memories/session/` files
- 🚫 **Never:** Delegate to any subagent (after initial Explore) before saving and verifying plan
- 🚫 **Never:** Have child agents ask users directly for plan content or approvals
- 🚫 **Never:** Instruct subagents (via prompt) to bypass these rules
- 🚫 **Never:** Substitute `read_file`/`grep_search`/`run_in_terminal` for `memory` because you suspect `memory` is unavailable — STOP and return `BLOCKED_NO_PLAN_MEMORY` instead
