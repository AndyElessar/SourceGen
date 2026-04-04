using static SourceGen.Ioc.SourceGenerator.Models.Constants;

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
        ImmutableEquatableSet<string>? asyncInitServiceTypeNames = null)
    {
        if(memberType is CollectionWrapperTypeData)
        {
            WriteCollectionResolution(writer, memberType, paramVar, serviceKey, isOptional: isNullable);
            return;
        }

        if(memberType is LazyTypeData or FuncTypeData or DictionaryTypeData or KeyValuePairTypeData or TaskTypeData)
        {
            WriteWrapperResolution(writer, memberType, paramVar, serviceKey, isOptional: isNullable, asyncInitServiceTypeNames);
            return;
        }

        if(hasNonNullDefault)
        {
            var defExpr = defaultValue ?? "default";
            var methodName = GetServiceResolutionMethod(serviceKey, isOptional: true);
            var svcCall = BuildServiceCall(methodName, memberTypeName, serviceKey);
            writer.WriteLine($"var {paramVar} = {svcCall} ?? {defExpr};");
            return;
        }

        var resolutionMethod = GetServiceResolutionMethod(serviceKey, isOptional: isNullable);
        var call = BuildServiceCall(resolutionMethod, memberTypeName, serviceKey);
        writer.WriteLine($"var {paramVar} = {call};");
    }

    /// <summary>
    /// Resolves a keyed method parameter and emits its variable declaration.
    /// </summary>
    private static string? ResolveMethodParameterWithKey(
        SourceWriter writer,
        ParameterData param,
        string paramVar,
        string methodKey,
        bool isKeyedRegistration,
        string? registrationKey,
        Func<TypeData, string>? typeNameResolver,
        ImmutableEquatableSet<string>? asyncInitServiceTypeNames = null)
    {
        if(TryResolveCommonParameter(writer, param, paramVar, isKeyedRegistration, registrationKey, methodKey, isOptional: false, typeNameResolver, asyncInitServiceTypeNames, out var resolvedVar))
        {
            return resolvedVar!;
        }

        var resolvedTypeName = typeNameResolver is not null ? typeNameResolver(param.Type) : param.Type.Name;
        if(param.HasDefaultValue)
        {
            var defExpr = param.DefaultValue ?? "default";
            var optionalCall = BuildServiceCall(GetServiceResolutionMethod(methodKey, isOptional: true), resolvedTypeName, methodKey);
            writer.WriteLine($"var {paramVar} = {optionalCall} ?? {defExpr};");
            return paramVar;
        }

        var requiredCall = BuildServiceCall(GetServiceResolutionMethod(methodKey, isOptional: false), resolvedTypeName, methodKey);
        writer.WriteLine($"var {paramVar} = {requiredCall};");
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
        string? registrationKey = null,
        ImmutableEquatableSet<string>? asyncInitServiceTypeNames = null)
    {
        var paramTypeName = param.Type.Name;

        if(TryResolveCommonParameter(writer, param, paramVar, isKeyedRegistration, registrationKey, param.ServiceKey, param.IsOptional, typeNameResolver: null, asyncInitServiceTypeNames, out var resolvedVar))
        {
            return resolvedVar!;
        }

        var isOptional = param.HasDefaultValue || param.IsOptional;
        var methodName = GetServiceResolutionMethod(param.ServiceKey, isOptional);
        var svcCall = BuildServiceCall(methodName, paramTypeName, param.ServiceKey);

        if(isOptional)
        {
            var defExpr = param.HasDefaultValue ? (param.DefaultValue ?? "default") : "default";
            writer.WriteLine($"var {paramVar} = {svcCall} ?? {defExpr};");
            return paramVar;
        }

        writer.WriteLine($"var {paramVar} = {svcCall};");
        return paramVar;
    }

    /// <summary>
    /// Writes collection resolution code for constructor parameters and injection members.
    /// Handles all collection types including IEnumerable&lt;T&gt;, IList&lt;T&gt;, T[], IReadOnlyList&lt;T&gt;, etc.
    /// </summary>
    private static void WriteCollectionResolution(
        SourceWriter writer,
        TypeData type,
        string paramVar,
        string? serviceKey,
        bool isOptional = false)
    {
        var servicesMethod = serviceKey is not null ? GetKeyedServices : GetServices;

        switch(type)
        {
            case EnumerableTypeData enumerable:
            {
                var call = BuildServiceCall(servicesMethod, enumerable.ElementType.Name, serviceKey);
                writer.WriteLine($"var {paramVar} = {call};");
                return;
            }

            case ReadOnlyCollectionTypeData readOnlyCollection:
            {
                var call = BuildServiceCall(servicesMethod, readOnlyCollection.ElementType.Name, serviceKey);
                writer.WriteLine($"var {paramVar} = {call}.ToArray();");
                return;
            }

            case CollectionTypeData collection:
            {
                var call = BuildServiceCall(servicesMethod, collection.ElementType.Name, serviceKey);
                writer.WriteLine($"var {paramVar} = {call}.ToArray();");
                return;
            }

            case ReadOnlyListTypeData readOnlyList:
            {
                var call = BuildServiceCall(servicesMethod, readOnlyList.ElementType.Name, serviceKey);
                writer.WriteLine($"var {paramVar} = {call}.ToArray();");
                return;
            }

            case ListTypeData list:
            {
                var call = BuildServiceCall(servicesMethod, list.ElementType.Name, serviceKey);
                writer.WriteLine($"var {paramVar} = {call}.ToArray();");
                return;
            }

            case ArrayTypeData array:
            {
                var call = BuildServiceCall(servicesMethod, array.ElementType.Name, serviceKey);
                writer.WriteLine($"var {paramVar} = {call}.ToArray();");
                return;
            }

            default:
            {
                var methodName = GetServiceResolutionMethod(serviceKey, isOptional);
                var call = BuildServiceCall(methodName, type.Name, serviceKey);
                writer.WriteLine($"var {paramVar} = {call};");
                return;
            }
        }
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
        bool isOptional = false,
        ImmutableEquatableSet<string>? asyncInitServiceTypeNames = null)
    {
        var expr = BuildWrapperExpression(type, serviceKey, isOptional, asyncInitServiceTypeNames);
        writer.WriteLine($"var {paramVar} = {expr};");
    }

    /// <summary>
    /// Builds an inline wrapper expression. Recursively handles nested wrappers.
    /// </summary>
    private static string BuildWrapperExpression(TypeData type, string? serviceKey, bool isOptional, ImmutableEquatableSet<string>? asyncInitServiceTypeNames = null)
    {
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

                var lazyInnerExpr = BuildInnerResolutionExpression(innerType, serviceKey, isOptional, asyncInitServiceTypeNames);
                return $"new global::System.Lazy<{innerType.Name}>(() => {lazyInnerExpr}, global::System.Threading.LazyThreadSafetyMode.ExecutionAndPublication)";
            }

            case FuncTypeData func:
            {
                var innerType = func.ReturnType;
                if(func.HasInputParameters || innerType is not WrapperTypeData)
                {
                    var methodName = GetServiceResolutionMethod(serviceKey, isOptional);
                    return BuildServiceCall(methodName, type.Name, serviceKey);
                }

                var funcInnerExpr = BuildInnerResolutionExpression(innerType, serviceKey, isOptional, asyncInitServiceTypeNames);
                return $"new global::System.Func<{innerType.Name}>(() => {funcInnerExpr})";
            }

            case KeyValuePairTypeData kvp:
            {
                var keyExpr = serviceKey ?? "default";
                var valueExpr = BuildInnerResolutionExpression(kvp.ValueType, serviceKey, isOptional, asyncInitServiceTypeNames);
                return $"new global::System.Collections.Generic.KeyValuePair<{kvp.KeyType.Name}, {kvp.ValueType.Name}>({keyExpr}, {valueExpr})";
            }

            case DictionaryTypeData dict:
            {
                var kvpTypeName = $"global::System.Collections.Generic.KeyValuePair<{dict.KeyType.Name}, {dict.ValueType.Name}>";
                var getServicesCall = BuildServiceCall(serviceKey is not null ? GetKeyedServices : GetServices, kvpTypeName, serviceKey);
                return $"{getServicesCall}.ToDictionary()";
            }

            case TaskTypeData task:
            {
                var innerTypeName = task.InnerType.Name;
                if(asyncInitServiceTypeNames?.Contains(innerTypeName) == true)
                {
                    var methodName = GetServiceResolutionMethod(serviceKey, isOptional);
                    return BuildServiceCall(methodName, type.Name, serviceKey);
                }

                var syncMethodName = GetServiceResolutionMethod(serviceKey, isOptional);
                var syncCall = BuildServiceCall(syncMethodName, innerTypeName, serviceKey);
                return $"global::System.Threading.Tasks.Task.FromResult({syncCall})";
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
    private static string BuildInnerResolutionExpression(TypeData innerType, string? serviceKey, bool isOptional, ImmutableEquatableSet<string>? asyncInitServiceTypeNames = null)
    {
        if(innerType is LazyTypeData or FuncTypeData or KeyValuePairTypeData or DictionaryTypeData or TaskTypeData)
        {
            return BuildWrapperExpression(innerType, serviceKey, isOptional, asyncInitServiceTypeNames);
        }

        if(innerType is CollectionWrapperTypeData collectionInner)
        {
            var getServicesCall = BuildServiceCall(
                serviceKey is not null ? GetKeyedServices : GetServices,
                collectionInner.ElementType.Name,
                serviceKey);

            return innerType is CollectionWrapperTypeData and not EnumerableTypeData
                ? $"{getServicesCall}.ToArray()"
                : getServicesCall;
        }

        var methodName = GetServiceResolutionMethod(serviceKey, isOptional);
        return BuildServiceCall(methodName, innerType.Name, serviceKey);
    }

    /// <summary>
    /// Builds a service resolution call for keyed or non-keyed services.
    /// </summary>
    private static string BuildServiceCall(string methodName, string typeName, string? serviceKey) =>
        serviceKey is not null
            ? $"sp.{methodName}<{typeName}>({serviceKey})"
            : $"sp.{methodName}<{typeName}>()";

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
        ImmutableEquatableSet<string>? asyncInitServiceTypeNames,
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
            WriteCollectionResolution(writer, param.Type, paramVar, serviceKey, isOptional);
            resolvedVar = paramVar;
            return true;
        }

        if(param.Type is LazyTypeData or FuncTypeData or DictionaryTypeData or KeyValuePairTypeData or TaskTypeData)
        {
            WriteWrapperResolution(writer, param.Type, paramVar, serviceKey, isOptional, asyncInitServiceTypeNames);
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
