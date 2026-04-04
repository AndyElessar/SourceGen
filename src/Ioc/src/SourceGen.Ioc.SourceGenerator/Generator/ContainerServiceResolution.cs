namespace SourceGen.Ioc;

partial class IocSourceGenerator
{

    /// <summary>
    /// Builds a factory method call string for container.
    /// </summary>
    private static string BuildFactoryCallForContainer(FactoryMethodData factory, ServiceRegistrationModel reg, ContainerRegistrationGroups groups)
    {
        var args = GetFactoryArguments(factory, reg, groups);
        var genericTypeArgs = BuildGenericFactoryTypeArgs(factory, reg.ServiceType);
        var factoryCallPath = genericTypeArgs is not null ? $"{factory.Path}<{genericTypeArgs}>" : factory.Path;

        return $"{factoryCallPath}({string.Join(", ", args)})";
    }

    /// <summary>
    /// Yields factory method arguments without allocating a List.
    /// </summary>
    private static IEnumerable<string> GetFactoryArguments(FactoryMethodData factory, ServiceRegistrationModel reg, ContainerRegistrationGroups groups)
    {
        if(factory.HasServiceProvider)
            yield return "this";

        if(factory.HasKey && reg.Key is not null)
            yield return reg.Key;

        foreach(var param in factory.AdditionalParameters)
            yield return BuildParameterForContainer(param, reg, groups);
    }

    private static string BuildServiceProviderFallbackExpression(
        string typeName,
        string? key,
        bool isOptional)
    {
        if(key is not null)
            return isOptional
                ? $"GetKeyedService(typeof({typeName}), {key}) as {typeName}"
                : $"({typeName})GetRequiredKeyedService(typeof({typeName}), {key})";
        return isOptional
            ? $"GetService(typeof({typeName})) as {typeName}"
            : $"({typeName})GetRequiredService(typeof({typeName}))";
    }

    /// <summary>
    /// Builds a service resolution call for container (direct call or GetService/GetRequiredService).
    /// When the dependency is registered in the same container, calls the resolver method directly.
    /// </summary>
    private static string BuildServiceResolutionCallForContainer(
        TypeData type,
        string? key,
        bool isOptional,
        ContainerRegistrationGroups groups)
    {
        // Collection types - use collection resolver method if available
        if(type is CollectionWrapperTypeData collectionType)
        {
            var elementTypeName = collectionType.ElementType.Name;

            // Keyed collection services - fallback to GetKeyedServices (no direct call support yet)
            if(key is not null)
            {
                return $"GetKeyedServices<{elementTypeName}>({key})";
            }

            // Check if element type is KeyValuePair<K,V> — use KVP resolver if available
            if(collectionType.ElementType is KeyValuePairTypeData kvpElement)
            {
                var kvpKeyType = kvpElement.KeyType.Name;
                var kvpValueType = kvpElement.ValueType.Name;
                if(HasKvpRegistrations(kvpKeyType, kvpValueType, groups))
                {
                    // IEnumerable, IReadOnlyCollection, ICollection → Dictionary resolver
                    // IReadOnlyList, IList, T[] → Array resolver (consistent with _localResolvers)
                    var isArrayType = collectionType.WrapperKind is WrapperKind.ReadOnlyList or WrapperKind.List or WrapperKind.Array;
                    var methodName = isArrayType
                        ? GetKvpArrayResolverMethodName(kvpKeyType, kvpValueType)
                        : GetKvpDictionaryResolverMethodName(kvpKeyType, kvpValueType);
                    return $"{methodName}()";
                }
            }

            // Check if we have a collection resolver for this element type
            if(groups.CollectionRegistrations.ContainsKey(elementTypeName))
            {
                var methodName = GetArrayResolverMethodName(elementTypeName);
                return $"{methodName}()";
            }

            return $"GetServices<{elementTypeName}>()";
        }

        // Wrapper types - Lazy<T>, Func<T>, KeyValuePair<K,V>, Task<T>
        if(type is LazyTypeData or FuncTypeData or KeyValuePairTypeData or DictionaryTypeData or TaskTypeData)
        {
            return BuildWrapperExpressionForContainer(type, key, isOptional, groups);
        }

        // Try to find direct resolver in this container
        if(groups.ByServiceTypeAndKey.TryGetValue((type.Name, key), out var registrations))
        {
            var cached = registrations[^1]; // Last wins
            // Async-init services: the sync method was not generated; use the async method instead.
            // Callers that depend on an async-init service should be taking Task<T>, not T directly.
            // The analyzer (SGIOC027/029) normally prevents this, but fall back gracefully.
            if(cached.IsAsyncInit)
            {
                if(cached.Registration.Lifetime == ServiceLifetime.Transient)
                    return $"{GetAsyncCreateMethodName(cached.ResolverMethodName)}()";
                return $"{GetAsyncResolverMethodName(cached.ResolverMethodName)}()";
            }
            return $"{cached.ResolverMethodName}()";
        }

        // Fallback to GetService/GetRequiredService for dependencies not in this container
        return BuildServiceProviderFallbackExpression(type.Name, key, isOptional);
    }

