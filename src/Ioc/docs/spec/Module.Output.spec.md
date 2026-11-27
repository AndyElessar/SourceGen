# Module output

> Parent document: [Constitution](../Constitution.md). If this document and the constitution disagree, the constitution wins.

The key words MUST, MUST NOT, SHOULD, SHOULD NOT, and MAY in this document are to be interpreted as described in BCP 14 (RFC 2119, RFC 8174) when, and only when, they appear in all capitals.

## Overview

A class that has `IocModuleAttribute` makes its assembly a module. The generator generates two things for a module:

| Output | Location | Reader |
| --- | --- | --- |
| Module info | `IocModuleInfoAttribute` on the carrier type. The content is a JSON string. | The generator of the importer, at compile time only |
| Entries | The nested static class `__IocModule` in the module class | The generated code of the composition root, at run time |

A module owns no instance (see [Composition model](../Constitution.md#6-composition-model)). It gives only registration information and entries. The composition root uses the module info to merge the instance definitions of the module into its own dependency graph. The composition root flattens the registrations of all modules into that one graph. The composition root calls an entry when it must create an object, get a `Type`, or get a wrapper type.

The module output contains only the instance definitions of the assembly scope (see [Registration scope](Semantics.spec.md#registration-scope) and [Views](Analysis.Model.spec.md#views)). An imported module is recorded only in the import list. The module output does not contain the private registrations of an `ExplicitOnly` container.

A public member or type that the generator generates and that the user does not call directly (the entries and the carrier type) SHOULD have `[EditorBrowsable(EditorBrowsableState.Never)]`.

## Module class

- The module class MUST be a `public partial class`.
- The module class MUST be a top-level type (not nested) and MUST NOT be generic. If it is not, the generator reports `SGIOC107` and does not generate the module output. The names of the carrier type and of the entry class come from the module class. This limit keeps those names free of ambiguity.
- Each assembly can have one module class at most (`SGIOC103`).
- One class can have both `IocModuleAttribute` and `IocContainerAttribute`. An `ExplicitOnly` container is the exception.

## Carrier type

```csharp
#region Define:
namespace Mod;

[IocModule]
public partial class ModModule;
#endregion

#region Generate:
namespace Mod
{
    [global::System.CodeDom.Compiler.GeneratedCode("SourceGen.Ioc", "1.0.0")]
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    [global::System.Diagnostics.CodeAnalysis.Experimental("SGIOCEXP001")]
    [global::SourceGen.Ioc.IocModuleInfo("{\"version\":1,...}")]
    public static class ModModule__IocModuleInfo;
}
#endregion
```

- Mapping: the carrier type is in the same namespace as the module class. Its name is `{module class name}__IocModuleInfo`. The importer gets the symbol of the module class from `IocImportModuleAttribute`. Then it looks for the carrier type by metadata name in the assembly that contains that symbol.
- The module info attribute is on the carrier type, which the generator generates separately. It is not on the module class itself. This lets NativeAOT trim the module info completely.
- The carrier type MUST NOT appear in generated code, and this includes `typeof`. The importer reads it only at compile time, through the symbol. NativeAOT can trim the module info only on this condition (tested, see the ADRs).
  - When nothing mentions the carrier type, the string is fully absent from the executable.
  - When the application contains MS.DI, the whole string stays in the executable in each of these cases: code stores `typeof(that type)` as a `Type`, uses it as a dictionary key, reads `.Name`, or puts the attribute on a class of which an instance is created (tested, see the ADRs).
- The module info MUST NOT be in an assembly-level attribute. Any code that calls `GetCustomAttributes()` on that assembly, for example to read version information, keeps the whole string (tested, see the ADRs).
- The module info MUST NOT be in a `private` or `internal` constant field. The generator of another assembly cannot read such a field (tested, see the ADRs).
- `IocModuleInfoAttribute` is defined in the `SourceGen.Ioc` library:

  ```csharp
  [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
  [EditorBrowsable(EditorBrowsableState.Never)]
  [Experimental("SGIOCEXP001")]
  public sealed class IocModuleInfoAttribute(string json) : Attribute
  {
      public string Json { get; } = json;
  }
  ```

  The attribute MUST NOT have `[Conditional]`. With `[Conditional]`, it does not go into metadata.

## JSON of the module info

The module info MUST be self-contained: the importer does not depend on a symbol in the module that it cannot access. For an accessible type, the importer can query the symbol, for example to check a generic constraint.

### Reader and writer

The generator has its own JSON reader and writer. It does not reference `System.Text.Json`. The generator and the CLI share the same source code. Each of them compiles the code as linked source files, so there is no additional DLL.

- Reason: the generator runs in the compiler process of another party. If a DLL that it depends on does not load, the full generation result disappears (`CS8785`). Also, the output MUST be the same bit for bit. It MUST NOT depend on the library version that the host has.
- Both approaches were built as prototypes and compared: the custom reader and writer, which this specification uses, and `System.Text.Json` (tested, see the ADRs).
- The generator converts the argument of the generated attribute to a C# string literal with `SymbolDisplay.FormatLiteral` of Roslyn.

### Write rules

The output MUST be deterministic: the same input gives a string that is the same bit for bit.

- The output contains no whitespace and no line breaks.
- The writer writes the properties in the order of the tables in this document.
- The writer omits a property that is equal to its default value. The writer always writes a property that is marked "Required" in the "Default" column of a table, also when it is an empty array.
- The writer escapes only the characters below in a string. It writes all other characters as they are. This includes all characters at U+007F and above, and paired surrogates.

  | Character | Output |
  | --- | --- |
  | `"`, `\` | `\"`, `\\` |
  | U+0008, U+000C, U+000A, U+000D, U+0009 | `\b`, `\f`, `\n`, `\r`, `\t` |
  | The other characters in U+0000 to U+001F | `\uXXXX`, four upper-case hexadecimal digits |
  | An unpaired surrogate | `\uXXXX`, four upper-case hexadecimal digits. Without the escape, the string is damaged after it goes through metadata (tested, see the ADRs). |

Order:

| List | Order |
| --- | --- |
| `definitions` | The entries with `derived` equal to `true` come first. The others come after them, with no separation between concrete registrations and open registrations. Each group follows steps 2 to 4 of [Ordering](Analysis.Model.spec.md#ordering). |
| `imports`, `serviceTypes` | The type sort key |
| `types` | The type sort key. When the keys are equal, an entry without `definition` comes first. Then the order follows the index in `definition`. Then it follows `decorator`: an entry without it comes first, then by index. |
| `demands` | The sort key of `type`, then `mode` (`none`, `key`, `allKeyed`), then the canonical form of the key |
| `tags` | Ordinal |
| `typeParameters` | The order of declaration (ordinal position) |
| `constraints` | Special constraints come first, in the order `class`, `struct`, `unmanaged`, `notnull`, `new`, `allowsRefStruct`. Type constraints come after them, by the type sort key. This is the order in the JSON. When the generator generates the `where` clause of an entry, it MUST put the constraints in the order of C# syntax (`new()` is last). |
| `dependencies` | The parameter order of the entry (the order has meaning) |
| `decorators` | The outermost layer first (the order has meaning) |
| `chains` | The index in `service` |

**Type sort key**: take the canonical string of the type reference (see [Canonical string of a type](Analysis.Model.spec.md#canonical-string-of-a-type)), remove each `[{Assembly}]`, and compare with ordinal comparison. When the results are equal, compare the full canonical strings.

The writer does not sort. `chains` and `types` contain indexes, so the party that builds the model MUST sort before it calculates the indexes.

### Read rules

A read has one of three results: success, unknown version, or format error. The reader does not throw exceptions.

How the reader decides the version:

1. The reader scans the properties of the root object from the start and stops at the first `version`. If the content before it is not valid JSON, or if there is no `version`, the result is a format error.
2. If the value is not a JSON number, has a fraction or an exponent, or is outside the range of `int`, the result is a format error.
3. If the value is an integer that the importer does not know, the result is unknown version. The generator reports `SGIOC202`, and the diagnostic names the module to rebuild. The importer does not try to be compatible. The reader does not read or check the remaining content, also when a second `version` comes later.
4. If the value is an integer that the importer knows, the reader reads all the content and applies the rules below.

General rules:

- The reader does not depend on the order of the properties.
- The reader ignores a property that it does not know. The value of that property MUST still be valid JSON, and the objects in it MUST NOT have duplicate properties.
- A property of a type reference that does not belong to the form of that reference is handled as an unknown property, and the reader ignores it. Examples are `accessible` on a type parameter and `rank` on a named type.
- The reader accepts an explicit default value (`"accessible":true`, `"rank":1`, `"mode":"none"`). It also accepts `[]` for an array that is marked "Omitted". The writer does not produce them. The only exception is `arguments` of a type reference. An empty array there is a format error, because "omitted" has a different meaning.
- `"key":null` is the same as no `key`.

Each of the cases below is a format error, and the generator reports `SGIOC206`:

- The JSON cannot be parsed.
- A property marked "Required" is missing, or the value of a property is not the JSON type that the table specifies.
- A property appears two times in the same object. This includes a second `version` in the root object.
- An enumeration string is unknown (`kind`, `lifetime`, `mode`, `disposable`, special constraints). Enumeration strings are case-sensitive.
- An explicit `null` appears. Only `key` is an exception.
- An index is out of range: `chains[].service`, `chains[].inner`, `types[].definition`, `types[].decorator`, or the `parameter` ordinal of a type parameter (see the table below for the ranges).
- A combination of properties is not valid:
  - A type reference is not exactly one of the three forms. `arguments` is an empty array. `rank` is less than 1. `parameter` is less than 0.
  - The `mode` of a demand is not `"key"` and the demand has `key`, or the `mode` is `"key"` and the demand has no `key`.
  - A key contains a property that is marked "-" for that kind of key.
  - `kind` and `instanceType` do not agree. The form of `id` does not agree with `kind`: the `id` of `implementation` is not a named type, or the `id` of `instance` has `parameters` or `arguments`.
  - The number of `chains[].arguments` is different from the number of `typeParameters` of the decorator. When both are omitted, the number is 0.
  - A type entry has `decorator` and no `definition`.
- The nesting depth of the JSON is more than 64 levels. The root object is level 1. Each level of generic arguments in a type reference uses two levels, so a generic type with approximately 28 nested levels is over the limit. The writer does not check this separately. The importer reports such a module info.

Range of the `parameter` ordinal (the ordinal MUST be less than the number of type parameters in the range):

| Location | Range |
| --- | --- |
| The `id` of an instance definition (`type`, `parameters`, and `arguments` of a factory), `instanceType`, `serviceTypes`, `dependencies`, the constraints of its type parameters, `chains[].arguments` | The `typeParameters` of that instance definition |
| The `type` of a decorator, its `dependencies`, the constraints of its type parameters | The `typeParameters` of that decorator |
| The `type` of a type entry | The decorator, when the type entry has `decorator`. If not, the instance definition that `definition` points to. |
| `imports`, `demands`, the types in a key | A type parameter MUST NOT appear |

The reader checks only the structure of the JSON:

- Some rules need an understanding of the types, for example that `wrap` is required when `type` is a wrapper type. The importer checks these rules when it uses the data. A violation also causes `SGIOC206`.
- The reader cannot know which range a `{"parameter": n}` came from. It can only check the ordinal with the table above. If the module puts a type entry on the wrong owner, the reader cannot find the error.

The format version is an integer. It is `1` now. It increases with each incompatible change to the structure of the JSON, to the signature conventions of the entries, or to the semantics of the entries.

### Root object

| Property | Type | Default | Content |
| --- | --- | --- | --- |
| `version` | Number | Required | The format version |
| `imports` | Type[] | `[]` | The module classes that this module imports at assembly scope |
| `demands` | Demand[] | `[]` | The `IocDiscoverAttribute` declarations and the resolve calls of this assembly |
| `definitions` | Instance definition[] | `[]` | The instance definitions of the assembly scope, and the derived registrations that the module closes itself |
| `types` | Type entry[] | `[]` | The types that the composition root cannot write |

### Types

A type is always in a structured form: the type definition, the tree of type arguments, the array dimensions, the type arguments of the containing type, and whether the type is accessible outside the assembly. The module info does not use a single display string for a type. This avoids type parameter substitution on strings.

A type reference (see [Type model](Analysis.Model.spec.md#type-model)) has three forms:

| Form | Property | Default | Content |
| --- | --- | --- | --- |
| Named type | `definition` | Required | The full metadata name, for example ``Mod.Outer`1+Inner`` |
|  | `assembly` | Required | The simple name of the assembly that defines the type in the compilation of the module: `ContainingAssembly.Name` of the symbol. For a type of the base class library, this is the reference assembly of the target framework of the module (for example `System.Runtime` or `netstandard`), not the implementation assembly. |
|  | `accessible` | `true` | Whether this node itself is accessible (type arguments not included) |
|  | `arguments` | Omitted | The flattened type arguments. Omitted means a non-generic type or an unbound generic. It MUST NOT be an empty array. |
| Array | `array` | Required | The element type |
|  | `rank` | `1` | The number of dimensions, 1 at least |
| Type parameter | `parameter` | Required | The ordinal, which starts at 0 |

A type reference MUST be exactly one of the three forms: exactly one of `definition`, `array`, and `parameter` appears.

```jsonc
{ "definition": "Mod.IHandler`1", "assembly": "Mod",
  "arguments": [ { "definition": "Mod.Dto", "assembly": "Mod", "accessible": false } ] }
{ "array": { "parameter": 0 } }
```

### Key

`null` means no key. Each other key is an object.

In the table below, each property that is not marked "-" is required. A property marked "-" MUST NOT appear for that kind of key.

| Property | Value key | Symbol key |
| --- | --- | --- |
| `kind` | `"value"` | `"symbol"` |
| `type` | The type of the value | The type that contains the member |
| `value` | The text of the value (see [Canonical form of a key](Annotations.spec.md#canonical-form-of-a-key)). It is always a JSON string. | - |
| `member` | - | The member name |
| `valueType` | - | The declared type of the member (the key type) |

A key in the module output is always accessible (`SGIOC105`). The importer writes its expression directly: a value key as a literal, an enum as `(global::Ns.Color)2`, and a symbol key as `global::Ns.Keys.Member`.

### Demands

The demand list holds the dependency demands of this assembly that belong to no instance definition: the types that `IocDiscoverAttribute` and resolve calls ask for. With it, the composition root can generate closed generics and wrapper types for the code in the module. The key of each demand is in one of three states: no key, a specified key, or all keyed registrations of that type.

| Property | Default | Content |
| --- | --- | --- |
| `type` | Required | The requested type (it can be a wrapper type) |
| `mode` | `"none"` | `"none"` / `"key"` / `"allKeyed"` |
| `key` | Omitted | Required when `mode` is `"key"`. In the other cases it MUST NOT appear. |

### Instance definitions

| Property | Default | Content |
| --- | --- | --- |
| `kind` | Required | `"implementation"` / `"factory"` / `"instance"` |
| `id` | Required | See below |
| `derived` | `false` | This is a derived registration that the module closes itself (see [Closing entry](#closing-entry)) |
| `key` | `null` | The key |
| `lifetime` | Required | `"Singleton"` / `"Scoped"` / `"Transient"` |
| `instanceType` | See the content | The return type of the factory (`T` for `Task<T>`), or the type of the instance member. Required when `kind` is `factory` or `instance`. It MUST NOT appear for `implementation`, where the instance type is `id`. |
| `serviceTypes` | `[]` | For `implementation`: the service types other than the implementation type itself. The implementation type itself is implicit and is not in this list. A decorator does not apply to the implementation type itself, so `chains[].service` always has a target. For `factory` and `instance`: all service types, with no implicit entry. When `ServiceTypes` is not set, the list holds the return type of the factory or the type of the instance member. When `ServiceTypes` is set, the list holds the specified types only. |
| `tags` | `[]` | The tags |
| `async` | `false` | Whether the instance itself is asynchronous: the implementation type has an asynchronous injection method, or the factory method returns `Task<T>`. The decorators do not count. Each decorator has its own `async`. The importer decides whether a closed definition is asynchronous after closing (see [Asynchronous classification](Analysis.Model.spec.md#asynchronous-classification)). |
| `disposable` | `"no"` | `"yes"` / `"no"` / `"runtime"` |
| `asyncDisposable` | `"no"` | Same as above |
| `typeParameters` | Omitted | The type parameters and constraints of an open registration |
| `entry` | Required | The method name of the [construction entry](#construction-entry) |
| `dependencies` | `[]` | The dependencies. Their order is the parameter order of the entry. |
| `decorators` | `[]` | The decorators. The first one is the outermost layer. |

Forms of `id`:

| `kind` | `id` |
| --- | --- |
| `implementation` | The implementation type. For an open registration it is an unbound generic (no `arguments`). |
| `factory` | `{ "type": containing type, "member": method name, "arity": number of type parameters, "parameters": [parameter types], "refKinds": [ref kinds], "arguments": [type arguments] }`. `type` and `member` are required. The default of `arity` is `0`, and it is omitted for a non-generic method. The default of `parameters` is `[]`, and it is omitted when the method has no parameters. `refKinds` has one string for each parameter: `"none"`, `"in"`, `"refReadonly"`, `"ref"`, or `"out"`. It is omitted when all parameters are `"none"`. With these, the `id` has all parts of the identity (see [Identity](Analysis.Model.spec.md#identity) in the analysis model). `arguments` appears only on a derived registration. |
| `instance` | `{ "type": containing type, "member": member name }`. Both are required. |

In an open registration, the types in `serviceTypes`, `instanceType`, and `dependencies` point to `typeParameters` with `{"parameter": n}`.

Disposal kind: `disposable` is for `IDisposable`, and `asyncDisposable` is for `IAsyncDisposable`. Each has one of three states:

| Value | Meaning |
| --- | --- |
| `"yes"` | Dispose |
| `"no"` | No disposal necessary |
| `"runtime"` | Check at run time. This state occurs only for a factory. |

### Type parameters

| Property | Default | Content |
| --- | --- | --- |
| `name` | Required | The name. It is used only to show the dependency graph. |
| `constraints` | Required | An array. Write `[]` when there are no constraints. A string is a special constraint: `"class"`, `"struct"`, `"unmanaged"`, `"notnull"`, `"new"`, `"allowsRefStruct"`. An object is a type constraint. It can use `parameter` to point to a type parameter in the same list. |

### Dependencies

| Property | Default | Content |
| --- | --- | --- |
| `type` | Required | The requested type |
| `key` | `null` | The key. The module has already replaced `InheritKey` with the key of the instance definition that owns the dependency. |
| `optional` | `false` | Whether the dependency is optional |
| `constructor` | `false` | Whether the dependency is a constructor parameter (used to map `Func<..., T>`) |
| `serviceKey` | `false` | Appears only on a decorator: this parameter has `[ServiceKey]`, and the composition root passes the key of the decorated instance definition |

The `[ServiceKey]` parameters of the instance definition itself are not in `dependencies`. An entry belongs to one instance definition, and the entry supplies the key itself.

### Decorators

| Property | Default | Content |
| --- | --- | --- |
| `type` | Required | The decorator type. A generic decorator is an unbound generic. |
| `typeParameters` | Omitted | The type parameters and constraints of a generic decorator |
| `entry` | Required | The method name of the [decorator entry](#decorator-entry) |
| `async` | `false` | Whether the decorator has an asynchronous injection method |
| `disposable`, `asyncDisposable` | `"no"` | The disposal kind |
| `dependencies` | Required | All parameters of the constructor (the inner parameter included) and the injected members. Their order is the parameter order of the entry. The types are expressed with the type parameters of the decorator itself. |
| `chains` | Required | The service types that this decorator applies to |

Each element of `chains` is a result of the mapping at the definition level (see [Mapping at the definition level](Analysis.Model.spec.md#mapping-at-the-definition-level)):

| Property | Default | Content |
| --- | --- | --- |
| `service` | Required | The index in `serviceTypes` |
| `inner` | Required | The index in `dependencies`: the inner parameter |
| `arguments` | Omitted | The type arguments of the decorator, expressed with the type parameters of the instance definition. Omitted for a non-generic decorator. |

When the composition root closes an instance definition, it substitutes the type arguments into `arguments` to get the type arguments of the decorator. Then it substitutes those into the `dependencies` of the decorator. The composition root does not need to match types itself.

### Type entries

| Property | Default | Content |
| --- | --- | --- |
| `type` | Required | The inaccessible type |
| `entry` | Required | The name of the entry method that returns `System.Type` |
| `wrap` | Omitted | The method name of the [wrapper entry](#type-entry-and-wrapper-entry). Required when `type` is a wrapper type. |
| `definition` | Omitted | The index in `definitions`. Required when `type` contains a type parameter: both entries are generic methods with the same type parameters as that instance definition. |
| `decorator` | Omitted | The index in the `decorators` of the instance definition that `definition` points to. Required when the type parameters in `type` belong to a decorator (they come from a dependency of a generic decorator): the type parameters and constraints of the entry then come from that decorator. |

The owner of a type entry, and how type entries are matched:

- A type entry belongs to one scope at most. A type entry without type parameters has no owner. In the other cases the owner is one instance definition or one decorator type. One `type` never contains type parameters of two scopes, because the dependencies of a decorator are expressed only with the type parameters of the decorator itself.
- The key for the removal of duplicates is "type + owner", not the type only. `IInternalLog<{parameter 0}>` as a service type of an open registration and as a dependency of a generic decorator gives the same JSON. But the constraints of the entry come from different owners, so there MUST be two type entries.
- When more than one instance definition uses the same decorator type, the generator generates the type entries of its dependencies only once. `definition` and `decorator` point to the first such instance definition in `definitions`.
- How the composition root matches:
  - An entry that belongs to an instance definition: `definition` is equal to the instance definition that the composition root closes, and `type` is structurally equal to the requested type.
  - An entry that belongs to a decorator: `decorators[].type` is equal to the decorator type in use, and `type` is structurally equal to the type of the dependency (expressed with the type parameters of the decorator). The composition root does not compare the index of the instance definition.
  - An entry without an owner: `type` is structurally equal.
- `definition` and `decorator` are structural pointers. The composition root resolves them before it filters tags and applies suppression. The type entry stays valid also when the composition root filters out the instance definition that it points to.
- Type arguments of the entry: when the entry belongs to a decorator, the composition root substitutes the type arguments of the instance definition that it closes into the `chains[].arguments` of that chain. When the same decorator applies to more than one service type of the same definition, the composition root calculates each chain separately.

The rules above work when an open registration and a concrete registration share one generic decorator, and the importer uses only the JSON that it reads (tested, see the ADRs).

### Content that is not in the module info

The module info does not contain these items:

- How a Lifetime is implemented (locks, caching, eager resolution).
- The wrapper types and closed generics of accessible types.
- Construction details (which constructor is used, the kinds and order of the injected members, default values).

## Entries

All entries are public static methods of `public static class __IocModule` in the module class.

```csharp
partial class ModModule
{
    [global::System.CodeDom.Compiler.GeneratedCode("SourceGen.Ioc", "1.0.0")]
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    [global::System.Diagnostics.CodeAnalysis.Experimental("SGIOCEXP001")]
    public static class __IocModule
    {
        // Entries
    }
}
```

Common rules:

- An inaccessible type MUST NOT appear in a signature.
- The module decides the method names and writes them into the module info. The importer does not derive names. A name MUST be unique in `__IocModule` and MUST be deterministic. It SHOULD stay stable across unrelated edits. Recommended: a prefix, the short type name, and an FNV-1a hash of the sort name and the key, for example `Create_Greeter_1a2b3c4d`. The examples in this document use simplified names so that they are easy to read.
- Entries MUST NOT be overloaded.
- The generated code always uses type names qualified with `global::`. Each `await` always uses `ConfigureAwait(false)`.

The shape of the entries passes NativeAOT on .NET 10 without warnings. A hand-written prototype verified this for construction entries, type entries, wrapper entries, asynchronous entries and generic entries (tested, see the ADRs).

### Parameter types

The erasure rule decides the types of the parameters and of the return value of an entry. For a type `X`:

| `X` | After erasure |
| --- | --- |
| Accessible | `X` |
| Inaccessible | `object` |

The entry restores an inaccessible parameter with a cast (`(IRepo)repo`). This is a checked cast. If the caller passes the wrong object, the cast throws `InvalidCastException`.

### Construction entry

Each instance definition has one construction entry. The composition root creates all imported instance definitions through the construction entry, and this includes types that are fully public. The entry hides which constructor is used, how members are injected, and what the default values are. The entry MUST let the caller omit optional dependencies.

```text
public static {return} {entry}{<type parameters>}({parameters}...) {where constraints}
```

| Part | Rule |
| --- | --- |
| Return | The instance type after erasure. The instance type is the implementation type, the return type of the factory, or the type of the instance member. An asynchronous instance definition returns `Task<that type>`. |
| Type parameters | For an open registration, the same type parameters as the implementation type (or the generic factory method). The constraints are copied as they are. |
| Parameters | One for each item of `dependencies`, in order. A required dependency is one parameter. Its type is the requested type after erasure. An optional dependency is two parameters: a `bool` flag (whether a value is supplied) and the value. |

Behavior in the entry:

1. The entry creates the instance and injects members. It follows [Constructor selection](Annotations.spec.md#constructor-selection) and the [Order](Annotations.spec.md#order) of injected members.
2. When an optional dependency is not supplied (the flag pattern):
   - A parameter with a default value: the entry uses the copied default literal.
   - A field or property that keeps its initializer: the entry does not assign it.
3. A `[ServiceKey]` parameter: the entry supplies the key of this instance definition.
4. Asynchronous: after synchronous injection is complete, the entry awaits the asynchronous injection methods one by one. When the factory returns `Task<T>`, the entry awaits it.

```csharp
#region Define:
public interface IClock;
public interface ILogger;
public interface IGreeter;
public interface IService;
public interface IWorker;
public interface IAsyncService;
internal interface IRepo;

[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IGreeter)])]
internal sealed class Greeter(IClock clock) : IGreeter;

[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)])]
internal sealed class Repo : IRepo;

[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IService)])]
internal sealed class Service(IRepo repo) : IService;

