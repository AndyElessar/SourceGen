---
name: code-graph-context
description: "Use when: exploring a codebase with CodeGraphContext MCP (sometimes informally called CodeContextGraph), indexing repositories or packages, loading bundles, watching directories, finding symbols, tracing callers/callees/importers, analyzing class hierarchies or overrides, finding dead code, checking complexity, repository stats, or running Cypher queries over the code graph."
argument-hint: "question=<where is X / who calls Y / load bundle Z> [path=<repo-or-file>]"
user-invocable: true
---

# CodeGraphContext MCP Guide

Use this skill when the user wants semantic codebase exploration through the CodeGraphContext MCP server instead of plain text search alone.

This guide is based on the official cookbook and MCP tools reference. It helps you choose the right tool, prepare the graph, and return answers with concrete evidence.

## Environment Check

The official docs use logical tool names such as `find_code`, `add_code_to_graph`, and `execute_cypher_query`.

Before following the full workflow, first check what the current environment actually exposes:

- List available MCP tools to confirm which tool names and prefixes exist.
- Some integrations expose the same capabilities under server-specific prefixes.
- Some integrations expose only a subset of CodeGraphContext capabilities.
- If indexing or watching tools are unavailable, work with the existing indexed graph or loaded bundles and clearly report that graph preparation cannot be performed from the current environment.

## When to Use

Use this skill for requests like:

- "Index this repository so we can inspect it semantically."
- "Where is `foo` defined?"
- "Who calls `helper`?"
- "Who imports this module?"
- "What does `foo` call?"
- "Show all indirect callers or callees."
- "Show me the class hierarchy for `Base`."
- "Which functions use this decorator or argument name?"
- "Find dead code or the most complex functions."
- "Run a custom Cypher query over the code graph."
- "Load a pre-indexed bundle for a package."
- "Keep this directory watched while I change code."

## When Not to Use

Do not reach for this skill first when the user only needs:

- a plain filename lookup — use `file_search` or workspace search
- a simple text search or grep-style match — use `grep_search`
- one small code snippet that does not require graph relationships — use `semantic_search`

Prefer this skill when the question is semantic, structural, cross-file, graph-shaped, or needs repository indexing.

## Supported Languages

`add_package_to_graph` supports: python, javascript, typescript, java, c, go, ruby, php, cpp. The indexing and Cypher workflow generalizes to any repository represented in the graph.

## Core Workflow

1. **Check available graph capabilities in the current environment.**
   - If indexing and monitoring tools are available, follow the full graph-preparation flow below.
   - If only analysis or Cypher tools are available, use the existing graph data and explicitly note any limitations.
2. **Make sure the graph has the data you need.**
   - Use `add_code_to_graph` for a local repository or folder.
   - Use `add_package_to_graph` for an external dependency.
   - Use `load_bundle` when a pre-indexed `.cgc` bundle is available.
   - Use `watch_directory` when the codebase is actively changing.
3. **Wait until indexing is usable.**
   - Check `check_job_status` for a specific job.
   - Use `list_jobs` for all background jobs.
   - Use `list_indexed_repositories` or `get_repository_stats` to confirm coverage.
4. **Choose the lightest tool that can answer the question** (see Tool Selection Guide).
5. **Add context when names are ambiguous.**
   - If multiple files define the same symbol, pass a file path in the `context` parameter.
   - Narrow Cypher queries by file name, path suffix, or node type.
6. **Answer with evidence.**
   - Include the relevant file path, line number, symbol name, and why it matters.
7. **If the result is empty or noisy, iterate.**
   - Verify indexing completed successfully.
   - Switch from exact to fuzzy search (`fuzzy_search: true`, `edit_distance`).
   - Add a file path context.
   - Fall back to a Cypher query for sharper filters.

## Tool Selection Guide

