using static SourceGen.Ioc.Constants;

namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    private static ImmutableEquatableArray<ResolvedConstructorParameter> ResolveConstructorParametersForContainerEntryModel(
        ServiceRegistrationModel registration,
        ServiceLookup serviceLookup,
        IReadOnlyDictionary<string, ImmutableEquatableArray<ServiceLookupEntry>> collectionRegistrations,
        WrapperFieldLookup wrapperFields,
        bool allowServiceKeyAttribute)
    {
        var constructorParameters = registration.Factory?.AdditionalParameters ?? registration.ImplementationType.ConstructorParameters;
        if(constructorParameters is null or { Length: 0 })
            return [];

        var resolved = new List<ResolvedConstructorParameter>(constructorParameters.Length);

        foreach(var parameter in constructorParameters)
        {
            var dependency = ResolveParameterDependencyForContainerEntryModel(
                parameter,
                registration,
                serviceLookup,
                collectionRegistrations,
                wrapperFields,
                allowServiceKeyAttribute);

            resolved.Add(new ResolvedConstructorParameter(parameter, dependency, parameter.IsOptional));
        }

        return resolved.ToImmutableEquatableArray();
    }

    private static ImmutableEquatableArray<ResolvedInjectionMember> ResolveInjectionMembersForContainerEntryModel(
        ImmutableEquatableArray<InjectionMemberData> injectionMembers,
        ServiceRegistrationModel ownerRegistration,
        ServiceLookup serviceLookup,
        IReadOnlyDictionary<string, ImmutableEquatableArray<ServiceLookupEntry>> collectionRegistrations,
        WrapperFieldLookup wrapperFields,
        bool allowServiceKeyAttributeForMethods)
    {
        if(injectionMembers.Length == 0)
            return [];

        var resolvedMembers = new List<ResolvedInjectionMember>(injectionMembers.Length);

        foreach(var member in injectionMembers)
        {
            switch(member)
            {
                case PropertyOrFieldInjectionData { Type.Name: IServiceProviderTypeName or IServiceProviderGlobalTypeName } serviceProvider:
                    resolvedMembers.Add(new ResolvedPropertyOrFieldInjection(serviceProvider, new ServiceProviderSelfDependency()));
                    break;

                case PropertyOrFieldInjectionData propertyOrField:
                    // A member with a non-null initializer keeps it when the service is not registered, like the Register output
                    var hasNonNullDefault = propertyOrField.HasDefaultValue && !propertyOrField.DefaultValueIsNull;
                    var dependency = ResolveServiceDependencyForContainerEntryModel(
                        propertyOrField.Type,
                        propertyOrField.Key,
                        propertyOrField.IsNullable || hasNonNullDefault,
                        serviceLookup,
                        collectionRegistrations,
                        wrapperFields,
                        hasNonNullDefault ? propertyOrField.DefaultValue : null);
                    resolvedMembers.Add(new ResolvedPropertyOrFieldInjection(propertyOrField, dependency));
                    break;

                case MethodInjectionData method:
                {
                    ImmutableEquatableArray<ResolvedDependency> parameterDependencies = [];
                    if(method.Parameters.Length > 0)
                    {
                        var resolvedParameters = new List<ResolvedDependency>(method.Parameters.Length);

                        foreach(var parameter in method.Parameters)
                        {
                            resolvedParameters.Add(ResolveParameterDependencyForContainerEntryModel(
                                parameter,
                                ownerRegistration,
                                serviceLookup,
                                collectionRegistrations,
                                wrapperFields,
                                allowServiceKeyAttributeForMethods));
                        }

                        parameterDependencies = resolvedParameters.ToImmutableEquatableArray();
                    }

                    resolvedMembers.Add(new ResolvedMethodInjection(method, parameterDependencies));
                    break;
                }
            }
        }

        return resolvedMembers.ToImmutableEquatableArray();
    }

    private static ImmutableEquatableArray<ResolvedDecorator> ResolveDecoratorsForContainerEntryModel(
        ServiceRegistrationModel registration,
        ServiceLookup serviceLookup,
        IReadOnlyDictionary<string, ImmutableEquatableArray<ServiceLookupEntry>> collectionRegistrations,
        WrapperFieldLookup wrapperFields)
    {
        if(registration.Decorators.Length == 0)
            return [];

        var decorators = new List<ResolvedDecorator>(registration.Decorators.Length);

        foreach(var decoratorType in registration.Decorators)
        {
            var constructorParameters = decoratorType.ConstructorParameters;
            var resolvedParameters = new List<ResolvedConstructorParameter>(constructorParameters?.Length ?? 0);

            if(constructorParameters is { Length: > 1 })
            {
                for(var i = 1; i < constructorParameters.Length; i++)
                {
                    var parameter = constructorParameters[i];
                    var dependency = ResolveServiceDependencyForContainerEntryModel(
                        parameter.Type,
                        parameter.ServiceKey,
                        parameter.IsOptional,
                        serviceLookup,
                        collectionRegistrations,
                        wrapperFields,
                        parameter.GetFallbackValue(parameter.Type.Name));

                    resolvedParameters.Add(new ResolvedConstructorParameter(parameter, dependency, parameter.IsOptional));
                }
            }

            var decoratorInjectionMembers = decoratorType.InjectionMembers ?? [];
            var resolvedInjectionMembers = ResolveInjectionMembersForContainerEntryModel(
                decoratorInjectionMembers,
                registration,
                serviceLookup,
                collectionRegistrations,
                wrapperFields,
                allowServiceKeyAttributeForMethods: false);

            var decoratorRegistration = new ServiceRegistrationModel(
                registration.ServiceType,
                decoratorType,
                registration.Lifetime,
                registration.Key,
                registration.KeyType,
                registration.KeyValueType,
                decoratorType is GenericTypeData { IsOpenGeneric: true },
                [],
                decoratorInjectionMembers,
                Factory: null,
                Instance: null);

            decorators.Add(new ResolvedDecorator(
                decoratorRegistration,
                resolvedParameters.ToImmutableEquatableArray(),
                resolvedInjectionMembers));
        }

        return decorators.ToImmutableEquatableArray();
    }

    /// <summary>
    /// Field and collection-resolver lookups for the container's Lazy/Func wrapper entries, and the container's member names.
    /// </summary>
    /// <param name="LazyFieldByResolver">Lazy wrapper field name by inner resolver method name.</param>
    /// <param name="FuncFieldByResolver">Func wrapper field name by inner resolver method name.</param>
    /// <param name="LazyCollectionServiceTypes">Inner service types with a generated <c>Lazy&lt;T&gt;[]</c> resolver.</param>
    /// <param name="FuncCollectionServiceTypes">Inner service types with a generated <c>Func&lt;T&gt;[]</c> resolver.</param>
    /// <param name="Names">The container's member names for type names.</param>
    private sealed record class WrapperFieldLookup(
        IReadOnlyDictionary<string, string> LazyFieldByResolver,
        IReadOnlyDictionary<string, string> FuncFieldByResolver,
        HashSet<string> LazyCollectionServiceTypes,
        HashSet<string> FuncCollectionServiceTypes,
        ContainerNames Names);

    private static ResolvedDependency ResolveParameterDependencyForContainerEntryModel(
        ParameterData parameter,
        ServiceRegistrationModel ownerRegistration,
        ServiceLookup serviceLookup,
        IReadOnlyDictionary<string, ImmutableEquatableArray<ServiceLookupEntry>> collectionRegistrations,
        WrapperFieldLookup wrapperFields,
        bool allowServiceKeyAttribute)
    {
        if(allowServiceKeyAttribute && parameter.HasServiceKeyAttribute)
        {
            return new ServiceKeyLiteralDependency(parameter.Type.Name, ownerRegistration.Key ?? "null");
        }

        if(parameter.Type.Name is IServiceProviderTypeName or IServiceProviderGlobalTypeName)
        {
            return new ServiceProviderSelfDependency();
        }

        return ResolveServiceDependencyForContainerEntryModel(
            parameter.Type,
            parameter.ServiceKey,
            parameter.IsOptional,
            serviceLookup,
            collectionRegistrations,
            wrapperFields,
            parameter.GetFallbackValue(parameter.Type.Name));
    }

    /// <param name="fallbackValue">The value of an optional dependency the provider does not resolve, or <see langword="null"/> for <c>null</c>.</param>
    private static ResolvedDependency ResolveServiceDependencyForContainerEntryModel(
        TypeData type,
        string? key,
        bool isOptional,
        ServiceLookup serviceLookup,
        IReadOnlyDictionary<string, ImmutableEquatableArray<ServiceLookupEntry>> collectionRegistrations,
        WrapperFieldLookup wrapperFields,
        string? fallbackValue = null)
    {
        if(type is CollectionWrapperTypeData collectionType)
        {
            var elementTypeName = collectionType.ElementType.Name;

            if(key is not null)
            {
                return new CollectionFallbackDependency(elementTypeName, IsKeyed: true, Key: key);
            }

            if(collectionType.ElementType is KeyValuePairTypeData kvpElement
                && HasKvpRegistrationsForContainerEntryModel(kvpElement.KeyType.Name, kvpElement.ValueType.Name, serviceLookup))
            {
                var isArrayType = collectionType.WrapperKind is WrapperKind.ReadOnlyList or WrapperKind.List or WrapperKind.Array;
                return isArrayType
                    ? new KvpResolverDependency(wrapperFields.Names.KvpArrayResolver(kvpElement.KeyType.Name, kvpElement.ValueType.Name))
                    : new DictionaryResolverDependency(wrapperFields.Names.KvpDictionaryResolver(kvpElement.KeyType.Name, kvpElement.ValueType.Name));
            }

            if(collectionRegistrations.ContainsKey(elementTypeName))
            {
                return new CollectionDependency(wrapperFields.Names.ArrayResolver(elementTypeName));
            }

            // Collections of Lazy<T> / Func<T> call the wrapper array resolver directly instead of GetServices<Lazy<T>>()
            if(collectionType.ElementType is LazyTypeData lazyElement
                && wrapperFields.LazyCollectionServiceTypes.Contains(lazyElement.InstanceType.Name))
            {
                return new CollectionDependency(wrapperFields.Names.LazyArrayResolver(lazyElement.InstanceType.Name));
            }

            if(collectionType.ElementType is FuncTypeData { HasInputParameters: false } funcElement
                && wrapperFields.FuncCollectionServiceTypes.Contains(funcElement.ReturnType.Name))
            {
                return new CollectionDependency(wrapperFields.Names.FuncArrayResolver(funcElement.ReturnType.Name));
            }

            return new CollectionFallbackDependency(elementTypeName, IsKeyed: false, Key: null);
        }

        if(type is LazyTypeData or FuncTypeData or KeyValuePairTypeData or DictionaryTypeData or TaskTypeData)
        {
            return ResolveWrapperDependencyForContainerEntryModel(
                type,
                key,
                isOptional,
                serviceLookup,
                collectionRegistrations,
                wrapperFields,
                useResolverMethods: true);
        }

        if(serviceLookup.TryGetValue(new(type.Name, key), out var registrations))
        {
            var cached = registrations[^1];
            if(cached.IsAsyncInit)
            {
                return cached.Registration.Lifetime == ServiceLifetime.Transient
                    ? new DirectServiceDependency(GetAsyncCreateMethodName(cached.ResolverMethodName))
                    : new DirectServiceDependency(GetAsyncResolverMethodName(cached.ResolverMethodName));
            }

            return new DirectServiceDependency(cached.ResolverMethodName);
        }

        return new FallbackProviderDependency(type.Name, key, isOptional, fallbackValue);
    }

    private static ResolvedDependency ResolveWrapperDependencyForContainerEntryModel(
        TypeData type,
        string? key,
        bool isOptional,
        ServiceLookup serviceLookup,
        IReadOnlyDictionary<string, ImmutableEquatableArray<ServiceLookupEntry>> collectionRegistrations,
        WrapperFieldLookup wrapperFields,
        bool useResolverMethods)
    {
        switch(type)
        {
            case LazyTypeData lazy:
            {
                var innerType = lazy.InstanceType;

                if(innerType is not WrapperTypeData && useResolverMethods)
                {
                    if(serviceLookup.TryGetValue(new(innerType.Name, key), out var innerRegistrations))
                    {
                        var inner = innerRegistrations[^1];
                        if(wrapperFields.LazyFieldByResolver.TryGetValue(inner.ResolverMethodName, out var fieldName))
                        {
                            return new LazyFieldReferenceDependency(fieldName);
                        }

                        // Transient services have no shared Lazy field: each consumer gets its own Lazy, like the Register output
                        if(!inner.IsAsyncInit)
                        {
                            return new LazyInlineDependency(innerType.Name, new DirectServiceDependency(inner.ResolverMethodName));
                        }
                    }

                    return new LazyInlineDependency(
                        innerType.Name,
                        new FallbackProviderDependency(innerType.Name, key, isOptional));
                }

                return new LazyInlineDependency(
                    innerType.Name,
                    ResolveInnerDependencyForContainerEntryModel(
                        innerType,
                        key,
                        isOptional,
                        serviceLookup,
                        collectionRegistrations,
                        wrapperFields));
            }

            case FuncTypeData func:
            {
                var innerType = func.ReturnType;

                if(func.HasInputParameters)
                {
                    if(serviceLookup.TryGetValue(new(innerType.Name, key), out var innerRegistrations))
                    {
                        var targetRegistration = innerRegistrations[^1].Registration;
                        return new MultiParamFuncDependency(
                            innerType.Name,
                            CreateFuncInputParameters(func.InputTypes),
                            ResolveConstructorParametersForContainerEntryModel(
                                targetRegistration,
                                serviceLookup,
                                collectionRegistrations,
                                wrapperFields,
                                allowServiceKeyAttribute: true),
                            ResolveInjectionMembersForContainerEntryModel(
                                targetRegistration.InjectionMembers,
                                targetRegistration,
                                serviceLookup,
                                collectionRegistrations,
                                wrapperFields,
                                allowServiceKeyAttributeForMethods: true),
                            ResolveDecoratorsForContainerEntryModel(
                                targetRegistration,
                                serviceLookup,
                                collectionRegistrations,
                                wrapperFields),
                            targetRegistration.ImplementationType.Name,
                            targetRegistration.Key);
                    }

                    return new FallbackProviderDependency(type.Name, key, isOptional);
                }

                if(innerType is not WrapperTypeData && useResolverMethods)
                {
                    if(serviceLookup.TryGetValue(new(innerType.Name, key), out var innerRegistrations))
                    {
                        var resolverMethodName = innerRegistrations[^1].ResolverMethodName;
                        if(wrapperFields.FuncFieldByResolver.TryGetValue(resolverMethodName, out var fieldName))
                        {
                            return new FuncFieldReferenceDependency(fieldName);
                        }
                    }

                    return new FuncInlineDependency(
                        innerType.Name,
                        new FallbackProviderDependency(innerType.Name, key, isOptional));
                }

                return new FuncInlineDependency(
                    innerType.Name,
                    ResolveInnerDependencyForContainerEntryModel(
                        innerType,
                        key,
                        isOptional,
                        serviceLookup,
                        collectionRegistrations,
                        wrapperFields));
            }

            case KeyValuePairTypeData kvp:
                return new KvpInlineDependency(
                    kvp.KeyType.Name,
                    kvp.ValueType.Name,
                    key ?? "default!", // A non-keyed registration has the default key, null for reference type keys
                    ResolveInnerDependencyForContainerEntryModel(
                        kvp.ValueType,
                        key,
                        isOptional,
                        serviceLookup,
                        collectionRegistrations,
                        wrapperFields));

            case DictionaryTypeData dictionary:
            {
                if(key is null && HasKvpRegistrationsForContainerEntryModel(dictionary.KeyType.Name, dictionary.ValueType.Name, serviceLookup))
                {
                    return new DictionaryResolverDependency(wrapperFields.Names.KvpDictionaryResolver(dictionary.KeyType.Name, dictionary.ValueType.Name));
                }

                var kvpTypeName = $"global::System.Collections.Generic.KeyValuePair<{dictionary.KeyType.Name}, {dictionary.ValueType.Name}>";
                return new DictionaryFallbackDependency(kvpTypeName, IsKeyed: key is not null, Key: key);
            }

            case TaskTypeData task:
            {
                if(serviceLookup.TryGetValue(new(task.InnerType.Name, key), out var innerRegistrations))
                {
                    var cached = innerRegistrations[^1];
                    if(cached.IsAsyncInit)
                    {
                        // Transient async-init services only have a creation method (no cached resolver)
                        var asyncMethodName = cached.Registration.Lifetime == ServiceLifetime.Transient
                            ? GetAsyncCreateMethodName(cached.ResolverMethodName)
                            : GetAsyncResolverMethodName(cached.ResolverMethodName);
                        return new TaskAsyncDependency(asyncMethodName, task.InnerType.Name, GetResolvedTypeName(cached.Registration));
                    }

                    return new TaskFromResultDependency(new DirectServiceDependency(cached.ResolverMethodName), task.InnerType.Name);
                }

                return new FallbackProviderDependency(type.Name, key, isOptional);
            }

            default:
                return ResolveServiceDependencyForContainerEntryModel(
                    type,
                    key,
                    isOptional,
                    serviceLookup,
                    collectionRegistrations,
                    wrapperFields);
        }
    }

    private static ResolvedDependency ResolveInnerDependencyForContainerEntryModel(
        TypeData innerType,
        string? key,
        bool isOptional,
        ServiceLookup serviceLookup,
        IReadOnlyDictionary<string, ImmutableEquatableArray<ServiceLookupEntry>> collectionRegistrations,
        WrapperFieldLookup wrapperFields)
    {
        if(innerType is LazyTypeData or FuncTypeData or KeyValuePairTypeData or DictionaryTypeData or TaskTypeData)
        {
            return ResolveWrapperDependencyForContainerEntryModel(
                innerType,
                key,
                isOptional,
                serviceLookup,
                collectionRegistrations,
                wrapperFields,
                useResolverMethods: false);
        }

        return ResolveServiceDependencyForContainerEntryModel(
            innerType,
            key,
            isOptional,
            serviceLookup,
            collectionRegistrations,
            wrapperFields);
    }

    private static ImmutableEquatableArray<ParameterData> CreateFuncInputParameters(ImmutableEquatableArray<TypeParameter> inputTypes)
    {
        if(inputTypes.Length == 0)
            return [];

        var parameters = new List<ParameterData>(inputTypes.Length);
        for(var i = 0; i < inputTypes.Length; i++)
        {
            parameters.Add(new ParameterData($"arg{i}", inputTypes[i].Type));
        }

        return parameters.ToImmutableEquatableArray();
    }

    private static bool HasKvpRegistrationsForContainerEntryModel(
        string keyTypeName,
        string valueTypeName,
        ServiceLookup serviceLookup)
    {
        if(!serviceLookup.LastKeyedEntriesByServiceType.TryGetValue(valueTypeName, out var keyedEntries))
            return false;

        foreach(var cached in keyedEntries)
        {
            if(IsKeyTypeCompatible(keyTypeName, cached.Registration.KeyValueType))
                return true;
        }

        return false;
    }
}