[IocRegister(ServiceLifetime.Transient, ServiceTypes = [typeof(IWorker)])]
internal sealed class Worker(ILogger? logger = null, int retries = 3) : IWorker;

[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IAsyncService)])]
internal sealed class AsyncService : IAsyncService
{
    [IocInject] public Task InitAsync(IClock clock) => Task.CompletedTask;
}
#endregion

#region Generate:
// The implementation type is inaccessible, so the entry returns object
public static object Create_Greeter(global::Mod.IClock clock)
    => new global::Mod.Greeter(clock);

public static object Create_Repo()
    => new global::Mod.Repo();

// An inaccessible dependency comes in as object, and the entry casts it
public static object Create_Service(object repo)
    => new global::Mod.Service((global::Mod.IRepo)repo);

// An optional dependency = flag + value. The default value is copied into the entry
public static object Create_Worker(bool hasLogger, global::Mod.ILogger logger, bool hasRetries, int retries)
    => new global::Mod.Worker(hasLogger ? logger : null, hasRetries ? retries : 3);

// Asynchronous
public static async global::System.Threading.Tasks.Task<object> Create_AsyncService(global::Mod.IClock clock)
{
    global::Mod.AsyncService instance = new global::Mod.AsyncService();
    await instance.InitAsync(clock).ConfigureAwait(false);
    return instance;
}
#endregion
```

In this example, the `int retries` of `Worker` is a dependency (type `System.Int32`, optional). The composition root finds no registration for `int` and passes `hasRetries: false`.

The construction entry of an open registration is a generic method:

```csharp
#region Define:
public interface IHandler<T>;

