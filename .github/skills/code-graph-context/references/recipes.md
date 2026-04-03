# CodeGraphContext Recipes

Concrete tool invocation examples organized by category. Each recipe shows the tool name, JSON arguments, and optional natural-language trigger.

## Contents

- [Repository Setup and Monitoring](#repository-setup-and-monitoring)
- [Code Search](#code-search)
- [Relationship Analysis](#relationship-analysis)
- [Quality and Maintainability](#quality-and-maintainability)
- [Visualization](#visualization)

---

## Repository Setup and Monitoring

### Index a local repository once

- Tool: `add_code_to_graph`

```json
{
  "path": "/absolute/path/to/repo",
  "is_dependency": false
}
```

### Add a package to the graph

- Tool: `add_package_to_graph`
- Supported languages: python, javascript, typescript, java, c, go, ruby, php, cpp

```json
{
  "package_name": "requests",
  "language": "python",
  "is_dependency": true
}
```

### Load a pre-indexed bundle

- Tool: `load_bundle`
- Set `clear_existing` to `true` to replace the current graph before loading.

```json
{
  "bundle_name": "pandas",
  "clear_existing": false
}
```

### Search registry for available bundles

- Tool: `search_registry_bundles`
- Use `unique_only` to de-duplicate versions.

```json
{
  "query": "pandas",
  "unique_only": true
}
```

### Watch a directory for live updates

- Tool: `watch_directory`

```json
{
  "path": "/absolute/path/to/repo"
}
```

### Stop watching a directory

- Tool: `unwatch_directory`

```json
{
  "path": "/absolute/path/to/repo"
}
```

### List currently watched paths

- Tool: `list_watched_paths`
- No arguments.

### Check indexing progress

- Tool: `check_job_status`

```json
{
  "job_id": "<job-id>"
}
```

### List all background jobs

- Tool: `list_jobs`
- No arguments.

### List all indexed repositories

- Tool: `list_indexed_repositories`
- No arguments.

### Get repository statistics

- Tool: `get_repository_stats`
- Optionally pass `repo_path` to limit to one repository.

```json
{
  "repo_path": "/absolute/path/to/repo"
}
```

### Delete a repository from the graph

- Tool: `delete_repository`

```json
{
  "repo_path": "/absolute/path/to/repo"
}
```

---

## Code Search

### Find where a symbol is defined

- "Where is the function `foo` defined?"
- Tool: `find_code`
- Use `fuzzy_search` when the exact name is uncertain.

```json
{
  "query": "foo",
  "fuzzy_search": false
}
```

### Fuzzy search with edit distance

- "Find code matching something like `hndler`."
- Tool: `find_code`

```json
{
  "query": "hndler",
  "fuzzy_search": true,
  "edit_distance": 2
}
```

---

## Relationship Analysis

All recipes below use `analyze_code_relationships`. The `context` parameter is an optional file path to disambiguate when a symbol name exists in multiple files.

### Find all callers of a function

- "Find all calls to `helper`."

```json
{
  "query_type": "find_callers",
  "target": "helper"
}
```

### Find all direct and indirect callers

- "Show me all functions that eventually call `helper`."

```json
{
  "query_type": "find_all_callers",
  "target": "helper"
}
```

### Find what a function calls

- "What functions are called inside `foo`?"

```json
{
  "query_type": "find_callees",
  "target": "foo",
  "context": "/absolute/path/to/module_a.py"
}
```

### Find all direct and indirect callees

- "Show me all functions that are eventually called by `foo`."

```json
{
  "query_type": "find_all_callees",
  "target": "foo",
  "context": "/absolute/path/to/module_a.py"
}
```

### Find importers of a module

- "Where is the `math` module imported?"

```json
{
  "query_type": "find_importers",
  "target": "math"
}
```

### Find the class hierarchy

- "Show me all classes that inherit from `Base`."
- The response includes child classes and their methods.

```json
{
  "query_type": "class_hierarchy",
  "target": "Base"
}
```

### Find overridden methods

- "Find all methods that override `foo`."

```json
{
  "query_type": "overrides",
  "target": "foo"
}
```

### Find a call chain between two functions

- "What is the call chain from `wrapper` to `helper`?"

```json
{
  "query_type": "call_chain",
  "target": "wrapper->helper"
}
```

### Find functions by decorator

- "Find all functions with the `log_decorator`."

```json
{
  "query_type": "find_functions_by_decorator",
  "target": "log_decorator"
}
```

### Find functions by argument name

- "Find all functions that take `self` as an argument."

```json
{
  "query_type": "find_functions_by_argument",
  "target": "self"
}
```

---

## Quality and Maintainability

### Find the most complex functions

- Tool: `find_most_complex_functions`

```json
{
  "limit": 5
}
```

### Calculate complexity for one function

- Tool: `calculate_cyclomatic_complexity`
- Use `path` to disambiguate when function names are shared across files.

```json
{
  "function_name": "try_except_finally",
  "path": "/absolute/path/to/module.py"
}
```

### Find unused code while excluding decorated entrypoints

- Tool: `find_dead_code`

```json
{
  "exclude_decorated_with": ["@app.route"]
}
```

---

## Visualization

### Visualize a Cypher query result

- Tool: `visualize_graph_query`
- Returns a URL to view the result as a graph.

```json
{
  "cypher_query": "MATCH (f:Function)-[:CALLS]->(g:Function) WHERE f.path ENDS WITH 'main.py' RETURN f, g LIMIT 30"
}
```
