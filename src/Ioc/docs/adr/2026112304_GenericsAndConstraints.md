---
status: accepted
date: 2026-11-23
---

# Generics and constraints

## Context and problem statement

MS.DI can register an open generic such as `IRepo<>` and close it at run time for any type argument. It does this with reflection.

The generator creates every instance with generated code: member injection, injection methods, decorators, keys, and wrappers all come from a construction plan. It must decide at compile time which closed types exist.

To close an open registration, the generator must also decide whether a type argument satisfies the generic constraints. Roslyn has no public API that checks a constraint.

Open generics were the most repaired area of the previous design.

## Decision outcome

1. **Open generics support only the closed types that the generator finds at compile time.** A closed type is found from a dependency, from `IocDiscoverAttribute`, or from a resolve call that is visible in source.
2. **The service collection extension does not register the open generic itself.** It registers only the derived registrations.
3. **The generator decides constraints with its own rule table.** `Compilation.ClassifyConversion` decides type constraints, with these additions:
   - `new()` is not satisfied when the type has a `required` member, also an inherited one, unless the parameterless constructor has `[SetsRequiredMembers]`.
   - `unmanaged` needs the `struct` rule as well as `ITypeSymbol.IsUnmanagedType`.
   - `Nullable<T>` satisfies a type constraint only by identity, or when the substituted constraint is `System.Object` or `System.ValueType`.
   - For a ref struct under `allows ref struct`, the generator checks the implemented interfaces, because a ref struct has no boxing conversion.

## Rejected options

- **Also register the open generic with MS.DI in the service collection extension**, as the previous design did. A closed type that appears only at run time would then work, but:
  - The same service gets two behaviors. A closed type found at compile time uses generated code. A closed type that appears at run time is created by MS.DI with reflection and loses member injection, injection methods, the chosen constructor, keys from `[IocInject]`, decorators, asynchronous initialization, optional dependencies by nullable annotation, and its wrappers.
  - Collections contain duplicates (see Evidence).
  - Under NativeAOT, MS.DI rejects an open generic closed with a value type at run time, with no publish warning.
  - The previous design needed two more diagnostics to explain when the open generic was not registered.

  The answer for a closed type that the generator cannot see is to declare it with `IocDiscoverAttribute` or register it directly.
- **`ClassifyConversion` alone for constraints.** It disagrees with the compiler in three groups of cases (see Evidence). One group makes the generator emit a closed type that does not compile.

## Evidence

Tested with `Microsoft.Extensions.DependencyInjection` 10.0.15 on .NET 10.0.15, JIT and NativeAOT, and with Roslyn 5.12.0.

MS.DI with a closed registration `IRepo<Foo> -> ClosedRepo` and an open registration `IRepo<> -> Repo<>`:

| Registration order | `GetServices<IRepo<Foo>>()` | `GetService<IRepo<Foo>>()` |
| --- | --- | --- |
| closed, open | 2 items: `ClosedRepo`, `Repo<Foo>` | `ClosedRepo` |
| open, closed | 2 items: `Repo<Foo>`, `ClosedRepo` | `ClosedRepo` |

- The collection always contains both. The single service is always the closed registration, in any order. Keyed registrations behave the same way.
- Under NativeAOT, MS.DI refuses to build an `IEnumerable<T>` whose element is a value type, and it rejects an open generic service closed with a value type. Publish gives no warning.

Constraint table:

- The test compared the rule table with the compiler for 34 constraint forms and 102 candidate types, plus 5 two-parameter forms and 22 x 22 pairs: 5,888 cells. Ground truth was whether `P<A>` compiles.
- `ClassifyConversion` alone gave 30 wrong cells, in three groups:

  | Case | Example | `ClassifyConversion` alone | Compiler |
  | --- | --- | --- | --- |
  | `new()` with a `required` member | `New<RequiredMember>` | satisfied | CS9040 |
  | Type constraint with `Nullable<T>` | `Naked<int?, object>` (`where T : U`) | not satisfied | accepted |
  | Type constraint with `allows ref struct` | `IFooAllowsRef<RefStructImpl>` | not satisfied | accepted |

- With the additions of this decision, all 5,888 cells agree.
- The same matrix with the constraints on a static generic method, where ground truth is whether `M<A>()` compiles, gives the same result in every cell.
- `ITypeSymbol.IsUnmanagedType` returns `true` for `int?`, but the compiler rejects `Unmanaged<int?>` with CS0453.
- Roslyn 5.12.0 has no public API for constraint checks. The only public members with "Constraint" in the name describe the declaration.

Not tested: MS.DI 11.0.0. The matrix used compilations in memory, not `dotnet build`.