[IocRegister(ServiceLifetime.Scoped, ServiceTypes = [typeof(IHandler<>)])]
internal sealed class Handler<T>(IClock clock) : IHandler<T> where T : class;
#endregion

#region Generate:
public static object Create_Handler<T>(global::Mod.IClock clock) where T : class
    => new global::Mod.Handler<T>(clock);
#endregion
```

The composition root calls `ModModule.__IocModule.Create_Handler<global::App.Order>(...)` and casts the result to `IHandler<Order>`.

The construction entry of an instance registration has no parameters. It returns the static member:

```csharp
public static global::Mod.IClock Create_SystemClock() => global::Mod.Clocks.System;
```

### Decorator entry

Each decorator type has one decorator entry. The number of instance definitions that use the decorator does not change this.

```text
public static {return} {entry}{<type parameters of the decorator>}({parameters}...) {where constraints}
```

- Return: the decorator type after erasure. When `async` is `true`, it is `Task<that type>`.
- Parameters: one for each item of the `dependencies` of the decorator, in order. The rules are the same as for the construction entry. The inner parameter is one of them. The composition root passes the inner instance at the position that `chains[].inner` gives.
- The type of a parameter with `serviceKey` equal to `true` is its declared type after erasure. The composition root passes the key.

```csharp
#region Define:
internal sealed class LoggingHandler<T>(IHandler<T> inner, ILogger logger) : IHandler<T>;
#endregion

