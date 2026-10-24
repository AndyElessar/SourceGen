---
description: "Use when writing or reviewing C# code. Covers C# 15 syntax, naming, formatting, and nullable conventions."
applyTo: '**/*.cs'
---

# C# Development

## C# 15 Syntax

- Always use the latest C# 15 features where applicable.
- Use pattern matching and switch expressions wherever possible.
- Use `nameof` instead of string literals when referring to member names.

## Naming Conventions

- PascalCase for type names, method names, and public members.
- camelCase for private fields and local variables.
- Prefix interfaces with "I" (e.g., `IUserService`).

## Formatting

- Apply code-formatting style defined in `.editorconfig`.
- Prefer file-scoped namespace declarations and single-line using directives.
- Insert a newline before the opening curly brace of any code block.
- Ensure XML doc comments for public APIs.

## Nullable Reference Types

- Declare variables non-nullable, and check for `null` at entry points.
- Always use `is null` / `is not null` instead of `== null` / `!= null`.
- Trust C# null annotations — don't add null checks when the type system guarantees non-null.

## References

- [pattern matching](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/patterns)
- [switch expression](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/switch-expression)
- [extension members](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/extension-methods)
- [extension declaration](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/extension)
- [extension members specification](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-14.0/extensions)
- [record](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/record)
- [collection expressions](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/collection-expressions)
- [union](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/union)
- [closed hierarchies](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-15.0/closed-hierarchies)