    /// <summary>
    /// Builds a wrapper expression for container resolution (Lazy, Func, KeyValuePair, Dictionary).
    /// Recursively handles nested wrapper types.
    /// </summary>
    private static string BuildWrapperExpressionForContainer(
        TypeData type,
        string? key,
        bool isOptional,
        ContainerRegistrationGroups groups,
        bool useResolverMethods = true)
    {
        switch(type)
        {
            case LazyTypeData lazy:
            {
                var innerType = lazy.InstanceType;
                // Direct Lazy<T> where T is not a wrapper — call wrapper resolver if available (only at top level)
                if(innerType is not WrapperTypeData && useResolverMethods)
                {
                    if(groups.ByServiceTypeAndKey.TryGetValue((innerType.Name, key), out var innerRegs))
                    {
                        var lastReg = innerRegs[^1];
                        var safeInnerType = GetSafeIdentifier(innerType.Name);
                        var safeImplType = GetSafeIdentifier(lastReg.Registration.ImplementationType.Name);
                        return $"_lazy_{safeInnerType}_{safeImplType}";
                    }
                    // Fallback: inner type not in this container — build inline via IServiceProvider
                    var lazyFallbackExpr = BuildServiceProviderFallbackExpression(innerType.Name, key, isOptional);
                    return $"new global::System.Lazy<{innerType.Name}>(() => {lazyFallbackExpr}, global::System.Threading.LazyThreadSafetyMode.ExecutionAndPublication)";
                }
                // Nested wrapper or inside nested context — inline construction
                var lazyInnerExpr = BuildInnerResolutionForContainer(innerType, key, isOptional, groups);
                return $"new global::System.Lazy<{innerType.Name}>(() => {lazyInnerExpr}, global::System.Threading.LazyThreadSafetyMode.ExecutionAndPublication)";
            }

            case FuncTypeData func:
            {
                var innerType = func.ReturnType;

                if(func.HasInputParameters)
                {
                    if(groups.ByServiceTypeAndKey.TryGetValue((innerType.Name, key), out var innerRegs))
                    {
                        var targetRegistration = innerRegs[^1].Registration;
                        return BuildContainerMultiParamFuncExpression(func, targetRegistration, groups);
                    }

                    // Fallback: inner return type not in this container — resolve the full Func<...> type
                    // directly from IServiceProvider. Do NOT call BuildServiceResolutionCallForContainer
                    // here as that would route FuncTypeData back to BuildWrapperExpressionForContainer,
                    // causing infinite recursion.
                    return BuildServiceProviderFallbackExpression(type.Name, key, isOptional);
                }

                // Direct Func<T> where T is not a wrapper — call wrapper resolver if available (only at top level)
                if(innerType is not WrapperTypeData && useResolverMethods)
                {
                    if(groups.ByServiceTypeAndKey.TryGetValue((innerType.Name, key), out var innerRegs))
                    {
                        var lastReg = innerRegs[^1];
                        var safeInnerType = GetSafeIdentifier(innerType.Name);
                        var safeImplType = GetSafeIdentifier(lastReg.Registration.ImplementationType.Name);
                        return $"_func_{safeInnerType}_{safeImplType}";
                    }
                    // Fallback: inner type not in this container — build inline via IServiceProvider
                    var funcFallbackExpr = BuildServiceProviderFallbackExpression(innerType.Name, key, isOptional);
                    return $"new global::System.Func<{innerType.Name}>(() => {funcFallbackExpr})";
                }
                // Nested wrapper or inside nested context — inline construction
                var funcInnerExpr = BuildInnerResolutionForContainer(innerType, key, isOptional, groups);
                return $"new global::System.Func<{innerType.Name}>(() => {funcInnerExpr})";
            }

            case KeyValuePairTypeData kvp:
            {
                var keyType = kvp.KeyType;
                var valueType = kvp.ValueType;
                var keyExpr = key ?? "default";
                var valueExpr = BuildInnerResolutionForContainer(valueType, key, isOptional, groups);
                return $"new global::System.Collections.Generic.KeyValuePair<{keyType.Name}, {valueType.Name}>({keyExpr}, {valueExpr})";
            }

            case DictionaryTypeData dict:
            {
                // Dictionary resolution: use KVP dictionary resolver if available, otherwise fallback.
                var keyType = dict.KeyType;
                var valueType = dict.ValueType;
                if(key is null && HasKvpRegistrations(keyType.Name, valueType.Name, groups))
                {
                    var methodName = GetKvpDictionaryResolverMethodName(keyType.Name, valueType.Name);
                    return $"{methodName}()";
                }
                var kvpTypeName = $"global::System.Collections.Generic.KeyValuePair<{keyType.Name}, {valueType.Name}>";
                if(key is not null)
                {
                    return $"GetKeyedServices<{kvpTypeName}>({key}).ToDictionary()";
                }
                return $"GetServices<{kvpTypeName}>().ToDictionary()";
            }

            case TaskTypeData task:
            {
                // Task<T> wrapper: route based on sync vs async-init registration.
                var innerType = task.InnerType;
                var innerTypeName = innerType.Name;

                if(groups.ByServiceTypeAndKey.TryGetValue((innerTypeName, key), out var innerRegs))
                {
                    var lastReg = innerRegs[^1];
                    if(lastReg.IsAsyncInit)
                    {
                        // Async-init: project Task<ImplType> → Task<ServiceType> via async lambda (not ContinueWith)
                        // so that exceptions propagate as-awaited rather than wrapped in AggregateException.
                        var asyncMethodName = GetAsyncResolverMethodName(lastReg.ResolverMethodName);
                        return $"((global::System.Func<global::System.Threading.Tasks.Task<{innerTypeName}>>)(async () => ({innerTypeName})(await {asyncMethodName}())))()";
                    }
                    else
                    {
                        // Sync-only: wrap in Task.FromResult with cast.
                        return $"global::System.Threading.Tasks.Task.FromResult(({innerTypeName}){lastReg.ResolverMethodName}())";
                    }
                }

                // Fallback to IServiceProvider
                return BuildServiceProviderFallbackExpression(type.Name, key, isOptional);
            }

            default:
                return BuildServiceResolutionCallForContainer(type, key, isOptional, groups);
        }
    }

