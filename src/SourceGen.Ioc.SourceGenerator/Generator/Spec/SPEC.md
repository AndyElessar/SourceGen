# Source Generator Spec

Source generators based on `Microsoft.Extensions.DependencyInjection.Abstractions`.

## Collecting Information

### 1. Registration Attributes

|Attribute|Purpose|Generic Version|
|:--------|:------|:--------------|
|`IocRegisterAttribute`|Mark class for registration|`IocRegisterAttribute<T>`|
|`IocRegisterForAttribute`|Register external types|`IocRegisterForAttribute<T>`|
|`IocRegisterDefaultsAttribute`|Default settings for registrations|`IocRegisterDefaultsAttribute<T>`|
|`IocImportModuleAttribute`|Import other assembly's settings|`IocImportModuleAttribute<T>`|
|`IocDiscoverAttribute`|Explicit closed generic discovery|`IocDiscoverAttribute<T>`|
|`IocGenericFactoryAttribute`|Generic factory type mapping|—|

### 2. Registration Properties

|Property|Source|
|:-------|:-----|
|Service Type|`TargetServiceType`, `ServiceTypes`, `RegisterAllInterfaces`, `RegisterAllBaseClasses`|
|Implementation Type|`IocRegisterForAttribute.ImplementationType`, marked class, defaults `ImplementationTypes`|
|Lifetime|Attribute → defaults → MSBuild `SourceGenIocDefaultLifetime` → `Transient`|
|Key / KeyType|Attribute → defaults|
|KeyValueType|Resolved `TypeData` of the key value (e.g., `string`, enum, `Guid`). `null` when `KeyType=Csharp` without `nameof()`|
|Decorators|`Decorators` property (with constructor params and type constraints)|
|Tags|Attribute → defaults|
|Factory|`Factory` property (method path, supports generic mapping)|
|Instance|`Instance` property (static instance path, e.g., `"MyService.Default"`)|
|ValidOpenGenericServiceTypes|Set of valid open generic service type names for constraint checking|

### 3. Type Hierarchy Collection

|Data|Description|
|:---|:----------|
|`AllInterfaces`|All interfaces implemented by the type|
|`AllBaseClasses`|All base classes (excluding `System.Object`)|
|`TypeParameters`|Generic type parameters with constraints|
|`ConstructorParameters`|Constructor parameters (for decorators)|
|`WrapperKind`|`None`, `Enumerable`, `ReadOnlyCollection`, `Collection`, `ReadOnlyList`, `List`, `Array`, `Lazy`, `Func`, `Dictionary`, or `KeyValuePair`|

### 4. Injection Members

|Member Type|Resolution|
|:----------|:---------|
|Property|With `[IocInject]`/`[Inject]`, set via object initializer|
|Field|With `[IocInject]`/`[Inject]`, set via object initializer|
|Method|With `[IocInject]`/`[Inject]`, called after construction|

### 5. IServiceProvider Invocations

Collect service types from invocations: `GetService<T>`, `GetRequiredService<T>`, `GetKeyedService<T>`, `GetRequiredKeyedService<T>`, `GetServices<T>`, `GetKeyedServices<T>` (and non-generic overloads)

### 6. Compilation Info

|Property|Source|
|:-------|:-----|
|Root Namespace|MSBuild `RootNamespace` (fallback: assembly name)|
|Assembly Name|Compilation options|
|Custom Method Name|`SourceGenIocName` MSBuild property|
|Default Lifetime|`SourceGenIocDefaultLifetime` MSBuild property (fallback: Transient)|
|Features|`SourceGenIocFeatures` MSBuild property (fallback: `Register,Container,PropertyInject,MethodInject`)|

### 7. Feature Flags

The `SourceGenIocFeatures` MSBuild property controls which outputs and injection member kinds are generated.

Available features:

|Feature|Description|
|:------|:----------|
|`Register`|Enable generation of the registration extension method output|
|`Container`|Enable generation of the container class output|
|`PropertyInject`|Enable property injection member generation|
|`FieldInject`|Enable field injection member generation|
|`MethodInject`|Enable method injection member generation|

Default value:

`Register,Container,PropertyInject,MethodInject`

Behavior:

- `Register`: Controls whether the registration extension method output is generated.
- `Container`: Controls whether the container class output is generated.
- `PropertyInject` / `FieldInject` / `MethodInject`: Control which injection member types are included in generated code.

Parsing rules:

- Comma-separated values.
- Case-insensitive matching.
- Whitespace is trimmed around each value.
- Invalid values are ignored.

## Parse Logic

### 1. Key Interpretation

|KeyType|Behavior|Example|
|:------|:-------|:------|
|`Value`|Use literal value|`42`, `"myString"`, `MyEnum.Value`|
|`Csharp`|Evaluate as C# expression|`MyClass.StaticField`, `nameof(...)`|