#region Generate:
public static object Decorate_LoggingHandler<T>(global::Mod.IHandler<T> inner, global::Mod.ILogger logger)
    => new global::Mod.LoggingHandler<T>(inner, logger);
#endregion
```

### Type entry and wrapper entry

The composition root cannot write an inaccessible type, but it still needs two things:

- The `Type` object of the type. The composition root uses it to answer a request by `Type` at run time (code in the module calls `sp.GetService(typeof(IRepo))`). It also uses it to resolve from the fallback provider by `Type` (`ILogger<InternalService>`).
- A way to create an instance of the type when it is a wrapper type (`Lazy<IRepo>`, `IRepo[]`).

Each item in `types` has one entry that returns `Type`. A wrapper type also has a wrapper entry.

```csharp
public static global::System.Type Type_IRepo() => typeof(global::Mod.IRepo);
```

A wrapper entry accepts the inner value after erasure and returns `object`. The outermost wrapper decides the parameters:

| Outermost layer of `type` | Parameters of the wrapper entry | Content |
| --- | --- | --- |
| `Lazy<X>` | `Func<E(X)>` | `new Lazy<X>(() => (X)f())` |
| `Func<X>` | `Func<E(X)>` | `new Func<X>(() => (X)f())` |
| `Func<A1, ..., An, X>` | `Func<E(A1), ..., E(An), E(X)>` | The matching delegate |
| `Task<X>` | `Task<E(X)>` | A `Task<X>` that converts the result. When the input is already complete, the entry allocates no state machine. |
| `KeyValuePair<K, X>` | `K`, `E(X)` | A boxed `KeyValuePair<K, X>` |
| Collection (each interface or an array) | `ReadOnlySpan<E(X)>` | `X[]` |
| Dictionary (each kind) | `ReadOnlySpan<KeyValuePair<K, E(X)>>` | `Dictionary<K, X>` |

`E(X)` is the type of `X` after erasure: `X` when `X` is accessible, and `object` when it is not. When the inner type is also an inaccessible wrapper type, the composition root first calls the wrapper entry of the inner type. Then it passes the resulting `object` to the outer wrapper entry. Thus the entries of the layers together build a wrapper with more than one layer.

```csharp
public static global::System.Type Type_Lazy_IRepo() => typeof(global::System.Lazy<global::Mod.IRepo>);
public static object Wrap_Lazy_IRepo(global::System.Func<object> resolve)
    => new global::System.Lazy<global::Mod.IRepo>(() => (global::Mod.IRepo)resolve());

