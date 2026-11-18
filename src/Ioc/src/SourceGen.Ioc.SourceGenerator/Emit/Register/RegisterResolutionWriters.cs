using static SourceGen.Ioc.Constants;

namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Resolves a property or field injection value and emits its variable declaration.
    /// </summary>
    private static void ResolveMemberValue(
        SourceWriter writer,
        TypeData? memberType,
        string memberTypeName,
        string paramVar,
        string? serviceKey,
        bool isNullable,
        bool hasNonNullDefault,
        string? defaultValue,
        RegisterWriteContext context)
    {
        if(memberType is CollectionWrapperTypeData)
        {
            WriteCollectionResolution(writer, memberType, paramVar, serviceKey, isNullable, context);
            return;
        }

        if(memberType is LazyTypeData or FuncTypeData or DictionaryTypeData or KeyValuePairTypeData or TaskTypeData)
        {
            WriteWrapperResolution(writer, memberType, paramVar, serviceKey, isNullable, context);
            return;
        }

        // The service provider itself is passed directly, like an IServiceProvider parameter
        if(memberTypeName is IServiceProviderTypeName or IServiceProviderGlobalTypeName)
        {
            writer.WriteLine($"var {paramVar} = sp;");
            return;
        }

        if(hasNonNullDefault && defaultValue is not null)
        {
            writer.WriteLine($"var {paramVar} = {BuildOptionalServiceCallWithFallback(memberTypeName, serviceKey, defaultValue)};");
            return;
        }

        var resolutionMethod = GetServiceResolutionMethod(serviceKey, isOptional: isNullable);
        var call = BuildServiceCall(resolutionMethod, memberTypeName, serviceKey);
        writer.WriteLine($"var {paramVar} = {call};");
    }

    /// <summary>
    /// Resolves a parameter of a keyed method and emits its variable declaration.
    /// The key of the method applies when the parameter has no key of its own.
    /// </summary>
    private static string? ResolveMethodParameterWithKey(
        SourceWriter writer,
        ParameterData param,
        string paramVar,
        string methodKey,
        bool isKeyedRegistration,
        string? registrationKey,
        Func<TypeData, string>? typeNameResolver,
        RegisterWriteContext context)
    {
        var key = param.ServiceKey ?? methodKey;

        if(TryResolveCommonParameter(writer, param, paramVar, isKeyedRegistration, registrationKey, key, isOptional: false, typeNameResolver, context, out var resolvedVar))
        {
            return resolvedVar!;
        }

        var resolvedTypeName = typeNameResolver is not null ? typeNameResolver(param.Type) : param.Type.Name;
        if(param.HasDefaultValue && param.GetFallbackValue(resolvedTypeName) is { } fallbackValue)
        {
            writer.WriteLine($"var {paramVar} = {BuildOptionalServiceCallWithFallback(resolvedTypeName, key, fallbackValue)};");
            return paramVar;
        }

        var methodName = GetServiceResolutionMethod(key, isOptional: param.HasDefaultValue);
        writer.WriteLine($"var {paramVar} = {BuildServiceCall(methodName, resolvedTypeName, key)}{(param.HasDefaultValue ? " ?? default" : "")};");
        return paramVar;
    }

    /// <summary>
    /// Writes parameter resolution code for [ServiceKey] attribute.
    /// When the service is registered as keyed, injects the registration key with appropriate type casting.
    /// When the service is not keyed, injects null.
    /// </summary>
    private static string WriteServiceKeyParameterResolution(
        SourceWriter writer,
        string paramVar,
        string paramTypeName,
        bool isKeyedRegistration,
        string? registrationKey)
    {
        if(isKeyedRegistration && registrationKey is not null)
        {
            writer.WriteLine($"var {paramVar} = {registrationKey};");
        }
        else
        {
            writer.WriteLine($"var {paramVar} = default({paramTypeName});");
        }

        return paramVar;
    }

    /// <summary>
    /// Resolve a parameter and emit its variable declaration.
    /// Produces lines like:
    /// var p = sp.GetService<T>() ?? <default>;
    /// var p = sp.GetKeyedService<T>(key) ?? <default>;
    /// or returns "sp" for IServiceProvider parameters (no var emitted).
    /// </summary>
    private static string ResolveParamAndEmitVar(
        SourceWriter writer,
        ParameterData param,
        string paramVar,
        bool isKeyedRegistration,
        string? registrationKey,
        RegisterWriteContext context)
    {
        var paramTypeName = param.Type.Name;

        if(TryResolveCommonParameter(writer, param, paramVar, isKeyedRegistration, registrationKey, param.ServiceKey, param.IsOptional, typeNameResolver: null, context, out var resolvedVar))
        {
            return resolvedVar!;
        }

        var isOptional = param.HasDefaultValue || param.IsOptional;
        if(isOptional && param.GetFallbackValue(paramTypeName) is { } fallbackValue)
        {
            writer.WriteLine($"var {paramVar} = {BuildOptionalServiceCallWithFallback(paramTypeName, param.ServiceKey, fallbackValue)};");
            return paramVar;
        }

        var methodName = GetServiceResolutionMethod(param.ServiceKey, isOptional);
        writer.WriteLine($"var {paramVar} = {BuildServiceCall(methodName, paramTypeName, param.ServiceKey)}{(isOptional ? " ?? default" : "")};");
        return paramVar;
    }

    /// <summary>
    /// Writes collection resolution code for constructor parameters and injection members.
    /// Handles all collection types including IEnumerable&lt;T&gt;, IList&lt;T&gt;, T[], IReadOnlyList&lt;T&gt;, etc.
    /// A collection type registered explicitly resolves that registration (see <see cref="BuildExplicitRegistrationCall"/>).
    /// </summary>
    private static void WriteCollectionResolution(
        SourceWriter writer,
        TypeData type,
        string paramVar,
        string? serviceKey,
        bool isOptional,
        RegisterWriteContext context)
    {
        var expression = type is CollectionWrapperTypeData collectionType
            ? BuildCollectionExpression(collectionType, serviceKey, isOptional, context)
            : BuildServiceCall(GetServiceResolutionMethod(serviceKey, isOptional), type.Name, serviceKey);
        writer.WriteLine($"var {paramVar} = {expression};");
    }

    /// <summary>
    /// Builds a collection expression: <c>GetServices&lt;T&gt;()</c> for <c>IEnumerable&lt;T&gt;</c>, followed by
    /// <c>.ToArray()</c> for the other collection types, or the explicit registration of the collection type.
    /// </summary>
    private static string BuildCollectionExpression(CollectionWrapperTypeData type, string? serviceKey, bool isOptional, RegisterWriteContext context)
    {
        if(BuildExplicitRegistrationCall(type, serviceKey, isOptional, context) is { } explicitCall)
        {
            return explicitCall;
        }

        var getServicesCall = BuildServiceCall(serviceKey is not null ? GetKeyedServices : GetServices, type.ElementType.Name, serviceKey);
        return type is EnumerableTypeData
            ? getServicesCall
            : WithExplicitRegistrationFallback(type, serviceKey, context, $"{getServicesCall}.ToArray()");
    }

    /// <summary>
    /// Writes wrapper type resolution code for Lazy&lt;T&gt;, Func&lt;T&gt;, IDictionary&lt;TKey, TValue&gt;,
    /// and KeyValuePair&lt;TKey, TValue&gt;. Supports nested wrapper types (e.g., Lazy&lt;KeyValuePair&lt;K, V&gt;&gt;).
    /// </summary>
    private static void WriteWrapperResolution(
        SourceWriter writer,
        TypeData type,
        string paramVar,
        string? serviceKey,
        bool isOptional,
        RegisterWriteContext context)
    {
        var expr = BuildWrapperExpression(type, serviceKey, isOptional, context);
        writer.WriteLine($"var {paramVar} = {expr};");
    }

    /// <summary>
    /// Builds an inline wrapper expression. Recursively handles nested wrappers.
    /// A wrapper type registered explicitly resolves that registration (see <see cref="BuildExplicitRegistrationCall"/>).
    /// </summary>
    private static string BuildWrapperExpression(TypeData type, string? serviceKey, bool isOptional, RegisterWriteContext context)
    {
        if(BuildExplicitRegistrationCall(type, serviceKey, isOptional, context) is { } explicitCall)
        {
            return explicitCall;
        }

        switch(type)
        {
            case LazyTypeData lazy:
            {
                var innerType = lazy.InstanceType;
                if(innerType is not WrapperTypeData)
                {
                    var methodName = GetServiceResolutionMethod(serviceKey, isOptional);
                    return BuildServiceCall(methodName, type.Name, serviceKey);
                }

                var lazyInnerExpr = BuildInnerResolutionExpression(innerType, serviceKey, isOptional, context);
                return WithExplicitRegistrationFallback(type, serviceKey, context,
                    $"new global::System.Lazy<{innerType.Name}>(() => {lazyInnerExpr}, global::System.Threading.LazyThreadSafetyMode.ExecutionAndPublication)");
            }

            case FuncTypeData func:
            {
                var innerType = func.ReturnType;
                if(func.HasInputParameters || innerType is not WrapperTypeData)
                {
                    var methodName = GetServiceResolutionMethod(serviceKey, isOptional);
                    return BuildServiceCall(methodName, type.Name, serviceKey);
                }

                var funcInnerExpr = BuildInnerResolutionExpression(innerType, serviceKey, isOptional, context);
                return WithExplicitRegistrationFallback(type, serviceKey, context, $"new global::System.Func<{innerType.Name}>(() => {funcInnerExpr})");
            }

            case KeyValuePairTypeData kvp:
            {
                // A non-keyed registration has the default key, null for reference type keys
                var keyExpr = serviceKey ?? "default!";
                var valueExpr = BuildInnerResolutionExpression(kvp.ValueType, serviceKey, isOptional, context);
                return $"new global::System.Collections.Generic.KeyValuePair<{kvp.KeyType.Name}, {kvp.ValueType.Name}>({keyExpr}, {valueExpr})";
            }

            case DictionaryTypeData dict:
            {
                var kvpTypeName = $"global::System.Collections.Generic.KeyValuePair<{dict.KeyType.Name}, {dict.ValueType.Name}>";
                var getServicesCall = BuildServiceCall(serviceKey is not null ? GetKeyedServices : GetServices, kvpTypeName, serviceKey);
                return WithExplicitRegistrationFallback(type, serviceKey, context, $"{getServicesCall}.ToDictionary()");
            }

            case TaskTypeData task:
            {
                var innerTypeName = task.InnerType.Name;
                if(context.AsyncInitServices?.Contains(new(innerTypeName, serviceKey)) == true)
                {
                    var methodName = GetServiceResolutionMethod(serviceKey, isOptional);
                    return BuildServiceCall(methodName, type.Name, serviceKey);
                }

                var syncMethodName = GetServiceResolutionMethod(serviceKey, isOptional);
                var syncCall = BuildServiceCall(syncMethodName, innerTypeName, serviceKey);
                var fromResult = $"global::System.Threading.Tasks.Task.FromResult({syncCall})";

                // An async-init registration registered with the consumer only for some passed tags: Task<T> when registered
                return context.AsyncInitFallbackServices?.Contains(new(innerTypeName, serviceKey)) == true
                    ? $"{BuildServiceCall(serviceKey is not null ? GetKeyedService : GetService, type.Name, serviceKey)} ?? {fromResult}"
                    : fromResult;
            }

            default:
            {
                var methodName = GetServiceResolutionMethod(serviceKey, isOptional);
                return BuildServiceCall(methodName, type.Name, serviceKey);
            }
        }
    }

    /// <summary>
    /// Builds an inner resolution expression — either a nested wrapper expression, a collection
    /// expression, or a direct service call. Supports nesting such as Lazy&lt;IEnumerable&lt;T&gt;&gt;.
    /// </summary>
    private static string BuildInnerResolutionExpression(TypeData innerType, string? serviceKey, bool isOptional, RegisterWriteContext context)
    {
        if(innerType is LazyTypeData or FuncTypeData or KeyValuePairTypeData or DictionaryTypeData or TaskTypeData)
        {
            return BuildWrapperExpression(innerType, serviceKey, isOptional, context);
        }

        if(innerType is CollectionWrapperTypeData collectionInner)
        {
            return BuildCollectionExpression(collectionInner, serviceKey, isOptional, context);
        }

        var methodName = GetServiceResolutionMethod(serviceKey, isOptional);
        return BuildServiceCall(methodName, innerType.Name, serviceKey);
    }

    /// <summary>
    /// Builds the call resolving the explicit registration of a wrapper or collection type (e.g. <c>Lazy&lt;T&gt;</c> or
    /// <c>IReadOnlyDictionary&lt;K, V&gt;</c> registered as a service type) that this compilation registers whenever the
    /// consumer is, or returns <see langword="null"/> when the wrapper is built instead. Like <c>MS.DI</c> resolving a
    /// constructor parameter, the injected value is then the one <c>GetService</c> returns.
    /// </summary>
    private static string? BuildExplicitRegistrationCall(TypeData type, string? serviceKey, bool isOptional, RegisterWriteContext context) =>
        context.IsRegisteredExplicitly(type.Name, serviceKey)
            ? BuildServiceCall(GetServiceResolutionMethod(serviceKey, isOptional), type.Name, serviceKey)
            : null;

    /// <summary>
    /// Wraps <paramref name="builtExpression"/> as <c>sp.GetService&lt;T&gt;() ?? built</c> when an explicit registration of the
    /// wrapper or collection type is registered with the consumer only for some passed tags: the dependency then resolves the
    /// explicit registration when it is registered, like <c>GetService</c>. The generated code never registers these types
    /// (direct <c>Lazy&lt;T&gt;</c> / <c>Func&lt;T&gt;</c> are resolved from <c>MS.DI</c> instead of built), so a registration
    /// found at runtime is an explicit one.
    /// </summary>
    private static string WithExplicitRegistrationFallback(TypeData type, string? serviceKey, RegisterWriteContext context, string builtExpression) =>
        context.MayBeRegisteredExplicitly(type.Name, serviceKey)
            ? $"{BuildServiceCall(serviceKey is not null ? GetKeyedService : GetService, type.Name, serviceKey)} ?? {builtExpression}"
            : builtExpression;

    /// <summary>
    /// Builds a service resolution call for keyed or non-keyed services.
    /// </summary>
    private static string BuildServiceCall(string methodName, string typeName, string? serviceKey) =>
        serviceKey is not null
            ? $"sp.{methodName}<{typeName}>({serviceKey})"
            : $"sp.{methodName}<{typeName}>()";

    /// <summary>
    /// Builds <c>sp.GetService(typeof(T)) switch { T service => service, _ => fallback }</c>, which returns
    /// <paramref name="fallbackValue"/> for an unregistered service. Unlike <c>sp.GetService&lt;T&gt;() ?? fallback</c>, it
    /// compiles for value types, which GetService&lt;T&gt; returns as <c>default</c>; the fallback is typed by the
    /// <c>T</c> arm, so target-typed initializers such as <c>new()</c> and <c>[]</c> create a <c>T</c>.
    /// </summary>
    private static string BuildOptionalServiceCallWithFallback(string typeName, string? serviceKey, string fallbackValue) =>
        serviceKey is not null
            ? $"sp.{GetKeyedService}(typeof({typeName}), {serviceKey}) switch {{ {GetPatternTypeName(typeName)} service => service, _ => {fallbackValue} }}"
            : $"sp.{GetService}(typeof({typeName})) switch {{ {GetPatternTypeName(typeName)} service => service, _ => {fallbackValue} }}";

    /// <summary>
    /// Attempts to resolve common parameter cases and emit any required variable declarations.
    /// Returns true when a resolution was produced.
    /// </summary>
    private static bool TryResolveCommonParameter(
        SourceWriter writer,
        ParameterData param,
        string paramVar,
        bool isKeyedRegistration,
        string? registrationKey,
        string? serviceKey,
        bool isOptional,
        Func<TypeData, string>? typeNameResolver,
        RegisterWriteContext context,
        out string? resolvedVar)
    {
        if(IsServiceProviderType(param.Type.Name))
        {
            resolvedVar = "sp";
            return true;
        }

        var resolvedTypeName = typeNameResolver is not null ? typeNameResolver(param.Type) : param.Type.Name;
        if(param.HasServiceKeyAttribute)
        {
            resolvedVar = WriteServiceKeyParameterResolution(writer, paramVar, resolvedTypeName, isKeyedRegistration, registrationKey);
            return true;
        }

        if(param.Type is CollectionWrapperTypeData)
        {
            WriteCollectionResolution(writer, param.Type, paramVar, serviceKey, isOptional, context);
            resolvedVar = paramVar;
            return true;
        }

        if(param.Type is LazyTypeData or FuncTypeData or DictionaryTypeData or KeyValuePairTypeData or TaskTypeData)
        {
            WriteWrapperResolution(writer, param.Type, paramVar, serviceKey, isOptional, context);
            resolvedVar = paramVar;
            return true;
        }

        resolvedVar = null;
        return false;
    }

    /// <summary>
    /// Checks if the type name represents System.IServiceProvider.
    /// </summary>
    private static bool IsServiceProviderType(string typeName) =>
        typeName is IServiceProviderGlobalTypeName or IServiceProviderTypeName or "IServiceProvider";
}