### 2. Default Settings Priority

When multiple defaults match an implementation type:

1. Directly on implementation type
2. On closest base class
3. On first interface in `AllInterfaces`

### 3. Settings Merge Order

`Explicit attribute` → `Matching defaults` → `MSBuild SourceGenIocDefaultLifetime` → `Transient`

### 4. Inject Attribute Matching

Match by name only: `IocInjectAttribute` or `InjectAttribute`  
(Supports third-party attributes like `Microsoft.AspNetCore.Components.InjectAttribute`)

### 5. Constructor Selection

|Priority|Condition|
|-------:|:--------|
|1|Marked with `[IocInject]`|
|2|Primary constructor|
|3|Constructor with most parameters|

### 6. Parameter Resolution

|Condition|Action|
|:--------|:-----|
|`[ServiceKey]` attribute|Inject registration key|
|`[FromKeyedServices]` or `[IocInject(Key=...)]`|Keyed service resolution|
|`IServiceProvider` type|Pass provider directly|
|Collection types (`IEnumerable<T>`, `T[]`, etc.)|Extract `T` as service type|

### 7. Property/Field Injection

Only members with `[IocInject]` or `[Inject]`:

|Condition|Behavior|
|:--------|:-------|
|With `Key`|Keyed service resolution|
|`IServiceProvider`|Pass provider directly|
|Collection types|Extract inner type as service|
|Nullable type|Assign resolved nullable value|
|Has default value|Use resolved if non-null|

### 8. Wrapper Kind Resolution

`WrapperKind` is a unified enum. Each value has a dedicated `TypeData` derived type.

|`WrapperKind`|TypeData Type|Types|Resolution|
|:------------|:------------|:----|:---------|
|`Enumerable`|`EnumerableTypeData`|`IEnumerable<T>`|MS.E.DI native collection support|
|`ReadOnlyCollection`|`ReadOnlyCollectionTypeData`|`IReadOnlyCollection<T>`|`GetServices<T>().ToArray()`|
|`Collection`|`CollectionTypeData`|`ICollection<T>`|`GetServices<T>().ToArray()`|
|`ReadOnlyList`|`ReadOnlyListTypeData`|`IReadOnlyList<T>`|`GetServices<T>().ToArray()`|
|`List`|`ListTypeData`|`IList<T>`|`GetServices<T>().ToArray()`|
|`Array`|`ArrayTypeData`|`T[]`|`GetServices<T>().ToArray()`|
|`Lazy`|`LazyTypeData`|`Lazy<T>`|Lazy-initialized service wrapper|
|`Func`|`FuncTypeData`|`Func<T>`|Factory delegate wrapper|
|`Dictionary`|`DictionaryTypeData`|`IDictionary<TKey, TValue>`|Dictionary of keyed services|
|`KeyValuePair`|`KeyValuePairTypeData`|`KeyValuePair<TKey, TValue>`|Single keyed service entry|

#### Type Hierarchy

```tree
TypeData
└── GenericTypeData
    ├── TypeParameterTypeData
    └── WrapperTypeData (WrapperKind)
        ├── CollectionWrapperTypeData
        │   ├── EnumerableTypeData          (Enumerable)
        │   ├── ReadOnlyCollectionTypeData  (ReadOnlyCollection)
        │   ├── CollectionTypeData          (Collection)
        │   ├── ReadOnlyListTypeData        (ReadOnlyList)
        │   ├── ListTypeData                (List)
        │   └── ArrayTypeData               (Array)
        ├── LazyTypeData                    (Lazy)
        ├── FuncTypeData                    (Func)
        ├── DictionaryTypeData              (Dictionary)
        └── KeyValuePairTypeData            (KeyValuePair)
```

Wrapper types support nesting. For example, `IEnumerable<Lazy<IMyService>>` is parsed as:

- `EnumerableTypeData` (`WrapperKind.Enumerable`)
  - `TypeParameters[0].Type` = `LazyTypeData` (`WrapperKind.Lazy`)
    - `TypeParameters[0].Type` = `TypeData` (`IMyService`)

### 9. Generic Factory Type Mapping

`IocGenericFactoryAttribute` maps service type parameters to factory method type parameters:

```csharp
// Single type parameter: IRequestHandler<>
[IocRegisterDefaults(typeof(IRequestHandler<>), Factory = nameof(Create))]
public class FactoryContainer
{
    // typeof(int) is placeholder, maps to T
    [IocGenericFactory(typeof(IRequestHandler<Task<int>>), typeof(int))]
    public static IRequestHandler<T> Create<T>() => new Handler<T>();
}

// Multiple type parameters: IRequestHandler<,>
[IocRegisterDefaults(typeof(IRequestHandler<,>), Factory = nameof(Create))]
public class FactoryContainer
{
    // decimal → T1, int → T2
    [IocGenericFactory(typeof(IRequestHandler<Task<int>, decimal>), typeof(decimal), typeof(int))]
    public static IRequestHandler<T1, T2> Create<T1, T2>() => new Handler<T1, T2>();
}
```