public static global::System.Type Type_IEnumerable_IRepo()
    => typeof(global::System.Collections.Generic.IEnumerable<global::Mod.IRepo>);
public static object Wrap_IEnumerable_IRepo(global::System.ReadOnlySpan<object> items)
{
    global::Mod.IRepo[] array = new global::Mod.IRepo[items.Length];
    for (int i = 0; i < items.Length; i++) array[i] = (global::Mod.IRepo)items[i];
    return array;
}
```

When `type` contains a type parameter (an inaccessible service type or dependency of an open registration), both entries are generic methods. Their type parameters and constraints come from the instance definition that `definition` points to. When the type parameter belongs to a generic decorator (a dependency of the decorator is a type such as `IInternalLog<Q>`), they come from the decorator that `decorator` points to:

```csharp
public static global::System.Type Type_Handler<T>() where T : class => typeof(global::Mod.Handler<T>);
```

#### Which types have a type entry

After the closing of the assembly view is complete, the generator collects the inaccessible types below. It removes duplicates by "type + [owner](#type-entries)".

1. The service types of each instance definition of this module, the implementation type itself included.
2. `IEnumerable<S>` for each type `S` of item 1 (always generated for each registered service type). When the instance definition is asynchronous, these are `IEnumerable<Task<S>>` and its inner `Task<S>` instead.
3. Each layer of the requested type, for the dependencies of each instance definition and decorator of this module and for the demand list: the full requested type, each wrapper layer, and the core service type.

The core service type of item 3 covers the case of an open generic of the fallback provider in the table of [Closing entry](#closing-entry). `ILogger<InternalService>` has no registration in the module or in its imports. The module still generates a type entry for it, and the composition root resolves from the fallback provider with that `Type`.

An accessible type has no entry. The composition root writes it itself.

### Closing entry

When the core service type of a demand contains an inaccessible type argument (`IValidator<InternalDto>`), the composition root cannot write `Create_Validator<InternalDto>(...)`. It also cannot check the constraints. The module does this closing in its own assembly view:

1. The closing process of the module (see [Closing](Analysis.Model.spec.md#closing)) produces derived registrations. The module outputs those whose closed type arguments contain an inaccessible type as instance definitions with `derived: true`.
2. Each of them has its own construction entry, which is a non-generic method. When the open registration comes from an imported module, the entry calls the generic construction entry of that module.
3. Their decorators are closed too. `decorators[].type` is the closed type. `entry` points to the non-generic decorator entry that the module generates for this closing. `chains[].arguments` is omitted.
4. The closed service types (always inaccessible) get type entries by the rules of the previous section.
5. The module does not decide suppression. The module outputs the derived registration and also the concrete registration that can suppress it. The composition root applies suppression (see [Suppression](Analysis.Model.spec.md#suppression)) after it filters tags.

```csharp
#region Define:
internal sealed class Dto;

