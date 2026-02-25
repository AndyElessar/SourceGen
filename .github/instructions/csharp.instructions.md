---
description: "Use when writing or reviewing C# code. Covers C# 14 syntax, naming, formatting, and nullable conventions."
applyTo: '**/*.cs'
---

# C# Development

## C# 14 Syntax

- Always use the latest C# 14 features where applicable.
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

## Testing

- Do not emit "Act", "Arrange" or "Assert" comments.
- Copy existing style in nearby files for test method names and capitalization.

## References

- [C# extension members](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/extension-methods)
- [C# extension declaration](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/extension)
- [C# extension members specification](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-14.0/extensions)
- [C# record](https://learn.microsoft.com/zh-tw/dotnet/csharp/language-reference/builtin-types/record)