---
status: accepted
date: 2026-11-23
---

# Cross-assembly composition

## Context and problem statement

An application often spreads its registrations over several assemblies. Many implementation types are internal to their assembly. The assembly that builds the final container or the final `Add{Name}` method cannot name those types.

In the previous design, an imported module was a container of its own. The importing container merged the resolver tables of its modules at run time through `IIocContainer<TContainer>.Resolvers`. Each module held instances, and the user called the `Add{Name}` method of every module.

## Decision outcome

1. **Only the composer owns instances.** A module holds no instance. It gives registration information (module info) and public entries. The composer flattens all modules into one dependency graph and manages lifetime, caching, and disposal.
2. **`IIocContainer` is not generic.** The type parameter `TContainer` existed only for the static abstract `Resolvers` member, which is removed.
3. **Module info is a JSON string in an attribute on a carrier type.** The generator emits a separate type, `{ModuleClass}__IocModuleInfo`, and puts `IocModuleInfoAttribute(string json)` on it. No generated code refers to the carrier type, not even with `typeof`. The importer reads it only at compile time through the symbol API.
4. **Entries let the composer use internal types.** A module emits public construction entries, decorator entries, type entries, wrapper entries, and closing entries. An entry signature never contains an inaccessible type. Such a type is passed as `object` and cast inside the entry.
5. **The JSON reader and writer are written by hand.** They do not use `System.Text.Json`. The generator and the CLI share the same source files. Reading is strict: an unknown version, an unknown enum string, a duplicate property, an index out of range, or an invalid combination of properties is an error.
6. **Ordering does not depend on imports.** Derived registrations come before concrete registrations. Inside each group the order is by sort name, then key. The order ignores which assembly declares a registration. An assembly therefore cannot override a registration of a module that it imports.
7. **Types that exist only for generated code are marked.** `__IocModule`, the carrier type, `IocModuleInfoAttribute`, `IocHolder`, `IocTask`, and `IocRegistrationMarker` have `[EditorBrowsable(Never)]` and `[Experimental("SGIOCEXP001")]`. Each generated file starts with `#pragma warning disable SGIOCEXP001`.

## Rejected options

- **Modules as containers that are merged at run time.** Instances then have several owners, the dependency graph is not known at compile time, and cross-module checks are not possible.
- **Another channel for module info.** No public generator API can emit a non-source file that another compilation reads. An assembly-level attribute keeps the whole string in a NativeAOT executable as soon as any code reads the attributes of that assembly. A `private` or `internal` constant is not visible to the generator of another assembly.
- **Module info on the module class.** The module class is used by generated code, so NativeAOT keeps its attribute string.
- **`System.Text.Json` with its source generator.** A prototype worked, but:
  - The .NET Framework compiler host needs four extra DLLs next to the generator. When one is missing, the generator fails with CS8785 and all generated output disappears.
  - The loaded `System.Text.Json` version depends on the host. The escaping of strings depends on that version, and the output must be identical byte for byte.
  - Its built-in encoders cannot write all non-ASCII characters unescaped, and they replace an unpaired surrogate with U+FFFD.
  - Loading inside the Visual Studio IDE was not verified.
- **Order the registrations of the current assembly after the imported ones**, so that the application overrides a module. The order would depend on the import structure, and the container (where ambiguity is an error) and `Add{Name}` would differ more. To replace an implementation of a module, use a key or a tag.
- **`[EditorBrowsable(Never)]` only.** It hides a name from IntelliSense but does not stop a user from typing it, and it often has no effect for project references.

## Evidence

Tested on Windows x64 with .NET SDK 11.0.100, Roslyn 5.12.0, .NET runtime 10.0.15, and `Microsoft.Extensions.DependencyInjection` 10.0.15.

Entries:

- A hand-written prototype of 12 scenarios across two assemblies published with NativeAOT (.NET 10) without warnings. It covered:
  - Internal dependencies passed as `object`.
  - Optional dependencies passed as a flag and a value.
  - Asynchronous entries.
  - Generic entries called with type arguments that only the application knows, including value types.
  - Wrappers over internal types for `Lazy<T>`, `Func<T>`, and arrays.
- A later probe added wrapper entries for `Func<A..., T>`, `Dictionary<TKey, T>`, `Task<T>`, and `KeyValuePair<K, T>` over internal reference and value types. NativeAOT publish had no warnings, and the output was equal to the JIT output.
- A constraint that refers to an internal type cannot be written on a public generic entry (CS0703, CS0314).
- A type entry for a decorator dependency that uses the type parameter of the decorator works. An open registration and a concrete registration shared one generic decorator `LoggingHandler<Q>` with an internal dependency `IInternalLog<Q>`. The importer used only the JSON to get `typeof(IInternalLog<T>)` for its own type `T` and to build the decorated instance. This passed with the .NET compiler, the .NET Framework compiler, and NativeAOT.

Module info channel:

- No public Roslyn API emits a non-source file that another compilation can read at compile time. The experimental `RegisterHostOutput` delegate does not run under `dotnet build`. Content given to `AddSource` is always compiled as C#.
- An attribute string of 20 MB still compiles.
- When no code mentions the carrier type, its attribute string is absent from the NativeAOT executable. When the application contains MS.DI, the whole string stays in the executable in each of these cases:
  - The code stores `typeof` of that type as a `Type`.
  - The code uses that `Type` as a dictionary key.
  - The code reads its `.Name`.
  - The attribute is on a class that the code creates with `new`.
- An assembly-level attribute is trimmed only while no code calls `GetCustomAttributes()` on that assembly.
- With a DLL reference, the generator of another assembly cannot read a `private const` or `internal const`. With `InternalsVisibleTo`, `internal const` is readable and `private const` is not. Attribute arguments are readable in all cases.

JSON reader and writer (two prototypes, compared on the same input):

| Item | `System.Text.Json` | Hand-written |
| --- | --- | --- |
| Lines of code, without tests | 359 | about 1,180 after all validation was added |
| Extra DLLs for the .NET Framework compiler | 4 (`System.Text.Json`, `System.Text.Encodings.Web`, `System.IO.Pipelines`, `Microsoft.Bcl.AsyncInterfaces`) | 0 |
| First read in a compiler process, .NET 11 / .NET Framework 4.8 | about 76 ms / 310 ms | about 10 ms / 21 ms |
| Read of 10,000 definitions, .NET 11 | about 122 ms | about 37 ms |

- Timings are single-machine `Stopwatch` measurements. Use them for the order of magnitude only.
- The hand-written reader passed 471 checks, including every truncated prefix of two sample documents and 200,000 random mutations, without an unhandled exception.
- An unpaired surrogate that is written unescaped into the attribute string is damaged in metadata: `D83D` reads back as three `FFFD` characters. The writer therefore escapes unpaired surrogates.
- `SymbolDisplay.FormatLiteral` round-trips all 65,536 UTF-16 code units through a C# string literal and metadata.

`[Experimental]`:

- A reference from another assembly is a compile error unless suppressed.
- A reference from non-experimental code in the same assembly is reported too.
- Applying an attribute class that is marked `[Experimental]` is reported.
- `#pragma warning disable` on the first line of a file suppresses all of them.
- Reading such a type or attribute through the symbol API in a generator reports nothing.

Type resolution across target frameworks (.NET SDK 11.0.100, compiler host with Roslyn 5.12.0, generator compiled against Microsoft.CodeAnalysis.CSharp 5.0.0; modules for netstandard2.0, net10.0, and net48; importers for net10.0, net11.0, and net48; Microsoft.Bcl.AsyncInterfaces 10.0.5; `dotnet build`):

- A net10.0 or net11.0 compilation references the facade assemblies `netstandard`, `mscorlib`, `System`, `System.Core`, `System.Memory`, and `System.Threading.Tasks.Extensions`. The reference assemblies do not contain `System.Private.CoreLib`.
- `IAssemblySymbol.GetTypeByMetadataName` returns `null` for a type that the assembly forwards. It finds a type only in the assembly that defines it.
- `IAssemblySymbol.ResolveForwardedType` resolves a forwarded top-level type correctly. `[netstandard]System.String` resolves to `System.Runtime` in a net10.0 importer and to `mscorlib` in a net48 importer. It returns `null` for the defining assembly and for a nested name such as `System.Environment+SpecialFolder`.
- The same type is a definition for one importer and a forwarded type for another. `[Microsoft.Bcl.AsyncInterfaces]System.IAsyncDisposable` is a definition in a net48 importer and a forwarded type in a net10.0 importer.
- "Find the assembly by name, then look for a defined type, then for a forwarded type" gave a symbol equal to the type in the signature of the module in every case that can be resolved.
- `Compilation.GetTypeByMetadataName` gave a wrong result in these cases:
  - The importer does not reference `Microsoft.Bcl.AsyncInterfaces`. The compiler sees the type of the module as an error type, but the lookup returns `[System.Runtime]System.IAsyncDisposable`.
  - The importer has two definitions of `System.IAsyncDisposable`. The lookup does not return `null`. It returns the one in the core library, and the module uses the other one.
  - The assembly name is not referenced at all. The lookup still returns a type.
- `Compilation.GetTypeByMetadataName` returns `null` for an ambiguous name only when none of the definitions is in the core library.
- The only case that no method resolves is an `assembly` that is not in the compilation of the importer. The compiler also treats that type as an error type.

Not tested for type resolution: extern aliases, two referenced assemblies with the same simple name, a missing target of a forwarder, an importer for netstandard2.0, and the IDE host.

Not tested: loading and behavior inside the Visual Studio or Rider IDE. Closing entries for a derived registration with a closed decorator were tested only as JSON.
