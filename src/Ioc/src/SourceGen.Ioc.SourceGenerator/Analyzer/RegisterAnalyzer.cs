using System.Collections.Concurrent;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace SourceGen.Ioc;

/// <summary>
/// Analyzer for IoC registration attributes.
/// Reports diagnostics for invalid attribute usage, circular dependencies, and service lifetime conflicts.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class RegisterAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// SGIOC001: Invalid Attribute Usage - IoCRegisterAttribute or IoCRegisterForAttribute is marked on private or abstract class,
    /// or on a type that cannot be constructed without a Factory or Instance.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidAttributeUsage = new(
        id: "SGIOC001",
        title: "Invalid Attribute Usage",
        messageFormat: "The type '{0}' cannot be registered because {1}",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "IoCRegisterAttribute and IoCRegisterForAttribute cannot be applied to private or abstract classes, or to a type that generated code cannot construct (an interface, or a class without an accessible constructor) without a Factory or Instance.");

    /// <summary>
    /// SGIOC001 reported at compilation end, once the Factory of the default settings is known: an implementation type that
    /// cannot be constructed without a Factory or Instance. Same rule as <see cref="InvalidAttributeUsage"/>, tagged
    /// <see cref="WellKnownDiagnosticTags.CompilationEnd"/>.
    /// </summary>
    public static readonly DiagnosticDescriptor UnconstructibleImplementation = new(
        id: "SGIOC001",
        title: "Invalid Attribute Usage",
        messageFormat: "The type '{0}' cannot be registered because {1}",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "IoCRegisterAttribute and IoCRegisterForAttribute cannot be applied to private or abstract classes, or to a type that generated code cannot construct (an interface, or a class without an accessible constructor) without a Factory or Instance.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    /// <summary>
    /// SGIOC002: Circular Dependency Detected - Circular dependencies are detected among registered services.
    /// </summary>
    public static readonly DiagnosticDescriptor CircularDependency = new(
        id: "SGIOC002",
        title: "Circular Dependency Detected",
        messageFormat: "Circular dependency detected: {0}",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Circular dependencies among registered services will cause runtime errors.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    /// <summary>
    /// SGIOC003: Service Lifetime Conflict Detected - Singleton service depending on Scoped service.
    /// </summary>
    public static readonly DiagnosticDescriptor SingletonDependsOnScoped = new(
        id: "SGIOC003",
        title: "Service Lifetime Conflict Detected",
        messageFormat: "Lifetime conflict: Singleton service '{0}' depends on Scoped service '{1}'",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A Singleton service should not depend on a Scoped service.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    /// <summary>
    /// SGIOC004: Dangerous Service Lifetime Dependency - Singleton service depending on Transient service.
    /// </summary>
    public static readonly DiagnosticDescriptor SingletonDependsOnTransient = new(
        id: "SGIOC004",
        title: "Dangerous Service Lifetime Dependency Detected",
        messageFormat: "Dangerous lifetime dependency: Singleton service '{0}' depends on Transient service '{1}'",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A Singleton service should not depend on a Transient service. The Transient instance will be captured and live for the application lifetime.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    /// <summary>
    /// SGIOC005: Dangerous Service Lifetime Dependency - Scoped service depending on Transient service.
    /// </summary>
    public static readonly DiagnosticDescriptor ScopedDependsOnTransient = new(
        id: "SGIOC005",
        title: "Dangerous Service Lifetime Dependency Detected",
        messageFormat: "Dangerous lifetime dependency: Scoped service '{0}' depends on Transient service '{1}'",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A Scoped service should not depend on a Transient service. The Transient instance will be captured for the scope lifetime.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    /// <summary>
    /// SGIOC007: Invalid Attribute Usage - InjectAttribute is marked on static member, inaccessible member, or method that does not return void.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidInjectAttributeUsage = new(
        id: "SGIOC007",
        title: "Invalid Attribute Usage",
        messageFormat: "InjectAttribute cannot be applied to '{0}' because {1}",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "InjectAttribute cannot be applied to static members, members that cannot be assigned/invoked, or methods that do not return void (or non-generic Task when AsyncMethodInject is enabled).");

    /// <summary>
    /// SGIOC008: Invalid Attribute Usage - Factory or Instance uses nameof() but the referenced member is not static or is inaccessible.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidFactoryOrInstanceMember = new(
        id: "SGIOC008",
        title: "Invalid Attribute Usage",
        messageFormat: "The {0} member '{1}' specified via nameof() is {2}",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "When using nameof() to specify a Factory or Instance, the referenced field or property must be static and accessible.");

    /// <summary>
    /// SGIOC009: Invalid Attribute Usage - Instance is specified with an explicit Lifetime that is not Singleton.
    /// </summary>
    public static readonly DiagnosticDescriptor InstanceRequiresSingleton = new(
        id: "SGIOC009",
        title: "Invalid Attribute Usage",
        messageFormat: "Instance registration '{0}' requires Singleton lifetime, but its lifetime is '{1}'",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "When using Instance to provide a pre-created object, the lifetime must be Singleton because the same instance will be returned for every resolution. An Instance registration without a Lifetime is Singleton; IocRegisterDefaults and the SourceGenIocDefaultLifetime MSBuild property do not apply to it.");

    /// <summary>
    /// SGIOC010: Invalid Attribute Usage - Both Factory and Instance are specified on the same attribute.
    /// </summary>
    public static readonly DiagnosticDescriptor FactoryAndInstanceConflict = new(
        id: "SGIOC010",
        title: "Invalid Attribute Usage",
        messageFormat: "Both Factory and Instance are specified on the same attribute; Instance takes precedence and Factory will be ignored",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "When both Factory and Instance are specified on the same IoCRegisterAttribute or IoCRegisterForAttribute, Instance takes precedence and Factory will be ignored. Remove one of them to avoid confusion.");

    /// <summary>
    /// SGIOC006: Duplicated Attribute Usage - Both FromKeyedServicesAttribute and InjectAttribute are marked on the same parameter.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicatedKeyedServiceAttribute = new(
        id: "SGIOC006",
        title: "Duplicated Attribute Usage",
        messageFormat: "Parameter '{0}' has both [FromKeyedServices] and [Inject] attributes; [FromKeyedServices] will take precedence",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "When both [FromKeyedServices] and [Inject] attributes are applied to the same parameter, [FromKeyedServices] takes precedence and [Inject] is ignored.");

    /// <summary>
    /// SGIOC011: Duplicated Registration Detected - Same implementation type, key, and at least one matching tag are registered multiple times.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicatedRegistration = new(
        id: "SGIOC011",
        title: "Duplicated Registration Detected",
        messageFormat: "Duplicated registration: type '{0}'{1} is already registered",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The same implementation type with the same key and at least one overlapping tag is registered multiple times. Only the last registration will be effective.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    /// <summary>
    /// SGIOC012: Duplicated IoCRegisterDefaults Detected - Same target type and at least one matching tag has multiple default settings.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicatedDefaultSettings = new(
        id: "SGIOC012",
        title: "Duplicated Registration Detected",
        messageFormat: "Duplicated IoCRegisterDefaults: target type '{0}' already has default settings defined",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The same target type with at least one overlapping tag has multiple IoCRegisterDefaultsAttribute definitions. Only the first definition will be used.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    /// <summary>
    /// SGIOC013: Key type is unmatched - ServiceKeyAttribute parameter type does not match the registered key type.
    /// </summary>
    public static readonly DiagnosticDescriptor ServiceKeyTypeMismatch = new(
        id: "SGIOC013",
        title: "Key type is unmatched",
        messageFormat: "[ServiceKey] parameter '{0}' has type '{1}' but the registered key type is '{2}'",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The parameter marked with [ServiceKey] attribute must have a type that matches or is assignable from the registered key type in [IoCRegister] or [IoCRegisterFor] attribute.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    /// <summary>
    /// SGIOC014: Key does not exist - ServiceKeyAttribute is marked on parameter but no Key is registered.
    /// </summary>
    public static readonly DiagnosticDescriptor ServiceKeyNotRegistered = new(
        id: "SGIOC014",
        title: "Key does not exist",
        messageFormat: "[ServiceKey] parameter '{0}' is used but no Key is specified in [IoCRegister] or [IoCRegisterFor] attribute",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The parameter marked with [ServiceKey] attribute requires a Key to be specified in [IoCRegister] or [IoCRegisterFor] attribute.");

    /// <summary>
    /// SGIOC014 reported at compilation end for the constructor and inject method parameters of a registration without a Key.
    /// Same rule as <see cref="ServiceKeyNotRegistered"/>, tagged <see cref="WellKnownDiagnosticTags.CompilationEnd"/>.
    /// </summary>
    public static readonly DiagnosticDescriptor ServiceKeyNotRegisteredForRegistration = new(
        id: "SGIOC014",
        title: "Key does not exist",
        messageFormat: "[ServiceKey] parameter '{0}' is used but no Key is specified in [IoCRegister] or [IoCRegisterFor] attribute",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The parameter marked with [ServiceKey] attribute requires a Key to be specified in [IoCRegister] or [IoCRegisterFor] attribute.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    /// <summary>
    /// SGIOC015: KeyValuePair's Key type is unmatched - Injected KeyValuePair/Dictionary key type does not match any registered keyed service's key type.
    /// </summary>
    public static readonly DiagnosticDescriptor KeyValuePairKeyTypeMismatch = new(
        id: "SGIOC015",
        title: "KeyValuePair's Key type is unmatched",
        messageFormat: "KeyValuePair parameter '{0}' has key type '{1}' but no keyed service for '{2}' has a matching key type",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "When injecting KeyValuePair<K, V>, IDictionary<K, V>, or collection of KeyValuePair<K, V>, the key type K must match the registered key type of at least one keyed service for V.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    /// <summary>
    /// SGIOC016: Factory Method is unmatched - Generic factory method has no valid type parameter mapping.
    /// </summary>
    public static readonly DiagnosticDescriptor GenericFactoryMissingAttribute = new(
        id: "SGIOC016",
        title: "Factory Method is unmatched",
        messageFormat: "Generic factory method '{0}' has no valid type parameter mapping: {1}",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "When using a generic factory method, you must mark it with [IocGenericFactory] attribute (or specify GenericFactoryTypeMapping) to specify how service type placeholders map to factory method type parameters. The mapping must provide one placeholder per factory method type parameter, and each placeholder must appear in the service type template.");

    /// <summary>
    /// SGIOC017: Generic Factory Method's type parameters are duplicated - Placeholder types in [IocGenericFactory] must be unique.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicatedGenericFactoryPlaceholders = new(
        id: "SGIOC017",
        title: "Generic Factory Method's type parameters are duplicated",
        messageFormat: "[IocGenericFactory] or GenericFactoryTypeMapping has duplicated placeholder type '{0}'; each placeholder type must be unique",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The placeholder types in [IocGenericFactory] or GenericFactoryTypeMapping (from second to last) must be unique. Duplicated types make it impossible to distinguish which type argument maps to which factory method type parameter.");

    /// <summary>
    /// SGIOC022: Inject attribute ignored due to disabled feature.
    /// </summary>
    public static readonly DiagnosticDescriptor InjectFeatureDisabled = new(
        id: "SGIOC022",
        title: "Inject attribute ignored due to disabled feature",
        messageFormat: "'{0}' has [IocInject] but {1} feature is not enabled. Add '{1}' to <SourceGenIocFeatures> in your project file.",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "When SourceGenIocFeatures disables a member injection feature, [IocInject] on that member is ignored during generation.");

    /// <summary>
    /// SGIOC023: Invalid InjectMembers element format.
    /// </summary>
    public static readonly DiagnosticDescriptor InjectMembersInvalidFormat = new(
        id: "SGIOC023",
        title: "Invalid InjectMembers element format",
        messageFormat: "InjectMembers element at index {0} is invalid; expected nameof(member) or new object[] {{ nameof(member), key [, KeyType] }}",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Each element in InjectMembers must be either a nameof() expression or an array literal with member name, key, and optional KeyType. With KeyType.Value (the default), the key must be a non-null constant.");

    /// <summary>
    /// SGIOC024: InjectMembers specifies non-injectable member.
    /// </summary>
    public static readonly DiagnosticDescriptor InjectMembersNonInjectableMember = new(
        id: "SGIOC024",
        title: "InjectMembers specifies non-injectable member",
        messageFormat: "Member '{0}' specified in InjectMembers cannot be injected: {1}",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Members specified in InjectMembers must be injectable: instance properties with accessible setters, non-readonly fields, and ordinary non-generic void-returning methods (or non-generic Task-returning when AsyncMethodInject is enabled), all of which must be public, internal, or protected internal, and not hidden on the registered type by a member declared with new.");

    /// <summary>
    /// SGIOC026: AsyncMethodInject feature requires MethodInject to be enabled.
    /// </summary>
    public static readonly DiagnosticDescriptor AsyncMethodInjectRequiresMethodInject = new(
        id: "SGIOC026",
        title: "Invalid feature combination",
        messageFormat: "'AsyncMethodInject' feature requires 'MethodInject' to be enabled. Add 'MethodInject' to <SourceGenIocFeatures>.",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "AsyncMethodInject delegates async method injection to the source generator, which requires MethodInject to already be enabled. Add MethodInject alongside AsyncMethodInject in SourceGenIocFeatures.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    /// <summary>
    /// SGIOC030: Synchronous dependency requested for async-init-only service.
    /// </summary>
    public static readonly DiagnosticDescriptor SyncDependencyOnAsyncInitService = new(
        id: "SGIOC030",
        title: "Synchronous dependency requested for async-init service",
        messageFormat: "'{0}' requires '{1}' but this service is async-init (its implementation or a decorator has async inject methods) and no synchronous registration exists. Use 'Task<{1}>'.",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "When a service is async-init, because its implementation or a decorator applied to it has async inject methods, consumers must request Task<T> instead of T, Lazy<T> or Func<..., T> because no synchronous resolution path exists.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    /// <summary>
    /// SGIOC028: [IocInject] method is declared as async void, which cannot be awaited.
    /// </summary>
    public static readonly DiagnosticDescriptor AsyncVoidInjectMethod = new(
        id: "SGIOC028",
        title: "async void injection method cannot be awaited",
        messageFormat: "[IocInject] method '{0}' is 'async void' which cannot be awaited. Change return type to 'Task' for async initialization, or remove the 'async' modifier for synchronous injection.",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Methods marked with [IocInject] that are declared as async void cannot be awaited by the source generator. Change the return type to Task to enable async injection, or remove the async modifier if the method is synchronous.");

    /// <summary>
    /// SGIOC032: Explicit service type is not compatible with the implementation and is skipped.
    /// </summary>
    public static readonly DiagnosticDescriptor IncompatibleServiceType = new(
        id: "SGIOC032",
        title: "Service type is not compatible with implementation",
        messageFormat: "Service type '{0}' cannot be registered for implementation '{1}' and will be skipped: {2}",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An open generic implementation can only be registered as open generic service types it implements, and a closed implementation cannot be registered as an open generic service type. Incompatible service types are skipped during generation.");

    /// <summary>
    /// SGIOC033: A Func factory with arguments creates a new instance per call, ignoring the service's Singleton or Scoped lifetime.
    /// </summary>
    public static readonly DiagnosticDescriptor FuncWithArgumentsIgnoresLifetime = new(
        id: "SGIOC033",
        title: "Func with arguments ignores the service lifetime",
        messageFormat: "'{0}' depends on '{1}', which creates a new '{2}' on every call although '{2}' is registered as {3}",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A Func<T1, ..., TReturn> with input parameters constructs a new instance from its arguments on every call, so a Singleton or Scoped lifetime of the service does not apply. Register the service as Transient, or depend on the parameterless Func<T> or the service itself to follow its lifetime.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    /// <summary>
    /// SGIOC034: More than one decorator constructor parameter can receive the decorated instance.
    /// </summary>
    public static readonly DiagnosticDescriptor MultipleDecoratedParameters = new(
        id: "SGIOC034",
        title: "Multiple decorator parameters can receive the decorated instance",
        messageFormat: "Parameter '{0}' of decorator '{1}' is of the decorated type '{2}' but is resolved as a dependency; only the first such parameter '{3}' receives the decorated instance",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The decorated instance is passed to the first decorator constructor parameter whose type is the decorated service type. Other parameters of that type are resolved from the container like any dependency, which is often unintended.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    /// <summary>
    /// SGIOC035: No decorator constructor parameter can receive the decorated instance.
    /// </summary>
    public static readonly DiagnosticDescriptor NoDecoratedParameter = new(
        id: "SGIOC035",
        title: "Decorator has no parameter for the decorated instance",
        messageFormat: "Decorator '{0}' has no constructor parameter of type '{1}' to receive the decorated instance, so it is not applied to '{1}'",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A decorator receives the decorated instance through its first constructor parameter whose type is the decorated service type. A decorator without such a parameter is skipped for that service type.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    /// <summary>
    /// SGIOC036: A dependency of an open generic registration nests type arguments deeper on every round of a dependency
    /// cycle without a deferred wrapper, so the object graph is infinite.
    /// </summary>
    public static readonly DiagnosticDescriptor InfiniteGenericDependency = new(
        id: "SGIOC036",
        title: "Dependency nests type arguments without end",
        messageFormat: "Dependency '{1}' of '{0}' nests type arguments deeper on every round of a dependency cycle without a Lazy<T> or Func<T> dependency, so its object graph is infinite and it is not closed at compile time",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Closing the dependency needs an open generic implementation on the same cycle closed with a type argument nested deeper, such as Node<List<T>> for Node<T>, so its closed forms never end. The generator does not close it, and resolving it at runtime from the open generic registration would never end.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    /// <summary>
    /// SGIOC037: A dependency of an open generic registration nests type arguments deeper on every round of a dependency
    /// cycle with a deferred wrapper, so the generator does not close it.
    /// </summary>
    public static readonly DiagnosticDescriptor DeferredExpandingGenericDependency = new(
        id: "SGIOC037",
        title: "Dependency nests type arguments without end behind a deferred wrapper",
        messageFormat: "Dependency '{1}' of '{0}' nests type arguments deeper on every round of a dependency cycle, so it is not closed at compile time",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Closing the dependency needs an open generic implementation on the same cycle closed with a type argument nested deeper, such as Node<List<T>> for Node<T>, so its closed forms never end. A Lazy<T> or Func<T> dependency on the cycle keeps the object graph finite, but the generator does not close the dependency: it is resolved at runtime from the open generic registration of the integrated IServiceProvider (by reflection, not Native AOT safe), and a container that does not integrate one cannot resolve it.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    /// <summary>
    /// SGIOC038: A registration or default settings attribute uses an open type declared in a generic type, which the generator cannot close.
    /// </summary>
    public static readonly DiagnosticDescriptor OpenTypeNestedInGenericType = new(
        id: "SGIOC038",
        title: "Open type nested in a generic type is not supported",
        messageFormat: "'{0}' is an open type declared in the generic type '{1}', which the generator cannot close, so it is ignored; use its closed types instead",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The generator closes open generic types with their own type arguments, not with those of their containing type, so an open type declared in a generic type, such as Outer<O>.Node<T> or typeof(Outer<>.INode<>), is ignored as a registered type, service type, decorator, implementation type or default settings target. A closed type, such as Outer<int>.Node<string>, is used like any type.");

    /// <summary>
    /// SGIOC038 reported at compilation end, once the default settings are known: an open type declared in a generic type that
    /// an open implementation would be registered as through RegisterAllInterfaces / RegisterAllBaseClasses. Same rule as
    /// <see cref="OpenTypeNestedInGenericType"/>, tagged <see cref="WellKnownDiagnosticTags.CompilationEnd"/>.
    /// </summary>
    public static readonly DiagnosticDescriptor ImplicitOpenTypeNestedInGenericType = new(
        id: "SGIOC038",
        title: "Open type nested in a generic type is not supported",
        messageFormat: "'{0}' is an open type declared in the generic type '{1}', which the generator cannot close, so it is ignored; use its closed types instead",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The generator closes open generic types with their own type arguments, not with those of their containing type, so an open type declared in a generic type, such as Outer<O>.Node<T> or typeof(Outer<>.INode<>), is ignored as a registered type, service type, decorator, implementation type or default settings target. A closed type, such as Outer<int>.Node<string>, is used like any type.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    /// <summary>
    /// SGIOC039: A service type of a registration with a generic factory does not match the factory's service type template,
    /// so the generator does not register it.
    /// </summary>
    public static readonly DiagnosticDescriptor ServiceTypeUnmatchedByGenericFactory = new(
        id: "SGIOC039",
        title: "Service type does not match the generic factory mapping",
        messageFormat: "Service type '{0}' of '{1}' does not match the service type template '{2}' of the generic factory '{3}', so it is not registered",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A generic factory is called with the type arguments its mapping ([IocGenericFactory] or GenericFactoryTypeMapping) takes from a service type that matches its service type template. A service type that does not match cannot be created by the factory, so it is not registered; an implementation type that does not match is constructed instead, unless it cannot be constructed (an interface, an abstract or static class, or a class without a public or internal constructor), in which case it is not registered either.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    /// <summary>
    /// SGIOC040: The Key of an open generic registration uses the implementation's type parameters, so the open registration
    /// is not registered.
    /// </summary>
    public static readonly DiagnosticDescriptor KeyUsesImplementationTypeParameters = new(
        id: "SGIOC040",
        title: "Key uses the type parameters of the open generic implementation",
        messageFormat: "Key '{0}' of '{1}' uses its type parameters, so it can differ per closed type and the open generic registration is not registered; only the closed types discovered at compile time are registered",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An open generic registration has a single key, while a Key that uses the implementation's type parameters, such as Node<T>.Name, is read from each closed type (Node<string>.Name) and does not compile without them. The open generic registration is therefore not registered, so a closed type that is not discovered at compile time, such as one only resolved at runtime, is not registered.");

    /// <summary>
    /// SGIOC041: A Factory, Instance or Key given with nameof() is a member of an unbound generic type whose type arguments
    /// cannot be determined, so the registration or default settings are ignored.
    /// </summary>
    public static readonly DiagnosticDescriptor NameofOfUnboundGenericType = new(
        id: "SGIOC041",
        title: "Member of an unbound generic type cannot be used",
        messageFormat: "{0} '{1}' is a member of the unbound generic type '{2}', whose type arguments cannot be determined, so {3}; give its type arguments",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "nameof() accepts an unbound generic type, such as nameof(Factories<>.Create), but generated code must name the member of a closed type. Only the type parameters of the registered open generic implementation type are closed, such as nameof(Node<>.Default) for Node<T>, so a member of another unbound generic type is not supported and the registration is ignored. Default settings apply to other types, so a Factory of [IocRegisterDefaults] cannot use any unbound generic type and the default settings are ignored.");

    /// <summary>
    /// SGIOC042: The closed registrations of an open generic registration call its Factory, so the open registration, which
    /// MS.DI would construct without the factory, is not registered.
    /// </summary>
    public static readonly DiagnosticDescriptor FactoryOpenGenericNotRegisteredOpen = new(
        id: "SGIOC042",
        title: "Open generic registration with a Factory is not registered open",
        messageFormat: "The closed types of '{0}' are created by the factory '{1}', so an open generic registration of it is not registered and a closed type that is only requested at runtime cannot be resolved; only the closed types discovered at compile time are registered",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Microsoft.Extensions.DependencyInjection constructs the implementation type of an open generic registration, such as services.AddSingleton(typeof(INode<>), typeof(Node<>)), without calling the factory. The open generic registration of a registration whose closed types call its Factory (its own or that of its default settings) is therefore not registered, so the factory is never bypassed: the closed types discovered at compile time call it, and a closed type that is only requested at runtime is not registered. Use the closed types in dependencies, register them, or discover them with [IocDiscover].",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    /// <summary>
    /// SGIOC043: A nameof() in InjectMembers names a method group with more than one injectable overload, so none is injected.
    /// </summary>
    public static readonly DiagnosticDescriptor InjectMembersAmbiguousMethodGroup = new(
        id: "SGIOC043",
        title: "InjectMembers names a method with several injectable overloads",
        messageFormat: "Method '{0}' specified in InjectMembers has more than one injectable overload on '{1}', so none of them is injected; mark the overload to inject with [IocInject] instead, or give it a unique name",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "nameof() of a method names its method group and cannot choose an overload. When more than one overload of the method can be injected into the registered type, including overloads inherited from base classes, the element is ambiguous and nothing is injected for it. Mark the overload to inject with [IocInject], which takes precedence over the InjectMembers element, or rename it.");

    /// <summary>
    /// SGIOC044: A Factory given with nameof() names a method group with more than one usable overload, so none is chosen.
    /// </summary>
    public static readonly DiagnosticDescriptor FactoryAmbiguousMethodGroup = new(
        id: "SGIOC044",
        title: "Factory names a method with several usable overloads",
        messageFormat: "Factory method '{0}' specified via nameof() has more than one overload that can be used as the factory, so none of them is chosen; give the factory method a unique name",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "nameof() of a method names its method group and cannot choose an overload. When more than one overload is static, accessible, returns a value and, when generic, has a type mapping ([IocGenericFactory] or GenericFactoryTypeMapping), including overloads inherited from base classes, the Factory is ambiguous: its parameters are not resolved, and it is called by its path with the service provider only, like a Factory given as a string. Give the factory method a unique name.");

    /// <summary>
    /// SGIOC046: A container partial accessor returns a Func with input parameters that the container does not construct,
    /// because a decorator applied to the service type of the Instance registration it resolves has async inject methods.
    /// </summary>
    public static readonly DiagnosticDescriptor AccessorFuncOfAsyncDecoratedInstance = new(
        id: "SGIOC046",
        title: "Func with input parameters is not generated for an Instance with async decorators",
        messageFormat: "Partial accessor '{0}' returns '{1}', but the container does not construct a Func with input parameters for '{2}': a decorator applied to it has async inject methods, which the Func cannot await, so it is resolved from the fallback provider",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The instance of an Instance registration is not decorated, but a Func<T1, ..., TReturn> with input parameters constructs a new instance and wraps it in the decorators applied to TReturn. It cannot await their async inject methods, so, like for an async-init service, the container does not construct it and resolves the Func from the fallback provider (the Register output does not register it).",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        AccessorFuncOfAsyncDecoratedInstance,
        FactoryOpenGenericNotRegisteredOpen,
        InjectMembersAmbiguousMethodGroup,
        FactoryAmbiguousMethodGroup,
        KeyUsesImplementationTypeParameters,
        NameofOfUnboundGenericType,
        OpenTypeNestedInGenericType,
        ImplicitOpenTypeNestedInGenericType,
        ServiceTypeUnmatchedByGenericFactory,
        MultipleDecoratedParameters,
        NoDecoratedParameter,
        InfiniteGenericDependency,
        DeferredExpandingGenericDependency,
        InvalidAttributeUsage,
        UnconstructibleImplementation,
        CircularDependency,
        SingletonDependsOnScoped,
        SingletonDependsOnTransient,
        ScopedDependsOnTransient,
        InvalidInjectAttributeUsage,
        InvalidFactoryOrInstanceMember,
        InstanceRequiresSingleton,
        FactoryAndInstanceConflict,
        DuplicatedKeyedServiceAttribute,
        DuplicatedRegistration,
        DuplicatedDefaultSettings,
        ServiceKeyTypeMismatch,
        ServiceKeyNotRegistered,
        ServiceKeyNotRegisteredForRegistration,
        KeyValuePairKeyTypeMismatch,
        GenericFactoryMissingAttribute,
        DuplicatedGenericFactoryPlaceholders,
        InjectFeatureDisabled,
        InjectMembersInvalidFormat,
        InjectMembersNonInjectableMember,
        AsyncMethodInjectRequiresMethodInject,
        SyncDependencyOnAsyncInitService,
        AsyncVoidInjectMethod,
        IncompatibleServiceType,
        FuncWithArgumentsIgnoresLifetime
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Register for compilation start to cache attribute symbols and collect services
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var features = ParseIocFeatures(context.Options);

        // SGIOC026: AsyncMethodInject requires MethodInject — report once per compilation
        if((features & IocFeatures.AsyncMethodInject) != 0 && (features & IocFeatures.MethodInject) == 0)
        {
            context.RegisterCompilationEndAction(static ctx =>
                ctx.ReportDiagnostic(Diagnostic.Create(AsyncMethodInjectRequiresMethodInject, Location.None)));
        }

        // Get attribute type symbols for faster lookup (including generic variants)
        var attributeSymbols = new IocAttributeSymbols(context.Compilation);

        // Check if any IoC attribute is available
        if(!attributeSymbols.HasAnyRegistrationAttribute)
            return;

        // The registered implementation types, keyed by runtime type (without tuple element names), like the generator's
        // dependency resolution
        var registeredServices = new ConcurrentDictionary<INamedTypeSymbol, ServiceInfo>(RuntimeTypeSymbolComparer.Instance);

        var analyzerContext = new AnalyzerContext(
            attributeSymbols,
            registeredServices,
            features,
            IocSourceGenerator.ReadDefaultLifetime(context.Options.AnalyzerConfigOptionsProvider.GlobalOptions) ?? ServiceLifetime.Transient,
            context.Compilation.Assembly);

        // Collect assembly-level IoCRegisterFor attributes first (synchronously during compilation start). Like type-level
        // registrations, they are resolved against the default settings at compilation end, once type-level default settings
        // and imported modules are known.
        var assemblyRegistrations = CollectAssemblyLevelRegistrations(context.Compilation, analyzerContext, context.CancellationToken);
        var attributeNameFilter = new IocAttributeNameFilter(
            context.Compilation,
            attributeSymbols.IocRegisterAttribute,
            attributeSymbols.IocRegisterAttribute_T1,
            attributeSymbols.IocRegisterForAttribute,
            attributeSymbols.IocRegisterForAttribute_T1,
            attributeSymbols.IocRegisterDefaultsAttribute,
            attributeSymbols.IocRegisterDefaultsAttribute_T1);

        // First pass: collect registrations, type-level IoCRegisterDefaultsAttribute (class, struct, interface) and
        // IocImportModuleAttribute, and do the immediate validation that does not depend on default settings (SGIOC001, 032)
        context.RegisterSymbolAction(ctx =>
        {
            CollectAndValidateNamedType(ctx, analyzerContext);
            AnalyzeImportedOpenRegistrations(ctx, analyzerContext);
        }, SymbolKind.NamedType);

        // SGIOC007 + SGIOC022 + SGIOC028: Analyze InjectAttribute usage and feature gating on members
        context.RegisterSymbolAction(ctx => AnalyzeInjectAttribute(ctx, analyzerContext), SymbolKind.Property, SymbolKind.Field);

        // Methods: SGIOC007/022/028 InjectAttribute usage, SGIOC006 duplicated keyed service attributes on parameters,
        // and SGIOC016/017 [IocGenericFactory] mappings
        context.RegisterSymbolAction(ctx =>
        {
            AnalyzeInjectAttribute(ctx, analyzerContext);
            AnalyzeDuplicatedKeyedServiceAttributes(ctx);
            AnalyzeIocGenericFactoryAttribute(ctx);
        }, SymbolKind.Method);

        // SGIOC008/010/023/024 + nameof() key type resolution for KeyType.Csharp registrations,
        // and SGIOC001/032 for assembly-level IoCRegisterFor attributes.
        // Using RegisterSyntaxNodeAction to avoid RS1030 warning (do not use Compilation.GetSemanticModel)
        context.RegisterSyntaxNodeAction(ctx => AnalyzeIocAttributeSyntax(ctx, analyzerContext, assemblyRegistrations, attributeNameFilter), SyntaxKind.Attribute);

        // Second pass: analyze dependencies after all services are collected (SGIOC002, SGIOC003-005)
        context.RegisterCompilationEndAction(ctx => AnalyzeAllDependencies(ctx, analyzerContext));
    }

    /// <summary>
    /// Binds each attribute that can be an IoC attribute once and dispatches to the IoC attribute syntax analyses.
    /// Assembly-level IoCRegisterFor attributes were bound at compilation start, so they are not bound again.
    /// </summary>
    private static void AnalyzeIocAttributeSyntax(
        SyntaxNodeAnalysisContext context,
        AnalyzerContext analyzerContext,
        Dictionary<(SyntaxTree Tree, TextSpan Span), AssemblyRegistration> assemblyRegistrations,
        IocAttributeNameFilter attributeNameFilter)
    {
        if(context.Node is not AttributeSyntax attributeSyntax)
            return;

        INamedTypeSymbol attributeClass;
        AttributeData? attributeData = null;
        if(assemblyRegistrations.TryGetValue((attributeSyntax.SyntaxTree, attributeSyntax.Span), out var assemblyRegistration))
        {
            AnalyzeAssemblyLevelRegistration(context.ReportDiagnostic, analyzerContext, assemblyRegistration, attributeSyntax.GetLocation());
            attributeData = assemblyRegistration.Attribute;
            attributeClass = attributeData.AttributeClass!;
        }
        else
        {
            // Binding every attribute in the compilation dominates this action, so skip names that cannot be IoC attributes
            if(!attributeNameFilter.MayBeIocAttribute(attributeSyntax, context.CancellationToken))
                return;

            var attributeSymbol = context.SemanticModel.GetSymbolInfo(attributeSyntax, context.CancellationToken).Symbol;
            if(attributeSymbol is not IMethodSymbol { ContainingType: { } boundAttributeClass })
                return;

            if(!AnalyzerHelpers.IsAnyIoCAttribute(boundAttributeClass, analyzerContext.AttributeSymbols))
                return;

            attributeClass = boundAttributeClass;
        }

        AnalyzeFactoryAndInstanceOnAttribute(context, attributeSyntax, attributeClass, attributeData, analyzerContext);
        AnalyzeNameofTypeParameters(context, attributeSyntax, attributeClass, attributeData, analyzerContext);
        AnalyzeOpenTypesNestedInGenericType(context, attributeSyntax, attributeClass, analyzerContext);
        ResolveCsharpKeyTypes(context, attributeSyntax, attributeClass, attributeData, analyzerContext);
        ResolveFactoryMethod(context, attributeSyntax, analyzerContext);
    }

    /// <summary>
    /// Resolves the <c>Factory</c> method given with <c>nameof()</c>, like the generator's <c>GetFactoryMethodData</c>, for the
    /// expanding dependencies of open generic registrations (SGIOC036/SGIOC037) at compilation end.
    /// </summary>
    private static void ResolveFactoryMethod(SyntaxNodeAnalysisContext context, AttributeSyntax attributeSyntax, AnalyzerContext analyzerContext)
    {
        foreach(var argument in attributeSyntax.ArgumentList?.Arguments ?? default)
        {
            if(argument.NameEquals?.Name.Identifier.Text == "Factory"
                && argument.Expression is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: "nameof" }, ArgumentList.Arguments: [var nameofArgument] }
                && RoslynExtensions.ResolveFactoryMethod(nameofArgument.Expression, attributeSyntax.ArgumentList, context.SemanticModel) is { } factory)
            {
                analyzerContext.FactoryMethods[(attributeSyntax.SyntaxTree, attributeSyntax.Span)] = factory;
            }
        }
    }

    /// <summary>
    /// Analyzes all dependencies after all services have been collected.
    /// This ensures we have a complete picture of all registered services before checking dependencies.
    /// First collects the default settings (SGIOC012 for duplicated IoCRegisterDefaults) and resolves every registration
    /// against them (SGIOC011 and SGIOC038, which depend on the default settings). Also reports SGIOC013-014 for
    /// ServiceKey type mismatches, and SGIOC015 for KeyValuePair key type mismatches.
    /// </summary>
    private static void AnalyzeAllDependencies(CompilationAnalysisContext context, AnalyzerContext analyzerContext)
    {
        // Type-level default settings and imported modules are only known once every symbol action ran, so the default
        // settings are collected, and the registrations resolved against them, here
        var treeOrder = AnalyzerHelpers.GetSyntaxTreeOrder(context.Compilation);
        CollectDefaults(context.ReportDiagnostic, context.Compilation, analyzerContext, treeOrder, context.CancellationToken);
        ResolveRegistrations(context.ReportDiagnostic, analyzerContext, treeOrder, context.CancellationToken);

        // Reuse these collections across all services to reduce allocations
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var pathStack = new Stack<INamedTypeSymbol>();
        var dependencyIndex = DependencyIndex.Build(analyzerContext.DependencyRegistrations, context.CancellationToken);
        var graph = new DependencyGraph(analyzerContext, dependencyIndex);
        var keyedServiceIndex = new KeyedServiceIndex(analyzerContext);
        var constructedByFunc = CollectRegistrationsConstructedByFunc(analyzerContext, context.CancellationToken);

        // SGIOC046: Partial accessors whose Func with input parameters the container does not construct
        AnalyzeContainerAccessorFuncsOfAsyncDecoratedInstances(context.ReportDiagnostic, analyzerContext, context.CancellationToken);

        foreach(var kvp in analyzerContext.RegisteredServices)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var serviceInfo = kvp.Value;

            // SGIOC013/SGIOC014: Analyze ServiceKey parameter type mismatches
            AnalyzeServiceKeyTypeMismatch(context.ReportDiagnostic, serviceInfo, constructedByFunc, analyzerContext, context.CancellationToken);

            // SGIOC015: Analyze KeyValuePair/Dictionary key type mismatches
            AnalyzeKeyValuePairKeyTypeMismatch(context.ReportDiagnostic, serviceInfo, analyzerContext, keyedServiceIndex, context.CancellationToken);

            // SGIOC033: Func factories with arguments ignore Singleton/Scoped lifetimes
            AnalyzeFuncWithArgumentsLifetime(context.ReportDiagnostic, serviceInfo, dependencyIndex, context.CancellationToken);

            AnalyzeDependencies(
                context.ReportDiagnostic,
                graph,
                serviceInfo,
                visited,
                pathStack,
                context.CancellationToken);
        }

        // SGIOC034/SGIOC035: Constructor parameters that can receive the decorated instance
        AnalyzeDecoratedParameters(context.ReportDiagnostic, analyzerContext, context.CancellationToken);

        // SGIOC036/SGIOC037: Dependencies of open generic registrations that nest type arguments without end
        AnalyzeExpandingDependencies(context.ReportDiagnostic, analyzerContext, context.CancellationToken);

        // SGIOC039: Service types that the mapping of a generic factory does not match
        AnalyzeGenericFactoryServiceTypes(context.ReportDiagnostic, analyzerContext, context.CancellationToken);

        // SGIOC042: Open generic registrations whose open registration is not registered, since their closed types call the factory
        AnalyzeFactoryOpenRegistrations(context.ReportDiagnostic, analyzerContext, context.CancellationToken);

        // SGIOC001: Implementation types that cannot be constructed without a Factory or Instance
        AnalyzeUnconstructibleImplementations(context.ReportDiagnostic, analyzerContext, context.CancellationToken);

        // ConstructedByFunc includes every pair of Resolved
        var asyncInitOnlyServiceTypes = BuildAsyncInitOnlyServiceTypes(analyzerContext);
        if(asyncInitOnlyServiceTypes.ConstructedByFunc.Count == 0)
            return;

        foreach(var serviceInfo in analyzerContext.RegisteredServices.Values)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            AnalyzeSyncDependenciesOnAsyncInitServices(
                context.ReportDiagnostic,
                serviceInfo.Constructor,
                serviceInfo.InjectedMembers,
                asyncInitOnlyServiceTypes,
                context.CancellationToken);

            // Like the generator, the members listed in a registration's InjectMembers are injected like its [IocInject] members
            if(!analyzerContext.InjectMembers.IsEmpty && serviceInfo.HasInjection)
            {
                AnalyzeListedInjectMembersSyncDependencies(
                    context.ReportDiagnostic,
                    serviceInfo,
                    analyzerContext,
                    asyncInitOnlyServiceTypes,
                    context.CancellationToken);
            }

            if(constructedByFunc.Count > 0)
            {
                AnalyzeFuncConstructedSyncDependencies(
                    context.ReportDiagnostic,
                    serviceInfo,
                    constructedByFunc,
                    analyzerContext,
                    asyncInitOnlyServiceTypes,
                    context.CancellationToken);
            }
        }

        // Decorators are not registered services, but the generator resolves their dependencies like a service's.
        // The parameter that receives the decorated instance depends on the decorated service type, so an applied decorator is
        // analyzed once per such parameter (and per closed form of an open generic decorator), and a dependency reported for
        // several of them is reported once.
        var reportedLocations = new HashSet<Location>();
        foreach(var applied in analyzerContext.AppliedDecorators)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var decorator = applied.Key;
            var constructor = decorator.GetSelectedConstructor(analyzerContext.Assembly);
            ImmutableArray<(ISymbol Member, AttributeData InjectAttribute)> injectedMembers = [.. decorator.GetInjectedMembers()];
            foreach(var decoratedParameterIndex in applied.Value.Keys)
            {
                AnalyzeSyncDependenciesOnAsyncInitServices(
                    diagnostic =>
                    {
                        if(reportedLocations.Add(diagnostic.Location))
                            context.ReportDiagnostic(diagnostic);
                    },
                    constructor,
                    injectedMembers,
                    asyncInitOnlyServiceTypes,
                    context.CancellationToken,
                    decoratedParameterIndex);
            }
        }
    }

    /// <summary>
    /// Reports SGIOC035 for a decorator that no constructor parameter lets decorate a service type it implements, and SGIOC034
    /// for each parameter of the decorated type after the first one, which is resolved as a dependency.
    /// </summary>
    private static void AnalyzeDecoratedParameters(
        Action<Diagnostic> reportDiagnostic,
        AnalyzerContext analyzerContext,
        CancellationToken cancellationToken)
    {
        foreach(var usages in analyzerContext.DecoratorUsages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var decorator = usages.Key;
            var constructor = decorator.GetSelectedConstructor(analyzerContext.Assembly);
            var decoratorName = decorator.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            foreach(var usage in usages.Value)
            {
                var parameterIndexes = usage.Value;
                var serviceTypeName = usage.Key.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                if(parameterIndexes.IsEmpty)
                {
                    reportDiagnostic(Diagnostic.Create(
                        NoDecoratedParameter,
                        GetSourceLocation(constructor) ?? GetSourceLocation(decorator),
                        decoratorName,
                        serviceTypeName));
                    continue;
                }

                if(constructor is null)
                    continue;

                var decoratedParameter = constructor.Parameters[parameterIndexes[0]];
                for(var i = 1; i < parameterIndexes.Length; i++)
                {
                    var parameter = constructor.Parameters[parameterIndexes[i]];
                    reportDiagnostic(Diagnostic.Create(
                        MultipleDecoratedParameters,
                        GetSourceLocation(parameter),
                        parameter.Name,
                        decoratorName,
                        serviceTypeName,
                        decoratedParameter.Name));
                }
            }
        }

        static Location? GetSourceLocation(ISymbol? symbol) =>
            symbol?.Locations.FirstOrDefault(static location => location.IsInSource);
    }

    /// <summary>
    /// The (service type, key) pairs that a synchronous dependency cannot resolve (SGIOC030).
    /// </summary>
    /// <param name="Resolved">The pairs whose registrations are all async-init.</param>
    /// <param name="ConstructedByFunc">
    /// The pairs that no registration lets a <c>Func</c> with input parameters construct: each is async-init, or an
    /// <c>Instance</c> registration (never async-init) whose <c>Func</c> would decorate a new instance with a decorator that
    /// has async inject methods. Includes every pair of <paramref name="Resolved"/>.
    /// </param>
    private readonly record struct AsyncInitServiceTypes(
        ImmutableHashSet<ServiceSymbolKey> Resolved,
        ImmutableHashSet<ServiceSymbolKey> ConstructedByFunc);

    private static AsyncInitServiceTypes BuildAsyncInitOnlyServiceTypes(AnalyzerContext analyzerContext)
    {
        // Keyed by runtime type, so registrations whose tuple element names differ are the same service
        var serviceTypeStates = new Dictionary<ServiceSymbolKey, (bool HasAsync, bool HasSync, bool HasFuncAsync, bool HasFuncSync)>();

        // Like the generator, an Instance is registered as is, without calling its inject methods, so it is never async-init.
        // Decided per registration: the same implementation type can be an Instance under one key and constructed under another
        Dictionary<INamedTypeSymbol, HashSet<ServiceSymbolKey>>? instanceRegistrations = null;
        foreach(var registration in analyzerContext.Registrations)
        {
            if(!registration.HasInstance)
                continue;

            instanceRegistrations ??= new(RuntimeTypeSymbolComparer.Instance);
            if(!instanceRegistrations.TryGetValue(registration.Implementation, out var instanceServiceTypes))
            {
                instanceServiceTypes = [];
                instanceRegistrations[registration.Implementation] = instanceServiceTypes;
            }

            instanceServiceTypes.UnionWith(registration.Registrations);
        }

        foreach(var serviceInfo in analyzerContext.RegisteredServices.Values)
        {
            var isAsyncInit = AnalyzerHelpers.IsAsyncInitImplementation(serviceInfo.Type, analyzerContext.Features);
            HashSet<ServiceSymbolKey>? instanceServiceTypes = null;
            instanceRegistrations?.TryGetValue(serviceInfo.Type, out instanceServiceTypes);

            // Use pre-computed AllRegistrations (populated at collection time from attribute data) when
            // available. This correctly handles assembly-level registrations where multiple IocRegisterFor
            // attributes target the same impl type with different keys — the second and subsequent
            // registrations are not stored in RegisteredServices but are recorded in AllRegistrations.
            IEnumerable<ServiceSymbolKey> serviceTypes =
                serviceInfo.AllRegistrations.IsEmpty
                    ? CollectRegisteredServiceTypesForAnalysis(serviceInfo, analyzerContext.AttributeSymbols)
                    : serviceInfo.AllRegistrations;

            foreach(var serviceType in serviceTypes)
            {
                serviceTypeStates.TryGetValue(serviceType, out var state);
                var isInstance = instanceServiceTypes?.Contains(serviceType) == true;
                var isAsyncDecorated = serviceInfo.AsyncDecoratedRegistrations.Contains(serviceType);

                // The async decorators of an Instance registration only keep a Func with input parameters from constructing it
                var isAsyncInitRegistration = !isInstance && (isAsyncInit || isAsyncDecorated);
                var isFuncAsyncInitRegistration = isAsyncInitRegistration || (isInstance && isAsyncDecorated);
                serviceTypeStates[serviceType] = (
                    HasAsync: state.HasAsync || isAsyncInitRegistration,
                    HasSync: state.HasSync || !isAsyncInitRegistration,
                    HasFuncAsync: state.HasFuncAsync || isFuncAsyncInitRegistration,
                    HasFuncSync: state.HasFuncSync || !isFuncAsyncInitRegistration);
            }
        }

        var asyncOnlyServiceTypes = ImmutableHashSet.CreateBuilder<ServiceSymbolKey>();
        var funcAsyncOnlyServiceTypes = ImmutableHashSet.CreateBuilder<ServiceSymbolKey>();
        foreach(var kvp in serviceTypeStates)
        {
            var serviceType = kvp.Key;
            var state = kvp.Value;
            if(state is { HasAsync: true, HasSync: false })
                asyncOnlyServiceTypes.Add(serviceType);

            if(state is { HasFuncAsync: true, HasFuncSync: false })
                funcAsyncOnlyServiceTypes.Add(serviceType);
        }

        return new(asyncOnlyServiceTypes.ToImmutable(), funcAsyncOnlyServiceTypes.ToImmutable());
    }

    private static HashSet<ServiceSymbolKey> CollectRegisteredServiceTypesForAnalysis(
        ServiceInfo serviceInfo,
        IocAttributeSymbols attributeSymbols)
    {
        var serviceTypes = new HashSet<ServiceSymbolKey>();
        var hasRegistrationAttribute = false;

        var implementationType = serviceInfo.Type;

        foreach(var attribute in implementationType.GetAttributes())
        {
            var attrClass = attribute.AttributeClass;
            if(attrClass is null || !AnalyzerHelpers.IsIoCRegistrationAttribute(attrClass, attributeSymbols))
                continue;

            hasRegistrationAttribute = true;
            var (serviceKey, _, _) = attribute.GetKeyInfo();
            foreach(var serviceType in AnalyzerHelpers.EnumerateRegisteredServiceTypes(implementationType, attribute))
            {
                serviceTypes.Add(new(serviceType, serviceKey));
            }
        }

        if(!hasRegistrationAttribute)
        {
            foreach(var serviceType in AnalyzerHelpers.EnumerateImplicitServiceTypes(implementationType))
            {
                serviceTypes.Add(new(serviceType, serviceInfo.ServiceKey));
            }
        }

        return serviceTypes;
    }

    /// <summary>
    /// SGIOC030 for the registrations of a type that a requested <c>Func</c> with input parameters constructs (see
    /// <see cref="CollectRegistrationsConstructedByFunc"/>), also with an Instance or a Factory: like the generator's
    /// <c>WriteFuncFactoryBody</c>, the <c>Func</c> resolves synchronously the parameters of the selected constructor, the
    /// injected properties and fields, and the parameters of the inject methods that do not return <c>Task</c>, except those
    /// it passes one of its arguments to. The members are those each registration injects: its <c>[IocInject]</c> members and
    /// the members listed in its <c>InjectMembers</c> (see <see cref="GetFuncInjectedMembers"/>). Only what the other
    /// registrations of the type do not already analyze is analyzed: the constructor when none is constructed, the
    /// <c>[IocInject]</c> members when none is injected, and the listed members when the registration is not injected.
    /// </summary>
    private static void AnalyzeFuncConstructedSyncDependencies(
        Action<Diagnostic> reportDiagnostic,
        ServiceInfo serviceInfo,
        Dictionary<ServiceActivation, List<INamedTypeSymbol>> constructedByFunc,
        AnalyzerContext analyzerContext,
        AsyncInitServiceTypes asyncInitOnlyServiceTypes,
        CancellationToken cancellationToken)
    {
        var hasListedMembers = !analyzerContext.InjectMembers.IsEmpty;
        if(serviceInfo.Constructor is not null && serviceInfo.HasInjection && !hasListedMembers)
            return;

        IMethodSymbol? constructor = null;
        ImmutableArray<(ISymbol Member, AttributeData InjectAttribute)> injectedMembers = default;
        ImmutableArray<(ISymbol Member, AttributeData InjectAttribute)> funcInjectedMembers = default;
        ImmutableArray<ISymbol> unlistedMembers = default;
        HashSet<Location>? reportedLocations = null;
        foreach(var activation in serviceInfo.Activations)
        {
            if(!constructedByFunc.TryGetValue(activation, out var funcTypes))
                continue;

            // The listed members of an injected registration are analyzed with it, all resolved
            var listedMembers = GetListedInjectMembers(analyzerContext, activation);
            var analyzedListedMembers = activation.IsInjected ? [] : listedMembers;
            if(serviceInfo.Constructor is not null && serviceInfo.HasInjection && analyzedListedMembers.IsEmpty)
                continue;

            if(constructor is null)
            {
                constructor = serviceInfo.Constructor ?? serviceInfo.Type.GetSelectedConstructor(analyzerContext.Assembly);
                injectedMembers = serviceInfo.HasInjection ? serviceInfo.InjectedMembers : [.. serviceInfo.Type.GetInjectedMembers()];

                // The Func never calls a Task-returning inject method, whatever the features
                funcInjectedMembers = serviceInfo.HasInjection
                    ? []
                    : [.. injectedMembers.Where(static injected => injected.Member is not IMethodSymbol method || !AnalyzerHelpers.IsNonGenericTaskType(method.ReturnType))];
                unlistedMembers = GetFuncInjectedMembers(injectedMembers, []);
                reportedLocations = [];
            }

            // Each registration's Funcs pass their arguments to its own members; a dependency is reported once
            var members = listedMembers.IsEmpty ? unlistedMembers : GetFuncInjectedMembers(injectedMembers, listedMembers);
            Func<ISymbol, bool> isResolved = target => IsResolvedByAnyFunc(funcTypes, target, constructor, members);
            void Report(Diagnostic diagnostic)
            {
                if(reportedLocations!.Add(diagnostic.Location))
                    reportDiagnostic(diagnostic);
            }

            AnalyzeSyncDependenciesOnAsyncInitServices(
                Report,
                serviceInfo.Constructor is null ? constructor : null,
                funcInjectedMembers,
                asyncInitOnlyServiceTypes,
                cancellationToken,
                isResolved: isResolved);

            AnalyzeListedMembersSyncDependencies(Report, analyzedListedMembers, injectedMembers, analyzerContext, asyncInitOnlyServiceTypes, cancellationToken, isResolved);
        }
    }

    /// <summary>
    /// SGIOC030 for the members listed in the <c>InjectMembers</c> of the registrations of a type that the container injects
    /// (without an Instance), which it injects like their <c>[IocInject]</c> members. A dependency is reported once.
    /// </summary>
    private static void AnalyzeListedInjectMembersSyncDependencies(
        Action<Diagnostic> reportDiagnostic,
        ServiceInfo serviceInfo,
        AnalyzerContext analyzerContext,
        AsyncInitServiceTypes asyncInitOnlyServiceTypes,
        CancellationToken cancellationToken)
    {
        HashSet<Location>? reportedLocations = null;
        foreach(var activation in serviceInfo.Activations)
        {
            if(!activation.IsInjected)
                continue;

            var listedMembers = GetListedInjectMembers(analyzerContext, activation);
            if(listedMembers.IsEmpty)
                continue;

            reportedLocations ??= [];
            AnalyzeListedMembersSyncDependencies(
                diagnostic =>
                {
                    if(reportedLocations.Add(diagnostic.Location))
                        reportDiagnostic(diagnostic);
                },
                listedMembers,
                serviceInfo.InjectedMembers,
                analyzerContext,
                asyncInitOnlyServiceTypes,
                cancellationToken,
                isResolved: null);
        }
    }

    /// <summary>
    /// SGIOC030 for the members listed in a registration's <c>InjectMembers</c> that are not <c>[IocInject]</c> members of the
    /// same name, which the generator injects with the key of their element (<c>{ nameof(X), key }</c>), otherwise without a key,
    /// reported at their element in <c>InjectMembers</c>.
    /// </summary>
    /// <param name="isResolved">
    /// For a <c>Func</c> with input parameters, gets whether a parameter, or a property or field, is resolved instead of
    /// receiving one of its arguments; the <c>Func</c> never calls a Task-returning method. <see langword="null"/> when the
    /// container injects the members.
    /// </param>
    private static void AnalyzeListedMembersSyncDependencies(
        Action<Diagnostic> reportDiagnostic,
        ImmutableArray<(ISymbol Member, Location Location)> listedMembers,
        ImmutableArray<(ISymbol Member, AttributeData InjectAttribute)> injectedMembers,
        AnalyzerContext analyzerContext,
        AsyncInitServiceTypes asyncInitOnlyServiceTypes,
        CancellationToken cancellationToken,
        Func<ISymbol, bool>? isResolved)
    {
        foreach(var (member, location) in listedMembers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if(IsInjectedMemberName(member, injectedMembers))
                continue;

            var key = location.SourceTree is { } tree && analyzerContext.InjectMemberKeys.TryGetValue((tree, location.SourceSpan), out var elementKey)
                ? elementKey
                : null;

            switch(member)
            {
                case IPropertySymbol property when isResolved?.Invoke(property) != false:
                    AnalyzeDependencyOnAsyncInitService(reportDiagnostic, property.Name, property.Type, location, key, asyncInitOnlyServiceTypes);
                    break;

                case IFieldSymbol field when isResolved?.Invoke(field) != false:
                    AnalyzeDependencyOnAsyncInitService(reportDiagnostic, field.Name, field.Type, location, key, asyncInitOnlyServiceTypes);
                    break;

                case IMethodSymbol method when isResolved is null || !AnalyzerHelpers.IsNonGenericTaskType(method.ReturnType):
                    AnalyzeParameterDependenciesOnAsyncInitServices(
                        reportDiagnostic,
                        method.Parameters,
                        asyncInitOnlyServiceTypes,
                        cancellationToken,
                        methodKey: key,
                        isResolved: isResolved);
                    break;
            }
        }
    }

    /// <param name="decoratedParameterIndex">
    /// For a decorator, the index of the constructor parameter that receives the decorated instance instead of a resolved
    /// service, which is not analyzed; -1 otherwise.
    /// </param>
    /// <param name="isResolved">
    /// Gets whether a parameter, or an injected property or field, is resolved, otherwise it is not analyzed; all are resolved
    /// when <see langword="null"/>.
    /// </param>
    private static void AnalyzeSyncDependenciesOnAsyncInitServices(
        Action<Diagnostic> reportDiagnostic,
        IMethodSymbol? constructor,
        ImmutableArray<(ISymbol Member, AttributeData InjectAttribute)> injectedMembers,
        AsyncInitServiceTypes asyncInitOnlyServiceTypes,
        CancellationToken cancellationToken,
        int decoratedParameterIndex = -1,
        Func<ISymbol, bool>? isResolved = null)
    {
        if(constructor is not null)
        {
            AnalyzeParameterDependenciesOnAsyncInitServices(
                reportDiagnostic,
                constructor.Parameters,
                asyncInitOnlyServiceTypes,
                cancellationToken,
                decoratedParameterIndex,
                isResolved: isResolved);
        }

        foreach(var (member, injectAttribute) in injectedMembers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if(member is not IMethodSymbol && isResolved?.Invoke(member) == false)
                continue;

            switch(member)
            {
                case IPropertySymbol property:
                    AnalyzeDependencyOnAsyncInitService(
                        reportDiagnostic,
                        property.Name,
                        property.Type,
                        injectAttribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation()
                            ?? property.Locations.FirstOrDefault(),
                        injectAttribute.GetKeyInfo().Key,
                        asyncInitOnlyServiceTypes);
                    break;

                case IFieldSymbol field:
                    AnalyzeDependencyOnAsyncInitService(
                        reportDiagnostic,
                        field.Name,
                        field.Type,
                        injectAttribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation()
                            ?? field.Locations.FirstOrDefault(),
                        injectAttribute.GetKeyInfo().Key,
                        asyncInitOnlyServiceTypes);
                    break;

                case IMethodSymbol method:
                    // An unkeyed parameter of a keyed inject method resolves with the method's key
                    AnalyzeParameterDependenciesOnAsyncInitServices(
                        reportDiagnostic,
                        method.Parameters,
                        asyncInitOnlyServiceTypes,
                        cancellationToken,
                        methodKey: injectAttribute.GetKeyInfo().Key,
                        isResolved: isResolved);
                    break;
            }
        }
    }

    /// <param name="methodKey">The key of an injected method, which applies to parameters without their own key.</param>
    /// <param name="isResolved">Gets whether a parameter is resolved, otherwise it is not analyzed; all are when <see langword="null"/>.</param>
    private static void AnalyzeParameterDependenciesOnAsyncInitServices(
        Action<Diagnostic> reportDiagnostic,
        ImmutableArray<IParameterSymbol> parameters,
        AsyncInitServiceTypes asyncInitOnlyServiceTypes,
        CancellationToken cancellationToken,
        int decoratedParameterIndex = -1,
        string? methodKey = null,
        Func<ISymbol, bool>? isResolved = null)
    {
        for(var i = 0; i < parameters.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var parameter = parameters[i];
            if(i == decoratedParameterIndex || ShouldSkipAsyncInitDependencyCheck(parameter) || isResolved?.Invoke(parameter) == false)
                continue;

            AnalyzeDependencyOnAsyncInitService(
                reportDiagnostic,
                parameter.Name,
                parameter.Type,
                parameter.Locations.FirstOrDefault(),
                parameter.GetServiceKeyAndAttributeInfo().ServiceKey ?? methodKey,
                asyncInitOnlyServiceTypes);
        }
    }

    private static void AnalyzeDependencyOnAsyncInitService(
        Action<Diagnostic> reportDiagnostic,
        string memberName,
        ITypeSymbol dependencyType,
        Location? location,
        string? serviceKey,
        AsyncInitServiceTypes asyncInitOnlyServiceTypes)
    {
        // Lazy<T> and Func<..., T> create T synchronously, so they cannot resolve an async-init service either. A Func with input
        // parameters constructs T, so it cannot construct the Instance registrations whose decorators have async inject methods
        var constructedByFunc = false;
        while(dependencyType.TryGetWrapperInfo(out var wrapperInfo)
            && wrapperInfo is { Kind: WrapperKind.Lazy or WrapperKind.Func, ElementType: { } elementType })
        {
            constructedByFunc |= TryGetFuncWithArgumentsReturnType(dependencyType, out _, out _);
            dependencyType = elementType;
        }

        if(AnalyzerHelpers.TryGetAsyncWrapperElementType(dependencyType) is not null)
            return;

        if(dependencyType.WithNullableAnnotation(NullableAnnotation.NotAnnotated) is not INamedTypeSymbol namedDependencyType)
            return;

        var asyncInitServiceTypes = constructedByFunc ? asyncInitOnlyServiceTypes.ConstructedByFunc : asyncInitOnlyServiceTypes.Resolved;
        if(!asyncInitServiceTypes.Contains(new(namedDependencyType, serviceKey)))
            return;

        reportDiagnostic(Diagnostic.Create(
            SyncDependencyOnAsyncInitService,
            location,
            memberName,
            namedDependencyType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }

    private static bool ShouldSkipAsyncInitDependencyCheck(IParameterSymbol parameter)
    {
        if(parameter.HasExplicitDefaultValue)
            return true;

        if(AnalyzerHelpers.IsWellKnownServiceType(parameter.Type))
            return true;

        return parameter.GetAttributes().Any(static attr => AnalyzerHelpers.IsServiceKeyAttribute(attr.AttributeClass));
    }

    private static IocFeatures ParseIocFeatures(AnalyzerOptions options)
    {
        options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(Constants.SourceGenIocFeaturesProperty, out var rawFeatures);
        return IocFeaturesHelper.Parse(rawFeatures);
    }
}

