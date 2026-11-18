# Ioc Analyzer

Index of the SourceGen.Ioc analyzer specs: the analyzer pipelines and the diagnostic rules.

## Pipeline Architecture

### Entry Points

| Analyzer | Role | File |
| :------- | :--- | :--- |
| `RegisterAnalyzer` | Registration validation, dependency/lifetime checks, key diagnostics | `Analyzer/RegisterAnalyzer.cs` |
| `ContainerAnalyzer` | Container class validation, resolvability, module cycle detection | `Analyzer/ContainerAnalyzer.cs` |

Both are `[DiagnosticAnalyzer(LanguageNames.CSharp)]` with lifecycle: `Initialize` → `CompilationStart` → actions → `CompilationEnd`.

| Spec | Scope |
| :--- | :---- |
| [RegisterAnalyzer](Analyzer.Register.spec.md) | CompilationStart setup, registered actions, compilation end analysis, default settings, partial files |
| [ContainerAnalyzer](Analyzer.Container.spec.md) | CompilationStart setup, registered actions, compilation end analysis, registration resolution, container scope |

### Diagnostic Coverage

| Analyzer | Diagnostics |
| :------- | :---------- |
| RegisterAnalyzer | SGIOC001, 002, 003, 004, 005, 006, 007, 008, 009, 010, 011, 012, 013, 014, 015, 016, 017, 022, 023, 024, 026, 028, 030, 032, 033, 034, 035, 036, 037, 038, 039, 040, 041, 042, 043, 044 |
| ContainerAnalyzer | SGIOC018, 019, 020, 021, 025, 027, 029, 031, 045 |

### CompilationEnd Diagnostics

A descriptor whose diagnostics are reported from a `CompilationEnd` action MUST carry `WellKnownDiagnosticTags.CompilationEnd`, and a descriptor reported from a symbol, syntax node or semantic model action MUST NOT. Live analysis of a single file only runs the untagged ones, and a compilation end diagnostic without the tag is never shown there, so the IDE could neither run nor display it correctly. Command-line builds report both alike.

A rule reported from both kinds of actions has two descriptors with the same ID, title, message, severity and category, one of them tagged:

| ID | Symbol / syntax node action | CompilationEnd action |
| :- | :-------------------------- | :-------------------- |
| SGIOC001 | `InvalidAttributeUsage`: private or abstract target type, imported open registrations the compiled assembly cannot construct | `UnconstructibleImplementation`: implementation types that cannot be constructed without a `Factory` or `Instance` |
| SGIOC014 | `ServiceKeyNotRegistered`: `[ServiceKey]` parameters of the registration's own `Factory` | `ServiceKeyNotRegisteredForRegistration`: `[ServiceKey]` constructor and inject method parameters |
| SGIOC038 | `OpenTypeNestedInGenericType`: open types nested in a generic type used by an attribute | `ImplicitOpenTypeNestedInGenericType`: those an open implementation would be registered as through `RegisterAllInterfaces` / `RegisterAllBaseClasses` |

The other diagnostics reported at compilation end are SGIOC002-005, 011-013, 015, 026, 030, 033-037, 039 and 042 (`RegisterAnalyzer`) and SGIOC018, 021, 025, 027, 029 and 045 (`ContainerAnalyzer`). The analyzer tests check every reported diagnostic: those that the syntax and semantic analysis of their file reports MUST NOT be tagged, and the others MUST be.

## Diagnostics

Format: ID - Level - Category - Description

| Spec | Diagnostics |
| :--- | :---------- |
| [Registration](Diagnostics.Registration.spec.md) | SGIOC001, SGIOC009, SGIOC010, SGIOC011, SGIOC012, SGIOC032, SGIOC038, SGIOC040, SGIOC041 |
| [Dependency](Diagnostics.Dependencies.spec.md) | SGIOC002, SGIOC003, SGIOC004, SGIOC005, SGIOC033 |
| [Injection](Diagnostics.Injection.spec.md) | SGIOC006, SGIOC007, SGIOC022, SGIOC023, SGIOC024, SGIOC028, SGIOC043 |
| [Key](Diagnostics.Keys.spec.md) | SGIOC013, SGIOC014, SGIOC015 |
| [Factory](Diagnostics.Factory.spec.md) | SGIOC008, SGIOC016, SGIOC017, SGIOC039, SGIOC042, SGIOC044 |
| [Container](Diagnostics.Container.spec.md) | SGIOC018, SGIOC019, SGIOC020, SGIOC021, SGIOC025, SGIOC031, SGIOC045 |
| [Async-init](Diagnostics.AsyncInit.spec.md) | SGIOC026, SGIOC027, SGIOC029, SGIOC030, SGIOC046 |
| [Decorator](Diagnostics.Decorators.spec.md) | SGIOC034, SGIOC035 |
| [Generic Dependency](Diagnostics.Generics.spec.md) | SGIOC036, SGIOC037 |