    /// <summary>
    /// Builds an inner resolution expression for container — handles nested wrappers, nested
    /// collections (via <see cref="BuildServiceResolutionCallForContainer"/>), or direct resolution.
    /// Supports nesting such as Lazy&lt;IEnumerable&lt;T&gt;&gt;.
    /// </summary>
    private static string BuildInnerResolutionForContainer(
        TypeData innerType,
        string? key,
        bool isOptional,
        ContainerRegistrationGroups groups)
    {
        if(innerType is LazyTypeData or FuncTypeData or KeyValuePairTypeData or DictionaryTypeData or TaskTypeData)
        {
            // Inner wrappers always use inline construction (no resolver methods). This is a deliberate
            // pragmatic choice: direct container resolution of nested wrappers (for example,
            // container.GetService<Lazy<Func<T>>>()) is extremely rare, while constructor injection is
            // fully covered by inline construction. This keeps implementation simple by avoiding
            // extensions to field scanning, naming conventions, and scoped-container infrastructure for
            // every nested wrapper shape. Nested wrappers also typically do not require cross-consumer
            // instance sharing, so each consumer owning its own inline-constructed instance is
            // semantically correct.
            // NOTE: Nested Task<T> shapes such as Lazy<Task<T>> or IEnumerable<Task<T>> are not
            // supported by the spec. The transform layer prevents these from reaching code generation
            // by downgrading their WrapperKind to None, so they fall back to IServiceProvider.
            return BuildWrapperExpressionForContainer(innerType, key, isOptional, groups, useResolverMethods: false);
        }

        // Delegates to BuildServiceResolutionCallForContainer which handles:
        // - Collection types (EnumerableTypeData, ReadOnlyCollectionTypeData, CollectionTypeData, ReadOnlyListTypeData, ListTypeData, ArrayTypeData) via GetServices/array resolvers
        // - Direct service resolution via resolver methods or GetRequiredService fallback
        return BuildServiceResolutionCallForContainer(innerType, key, isOptional, groups);
    }

