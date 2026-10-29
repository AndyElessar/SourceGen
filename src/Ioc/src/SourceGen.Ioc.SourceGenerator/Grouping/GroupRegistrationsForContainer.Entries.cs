namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    private static ServiceContainerEntry CloneServiceEntryWithRegistration(
        ServiceContainerEntry entry,
        ServiceRegistrationModel registration)
    {
        return entry with { Registration = registration };
    }

    /// <summary>
    /// Creates a ServiceLookupEntry with pre-computed field and method names.
    /// Computes both names in a single pass to avoid redundant string operations.
    /// </summary>
    private static ServiceLookupEntry CreateServiceLookupEntry(
        ServiceRegistrationModel reg,
        EagerResolveOptions eagerResolveOptions,
        ReservedMemberNames reservedNames,
        ContainerNames names)
    {
        var (fieldName, methodName) = ComputeServiceNames(reg, names);
        return CreateServiceLookupEntry(reg, methodName, fieldName, eagerResolveOptions, reservedNames);
    }

    private static ServiceLookupEntry CreateServiceLookupEntry(
        ServiceRegistrationModel reg,
        string methodName,
        string fieldName,
        EagerResolveOptions eagerResolveOptions,
        ReservedMemberNames reservedNames)
    {
        // Avoid naming conflicts with user-declared and generated infrastructure members
        if(reservedNames.IsReservedResolverName(methodName))
        {
            methodName = $"{methodName}_Resolve";
        }

        if(reservedNames.IsReservedFieldName(fieldName))
        {
            fieldName = $"{fieldName}_Resolve";
        }

        // Determine if this registration should be eagerly resolved via EagerResolveOptions.
        // Instance registrations are inherently eager (no field caching needed).
        // Transient services are not supported for eager resolution.
        var isAsyncInit = HasAsyncInitMembers(reg);
        var isEager = reg.Instance is null && reg.Lifetime switch
        {
            ServiceLifetime.Singleton => (eagerResolveOptions & EagerResolveOptions.Singleton) != 0,
            ServiceLifetime.Scoped => (eagerResolveOptions & EagerResolveOptions.Scoped) != 0,
            _ => false // Transient is never eager
        };

        return new ServiceLookupEntry(reg, methodName, fieldName, isAsyncInit, isEager);
    }

    /// <summary>
    /// Creates the entry resolving a decorated service type: it wraps the instance resolved by
    /// <paramref name="inner"/> (the undecorated implementation type entry) with the decorators.
    /// </summary>
    private static ServiceLookupEntry CreateDecoratedServiceLookupEntry(
        ServiceRegistrationModel reg,
        ServiceLookupEntry inner,
        EagerResolveOptions eagerResolveOptions,
        ReservedMemberNames reservedNames,
        ContainerNames names)
    {
        var safeServiceType = names.Get(reg.ServiceType.Name);
        var entry = CreateServiceLookupEntry(
            reg,
            $"{inner.ResolverMethodName}_As_{safeServiceType}",
            $"{inner.FieldName}_As_{safeServiceType}",
            eagerResolveOptions,
            reservedNames);

        // Transient async-init services only have a creation method (no cached resolver)
        var innerResolverMethodName = !inner.IsAsyncInit
            ? inner.ResolverMethodName
            : reg.Lifetime == ServiceLifetime.Transient
                ? GetAsyncCreateMethodName(inner.ResolverMethodName)
                : GetAsyncResolverMethodName(inner.ResolverMethodName);

        return entry with { InnerResolverMethodName = innerResolverMethodName };
    }

    /// <summary>
    /// Returns <see langword="true"/> when the registration has at least one
    /// <see cref="InjectionMemberType.AsyncMethod"/> member, making it an async-init service.
    /// </summary>
    private static bool HasAsyncInitMembers(ServiceRegistrationModel reg)
    {
        foreach(var m in reg.InjectionMembers)
        {
            if(m.MemberType == InjectionMemberType.AsyncMethod)
                return true;
        }
        return false;
    }

    private static ImmutableEquatableArray<ContainerEntry> CreateServiceContainerEntries(
        ImmutableEquatableArray<ServiceLookupEntry> registrations,
        ThreadSafeStrategy threadSafeStrategy,
        ServiceLookup serviceLookup,
        IReadOnlyDictionary<string, ImmutableEquatableArray<ServiceLookupEntry>> collectionRegistrations,
        WrapperFieldLookup wrapperFields)
    {
        var entries = new List<ContainerEntry>(registrations.Length);

        foreach(var cached in registrations)
        {
            var reg = cached.Registration;

            // A decorated service entry takes the instance from its inner entry, which does the construction and injection
            var isDecoratedEntry = cached.InnerResolverMethodName is not null;
            var constructorParameters = isDecoratedEntry ? [] : ResolveConstructorParametersForContainerEntryModel(
                reg,
                serviceLookup,
                collectionRegistrations,
                wrapperFields,
                allowServiceKeyAttribute: true);
            var injectionMembers = isDecoratedEntry ? [] : ResolveInjectionMembersForContainerEntryModel(
                reg.InjectionMembers,
                reg,
                serviceLookup,
                collectionRegistrations,
                wrapperFields,
                allowServiceKeyAttributeForMethods: true);
            var decorators = ResolveDecoratorsForContainerEntryModel(
                reg,
                serviceLookup,
                collectionRegistrations,
                wrapperFields);

            var entry = CreateServiceContainerEntryModel(
                cached,
                threadSafeStrategy,
                constructorParameters,
                injectionMembers,
                decorators);

            if(isDecoratedEntry && entry is ServiceContainerEntry serviceEntry)
            {
                entry = serviceEntry with { InnerResolverMethodName = cached.InnerResolverMethodName };
            }

            entries.Add(entry);
        }

        return entries.ToImmutableEquatableArray();
    }

    private static ContainerEntry CreateServiceContainerEntryModel(
        ServiceLookupEntry cached,
        ThreadSafeStrategy threadSafeStrategy,
        ImmutableEquatableArray<ResolvedConstructorParameter> constructorParameters,
        ImmutableEquatableArray<ResolvedInjectionMember> injectionMembers,
        ImmutableEquatableArray<ResolvedDecorator> decorators)
    {
        var reg = cached.Registration;

        if(reg.Instance is not null)
        {
            return new InstanceContainerEntry(
                reg,
                cached.ResolverMethodName,
                constructorParameters,
                injectionMembers,
                decorators);
        }

        if(cached.IsAsyncInit)
        {
            if(reg.Lifetime is ServiceLifetime.Singleton or ServiceLifetime.Scoped)
            {
                return new AsyncContainerEntry(
                    reg,
                    cached.ResolverMethodName,
                    cached.FieldName!,
                    cached.IsEager,
                    GetEffectiveThreadSafeStrategy(threadSafeStrategy, true),
                    constructorParameters,
                    injectionMembers,
                    decorators);
            }

            return new AsyncTransientContainerEntry(
                reg,
                cached.ResolverMethodName,
                constructorParameters,
                injectionMembers,
                decorators);
        }

        if(reg.Lifetime is ServiceLifetime.Singleton or ServiceLifetime.Scoped)
        {
            // Only eager singletons are always created in a constructor (the root's) before any other thread can
            // resolve them. Eager scoped services are created in scope constructors, but the root creates them on
            // demand, so they keep the synchronized shape.
            if(cached.IsEager && reg.Lifetime == ServiceLifetime.Singleton)
            {
                return new EagerContainerEntry(
                    reg,
                    cached.ResolverMethodName,
                    cached.FieldName!,
                    constructorParameters,
                    injectionMembers,
                    decorators);
            }

            return new LazyThreadSafeContainerEntry(
                reg,
                cached.ResolverMethodName,
                cached.FieldName!,
                cached.IsEager,
                threadSafeStrategy,
                constructorParameters,
                injectionMembers,
                decorators);
        }

        return new TransientContainerEntry(
            reg,
            cached.ResolverMethodName,
            constructorParameters,
            injectionMembers,
            decorators);
    }

    private static ImmutableEquatableArray<ContainerEntry> CreateCollectionContainerEntries(
        IReadOnlyDictionary<string, ImmutableEquatableArray<ServiceLookupEntry>> collectionRegistrations,
        ContainerNames names)
    {
        var entries = new List<ContainerEntry>(collectionRegistrations.Count);

        foreach(var kvp in collectionRegistrations)
        {
            var elementResolvers = new List<ResolvedDependency>(kvp.Value.Length);
            var uniqueKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach(var cached in kvp.Value)
            {
                var uniqueKey = cached.Registration.Instance ?? cached.ResolverMethodName;
                if(!uniqueKeys.Add(uniqueKey))
                    continue;

                if(cached.Registration.Instance is not null)
                {
                    elementResolvers.Add(new InstanceExpressionDependency(cached.Registration.Instance));
                }
                else
                {
                    elementResolvers.Add(new DirectServiceDependency(cached.ResolverMethodName));
                }
            }

            if(elementResolvers.Count == 0)
                continue;

            entries.Add(new CollectionContainerEntry(
                kvp.Key,
                names.ArrayResolver(kvp.Key),
                elementResolvers.ToImmutableEquatableArray()));
        }

        return entries.ToImmutableEquatableArray();
    }

    /// <summary>
    /// Collects the element types that registrations request as collections (constructor, factory and decorator
    /// parameters, injection members, including inside Lazy/Func/Task wrappers), plus those requested through
    /// IServiceProvider invocations.
    /// </summary>
    private static HashSet<string> CollectRequestedCollectionServiceTypes(
        ImmutableEquatableArray<ServiceRegistrationModel> registrations,
        ImmutableEquatableArray<string> requestedCollectionServiceTypes)
    {
        var requested = new HashSet<string>(requestedCollectionServiceTypes, StringComparer.Ordinal);

        foreach(var reg in registrations)
        {
            ScanParameters(reg.Factory?.AdditionalParameters ?? reg.ImplementationType.ConstructorParameters);
            ScanInjectionMembers(reg.InjectionMembers);

            foreach(var decorator in reg.Decorators)
            {
                ScanParameters(decorator.ConstructorParameters);
                ScanInjectionMembers(decorator.InjectionMembers);
            }
        }

        return requested;

        void ScanParameters(ImmutableEquatableArray<ParameterData>? parameters)
        {
            if(parameters is null)
                return;

            foreach(var parameter in parameters)
                ScanType(parameter.Type);
        }

        void ScanInjectionMembers(ImmutableEquatableArray<InjectionMemberData>? members)
        {
            if(members is null)
                return;

            foreach(var member in members)
            {
                if(member.Type is not null)
                    ScanType(member.Type);

                ScanParameters(member.Parameters);
            }
        }

        void ScanType(TypeData type)
        {
            switch(type)
            {
                case CollectionWrapperTypeData collection:
                    requested.Add(collection.ElementType.Name);
                    ScanType(collection.ElementType);
                    break;
                case LazyTypeData lazy:
                    ScanType(lazy.InstanceType);
                    break;
                case FuncTypeData func:
                    ScanType(func.ReturnType);
                    break;
                case TaskTypeData task:
                    ScanType(task.InnerType);
                    break;
            }
        }
    }

    /// <param name="InnerResolverMethodName">
    /// For a decorated service entry, the method resolving the undecorated instance it wraps
    /// (<c>Task&lt;T&gt;</c> for async-init services); otherwise <see langword="null"/>.
    /// </param>
    private readonly record struct ServiceLookupEntry(
        ServiceRegistrationModel Registration,
        string ResolverMethodName,
        string? FieldName,
        bool IsAsyncInit,
        bool IsEager,
        string? InnerResolverMethodName = null);

    /// <summary>
    /// Registrations by (service type, key), with the last registration of each keyed service indexed by service type,
    /// so KeyValuePair and Dictionary dependencies find their keyed registrations without scanning every service.
    /// </summary>
    private sealed class ServiceLookup : Dictionary<(string ServiceType, string? Key), ImmutableEquatableArray<ServiceLookupEntry>>
    {
        public ServiceLookup(Dictionary<(string ServiceType, string? Key), List<ServiceLookupEntry>> registrations)
            : base(registrations.Count)
        {
            foreach(var kvp in registrations)
            {
                var entries = kvp.Value.ToImmutableEquatableArray();
                Add(kvp.Key, entries);

                if(kvp.Key.Key is null)
                    continue;

                if(!LastKeyedEntriesByServiceType.TryGetValue(kvp.Key.ServiceType, out var keyedEntries))
                {
                    keyedEntries = [];
                    LastKeyedEntriesByServiceType[kvp.Key.ServiceType] = keyedEntries;
                }
                keyedEntries.Add(entries[^1]);
            }
        }

        public Dictionary<string, List<ServiceLookupEntry>> LastKeyedEntriesByServiceType { get; } = new(StringComparer.Ordinal);
    }
}