public interface IValidator<T>;

[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IValidator<>)])]
public sealed class Validator<T> : IValidator<T>;

[IocRegister(ServiceLifetime.Scoped)]
internal sealed class DtoConsumer(IValidator<Dto> validator);
#endregion

#region Generate:
// The generic entry of the open registration
public static global::Mod.Validator<T> Create_Validator<T>() => new global::Mod.Validator<T>();

// The closing entry of Validator<Dto>: the composition root cannot write Dto
public static object Create_Validator_Dto() => new global::Mod.Validator<global::Mod.Dto>();
#endregion
```

When code in the module needs an open generic closed with an inaccessible type, the handling depends on the source of the open generic:

| Source of the open generic | Output of the module | What the composition root does |
| --- | --- | --- |
| The module itself, or a module that it imports | An instance definition with `derived: true`, and a closing entry | Handles it as a normal closed definition |
| None of them | A type entry for the core service type | With a fallback provider: resolves from the fallback provider by `Type`. Without one: missing dependency. |
| The assembly of the composition root itself, or another module that this module does not import | Same as above (the module cannot know) | `SGIOC405`: the composition root finds in its own view a service type pattern of an open registration that matches this type, but there is no related closing |

An example of the second case is an open generic of the fallback provider, such as `ILogger<>` or `IOptions<>`.

The third case is not supported. It is an error at the composition root. The module cannot know this case at its compile time, so the error is not in the module.

A "generic visitor" can solve the third case: the module gives an entry that accepts a visitor, and the composition root implements the visitor. This works under NativeAOT (tested, see the ADRs). It is a deferred feature (see [Deferred features](../Constitution.md#53-deferred-features)).

## Checks on the module side

| Condition | Diagnostic | Consequence |
| --- | --- | --- |
| A key in the module output is inaccessible | `SGIOC105` | The module output is not generated |
| An open registration or generic decorator in the output has a constraint that references an inaccessible type | `SGIOC106` | The module output is not generated |
| The module class is not a top-level type, or is generic | `SGIOC107` | The module output is not generated |
| The same assembly has more than one module | `SGIOC103` | The module output is not generated |
| The definite check finds an error in the dependency graph | `SGIOC3xx` | The module output is not generated |

- `SGIOC105`: a key in the module output MUST be accessible outside the assembly (see [Keyed services](Semantics.spec.md#keyed-services)). The error is in the assembly of the module.
- `SGIOC106`: a generic constraint of an open generic implementation or decorator in the module output MUST NOT refer to a type that is not accessible outside the assembly. The error is in the assembly of the module. The importer cannot check such a constraint, and the public generic entry cannot be declared.

When the module output is not generated, the importer sees a class that has `IocModuleAttribute` but no carrier type. The generator reports `SGIOC201`.

## Importer

### Import rules

- The tag settings of the composition root decide the filter. The composition root treats imported registrations and its own registrations the same.
- An `ExplicitOnly` container includes all registrations that it imports.
- The module info of each module contains only the registrations and the import list of its own assembly. The importer walks the imports recursively itself (see [Read sequence](#read-sequence)).
- The composition root applies decorators.
- The composition root decides the closed generics from the complete dependency graph. The exception is a closed generic whose type arguments contain an inaccessible type (see [Closing entry](#closing-entry)).
- The order of imports does not affect any observable behavior (see the ordering rule in [Registration model](Semantics.spec.md#registration-model)).

### Read sequence

1. Get the symbol of the module class from `IocImportModuleAttribute.Module`.
2. Look for the carrier type in the assembly that contains that symbol, and read the string argument of `IocModuleInfoAttribute`. If the carrier type or the attribute is not found, report `SGIOC201`.
3. Parse the JSON. For an unknown version, report `SGIOC202`. For a format error, report `SGIOC206`.
4. Do the same recursively for each item of `imports`. If the assembly of a module class is not in the references of the importer, report `SGIOC204`.
5. Record the path during the traversal. If the traversal comes back to a module that is already on the path, report `SGIOC203`.
6. When more than one path leads to the same module, process it only once. A module is identified by the assembly and the full metadata name of its module class.
7. Merge the instance definitions of all modules. If an instance definition with the same identity appears in two assemblies, report `SGIOC205`.

The generator reports each problem only once. When the read of a module fails, the generator does not report the missing dependencies that occur because the instance definitions of that module do not exist. The outputs that depend on that import are not generated.

### How the importer resolves types

| Type | Handling |
| --- | --- |
| The full type reference is accessible | Resolve each named type with the steps below. Then build the type reference again from the symbol. |
| The type reference contains an inaccessible node | Do not look for a symbol. An inaccessible node is identified by `assembly` and `definition` in the JSON. |
| A step below fails | `SGIOC204`: the importer has no reference to the assembly that contains the type |

Steps to resolve a named type from `assembly` and `definition`:

1. Find the assembly with the simple name `assembly` among the assembly of the compilation and the assemblies that it references. The step fails when there is no such assembly, or more than one.
2. Split `definition` at each `+`. Take the outermost type name. Look for it in that assembly as a type that the assembly defines. If the assembly does not define it, look for it as a type that the assembly forwards. The importer MUST try both. The same type is a definition for one target framework and a forwarded type for another.
3. For each further part of `definition`, find the nested type with that metadata name in the type of the step before. A lookup of a forwarded type does not accept a nested name, so the importer goes down one level at a time.
4. The step fails when the result is missing or is an error type.

The importer MUST NOT look for the type by metadata name in the whole compilation. That lookup ignores `assembly`. It can return a different type with the same name, and it succeeds for an assembly that the importer does not reference (tested, see the ADRs).

A module and its importer can have different target frameworks, so the `assembly` of the same type can differ between them. The importer decides whether two accessible types are the same from the resolved symbols. It MUST NOT compare the `assembly` and `definition` strings of the JSON for that purpose.

The importer does not depend on inaccessible symbols in the module. For an inaccessible type, the importer does only three things: it compares for equality, it calls the type entry to get the `Type`, and it calls the wrapper entry to create an instance.

### How the importer uses entries

| Need | Procedure |
| --- | --- |
| Create an imported instance | Call `entry`. The arguments follow the order of `dependencies`. For an optional dependency, pass the flag and the value. |
| Use the instance as an accessible service type `S` | When the entry returns `object`, cast with `(S)instance` |
| Pass the instance to an inaccessible parameter of another entry | Pass the `object` directly |
| Apply a decorator | Call the `entry` of the decorator. Pass the inner instance at the position `chains[].inner`. The other positions are normal dependencies. |
| Register or look up an inaccessible service type by `Type` | Call `types[].entry` |
| Create an inaccessible wrapper type | Call `types[].wrap` |
| Close an open registration when all type arguments are accessible | Call the generic `entry` with the type arguments |
| Close an open registration when a type argument is inaccessible | Use the `derived` instance definition in the module output |

## Complete example

```jsonc
// The JSON on Mod.ModModule__IocModuleInfo (the real output has no whitespace and no comments)
{
  "version": 1,
  "definitions": [
    {
      "kind": "implementation",
      "id": { "definition": "Mod.Handler`1", "assembly": "Mod", "accessible": false },
      "lifetime": "Scoped",
      "serviceTypes": [
        { "definition": "Mod.IHandler`1", "assembly": "Mod", "arguments": [ { "parameter": 0 } ] }
      ],
      "typeParameters": [ { "name": "T", "constraints": [ "class" ] } ],
      "entry": "Create_Handler",
      "dependencies": [
        { "type": { "definition": "Mod.IClock", "assembly": "Mod" }, "constructor": true }
      ],
      "decorators": [
        {
          "type": { "definition": "Mod.LoggingHandler`1", "assembly": "Mod", "accessible": false },
          "typeParameters": [ { "name": "T", "constraints": [] } ],
          "entry": "Decorate_LoggingHandler",
          "dependencies": [
            { "type": { "definition": "Mod.IHandler`1", "assembly": "Mod", "arguments": [ { "parameter": 0 } ] }, "constructor": true },
            { "type": { "definition": "Mod.ILogger", "assembly": "Mod" }, "constructor": true }
          ],
          "chains": [ { "service": 0, "inner": 0, "arguments": [ { "parameter": 0 } ] } ]
        }
      ]
    },
    {
      "kind": "implementation",
      "id": { "definition": "Mod.Repo", "assembly": "Mod", "accessible": false },
      "lifetime": "Singleton",
      "serviceTypes": [ { "definition": "Mod.IRepo", "assembly": "Mod", "accessible": false } ],
      "entry": "Create_Repo"
    },
    {
      "kind": "implementation",
      "id": { "definition": "Mod.Service", "assembly": "Mod", "accessible": false },
      "lifetime": "Singleton",
      "serviceTypes": [ { "definition": "Mod.IService", "assembly": "Mod" } ],
      "entry": "Create_Service",
      "dependencies": [
        { "type": { "definition": "Mod.IRepo", "assembly": "Mod", "accessible": false }, "constructor": true }
      ]
    }
  ],
  "types": [
    { "type": { "definition": "Mod.Handler`1", "assembly": "Mod", "accessible": false, "arguments": [ { "parameter": 0 } ] },
      "entry": "Type_Handler", "definition": 0 },
    { "type": { "definition": "Mod.IRepo", "assembly": "Mod", "accessible": false }, "entry": "Type_IRepo" },
    { "type": { "definition": "Mod.Repo", "assembly": "Mod", "accessible": false }, "entry": "Type_Repo" },
    { "type": { "definition": "Mod.Service", "assembly": "Mod", "accessible": false }, "entry": "Type_Service" }
    // and the IEnumerable<...> of each, with wrap
  ]
}
```

### Example with all features

This example uses each part of the format one time at least. The assembly `Core` contains the module class `Core.CoreModule`. That module registers `Core.ILogger` as a Singleton.

```csharp
#region Define:
namespace Mod;

