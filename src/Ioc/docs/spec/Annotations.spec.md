# How the generator reads annotations

> Parent document: [Constitution](../Constitution.md). If this document and the constitution disagree, the constitution wins.

The key words MUST, MUST NOT, SHOULD, SHOULD NOT, and MAY in this document are to be interpreted as described in BCP 14 (RFC 2119, RFC 8174) when, and only when, they appear in all capitals.

## Overview

This document defines how the attributes that the user writes become the input of the analysis model. It covers these subjects for each attribute: where the user can put it, the enum values, how the generator reads `nameof()` and keys, how the generator selects the constructor and the injected members, and how the generator extracts dependency sites. The result is the set of declarations that the [analysis model](Analysis.Model.spec.md) uses: registration declarations, injection declarations, discovery demands, import declarations, and container and module declarations.

This document does not repeat the semantics that the constitution and the [semantics specification](Semantics.spec.md) state, such as optional dependencies and lifetime limits (see [Dependency resolution](Semantics.spec.md#dependency-resolution)). It adds only the details of how the generator reads the attributes.

## Attribute declarations

The user sets up DI with the attributes of this section.

All attributes are `sealed` and have `Inherited = false`.

| Attribute | `AttributeTargets` | `AllowMultiple` |
| --- | --- | --- |
| `IocRegisterAttribute` | `Class` | `true` (to register one type with different keys, or to add service types in more than one attribute) |
| `IocRegisterForAttribute` | `Assembly \| Class` | `true` |
| `IocRegisterBatchAttribute` | `Assembly \| Class` | `true` |
| `IocFactoryAttribute` | `Method` | `true` |
| `IocFactoryForAttribute` | `Assembly \| Class` | `true` |
| `IocInstanceAttribute` | `Field \| Property` | `true` |
| `IocInstanceForAttribute` | `Assembly \| Class` | `true` |
| `IocInjectAttribute` | `Constructor \| Method \| Parameter \| Field \| Property` | `false` |
| `IocInjectMemberAttribute` | `Assembly \| Class` | `true` |
| `IocDiscoverAttribute` | `Assembly \| Class` | `true` |
| `IocImportModuleAttribute` | `Assembly \| Class` | `true` |
| `IocModuleAttribute` | `Class` | `false` |
| `IocContainerAttribute` | `Class` | `false` |
| `IocServiceCollectionExtensionsAttribute` | `Assembly` | `false` (opt-in to generate `Add{Name}`. See [Conditions for generation](Register.Output.spec.md#conditions-for-generation) in the `Add{Name}` output specification) |

- These attributes have `[Conditional("SOURCEGEN")]`. The file `build\SourceGen.Ioc.props` of the package defines `SOURCEGEN`. The annotation that belongs to the type itself (`IocInjectAttribute`) therefore goes into metadata. Another assembly that registers the type with `IocRegisterForAttribute` can read it. `IocModuleInfoAttribute` carries the module info and MUST NOT have `[Conditional]` (see [Carrier type](Module.Output.spec.md#carrier-type)).
- When an `Assembly | Class` attribute is on a class, that class MUST have `IocModuleAttribute` or `IocContainerAttribute`. If it does not, the generator reports `SGIOC025` (skip the declaration). These attributes have no meaning on an ordinary class, and to ignore them silently is against explicitness.

The declarations below show the constructor and the properties of each attribute. They do not show `[AttributeUsage]` and `[Conditional]`. The table above gives those.

### Implementation type registration

```csharp
// A class with this attribute is registered in the IoC container as an implementation type
public sealed class IocRegisterAttribute(ServiceLifetime lifetime) : Attribute
{
  public ServiceLifetime Lifetime { get; } = lifetime;
  public Type[] ServiceTypes { get; init; } = [];
  public KeyType KeyType { get; init; } = KeyType.Value;
  // The key of the service. Use it together with KeyType
  // With KeyType.Csharp, Key is a string and MUST be specified with `nameof()`
  public object? Key { get; init; }
  // Decorators. The first item of the array is the outermost layer
  public Type[] Decorators { get; init; } = [];
  public string[] Tags { get; init; } = [];
}

// Gives the registration settings of an implementation type. Use it when the attribute is not wanted directly on the type, or when the user does not own the type
public sealed class IocRegisterForAttribute(Type implementationType, ServiceLifetime lifetime) : Attribute
{
  public Type ImplementationType { get; } = implementationType;
  public ServiceLifetime Lifetime { get; } = lifetime;
  public Type[] ServiceTypes { get; init; } = [];
  public KeyType KeyType { get; init; } = KeyType.Value;
  public object? Key { get; init; }
  public Type[] Decorators { get; init; } = [];
  public string[] Tags { get; init; } = [];
}
```

### Batch registration

```csharp
/// Batch setting. More than one implementation type shares one Lifetime and one set of Decorators
/// Its main use is the registration of open generics, for example several handlers that register `IRequestHandler<,>` and share middleware decorators
/// Service types and implementation types are CROSS JOINed. Each implementation type MUST implement each service type. If not, it is an error
/// It has no Key. Use `IocRegisterForAttribute` when a key is necessary
public sealed class IocRegisterBatchAttribute : Attribute
{
  public required ServiceLifetime Lifetime { get; init; }
  /// The service types to register
  public required Type[] ServiceTypes { get; init; }
  /// The implementation types to register
  public required Type[] ImplementationTypes { get; init; }
  public Type[] Decorators { get; init; } = [];
  public string[] Tags { get; init; } = [];
}
```

### Injection

```csharp
// Marks a constructor/constructor parameter/field/property/method/method parameter/partial accessor
// On a constructor, it selects the constructor to use. On a method, it marks the method as an injection method. In these two cases Key and LookupMode MUST NOT be specified
// On a parameter/field/property/partial accessor, it can specify the key to resolve
// It aligns with [FromKeyedServices] of MS.DI, but it can also be used on positions other than parameters
//   [IocInject]                            -> not keyed
//   [IocInject(Key = "a")]                 -> the specified key
//   [IocInject(LookupMode = InheritKey)]   -> the key of the owning service
//   [IocInject(LookupMode = NullKey)]      -> explicitly not keyed
public sealed class IocInjectAttribute : Attribute
{
  public KeyType KeyType { get; init; } = KeyType.Value;
  public object? Key { get; init; }
  // Uses ServiceKeyLookupMode of MS.DI. With InheritKey or NullKey, Key MUST NOT be specified
  public ServiceKeyLookupMode LookupMode { get; init; } = ServiceKeyLookupMode.ExplicitKey;
}

// Specifies the member to inject on a type that the user does not own. It MUST be in the same assembly and the same registration scope as the registration attribute of that type (for example IocRegisterFor)
public sealed class IocInjectMemberAttribute(Type implementationType, string member) : Attribute
{
  // The implementation type to inject into
  public Type ImplementationType { get; } = implementationType;
  // The name of the member to inject, which SHOULD be specified with `nameof()`. It MUST be a field, property or method
  public string Member { get; } = member;
  // Key has an effect only on a field or property. A key for a method parameter is an error. Use a factory registration
  public KeyType KeyType { get; init; } = KeyType.Value;
  public object? Key { get; init; }
}
```

### Factory registration

```csharp
// Specifies a factory method that creates the service instance
public sealed class IocFactoryForAttribute(string factory, ServiceLifetime lifetime) : Attribute
{
  // The factory method, which MUST be specified with `nameof()`. It MUST be static
  // The IoC container injects the parameters of the method
  // A return type of `Task<T>` makes it an asynchronous factory of `T`
  public string Factory { get; } = factory;
  public ServiceLifetime Lifetime { get; } = lifetime;
  // The service types to register. If not specified, the return type of the factory method is the service type
  // A generic factory method MUST NOT specify it. The service type is always the return type
  public Type[] ServiceTypes { get; init; } = [];
  public KeyType KeyType { get; init; } = KeyType.Value;
  public object? Key { get; init; }
  public string[] Tags { get; init; } = [];
}

// Goes directly on the factory method. The method MUST be static
// The properties have the same meaning as those of `IocFactoryForAttribute`
public sealed class IocFactoryAttribute(ServiceLifetime lifetime) : Attribute
{
  public ServiceLifetime Lifetime { get; } = lifetime;
  // The service types to register. If not specified, the return type of the factory method is the service type
  // A generic factory method MUST NOT specify it. The service type is always the return type
  public Type[] ServiceTypes { get; init; } = [];
  public KeyType KeyType { get; init; } = KeyType.Value;
  public object? Key { get; init; }
  public string[] Tags { get; init; } = [];
}

// Example:
[assembly: IocFactoryFor(nameof(Factory.Create), ServiceLifetime.Singleton)]

public static class Factory
{
  // The service type is IHandler<T2, T1>. A dependency on IHandler<int, decimal> calls Create<decimal, int>
  public static IHandler<T2, T1> Create<T1, T2>(...) => ...;
}
```

### Instance registration

```csharp
// Specifies a service instance. It is always a Singleton
public sealed class IocInstanceForAttribute(string instance) : Attribute
{
  // The instance, which MUST be specified with `nameof()`. It MUST be static
  public string Instance { get; } = instance;
  // The service types to register. If not specified, the type of the instance is the service type
  public Type[] ServiceTypes { get; init; } = [];
  public KeyType KeyType { get; init; } = KeyType.Value;
  public object? Key { get; init; }
  public string[] Tags { get; init; } = [];
}

// Goes directly on the static field or property that holds the instance. It is always a Singleton
// The properties have the same meaning as those of `IocInstanceForAttribute`
public sealed class IocInstanceAttribute : Attribute
{
  // The service types to register. If not specified, the type of the instance is the service type
  public Type[] ServiceTypes { get; init; } = [];
  public KeyType KeyType { get; init; } = KeyType.Value;
  public object? Key { get; init; }
  public string[] Tags { get; init; } = [];
}
```

### Discovery

```csharp
// Marks a type that must be discovered: a closed generic type or a wrapper type
// Without a Key, the demand is not keyed
public sealed class IocDiscoverAttribute(Type type) : Attribute
{
  public Type Type { get; } = type;
  public KeyType KeyType { get; init; } = KeyType.Value;
  public object? Key { get; init; }
}
```

### Modules and containers

```csharp
// The generator generates the module output for the marked class, for other assemblies to import. One at most for each assembly
public sealed class IocModuleAttribute : Attribute;

// Imports another module. The type MUST have `IocModuleAttribute`
// It goes on the assembly, a module class or a container class
public sealed class IocImportModuleAttribute(Type module) : Attribute
{
  public Type Module { get; } = module;
}

// The generator generates the IoC container code for the marked class
public sealed class IocContainerAttribute : Attribute
{
  // Whether to integrate with `IServiceCollection` (whether there is a fallback provider)
  public bool IntegrateServiceProvider { get; init; } = true;
  // true: an isolated container. It includes only the registrations and imports that are on this container class
  // It MUST NOT be set together with IncludeTags, and the class MUST NOT also have IocModuleAttribute. Each of these is an error
  public bool ExplicitOnly { get; init; }
  // The tags to include
  public string[] IncludeTags { get; init; } = [];
  // The thread safety strategy
  public ThreadSafeStrategy ThreadSafeStrategy { get; init; } = ThreadSafeStrategy.Lock;
  // The eager resolution options
  public EagerResolveOptions EagerResolveOptions { get; init; } = EagerResolveOptions.Singleton;
}
```

## Enum values

The annotation enums have these values:

```csharp
public enum KeyType
{
    Value = 0,   // The key is the constant value of the attribute argument
    Csharp = 1,  // The key is the static member that nameof() points to
}

[Flags]
public enum EagerResolveOptions
{
    None = 0,
    Singleton = 1,
    Scoped = 1 << 1,
    SingletonAndScoped = Singleton | Scoped,
}

public enum ThreadSafeStrategy
{
    None = 0,
    Lock = 1,
    CompareExchange = 1 << 1,
}
```

For the behavior of each value of `ThreadSafeStrategy`, see [Thread safety](Container.Output.spec.md#thread-safety).

## How the generator reads `nameof()`

In an attribute argument, `nameof(X.Y)` is semantically only the string `"Y"`. The generator can get the symbol information only from the syntax.

| Argument | How the generator reads it |
| --- | --- |
| `IocFactoryForAttribute.Factory`, `IocInstanceForAttribute.Instance`, the `Key` of `KeyType.Csharp` | The syntax of the argument MUST be a `nameof(...)` expression directly. The generator binds the operand to get the symbol. |
| `IocInjectMemberAttribute.Member` | The generator uses the string value as a name and looks it up in `ImplementationType` by [member lookup by name](#member-lookup-by-name). `nameof()` is only the recommended way to write it. The type that the operand points to does not change the result. |

For the three arguments that need a symbol, the generator reports `SGIOC011` (skip the declaration) in these cases:

- The argument is not a direct `nameof(...)` expression. Examples are a string literal, a constant, and a string concatenation.
- The operand does not bind to a symbol.
- The symbol has the wrong kind. `Factory` MUST be a method. `Instance` MUST be a field or a property. A key MUST be a field, a property, or a constant.
- The symbol of a key is not a static member.

When `Factory` or `Instance` binds to a symbol of the right kind, the generator applies the [checks of a factory or instance member](#checks-of-a-factory-or-instance-member).

When the operand binds to a method group that has more than one method, the generator reports `SGIOC010` (skip the declaration). An `IocFactoryAttribute` that is directly on a method does not use a name, so it does not have this problem.

An inaccessible member (private, for example) is not an IoC semantic error. The generator generates code as usual, and the compiler reports the error.

## Keys

### The three forms of a key

| Form | Source | Compile-time identity |
| --- | --- | --- |
| No key | `Key` is not set or is `null` | None |
| Value key | `KeyType.Value`, or `KeyType.Csharp` that points to a `const` | (type of the value, value) |
| Symbol key | `KeyType.Csharp` that points to a static field or property that is not `const` | (declaring type, member name) |

- `KeyType.Value` with a `Key` of `null` is the same as no key.
- A value key supports these types: `bool`, `char`, `string`, the integer types (`sbyte` to `ulong`), `float`, `double`, and enums. For `typeof(...)` or an array as a key, the generator reports `SGIOC022` (skip the declaration). An array has no value equality at run time. A `Type` as a key is rare, so it is not supported for now.
- The identity of a value key includes the type. `1` (`int`) and `1L` (`long`) are different keys. This agrees with MS.DI, which compares the boxed values with `Equals` at run time.
- A value key of an enum type is identified by its underlying integer value. The generated code writes it as a cast, `(global::Ns.Color)2`.
- The key type of a symbol key is the declared type of the field or property. The generator uses it to match the `TKey` of a dictionary and for a `[ServiceKey]` parameter.
- `KeyedService.AnyKey`: when the user registers with it, the generator reports `SGIOC007`. The condition is `KeyType.Csharp` with the symbol `Microsoft.Extensions.DependencyInjection.KeyedService.AnyKey`.

### Canonical form of a key

The generator uses the canonical form for matching, for ordering, and in the module info. Two keys are equal if and only if their canonical strings are equal (ordinal).

| Form | Canonical string |
| --- | --- |
| No key | The empty string |
| Value key | `V\|{canonical string of the type}\|{text of the value}` |
| Symbol key | `S\|{canonical string of the declaring type}\|{member name}` |

The generator always produces the text of the value with the invariant culture:

| Type | Text |
| --- | --- |
| `bool` | `true` / `false` |
| `char` | The decimal value of the UTF-16 code unit |
| `string` | The string as it is, without quotes and without escapes |
| Integer, enum | Decimal (for an enum, the underlying value) |
| `float`, `double` | The round-trip format (`"R"`) |

For the canonical string of a type, see [Canonical string of a type](Analysis.Model.spec.md#canonical-string-of-a-type).

### Accessibility

- A key that appears in the module output MUST be accessible from outside the assembly. For a symbol key, the member and all its containing types are `public`. For a value key, the type is `public` (only an enum needs this check). If not, the generator reports `SGIOC105` (do not generate the module output).
- An assembly that has no module output is not checked.

## Registration declarations

Each registration attribute gives one or more registration declarations. The fields are: kind, identity, key, lifetime, list of service types, Decorators (ordered), Tags (a set), and registration scope.

| Attribute | Kind | Identity | Expansion |
| --- | --- | --- | --- |
| `IocRegisterAttribute` | Implementation type | The class that has the attribute | One declaration |
| `IocRegisterForAttribute` | Implementation type | `ImplementationType` | One declaration |
| `IocRegisterBatchAttribute` | Implementation type | Each element of `ImplementationTypes` | One declaration for each implementation type. The key is no key. The service types are the full `ServiceTypes`. |
| `IocFactoryAttribute` | Factory | The method that has the attribute | One declaration |
| `IocFactoryForAttribute` | Factory | The method that `nameof()` points to | One declaration |
| `IocInstanceAttribute` | Instance | The field or property that has the attribute | One declaration. The lifetime is always Singleton. |
| `IocInstanceForAttribute` | Instance | The field or property that `nameof()` points to | One declaration. The lifetime is always Singleton. |

The registration scope follows the rules in [Registration scope](Semantics.spec.md#registration-scope): an attribute on an `ExplicitOnly` container class belongs to that container, and all other attributes belong to the full assembly.

### Checks of the implementation type

The user gives the implementation type with `typeof()` or as the class that has the attribute. It can have only three shapes: non-generic, closed generic (`Handler<Foo>`), or unbound generic (`Handler<>`).

| Condition | Diagnostic |
| --- | --- |
| It is not a class, or it is an abstract type, a static type, or a delegate type | `SGIOC003` |
| It uses a `file` type: the type, one of its outer types, or one of its type arguments is a `file` type | `SGIOC003` (see the note below) |
| It is a nested open generic (`Outer<>.Inner<>`: a containing type has type parameters that are not specified) | `SGIOC005` |

A type argument that is not a concrete type (`typeof(Handler<T>)`) cannot be written in an attribute, so the generator does not handle it.

The generated code is in a separate file. It cannot name a `file` type, because the compiler changes the metadata name of that type. The compile errors in the generated file would not show the cause, so the generator reports `SGIOC003`. Other accessibility errors are errors that the C# compiler reports (see the [constitution](../Constitution.md)). For example, a `private` nested implementation type gives CS0122 in the generated file, and the generator reports no diagnostic.

### Expansion of service types

In this section, `P` is the list of type parameters of the implementation type. `P` is empty when the implementation type is non-generic or closed. The result of the expansion is a set of service type patterns. A service type pattern is a type written in terms of `P`. When `P` is empty, it is a concrete type.

1. The implementation type itself is always a service type. The user does not need to write it in `ServiceTypes`. It is not an error to write it.
2. The generator handles each element of `ServiceTypes` by its shape:

   | Element of `ServiceTypes` | Implementation type | Result |
   | --- | --- | --- |
   | Concrete type (non-generic or closed) | Non-generic or closed | The type itself. The generator does not check that the implementation type converts to it. The generated cast leaves this check to the compiler (`IocRegisterBatchAttribute` is an exception, see below). |
   | Concrete type | Unbound generic | `SGIOC004`: the type arguments cannot come from the service type |
   | Unbound generic (`IHandler<,>`) | Any | Each constructed type in the base class chain and in all interfaces of the implementation type whose original definition is equal to the element. When there is none, the generator reports `SGIOC004`. |

3. When the implementation type is an unbound generic, each service type pattern MUST mention each type parameter of `P`. If not, the generator reports `SGIOC004`. For example, `class Foo<T> : IFoo<T>, IMarker` is registered with `ServiceTypes = [typeof(IMarker)]`. A demand for `IMarker` cannot decide `T`.
4. `IocRegisterBatchAttribute`: for each service type, each implementation type MUST give a minimum of one result (unbound generic) or MUST be convertible (concrete type). If not, the generator reports `SGIOC008` and skips the declaration of that implementation type.

The generator then checks each service type that the expansion gives:

| Condition | Diagnostic |
| --- | --- |
| It is a value type, a pointer, `void`, or a type parameter itself | `SGIOC004` |
| It is a wrapper type (see [Grammar of wrapper types](Analysis.Model.spec.md#grammar-of-wrapper-types)) | `SGIOC006` |
| It is a built-in service type: `IServiceProvider`, `IServiceScopeFactory`, `IServiceProviderIsService`, `IServiceProviderIsKeyedService` | `SGIOC004`. The container supplies these itself, and a user registration causes an ambiguity. |
| It is a nested open generic | `SGIOC005` |

### Service types of a factory

| Case | Service types |
| --- | --- |
| Non-generic method, `ServiceTypes` not set | The return type |
| Non-generic method, `ServiceTypes` set | The specified types. They can only be concrete types. For an unbound generic, the generator reports `SGIOC004`. The return type itself is not added automatically. |
| Generic method | The return type, written in terms of the type parameters of the method. When `ServiceTypes` is set, the generator reports `SGIOC009`. The return type MUST mention each type parameter. If not, the generator reports `SGIOC004`. |
| Returns `Task<T>` | Apply the three rows above with `T` in place of the return type. This is an asynchronous factory. |

When the method returns `void`, `Task` (non-generic), `ValueTask`, `ValueTask<T>`, or a value type, the generator reports `SGIOC004`. A factory has no type of its own: the method is not a service type.

### Service types of an instance

When `ServiceTypes` is not set, the service type is the type of the field or property. When `ServiceTypes` is set, the service types are the specified types, and they can only be concrete types. When the type is a value type, the generator reports `SGIOC004`.

### Checks of a factory or instance member

These checks apply to the factory method and to the instance member. They apply when the attribute is directly on the member (`IocFactoryAttribute`, `IocInstanceAttribute`), and when `nameof()` names the member (`IocFactoryForAttribute`, `IocInstanceForAttribute`). The generator reports `SGIOC026` (skip the declaration) in these cases:

| Condition | Reason |
| --- | --- |
| The method, field, or property is not static | The composer has no object on which to call an instance member |
| The type that contains the member is an open generic: it, or one of its outer types, has type parameters without type arguments (`Factories<T>.Create` with the attribute on the member, `nameof(Factories<>.Create)`) | The generator cannot know the type arguments of the containing type. Use a generic factory method in a non-generic type. A closed containing type (`nameof(Factories<int>.Create)`) is valid |

```csharp
#region Define:
// Valid: the containing type is closed
[assembly: IocFactoryFor(nameof(Clocks<int>.Create), ServiceLifetime.Singleton)]

public static class Clocks<T>
{
    public static IClock Create() => new SystemClock();
}
#endregion

#region Invalid:
public class Factories
{
    // SGIOC026: not static
    [IocFactory(ServiceLifetime.Singleton)]
    public IClock Create() => new SystemClock();
}

public static class Factories<T>
{
    // SGIOC026: the containing type is an open generic
    [IocFactory(ServiceLifetime.Singleton)]
    public static IHandler<T> Create() => new Handler<T>();
}
#endregion
```

### Other checks

| Condition | Diagnostic |
| --- | --- |
| `Decorators` is set, but the expansion gives no service type other than the implementation type itself | `SGIOC015` |
| An element of `Decorators` is not valid (the same rules as [checks of the implementation type](#checks-of-the-implementation-type)) | `SGIOC003` / `SGIOC005` |

The checks that match decorators to service types (`SGIOC012` to `SGIOC014`) need the full instance definition. See [Decorators](Analysis.Model.spec.md#decorators).

## Constructor selection

These rules apply to implementation types and to decorators.

1. The candidates are all instance constructors that the type declares. Static constructors are not candidates. The copy constructor of a record is protected, so it is not one of the public constructors.
2. When exactly one constructor has `[IocInject]` or `[ActivatorUtilitiesConstructor]`, at any access level, the generator uses it.
3. When no constructor is marked and the type has a primary constructor, the generator uses the primary constructor. The number of other constructors does not matter.
4. When no constructor is marked and the type has no primary constructor, the generator uses the public constructor if there is exactly one.
5. In all other cases the generator reports `SGIOC002` (skip the declaration). There are two such cases. In the first case, more than one constructor is marked. In the second case, no constructor is marked, there is no primary constructor, and the number of public constructors is zero or more than one.

- If the generated code cannot access the marked constructor, the compiler reports the error.
- Roslyn has no public API to find the primary constructor. The generator can find it only from syntax: the declaration syntax of the constructor is the type declaration itself. Thus step 3 applies only to types in the current compilation. A type from another assembly (registered with `IocRegisterForAttribute`) goes directly to step 4.
  - To decide if a type belongs to the current compilation, the generator MUST compare the assembly of the type. It MUST NOT use the availability of syntax as the test. A project reference in the IDE gives syntax for types of other assemblies too. A command-line build (DLL reference) does not. The two builds would select different constructors.
- The positional parameters of a record are its primary constructor.

## Injected members

### Types that the user owns

An instance field, property, or method with `[IocInject]` on an implementation type or a decorator type is an injected member.

Member range and inheritance:

- The members that base classes declare are included. The generator goes through the full base class chain.
- A member that is hidden by `new` is not injected. The generator uses only the declaration that is visible by name from the most derived type.
- For an `override` member, the generator searches the override chain from the most derived type to the base. The first `[IocInject]` that it finds applies. This is the same as the inheritance semantics of a C# attribute with `AllowMultiple = false`. If no declaration in the chain has the attribute, the member is not an injected member.
- The generator does not check `[IocInject]` on a static member, a read-only field, or a property without a setter. It generates the assignment as usual, and the compiler reports the error.

### Types that the user does not own

`IocInjectMemberAttribute(typeof(Impl), nameof(Impl.Member))` makes one member of `Impl` an injected member.

| Condition | Diagnostic |
| --- | --- |
| The same assembly and the same registration scope have no registration attribute for `Impl` | `SGIOC021` |
| [Member lookup by name](#member-lookup-by-name) does not find the member | `SGIOC011` |
| The member that is found is an overloaded method | `SGIOC010` |
| The member is a method and `Key` is set | `SGIOC020` |
| The member already has `[IocInject]` | `SGIOC024`: one member MUST NOT have two settings |

- The attribute applies to all instance definitions of `Impl` in that registration scope. Each different key is a different instance definition, and each one gets the injection.
- The attribute does not apply to decorators. When `Impl` appears only as a decorator and has no registration attribute of its own, the generator reports `SGIOC021`. When a decorator needs member injection, put `[IocInject]` on the decorator type.

### Member lookup by name

The input is a type `Impl` and a name:

1. The generator searches from `Impl` up through the base classes, one level at a time. The first type that declares an instance member with that name wins, and the search stops there. This agrees with C# member lookup: a base member that is hidden by `new` is not found.
2. When that level has more than one method with that name, the method is overloaded.
3. Only fields, properties, and methods are accepted. If the member that is found is of another kind, such as an event or a nested type, the result is "not found".

### Checks of an injection method

These checks apply to an injection method that is specified in either of the two ways. The generator reports `SGIOC016` in the cases below. The consequence is skip the declaration, and the skipped declaration is the instance definition that owns the method.

- The method is generic.
- A parameter has `ref`, `out`, or `in`.
- The return type is not `void` or `Task`. `Task<X>` and `ValueTask` are thus not permitted.
- The method is `async void`.

An injection method that returns `Task` is an asynchronous injection method.

### Order

The order of the steps in a construction plan is fixed:

1. The constructor. Injected properties that are `required` or `init` are assigned in the object initializer of the same expression.
2. The other injected fields and properties.
3. The synchronous injection methods.
4. The asynchronous injection methods. The generated code awaits them one at a time.

The order inside one step is as follows. Members of a base class come first. Inside one type, the order is the order of Roslyn `GetMembers()`. That is the declaration order inside one file, and for a partial type across files, the order of the files in the compilation. Members that are specified with `IocInjectMemberAttribute` are sorted together with `[IocInject]` members by this order. The order in which the attributes are written has no effect. The order across files depends on how the project lists its files, and the user SHOULD NOT rely on it.

## Extraction of dependency sites

Each dependency site gives one dependency:

| Field | Content |
| --- | --- |
| Requested type | The type of the parameter, field, or property. For a partial accessor, the return type. |
| Key specification | See the table below |
| Optional mode | Required / has a default value (the default constant is recorded) / nullable / keep the initializer. The rules that decide the mode are in [Dependency resolution](Semantics.spec.md#dependency-resolution). |
| Position | Constructor parameter / factory parameter / parameter of an injection method / injected field or property / partial accessor |
| Is a constructor parameter | Used to map `Func<..., T>` |

### Key specification

| Annotation | Result |
| --- | --- |
| No annotation, `[IocInject]` without arguments, `[IocInject(LookupMode = NullKey)]`, `[FromKeyedServices(null)]` | No key |
| `[IocInject(Key = k)]`, `[IocInject(KeyType = Csharp, Key = nameof(...))]`, `[FromKeyedServices(k)]` | The specified key. The argument of `[FromKeyedServices]` is always a value key. |
| `[IocInject(LookupMode = InheritKey)]`, `[FromKeyedServices]` without arguments | The key of the instance definition that owns the site. When that instance definition has no key, the result is no key. For a decorator, the owner is the instance definition that it decorates. |
| `[ServiceKey]` | Not a dependency. The parameter receives the key of the instance definition that owns it. |

The generator does not resolve `InheritKey` during extraction. It keeps `InheritKey` until the instance definition is known, because one type can have more than one instance definition with different keys.

| Condition | Diagnostic |
| --- | --- |
| `LookupMode` is `InheritKey` or `NullKey`, and `Key` is set too | `SGIOC017` |
| The same position has `[IocInject]` and `[FromKeyedServices]` | `SGIOC018` |
| `[IocInject]` on a constructor or on an injection method sets `Key` or `LookupMode` | `SGIOC019` |
| The instance definition that owns a `[ServiceKey]` parameter has no key, and the parameter is required | `SGIOC023`. When the parameter is nullable or has a default value, it gets `null` or the default value. |
| The type comes from metadata (it is not in the current compilation), and its `[IocInject]` uses `KeyType.Csharp` | `SGIOC011`: metadata keeps only the string, so the generator cannot know the symbol. Register with a factory instead. |

The generator does not check that the type of a `[ServiceKey]` parameter is compatible with the key. The generated code passes the key expression directly, and the compiler reports the error.

### Partial accessor

A partial member of a container class with one of these shapes is a partial accessor:

- A partial method without an implementation part that is an instance method, is not generic, has no parameters, and has a return type other than `void`.
- A partial property without an implementation part that is an instance property and has only `get`.

`[IocInject]` on the member specifies the key. `InheritKey` has no meaning here and counts as no key. A nullable return type means that the dependency is optional.

A partial member that does not have one of these shapes is not a partial accessor. The generator does not implement it, and the compiler reports the missing implementation.

## Example

```csharp
#region Define:
public static class Keys
{
    public const string Main = "main";               // const, so a value key. The same as Key = "main"
    public static readonly object Backup = new();    // not const, so a symbol key
}

public interface IRepo;
public interface IHandler<TRequest, TResponse>;

// Two instance definitions: (Repo, no key) and (Repo, "main")
[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)])]
[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)], KeyType = KeyType.Csharp, Key = nameof(Keys.Main))]
public class Repo : IRepo;

// Service type patterns: QueryHandler<T> itself, and IHandler<Query<T>, List<T>>
[IocRegister(ServiceLifetime.Scoped, ServiceTypes = [typeof(IHandler<,>)])]
public class QueryHandler<T>([IocInject(Key = "main")] IRepo repo) : IHandler<Query<T>, List<T>>;
#endregion

#region Invalid:
// SGIOC004: a concrete service type cannot decide T
[IocRegister(ServiceLifetime.Scoped, ServiceTypes = [typeof(IHandler<Query<int>, List<int>>)])]
public class BadHandler<T> : IHandler<Query<T>, List<T>>;

// SGIOC002: two public constructors and none is marked
[IocRegister(ServiceLifetime.Transient)]
public class Ambiguous
{
    public Ambiguous() { }
    public Ambiguous(IRepo repo) { }
}

// SGIOC011: the key is not a direct nameof() expression
[IocRegister(ServiceLifetime.Singleton, KeyType = KeyType.Csharp, Key = "Keys.Backup")]
public class BackupRepo : IRepo;
#endregion
```

## See also

- [Analysis model](Analysis.Model.spec.md)
- [Diagnostic list](Diagnostics.spec.md)
- [Documented differences from MS.DI](../Constitution.md#8-documented-differences-from-msdi)