| Goal | Preferred tool | Use it when | Good follow-up |
|---|---|---|---|
| Index a local repo | `add_code_to_graph` | You need to scan a folder once | `check_job_status`, `list_indexed_repositories` |
| Index an external package | `add_package_to_graph` | You need dependency context in the graph | `check_job_status` |
| Load pre-indexed data | `load_bundle` | A `.cgc` bundle is available or registry bundles are faster | `search_registry_bundles` |
| Search the registry | `search_registry_bundles` | You want to find available bundles before loading | `load_bundle` |
| Keep the graph fresh | `watch_directory` | The repo is changing during the session | `list_watched_paths`, `unwatch_directory` |
| Remove a repo from graph | `delete_repository` | You want to clean up or re-index | `add_code_to_graph` |
| Find code by name or snippet | `find_code` | The user knows a symbol, keyword, or snippet | `analyze_code_relationships` |
| Trace relationships | `analyze_code_relationships` | Callers, callees, importers, hierarchy, overrides, decorators, argument names, or chains | `execute_cypher_query` |
| Find dead code | `find_dead_code` | The user asks about unused functions | `execute_cypher_query` |
| Measure complexity | `calculate_cyclomatic_complexity` / `find_most_complex_functions` | The user wants maintainability signals | `execute_cypher_query` |
| Inspect repo coverage | `get_repository_stats` | The user wants counts of files, functions, classes, or modules | `list_indexed_repositories` |
| Ask a custom structural question | `execute_cypher_query` | You need filtering, aggregation, joins, or pattern matching | `visualize_graph_query` |
| Visualize results | `visualize_graph_query` | A relationship is easier to understand as a graph | share the generated URL |

## `analyze_code_relationships` Query Types

| `query_type` | Purpose |
|---|---|
| `find_callers` | Direct callers of a function |
| `find_all_callers` | Direct and indirect (transitive) callers |
| `find_callees` | Functions called by the target |
| `find_all_callees` | Direct and indirect callees |
| `find_importers` | Files that import a module |
| `class_hierarchy` | Child classes, methods, inheritance tree |
| `overrides` | Methods that override the target |
| `call_chain` | Path between two functions (`"source->target"`) |
| `find_functions_by_decorator` | Functions using a specific decorator |
| `find_functions_by_argument` | Functions accepting a named argument |

## Decision Rules

### Pick the indexing path first

- **Local source tree:** use `add_code_to_graph`.
- **Dependency or library package:** use `add_package_to_graph`.
- **Known pre-indexed package:** prefer `load_bundle` for speed.
- **Continuously changing repo:** prefer `watch_directory` after the initial scan.

### Prefer simple tools before Cypher

- Use `find_code` before writing a custom query when the user is looking for a symbol or snippet.
- Use `analyze_code_relationships` before Cypher for callers, callees, importers, hierarchy, override, decorator, and chain questions.
- Use `execute_cypher_query` for aggregation, cross-file joins, custom filters, or security-style scans.

### Use context for disambiguation

- If a symbol name is duplicated, provide a file path in tools that support a `context` or `path` parameter.
- If a relationship query could match multiple definitions, constrain it with path context.
- In Cypher, use `WHERE ... ENDS WITH 'file.py'` or similar path filters.

## Response Quality Checklist

Before finishing, make sure you can say yes to these:

- The repository, package, or bundle is available in the graph.
- The indexing job completed, or you clearly reported that it is still running.
- You chose the simplest tool that could answer the question.
- You included concrete evidence such as paths, line numbers, or symbol names.
- If the answer used Cypher, the query stayed read-only and reasonably bounded (use `LIMIT`).
- If the result was empty, you explained whether the likely cause was indexing, ambiguity, or query precision.

## Practical Notes

- The official cookbook uses logical tool names like `find_code` and `execute_cypher_query`. Some integrations expose the same capabilities under server-specific prefixes. Use the tool names that exist in the current environment.
- If the current environment exposes only part of the CodeGraphContext toolset, prefer the available graph tools and be explicit about what cannot be done without indexing, monitoring, or bundle-management support.
- Prefer built-in graph tools for common tasks, and save raw Cypher for questions that need custom aggregation or graph traversal.

## Resources

- [Tool recipes and examples](./references/recipes.md) — Concrete JSON arguments for every tool
- [Cypher query patterns](./references/cypher-patterns.md) — Reusable Cypher queries by category
- Official cookbook: https://github.com/CodeGraphContext/CodeGraphContext/blob/main/docs/docs/cookbook.md
- Official MCP tools reference: https://github.com/CodeGraphContext/CodeGraphContext/blob/main/docs/MCP_TOOLS.md
