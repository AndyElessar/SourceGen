# Source Generator Spec

Source generators based on `Microsoft.Extensions.DependencyInjection.Abstractions`.

## Collection information

1. Find classes marked with `SourceGen.Ioc.IocRegisterAttribute` and `SourceGen.Ioc.IocRegisterForAttribute`.
    - `IocRegisterAttribute` supports generic version: `IocRegisterAttribute<T>`
    - `IocRegisterForAttribute` supports generic version: `IocRegisterForAttribute<T>`

2. Find `SourceGen.Ioc.IocRegisterDefaultsAttribute`. If exists, apply its settings as defaults to classes that step 1 finds, unless they override with their own attributes.
    - Supports generic version: `IocRegisterDefaultsAttribute<T>`

3. Service registration settings to collect:
    - Service type (from `TargetServiceType`, `ServiceTypes`, `RegisterAllInterfaces`, `RegisterAllBaseClasses`, `IocDiscoverAttribute`)
    - Implementation type (from `IocRegisterForAttribute.ImplementationType`, the class marked with `IocRegisterAttribute` and `IocRegisterDefaultsAttribute.ImplementationTypes`) and its constructor's parameters and members with `IocRegisterAttribute`, `InjectAttribute`
    - Lifetime (from `IocRegisterAttribute`, `IocRegisterForAttribute` or default settings)
    - Key and KeyType (from `IocRegisterAttribute`, `IocRegisterForAttribute` or default settings)
    - Decorators type (from `IocRegisterAttribute.Decorators`, `IocRegisterForAttribute.Decorators` or default settings) and its constructor's parameters and its type arguments constraints
    - Tags and TagOnly (from `IocRegisterAttribute`, `IocRegisterForAttribute` or default settings)
    - Factory (from `IocRegisterAttribute.Factory`, `IocRegisterForAttribute.Factory` or default settings)
    - Instance (from `IocRegisterAttribute.Instance`, `IocRegisterForAttribute.Instance`)
    - `IServiceProvider` invocations: `GetService(Type)`, `GetService<T>()`, `GetRequiredService(Type)`, `GetRequiredService<T>()`, `GetKeyedService(Type, Key)`, `GetKeyedService<T>(Key)`, `GetRequiredKeyedService(Type, Key)`, `GetRequiredKeyedService<T>(Key)`, `GetServices(Type)`, `GetServices<T>()`, `GetKeyedServices(Type)`, `GetKeyedServices<T>()`, collect type data from `T`, regarded as Service type
    - Generic factory method's type parameter hint (from `IocGenericFactoryAttribute`)
    - Other assembly's setting from `IocImportModuleAttribute` (supports generic version: `IocImportModuleAttribute<T>`)
    - `IocDiscoverAttribute` for explicit closed generic discovery (supports generic version: `IocDiscoverAttribute<T>`)

4. Collect compilation info:
    - Project root namespace (from MSBuild `RootNamespace` property, fallback to assembly name)
    - Assembly name (from compilation options)
    - Project properties: `SourceGenIocName` (customize generated method name)

## Parse logic

1. When `KeyType` is `KeyType.Value`, take `Key` as value, when `KeyType` is `KeyType.Csharp`, take `Key` as C# code snippet.
    - `KeyType.Value`:
      - Primitive types (int, string, bool, etc.) should be represented as itself (e.g., `42`, `"myString"`, `true`)
      - Enum types should be represented as `EnumType.EnumValue`
    - `KeyType.Csharp`:
      - String should be represented literal (e.g. `"MyClass.MyStaticField"` => `Add*Key(MyClass.MyStaticField)`)
      - Can use `nameof()` for compile-time safety (e.g. `nameof(MyClass.MyStaticField)` => `Add*Key(MyClass.MyStaticField)`)

2. When multiple default settings are found on an implementation type, use first setting by order:
    1. The one directly on the implementation type
    2. The one on the closest base class
    3. The one on the first interface in `RegistrationData.AllInterfaces`

3. `IocInjectAttribute`, `InjectAttribute`:
    - Only check with name `IocInjectAttribute` or `InjectAttribute`, so user can use other library's attribute, like `Microsoft.AspNetCore.Components.InjectAttribute`, make sure the Key interpret logic is compatible with `Microsoft.AspNetCore.Components.InjectAttribute`.

4. Constructor selection order:
    1. With `[IocInject]`
    2. Primary constructor
    3. Constructor have most paramters

5. Constructor and Method Parameter Analysis:
    - `[ServiceKey]` attribute: Inject the registration key (or default if non-keyed)
    - `[FromKeyedServices]` attribute: Use keyed service resolution
    - `[IocInject]` attribute with Key: Use keyed service resolution
    - `IServiceProvider` type: Pass the service provider directly
    - Collection types (`IEnumerable<T>`, `T[]`, `IReadOnlyList<T>`, etc.): Extract data of `T`, regarded as Service type
    - Built-in types without attributes and without default value: Skip (unresolvable)
    - Parameters with default values: See rule for optional parameters below

    **Optional Parameter Handling**:
    When a parameter has a default value:
    - If the type is a built-in type (int, string, etc.) or collection of built-in types: Skip (use default value)
    - If the type is resolvable from DI: Use conditionally assign:
      - If the resolved value is not null: Use the resolved value
      - If the resolved value is null: Do not specify the argument (use default value)

6. Property Analysis:
    - Only analyze property with `IocRegisterAttribute` and `InjectAttribute`
    - With Key: Use keyed service resolution
    - `IServiceProvider` type: Pass the service provider directly
    - Collection types (`IEnumerable<T>`, `T[]`, `IReadOnlyList<T>`, etc.): Extract data of `T`, regarded as Service type
    - Built-in types without Key and without default value: Skip (unresolvable)
    - Nullable Annotation: assign a resolved nullable value

7. Generic factory method type parameter mapping:
    - Collect type array in `IocGenericFactoryAttribute`
    - The first type is the service type going to map, it can't be unbound generic type, type parameter must be filled with a type for placeholder
    - Following types is mapping to generic factory method's type parameter in order, the type should be able to match the first type's placeholder

    ```csharp
    // Service type is IRequestHandler<>, has 1 type parameter
    [IocRegisterDefaults(typeof(IRequestHandler<>), Factory = nameof(FactoryContainer.Create))]
    public class FactoryContainer
    {   //                                              ┌--------------┐ "int" is a placeholder, make sure placeholders is unique
        //                                              │              │ in the context of the generic type mapping.
        //                                              ↓              ↓
        [IocGenericFactory(typeof(IRequestHandler<Task<int>>), typeof(int))]
        public static Create<T>()=>new Handler<T>();
    }

    // Service type is IRequestHandler<,>, has 2 type parameter
    [IocRegisterDefaults(typeof(IRequestHandler<,>), Factory = nameof(FactoryContainer.Create))]
    public class FactoryContainer
    {   //                                              ┌----------------------------------------┐
        //                                              │                                        │
        //                                              ↓                                        ↓
        [IocGenericFactory(typeof(IRequestHandler<Task<int>, decimal>), typeof(decimal), typeof(int))]
        public static Create<T1, T2>()=>new Handler<T1, T2>();//↑                ↑
    }                                                         //└----------------┘
    ```

## Generators

1. Registration generator: generate `IServiceCollection` register code.\
[Registration features spec](Registration.md)

2. Container generator: generate container that implement `IServiceProvider`.\
[Container features spec](Container.md)
