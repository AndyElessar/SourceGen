# Cypher Query Patterns for CodeGraphContext

All queries use `execute_cypher_query`. Queries are read-only. Always add `LIMIT` to avoid unbounded results.

## Contents

- [Discovery](#discovery)
- [Imports](#imports)
- [Call Graph](#call-graph)
- [Classes and Inheritance](#classes-and-inheritance)
- [Code Quality and Refactoring](#code-quality-and-refactoring)
- [Security](#security)

---

## Discovery

### List all function definitions

```json
{
  "cypher_query": "MATCH (n:Function) RETURN n.name, n.path, n.line_number LIMIT 50"
}
```

### List all classes

```json
{
  "cypher_query": "MATCH (n:Class) RETURN n.name, n.path, n.line_number LIMIT 50"
}
```

### Find all functions in one file

```json
{
  "cypher_query": "MATCH (f:Function) WHERE f.path ENDS WITH 'module_a.py' RETURN f.name"
}
```

### Find all classes in one file

```json
{
  "cypher_query": "MATCH (c:Class) WHERE c.path ENDS WITH 'advanced_classes.py' RETURN c.name"
}
```

### List top-level functions and classes in a file

```json
{
  "cypher_query": "MATCH (f:File)-[:CONTAINS]->(n) WHERE f.name = 'module_a.py' AND (n:Function OR n:Class) AND n.context IS NULL RETURN n.name"
}
```

### Count functions per file

```json
{
  "cypher_query": "MATCH (f:Function) RETURN f.path, count(f) AS function_count ORDER BY function_count DESC"
}
```

---

## Imports

### List all package imports from a directory

```json
{
  "cypher_query": "MATCH (f:File)-[:IMPORTS]->(m:Module) WHERE f.path ENDS WITH '.py' RETURN DISTINCT m.name"
}
```

### Find all modules imported by a specific file

```json
{
  "cypher_query": "MATCH (f:File {name: 'module_a.py'})-[:IMPORTS]->(m:Module) RETURN m.name AS imported_module_name"
}
```

### Find circular file imports

```json
{
  "cypher_query": "MATCH (f1:File)-[:IMPORTS]->(m2:Module), (f2:File)-[:IMPORTS]->(m1:Module) WHERE f1.name = m1.name + '.py' AND f2.name = m2.name + '.py' RETURN f1.name, f2.name"
}
```

---

## Call Graph

### Find cross-module calls

- "Find functions in `module_a.py` that call `helper` in `module_b.py`."

```json
{
  "cypher_query": "MATCH (caller:Function)-[:CALLS]->(callee:Function {name: 'helper'}) WHERE caller.path ENDS WITH 'module_a.py' AND callee.path ENDS WITH 'module_b.py' RETURN caller.name"
}
```

### Find recursive functions

```json
{
  "cypher_query": "MATCH p=(f:Function)-[:CALLS]->(f2:Function) WHERE f.name = f2.name AND f.path = f2.path RETURN p"
}
```

### Find most connected hub functions

```json
{
  "cypher_query": "MATCH (f:Function) OPTIONAL MATCH (f)-[:CALLS]->(callee:Function) OPTIONAL MATCH (caller:Function)-[:CALLS]->(f) WITH f, count(DISTINCT callee) AS calls_out, count(DISTINCT caller) AS calls_in ORDER BY (calls_out + calls_in) DESC LIMIT 5 MATCH p=(f)-[*0..2]-() RETURN p"
}
```

### Find dead code via Cypher

```json
{
  "cypher_query": "MATCH (f:Function) WHERE NOT (()-[:CALLS]->(f)) AND f.is_dependency = false RETURN f.name, f.path"
}
```

### Find all calls to a function with a specific argument

```json
{
  "cypher_query": "MATCH ()-[r:CALLS]->(f:Function {name: 'helper'}) WHERE 'x' IN r.args RETURN r.full_call_name, r.line_number, r.path"
}
```

### Find functions that call `super()`

```json
{
  "cypher_query": "MATCH (f:Function)-[r:CALLS]->() WHERE r.full_call_name STARTS WITH 'super(' RETURN f.name, f.path"
}
```

---

## Classes and Inheritance

### Find all dataclasses

```json
{
  "cypher_query": "MATCH (c:Class) WHERE 'dataclass' IN c.decorators RETURN c.name, c.path"
}
```

### Find classes with a specific method

```json
{
  "cypher_query": "MATCH (c:Class)-[:CONTAINS]->(m:Function {name: 'greet'}) RETURN c.name, c.path"
}
```

### Find inheritance depth for all classes

```json
{
  "cypher_query": "MATCH (c:Class) OPTIONAL MATCH path = (c)-[:INHERITS*]->(parent:Class) RETURN c.name, c.path, length(path) AS depth ORDER BY depth DESC"
}
```

### Find methods that override a parent method (via Cypher)

```json
{
  "cypher_query": "MATCH (c:Class)-[:INHERITS]->(p:Class), (c)-[:CONTAINS]->(m:Function), (p)-[:CONTAINS]->(m_parent:Function) WHERE m.name = m_parent.name RETURN m.name as method, c.name as child_class, p.name as parent_class"
}
```

### Find decorated methods in a class

```json
{
  "cypher_query": "MATCH (c:Class {name: 'Child'})-[:CONTAINS]->(m:Function) WHERE m.decorators IS NOT NULL AND size(m.decorators) > 0 RETURN m.name"
}
```

---

## Code Quality and Refactoring

### Find functions with many arguments

```json
{
  "cypher_query": "MATCH (f:Function) WHERE size(f.args) > 5 RETURN f.name, f.path, size(f.args) as arg_count"
}
```

### Find large functions that may need refactoring

```json
{
  "cypher_query": "MATCH (f:Function) WHERE f.end_line - f.line_number > 20 RETURN f"
}
```

### Find documented functions (those with docstrings)

```json
{
  "cypher_query": "MATCH (f:Function) WHERE f.docstring IS NOT NULL AND f.docstring <> '' RETURN f.name, f.path LIMIT 50"
}
```

---

## Security

### Find likely hardcoded secrets

The official cookbook uses `f.source_code`. If the current deployment uses a different schema, try the equivalent source-text property such as `f.source`.

```json
{
  "cypher_query": "WITH ['password', 'api_key', 'apikey', 'secret_token', 'token', 'auth', 'access_key', 'private_key', 'client_secret', 'sessionid', 'jwt'] AS keywords MATCH (f:Function) WHERE ANY(word IN keywords WHERE toLower(f.source_code) CONTAINS word) RETURN f"
}
```