public static class Keys
{
    // Not const, so a symbol key
    public static readonly string UtcClock = "utc";
}

public interface IEntity;
public interface IClock;
public interface IStore;
public interface IReport;
public interface IHandler<T>;

public sealed class Order : IEntity;

[IocModule]
[IocImportModule(typeof(Core.CoreModule))]
[IocDiscover(typeof(IHandler<Order>), Key = "main")]
[IocInstanceFor(nameof(Clocks.Utc), KeyType = KeyType.Csharp, Key = nameof(Keys.UtcClock))]
public partial class ModModule;

public static class Clocks
{
    public static readonly IClock Utc = new UtcClock();
}

internal sealed class UtcClock : IClock;

[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)])]
public sealed class Store : IStore, IDisposable
{
    public void Dispose() { }
}

// An open registration with a value key, two tags and two decorators. The type is inaccessible
[IocRegister(ServiceLifetime.Scoped, ServiceTypes = [typeof(IHandler<>)], Key = "main",
    Decorators = [typeof(RetryHandler<>), typeof(LoggingHandler<>)], Tags = ["web", "api"])]
internal sealed class Handler<T>(
    IStore store,
    [IocInject(KeyType = KeyType.Csharp, Key = nameof(Keys.UtcClock))] IClock? clock = null) : IHandler<T>
    where T : class, IEntity, new();

public sealed class RetryHandler<T>(IHandler<T> inner) : IHandler<T>;

public sealed class LoggingHandler<T>(Core.ILogger logger, IHandler<T> inner) : IHandler<T>;

internal sealed class Report(Core.ILogger logger) : IReport;