    /// <summary>
    /// Builds an inline multi-parameter Func expression for container resolution.
    /// Uses first-unused type matching from Func input args to constructor/method/property injection targets.
    /// </summary>
    private static string BuildContainerMultiParamFuncExpression(
        FuncTypeData funcType,
        ServiceRegistrationModel registration,
        ContainerRegistrationGroups groups)
    {
        var inputTypes = funcType.InputTypes;
        var inputArgNames = new string[inputTypes.Length];
        var inputArgTypeNames = new string[inputTypes.Length];
        var inputArgUsed = new bool[inputTypes.Length];

        for(var i = 0; i < inputTypes.Length; i++)
        {
            inputArgNames[i] = $"arg{i}";
            inputArgTypeNames[i] = inputTypes[i].Type.Name;
            inputArgUsed[i] = false;
        }

        var lambdaParams = string.Join(", ", inputTypes.Select(static (t, i) => $"{t.Type.Name} arg{i}"));

        var statements = new List<string>();
        var ctorParams = registration.ImplementationType.ConstructorParameters ?? [];
        var ctorEntries = new List<(string Name, string? Value, bool NeedsConditional)>(ctorParams.Length);

        var resolvedParamIndex = 0;
        foreach(var param in ctorParams)
        {
            var matchedArg = TryConsumeMatchingFuncInputArg(param.Type.Name, inputArgNames, inputArgTypeNames, inputArgUsed);
            if(matchedArg is not null)
            {
                ctorEntries.Add((param.Name, matchedArg, false));
                continue;
            }

            var paramVar = $"p{resolvedParamIndex}";
            var expr = BuildServiceResolutionCallForContainer(param.Type, param.ServiceKey, param.IsOptional, groups);
            statements.Add($"var {paramVar} = {expr};");
            ctorEntries.Add((param.Name, paramVar, false));
            resolvedParamIndex++;
        }

        var propertyInits = new List<string>();
        var propertyIndex = 0;
        foreach(var member in registration.InjectionMembers)
        {
            if(member.MemberType is not (InjectionMemberType.Property or InjectionMemberType.Field))
                continue;

            var memberType = member.Type;
            if(memberType is null)
                continue;

            var matchedArg = TryConsumeMatchingFuncInputArg(memberType.Name, inputArgNames, inputArgTypeNames, inputArgUsed);
            if(matchedArg is not null)
            {
                propertyInits.Add($"{member.Name} = {matchedArg}");
                continue;
            }

            var memberVar = $"s0_p{propertyIndex}";
            var expr = BuildServiceResolutionCallForContainer(memberType, member.Key, member.IsNullable, groups);
            statements.Add($"var {memberVar} = {expr};");
            propertyInits.Add($"{member.Name} = {memberVar}");
            propertyIndex++;
        }

        var ctorArgs = BuildArgumentListFromEntries([.. ctorEntries]);
        var initializerPart = propertyInits.Count > 0 ? $" {{ {string.Join(", ", propertyInits)} }}" : string.Empty;
        var ctorInvocation = BuildConstructorInvocation(registration.ImplementationType.Name, ctorArgs, initializerPart);
        statements.Add($"var s0 = {ctorInvocation};");

        var methodIndex = 0;
        foreach(var method in registration.InjectionMembers)
        {
            if(method.MemberType != InjectionMemberType.Method)
                continue;

            var methodParams = method.Parameters ?? [];
            var methodEntries = new List<(string Name, string? Value, bool NeedsConditional)>(methodParams.Length);
            foreach(var param in methodParams)
            {
                var matchedArg = TryConsumeMatchingFuncInputArg(param.Type.Name, inputArgNames, inputArgTypeNames, inputArgUsed);
                if(matchedArg is not null)
                {
                    methodEntries.Add((param.Name, matchedArg, false));
                    continue;
                }

                var paramVar = $"s0_m{methodIndex}";
                var expr = BuildServiceResolutionCallForContainer(param.Type, method.Key ?? param.ServiceKey, param.IsOptional, groups);
                statements.Add($"var {paramVar} = {expr};");
                methodEntries.Add((param.Name, paramVar, false));
                methodIndex++;
            }

            var methodArgs = BuildArgumentListFromEntries([.. methodEntries]);
            statements.Add($"s0.{method.Name}({methodArgs});");
        }

        statements.Add("return s0;");

        return $"new {funcType.Name}(({lambdaParams}) => {{ {string.Join(" ", statements)} }})";
    }
}
