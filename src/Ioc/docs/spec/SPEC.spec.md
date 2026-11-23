# SourceGen.Ioc specifications: index

## Purpose

- The [Constitution](../Constitution.md) holds the highest rules. It defines the purpose, principles, terms, scope, and limits.
- The specifications in this directory **refine** the constitution. They add the topics that the constitution leaves to the specifications. They are precise enough to implement from and to write tests from.
- If a specification and the constitution disagree, the constitution wins. The person or agent who finds the conflict MUST tell the user, and the specification SHOULD be corrected. A specification does not repeat semantics that the constitution or [Registration and resolution semantics](Semantics.spec.md) already states. It only links to them.
- The terms follow the table of [Terms](../Constitution.md#3-terms) in the constitution.

## Documents

Read them in this sequence.

| # | Document | Scope |
| --- | --- | --- |
| 1 | [Registration and resolution semantics](Semantics.spec.md) | The registration model, dependency resolution, member injection, asynchronous services, decorators, open generics, wrapper types, factory and instance, keyed services, tags, the registration scope, the limits. Read it first. The other specifications refine it |
| 2 | [How the generator reads annotations](Annotations.spec.md) | The declarations, targets, and enum values of the attributes, `nameof()`, the forms and the canonical form of a key, the expansion of service types, the selection of the constructor and the injected members, the extraction of dependency sites |
| 3 | [Analysis model of the dependency graph](Analysis.Model.spec.md) | The type model, instance definitions, views, demands, the grammar of wrapper types, closing, generic constraints, dependency resolution, decorators, asynchronous services, the two levels of checks, the construction plan, the incremental pipeline |
| 4 | [Module output](Module.Output.spec.md) | The carrier type, the JSON syntax of the module info, the signatures of the construction entry / decorator entry / type entry / wrapper entry / closing entry, the reading flow of the importer |
| 5 | [Container output](Container.Output.spec.md) | The root and scopes, resolution, lookup by `Type`, the caching of wrapper types and collections, the behavior of the fallback provider item by item, eager resolution, thread safety, disposal, partial accessors |
| 6 | [`Add{Name}` output](Register.Output.spec.md) | The method signature, tags, holders, the disposal of decorator chains, asynchronous services, the registration of wrapper types and collections |
| 7 | [Diagnostic list](Diagnostics.spec.md) | The common rules (numbering, levels, the two pipelines), the ID, message, level, consequence, and location of each diagnostic, and an index of the diagnostics by problem |

"How the generator reads annotations" is the preliminary part that was separated from the analysis model.

## Writing conventions

- Normative words: MUST and MUST NOT are absolute requirements and prohibitions. SHOULD means that a valid reason can justify a different behavior. MAY means that the item is optional.
- "Reports `SGIOCnnn`" always means that the generator reports that diagnostic. For its level and consequence, see the [Diagnostic list](Diagnostics.spec.md).
- Code samples are divided with `#region Define:`, `#region Generate:`, and `#region Invalid:`. A sample of generated code is an illustration, unless its section explicitly specifies the signature (the entries of a module).

## Types only for generated code

The types below are public, because generated code must use them across assemblies. They are not an API for the user, and their signatures can change with the version of the generator.

| Type | Source |
| --- | --- |
| The entry class `__IocModule` inside the module class | Generated |
| The carrier type `{module class name}__IocModuleInfo` | Generated |
| `IocModuleInfoAttribute` | The `SourceGen.Ioc` library |
| `IocHolder<T>`, `IocHolder` | The `SourceGen.Ioc` library |
| `IocTask<T>` | The `SourceGen.Ioc` library |
| `IocRegistrationMarker` | The `SourceGen.Ioc` library |

- They MUST have both `[EditorBrowsable(EditorBrowsableState.Never)]` and `[Experimental("SGIOCEXP001", Message = "...")]`. All of them share the same diagnostic ID. The `Message` says that SourceGen.Ioc generates the type, or that the type is only for the code that SourceGen.Ioc generates, and that the user should not use it directly.
- When user code refers to them, the user gets the compile error `SGIOCEXP001`. The code compiles only when the user suppresses the diagnostic explicitly. The details below hold (tested, see the ADRs):
  - A reference inside the same assembly also reports the diagnostic. The code of the `SourceGen.Ioc` library that refers to these types MUST also suppress it.
  - Code inside any `[Experimental]` type does not report it.
  - The use of an attribute class that has `[Experimental]` (`[IocModuleInfo("...")]`) also reports it. Thus a generated file always needs the `#pragma` below.
  - When the generator reads these types and attributes with the symbol API, no diagnostic is triggered.
  - It is a warning that is raised to an error by default. `NoWarn` and `.editorconfig` can disable it or lower it. `[SuppressMessage]` has no effect.
- Each file that the generator generates starts with `#pragma warning disable SGIOCEXP001`, whether or not the file uses these types.
- To stay short, the samples in the specifications omit `Message` and the `#pragma` at the start of the file.

## Open questions

Questions for which there was not enough basis to decide at the time of writing:

1. **The output form of the dependency graph information.** The constitution requires that the information that the generator emits is enough to rebuild the dependency graph (see [Principles](../Constitution.md#4-principles), section 4.7). The module info already covers modules. These specifications do not define the form in which the dependency graph of a container is emitted (the resolution result of each edge, the candidates that constraints excluded and the reasons, the real composition of each decorator chain). This will be decided together with the specification of the tool that shows the graph.

## Not covered by these specifications

- The test specification: the organization of snapshot tests, the NativeAOT test project, the specific cases of the incremental tests. The section [Tests](Diagnostics.spec.md#tests) of the Diagnostic list gives only the minimum requirements for diagnostics.
- The benchmark specification: the list of items to verify one by one, and the pass criteria.
- The naming rules for generated members: only the requirements are specified (deterministic, no collisions), not the exact names.
- The structure of the generator project itself (the division into files, the use of `SourceWriter`).
- The features that the constitution lists as deferred (see [Scope](../Constitution.md#5-scope), section 5.3): the activators of MVC and Blazor, feature switches, the tool that shows the dependency graph, the generic visitor, service lookup by a generated `switch`, and the compile-time check for a covered `Add{Name}` call.
