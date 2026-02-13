# Agent Workflow

## Principle

Delegate work to SubAgents for context isolation and parallel efficiency. Every modification must end with testing and review.

## SubAgent Delegation

| Task Type | Delegate To | Purpose |
| --------- | ----------- | ------- |
| Codebase exploration | `Explore` SubAgent | Read-only research, gather context before implementation |
| Implementation | Main Agent | Apply changes with full tool access |
| Test execution | Main Agent | Run tests via `runTests` tool |
| Code review | `Explore` SubAgent | Review changes for correctness, conventions, and regressions |

### When to Use SubAgents

- **MUST**: Use `Explore` SubAgent for initial codebase research before making changes.
- **MUST**: Use `Explore` SubAgent for post-change review — check for missed edge cases, convention violations, and unintended side effects.
- **MUST NOT**: Use SubAgents for file edits — only the main agent should write code.

## Mandatory Final Steps

Every task that modifies code **MUST** complete these steps before finishing:

### 1. Run Tests

- **MUST**: Run all related tests using `runTests` tool.
- **MUST**: Fix any failing tests before proceeding to review.
- **MUST NOT**: Skip tests or mark failures as ignored.
- For AOT tests, publish and run as native binary (see `copilot-instructions.md`).

### 2. SubAgent Review

- **MUST**: Launch an `Explore` SubAgent to review the completed changes.
- Review checklist:
  - [ ] Changes match the spec / requirements
  - [ ] No unintended behavioral changes
  - [ ] Code follows project conventions (C# 14, file-scoped namespaces, nullable enabled)
  - [ ] No unrelated files modified
  - [ ] Tests cover new/changed behavior
- **MUST NOT**: Skip review, even for small changes.

## Workflow Summary

```list
1. Explore   → SubAgent: gather context, read specs
2. Plan      → Main: break into tasks, confirm approach
3. Implement → Main: make incremental changes
4. Test      → Main: run tests, fix failures
5. Review    → SubAgent: verify correctness and conventions
6. Done      → Only after tests pass AND review clears
```
