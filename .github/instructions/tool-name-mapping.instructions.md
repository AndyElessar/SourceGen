---
description: "Use when agent files reference #tool: prefixed tool names. Maps agent tool declarations to runtime function names."
---

# Runtime Tool Name Mapping

Agent `.agent.md` files declare tools using `#tool:` prefixes in YAML frontmatter. This document maps those declarations to the actual runtime function names used when calling tools.

## Core Tools

| Agent Tool Declaration | Runtime Function Name(s) |
|------------------------|--------------------------|
| `vscode/memory` | `memory` |
| `vscode/askQuestions` | `vscode_askQuestions` |
| `agent` | `runSubagent`, `search_subagent` |
| `read` | `read_file` |
| `search` | `grep_search`, `semantic_search`, `file_search` |
| `edit` | `replace_string_in_file`, `multi_replace_string_in_file`, `create_file`, `create_directory` |
| `todo` | `manage_todo_list` |
| `web` | `fetch_webpage` |
| `browser` | `open_browser_page` |

## Execute Tools

| Agent Tool Declaration | Runtime Function Name(s) |
|------------------------|--------------------------|
| `execute` (full) | `run_in_terminal`, `get_terminal_output`, `await_terminal`, `kill_terminal` |
| `execute/runInTerminal` | `run_in_terminal` |
| `execute/getTerminalOutput` | `get_terminal_output` |
| `execute/testFailure` | `test_failure` |

## MCP Tool Prefixes

| Agent Tool Declaration | Runtime Function Pattern |
|------------------------|--------------------------|
| `microsoftdocs/mcp/*` | `mcp_microsoftdocs_*` |
| `github/*` | `mcp_github_*` or `mcp_io_github_git_*` |
| `github.vscode-pull-request-github/*` | `github-pull-request_*` |
| `vscode.mermaid-chat-features/*` | Mermaid rendering tools |

## Notes

- Agent files use `#tool:` prefixed names in documentation and prose (e.g., `#tool:vscode/memory`).
- Runtime code MUST use the actual function names from the mapping table above.
- When an agent declares a subset of `execute` (e.g., `execute/getTerminalOutput` only), only the mapped subset function is available to that agent.