public static class Reports
{
    // Returns Task<T>, so an asynchronous factory of IReport
    [IocFactory(ServiceLifetime.Singleton)]
    public static Task<IReport> CreateAsync(Core.ILogger logger)
        => Task.FromResult<IReport>(new Report(logger));
}
#endregion
```

The JSON on `Mod.ModModule__IocModuleInfo` follows. The real output has no whitespace and no line breaks.

```json
{
  "version": 1,
  "imports": [
    { "definition": "Core.CoreModule", "assembly": "Core" }
  ],
  "demands": [
    {
      "type": {
        "definition": "Mod.IHandler`1", "assembly": "Mod",
        "arguments": [ { "definition": "Mod.Order", "assembly": "Mod" } ]
      },
      "mode": "key",
      "key": { "kind": "value", "type": { "definition": "System.String", "assembly": "System.Runtime" }, "value": "main" }
    }
  ],
  "definitions": [
    {
      "kind": "instance",
      "id": { "type": { "definition": "Mod.Clocks", "assembly": "Mod" }, "member": "Utc" },
      "key": {
        "kind": "symbol",
        "type": { "definition": "Mod.Keys", "assembly": "Mod" },
        "member": "UtcClock",
        "valueType": { "definition": "System.String", "assembly": "System.Runtime" }
      },
      "lifetime": "Singleton",
      "instanceType": { "definition": "Mod.IClock", "assembly": "Mod" },
      "serviceTypes": [ { "definition": "Mod.IClock", "assembly": "Mod" } ],
      "entry": "Create_Clocks_Utc"
    },
    {
      "kind": "implementation",
      "id": { "definition": "Mod.Handler`1", "assembly": "Mod", "accessible": false },
      "key": { "kind": "value", "type": { "definition": "System.String", "assembly": "System.Runtime" }, "value": "main" },
      "lifetime": "Scoped",
      "serviceTypes": [
        { "definition": "Mod.IHandler`1", "assembly": "Mod", "arguments": [ { "parameter": 0 } ] }
      ],
      "tags": [ "api", "web" ],
      "typeParameters": [
        { "name": "T", "constraints": [ "class", "new", { "definition": "Mod.IEntity", "assembly": "Mod" } ] }
      ],
      "entry": "Create_Handler",
      "dependencies": [
        { "type": { "definition": "Mod.IStore", "assembly": "Mod" }, "constructor": true },
        {
          "type": { "definition": "Mod.IClock", "assembly": "Mod" },
          "key": {
            "kind": "symbol",
            "type": { "definition": "Mod.Keys", "assembly": "Mod" },
            "member": "UtcClock",
            "valueType": { "definition": "System.String", "assembly": "System.Runtime" }
          },
          "optional": true,
          "constructor": true
        }
      ],
      "decorators": [
        {
          "type": { "definition": "Mod.RetryHandler`1", "assembly": "Mod" },
          "typeParameters": [ { "name": "T", "constraints": [] } ],
          "entry": "Decorate_RetryHandler",
          "dependencies": [
            { "type": { "definition": "Mod.IHandler`1", "assembly": "Mod", "arguments": [ { "parameter": 0 } ] }, "constructor": true }
          ],
          "chains": [ { "service": 0, "inner": 0, "arguments": [ { "parameter": 0 } ] } ]
        },
        {
          "type": { "definition": "Mod.LoggingHandler`1", "assembly": "Mod" },
          "typeParameters": [ { "name": "T", "constraints": [] } ],
          "entry": "Decorate_LoggingHandler",
          "dependencies": [
            { "type": { "definition": "Core.ILogger", "assembly": "Core" }, "constructor": true },
            { "type": { "definition": "Mod.IHandler`1", "assembly": "Mod", "arguments": [ { "parameter": 0 } ] }, "constructor": true }
          ],
          "chains": [ { "service": 0, "inner": 1, "arguments": [ { "parameter": 0 } ] } ]
        }
      ]
    },
    {
      "kind": "factory",
      "id": {
        "type": { "definition": "Mod.Reports", "assembly": "Mod" },
        "member": "CreateAsync",
        "parameters": [ { "definition": "Core.ILogger", "assembly": "Core" } ]
      },
      "lifetime": "Singleton",
      "instanceType": { "definition": "Mod.IReport", "assembly": "Mod" },
      "serviceTypes": [ { "definition": "Mod.IReport", "assembly": "Mod" } ],
      "async": true,
      "disposable": "runtime",
      "asyncDisposable": "runtime",
      "entry": "Create_Reports_CreateAsync",
      "dependencies": [
        { "type": { "definition": "Core.ILogger", "assembly": "Core" } }
      ]
    },
    {
      "kind": "implementation",
      "id": { "definition": "Mod.Store", "assembly": "Mod" },
      "lifetime": "Singleton",
      "serviceTypes": [ { "definition": "Mod.IStore", "assembly": "Mod" } ],
      "disposable": "yes",
      "entry": "Create_Store"
    }
  ],
  "types": [
    {
      "type": { "definition": "Mod.Handler`1", "assembly": "Mod", "accessible": false, "arguments": [ { "parameter": 0 } ] },
      "entry": "Type_Handler",
      "definition": 1
    },
    {
      "type": {
        "definition": "System.Collections.Generic.IEnumerable`1", "assembly": "System.Runtime",
        "arguments": [
          { "definition": "Mod.Handler`1", "assembly": "Mod", "accessible": false, "arguments": [ { "parameter": 0 } ] }
        ]
      },
      "entry": "Type_IEnumerable_Handler",
      "wrap": "Wrap_IEnumerable_Handler",
      "definition": 1
    }
  ]
}
```

Notes on this output:

- `imports` records only the module class. The definitions of `Core.CoreModule` are not in this JSON.
- The demand comes from `IocDiscoverAttribute`. Its `mode` is `"key"`, so it has a `key`. All type arguments of `IHandler<Order>` are accessible, so the module outputs no derived registration. The composition root closes `Handler<Order>` with the generic entry `Create_Handler`.
- The order of `definitions` follows the sort names: `Mod.Clocks.Utc`, ``Mod.Handler`1``, `Mod.Reports.CreateAsync(...)`, `Mod.Store`. The type entries use the index `1` of `Handler<>` in this order.
- `Keys.UtcClock` is a static field that is not `const`, so it is a symbol key. `"main"` is a value key.
- The source code gives the tags as `web`, `api`. The JSON has them in ordinal order.
- The constraints of `T` are in the order of the JSON: `class`, `new`, then the type constraint `IEntity`. The `where` clause of `Create_Handler` uses the order of C# syntax.
- `clock` has a default value, so its dependency has `optional`. Its key is specified on the parameter.
- `RetryHandler<>` is first in `Decorators`, so it is the outermost layer. The inner parameter of `LoggingHandler<>` is its second constructor parameter, so `inner` is `1`.
- The service type of the factory and the service type of the instance are in `serviceTypes`. A factory and an instance have no implementation type, so no service type is implicit.
- The factory returns `Task<IReport>`. Thus `async` is `true` and `instanceType` is `IReport`. `IReport` is an interface, so the two disposal kinds are `"runtime"`.
- A factory parameter is not a constructor parameter, so the dependency of the factory has no `constructor`.
- `Store` implements `IDisposable`, so `disposable` is `"yes"`.
- `Handler<>` is the only inaccessible type. It gets a type entry for itself and one for its `IEnumerable<>`, which is a wrapper type and thus has `wrap`. Both contain a type parameter, so both have `definition`.

## See also

- [Semantics](Semantics.spec.md)
- [Analysis model of the dependency graph](Analysis.Model.spec.md)
- [Container output](Container.Output.spec.md)
- [Service collection extension output](ServiceCollection.Output.spec.md)
- [Diagnostic list](Diagnostics.spec.md)