## Generators

1. Registration generator: generate `IServiceCollection` register code.\
[Registration features spec](Registration.md)

2. Container generator: generate container that implement `IServiceProvider`.\
[Container features spec](Container.md)

## Implementation Requirements

### Source Generator Architecture

Implemented at `IocSourceGenerator`, using the Incremental Generator pattern.
The generated code requires .NET 10.0 or later.

```filetree
src/SourceGen.Ioc.SourceGenerator/
├── Generator/
│   ├── IocSourceGenerator.cs              # Main generator (partial) with Initialize()
│   ├── Transform*.cs                      # Attribute → model transforms (Register, DefaultSettings, ImportModule, Discover, Container)
│   ├── ProcessSingleRegistration.cs       # Apply defaults to individual registrations
│   ├── CombineAndResolveClosedGenerics.cs # Combine results & resolve closed generics from open generics
│   ├── IServiceProviderInvocations.cs     # Collect IServiceProvider invocations
│   ├── GroupRegistrationsForContainer.cs  # Group registrations for container generation
│   ├── Generate*Output.cs                 # Code emitters (Register, Container)
│   ├── LazyFuncRegistrationHelper.cs      # Lazy/Func wrapper registration helper
│   ├── KvpRegistrationHelper.cs           # KeyValuePair registration helper
│   └── Spec/                              # SPEC.md, Registration.md, Container.md
├── Models/                                # Immutable data models (RegistrationData, TypeData, etc.)
└── Analyzer/                              # Diagnostic analyzers & SPEC.md
```

### Data Flow

```mermaid
flowchart TB
    subgraph IocSourceGenerator.Initialize
        subgraph Attribute Providers
            IocRegister["[IocRegister]"]
            IocRegisterFor["[IocRegisterFor]"]
            IocRegisterDefaults["[IocRegisterDefaults]"]
            IocImportModule["[IocImportModule]"]
            IocDiscover["[IocDiscover]"]
            Invocations["IServiceProvider Invocations"]
            IocContainer["[IocContainer]"]
        end

        subgraph Default Settings
            allDefaultSettings["allDefaultSettings"]
            allImportedDefaultSettings["allImportedDefaultSettings"]
            combinedDefaultSettings["combinedDefaultSettings<br/>(DefaultSettingsMap)"]
        end

        subgraph Registration Pipeline
            allBasicResults["allBasicResults<br/>ImmutableEquatableArray#lt;ServiceRegistrationWithTags#gt;"]
            combinedClosedGenericDependencies["combinedClosedGenericDependencies<br/>(Invocations + Discover)"]
            allOpenGenericEntries["allOpenGenericEntries<br/>(Factory-based + Imported)"]
            CombineResolve["CombineAndResolveClosedGenerics"]
            serviceRegistrations["serviceRegistrations<br/>ImmutableEquatableArray#lt;ServiceRegistrationWithTags#gt;"]
        end

        subgraph Container Pipeline
            ContainerModel["ContainerModel"]
            CombineGroup["Combine & Group<br/>(GroupRegistrationsForContainer)"]
            ContainerWithGroups["ContainerWithGroups"]
        end

        subgraph Output Generation
            GenerateRegisterOutput["GenerateRegisterOutput<br/>(.ServiceRegistration.g.cs)"]
            GenerateContainerOutput["GenerateContainerOutput<br/>(.Container.g.cs)"]
        end

        IocRegisterDefaults --> allDefaultSettings
        IocRegisterDefaults -->|OpenGenericEntries| allOpenGenericEntries
        IocImportModule --> allImportedDefaultSettings
        IocImportModule -->|OpenGenericEntries| allOpenGenericEntries
        allDefaultSettings --> combinedDefaultSettings
        allImportedDefaultSettings --> combinedDefaultSettings

        IocRegister --> allBasicResults
        IocRegisterFor --> allBasicResults
        IocRegisterDefaults -->|ImplementationTypes| allBasicResults
        combinedDefaultSettings -.->|applied to| allBasicResults

        IocDiscover --> combinedClosedGenericDependencies
        Invocations --> combinedClosedGenericDependencies

        allBasicResults --> CombineResolve
        combinedClosedGenericDependencies --> CombineResolve
        allOpenGenericEntries --> CombineResolve
        CombineResolve --> serviceRegistrations

        IocContainer --> ContainerModel
        ContainerModel --> CombineGroup
        serviceRegistrations --> CombineGroup
        CombineGroup --> ContainerWithGroups

        serviceRegistrations --> GenerateRegisterOutput
        ContainerWithGroups --> GenerateContainerOutput
    end
```
