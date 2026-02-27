namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Transforms container and registrations into grouped data for code generation.
    /// This step is separated from output generation to enable incremental generator caching.
    /// </summary>
    private static ContainerWithGroups GroupRegistrationsForContainer(
        ContainerModel container,
        ImmutableEquatableArray<ServiceRegistrationWithTags> allRegistrations)
    {
        // Filter registrations based on ExplicitOnly mode
        var registrations = FilterRegistrationsForContainer(container, allRegistrations);

        // Collect partial accessor method names to avoid naming conflicts
        var reservedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach(var accessor in container.PartialAccessors)
        {
            if(accessor.Kind == PartialAccessorKind.Method)
            {
                reservedNames.Add(accessor.Name);
            }
        }

        // Group registrations for code generation
        var groups = BuildContainerRegistrationGroups(registrations, container.EagerResolveOptions, reservedNames);

        return new ContainerWithGroups(container, groups);
    }

    /// <summary>
    /// Filters registrations based on the container's ExplicitOnly and IncludeTags settings.
    /// Priority: ExplicitOnly > IncludeTags > All registrations.
    /// </summary>
    private static ImmutableEquatableArray<ServiceRegistrationModel> FilterRegistrationsForContainer(
        ContainerModel container,
        ImmutableEquatableArray<ServiceRegistrationWithTags> allRegistrations)
    {
        // ExplicitOnly takes precedence over IncludeTags
        if(container.ExplicitOnly)
        {
            // Only include explicit registrations from the container class
            var builder = new List<ServiceRegistrationModel>();

            foreach(var explicitReg in container.ExplicitRegistrations)
            {
                // Convert RegistrationData to ServiceRegistrationModel
                // For explicit registrations, we process them with default settings
                var processed = ProcessExplicitRegistrationForContainer(explicitReg);
                if(processed is not null)
                {
                    builder.Add(processed);
                }
            }

            return builder.ToImmutableEquatableArray();
        }

        // Apply IncludeTags filtering if specified
        if(container.IncludeTags.Length > 0)
        {
            // Only include services that have at least one matching tag
            return allRegistrations
                .Where(r => r.Tags.Length > 0 && r.Tags.Any(tag => container.IncludeTags.Contains(tag, StringComparer.Ordinal)))
                .Select(static r => r.Registration)
                .ToImmutableEquatableArray();
        }

        // Include all registrations from the assembly
        return allRegistrations
            .Select(static r => r.Registration)
            .ToImmutableEquatableArray();
    }

    /// <summary>
    /// Converts explicit RegistrationData to ServiceRegistrationModel for container generation.
    /// </summary>
    private static ServiceRegistrationModel? ProcessExplicitRegistrationForContainer(RegistrationData data)
    {
        // Get the first service type, or use implementation type
        var serviceType = data.ServiceTypes.Length > 0
            ? data.ServiceTypes[0]
            : data.ImplementationType;

        return new ServiceRegistrationModel(
            serviceType,
            data.ImplementationType,
            data.Lifetime,
            data.Key,
            data.KeyType,
            data.KeyValueType,
            data.ImplementationType is GenericTypeData { IsOpenGeneric: true },
            data.Decorators,
            data.InjectionMembers,
            data.Factory,
            data.Instance);
    }

    /// <summary>
    /// Groups registrations by service type and key for efficient lookup and collection resolution.
    /// Pre-computes field names, method names, and disposal lists to avoid redundant calculations.
    /// </summary>
    private static ContainerRegistrationGroups BuildContainerRegistrationGroups(
        ImmutableEquatableArray<ServiceRegistrationModel> registrations,
        EagerResolveOptions eagerResolveOptions,
        HashSet<string> reservedNames)
    {
        // Group by (ServiceType.Name, Key) for efficient lookup
        var byServiceTypeAndKey = new Dictionary<(string ServiceType, string? Key), List<CachedRegistration>>();

        // Track all unique service types for IsService checks
        var allServiceTypes = new HashSet<string>();

        // Track unique implementations per lifetime using Dictionary instead of List + index tracking.
        // Key: (ImplementationName, ServiceKey, InstanceOrFactory), Value: (CachedRegistration, HasDecorators)
        var singletonMap = new Dictionary<(string ImplName, string? Key, string? InstanceOrFactory), (CachedRegistration Cached, bool HasDecorators)>();
        var scopedMap = new Dictionary<(string ImplName, string? Key, string? InstanceOrFactory), (CachedRegistration Cached, bool HasDecorators)>();
        var transientMap = new Dictionary<(string ImplName, string? Key, string? InstanceOrFactory), (CachedRegistration Cached, bool HasDecorators)>();

        var hasOpenGenerics = false;
        var hasKeyedServices = false;

        foreach(var reg in registrations)
        {
            if(reg.IsOpenGeneric)
            {
                hasOpenGenerics = true;
                // Skip open generics for most processing but track the flag
                continue;
            }

            // Pre-compute field and method names once, including IsEager flag
            var cached = CreateCachedRegistration(reg, eagerResolveOptions, reservedNames);

            var key = (reg.ServiceType.Name, reg.Key);
            if(reg.Key is not null)
            {
                hasKeyedServices = true;
            }

            if(!byServiceTypeAndKey.TryGetValue(key, out var list))
            {
                list = [];
                byServiceTypeAndKey[key] = list;
            }
            list.Add(cached);

            // Also add implementation type as a service type (for self-registration)
            if(reg.ImplementationType.Name != reg.ServiceType.Name)
            {
                var implKey = (reg.ImplementationType.Name, reg.Key);
                if(!byServiceTypeAndKey.TryGetValue(implKey, out var implList))
                {
                    implList = [];
                    byServiceTypeAndKey[implKey] = implList;
                }
                implList.Add(cached);
            }

            // Track closed types for IsService checks
            allServiceTypes.Add(reg.ServiceType.Name);
            allServiceTypes.Add(reg.ImplementationType.Name);

            // Group by lifetime - prefer registration with decorators for field generation
            // Include Instance or Factory in the key to distinguish multiple instance/factory registrations
            var instanceOrFactory = reg.Instance ?? reg.Factory?.Path;
            var lifetimeKey = (reg.ImplementationType.Name, reg.Key, instanceOrFactory);
            var hasDecorators = reg.Decorators.Length > 0;

            var targetMap = reg.Lifetime switch
            {
                ServiceLifetime.Singleton => singletonMap,
                ServiceLifetime.Scoped => scopedMap,
                _ => transientMap
            };

            if(targetMap.TryGetValue(lifetimeKey, out var existing))
            {
                // Skip if already seen with decorators, or if current doesn't have decorators
                if(existing.HasDecorators || !hasDecorators)
                {
                    continue;
                }
            }

            targetMap[lifetimeKey] = (cached, hasDecorators);
        }

        // Convert maps to lists (preserving insertion order via Dictionary enumeration order in .NET)
        var singletons = singletonMap.Values.Select(static v => v.Cached).ToList();
        var scoped = scopedMap.Values.Select(static v => v.Cached).ToList();
        var transients = transientMap.Values.Select(static v => v.Cached).ToList();

        // Collect service types with multiple registrations for IEnumerable<T> resolution
        var collectionServiceTypes = new List<string>();
        var collectionRegistrations = new Dictionary<string, ImmutableEquatableArray<CachedRegistration>>();

        foreach(var kvp in byServiceTypeAndKey)
        {
            // Include non-keyed service types with multiple registrations
            if(kvp.Key.Key is null && kvp.Value.Count > 1)
            {
                // Deduplicate resolver method names to count unique implementations
                var uniqueResolvers = new HashSet<string>();
                foreach(var cached in kvp.Value)
                {
                    uniqueResolvers.Add(cached.ResolverMethodName);
                }

                // Only generate collection if there are multiple unique resolvers
                if(uniqueResolvers.Count > 1)
                {
                    collectionServiceTypes.Add(kvp.Key.ServiceType);
                    collectionRegistrations[kvp.Key.ServiceType] = kvp.Value.ToImmutableEquatableArray();
                }
            }
        }

        // Pre-compute reversed lists for disposal (to avoid repeated .Reverse() calls)
        var reversedSingletons = new List<CachedRegistration>(singletons.Count);
        for(var i = singletons.Count - 1; i >= 0; i--)
        {
            reversedSingletons.Add(singletons[i]);
        }

        var reversedScoped = new List<CachedRegistration>(scoped.Count);
        for(var i = scoped.Count - 1; i >= 0; i--)
        {
            reversedScoped.Add(scoped[i]);
        }

        // Convert to immutable collections
        var immutableByServiceTypeAndKey = byServiceTypeAndKey
            .ToImmutableEquatableDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToImmutableEquatableArray());

        var immutableSingletons = singletons.ToImmutableEquatableArray();
        var immutableScoped = scoped.ToImmutableEquatableArray();
        var immutableTransients = transients.ToImmutableEquatableArray();

        var eagerSingletons = immutableSingletons
            .Where(static c => c.IsEager)
            .ToImmutableEquatableArray();
        var eagerScoped = immutableScoped
            .Where(static c => c.IsEager)
            .ToImmutableEquatableArray();

        var lazyFuncEntries = CollectContainerLazyFuncEntries(
            immutableSingletons,
            immutableScoped,
            immutableTransients,
            immutableByServiceTypeAndKey);
        var kvpEntries = CollectContainerKvpEntries(
            immutableSingletons,
            immutableScoped,
            immutableTransients,
            immutableByServiceTypeAndKey);

        return new ContainerRegistrationGroups(
            immutableByServiceTypeAndKey,
            allServiceTypes.ToImmutableEquatableSet(),
            immutableSingletons,
            immutableScoped,
            immutableTransients,
            eagerSingletons,
            eagerScoped,
            lazyFuncEntries,
            kvpEntries,
            hasOpenGenerics,
            hasKeyedServices,
            collectionServiceTypes.ToImmutableEquatableArray(),
            collectionRegistrations.ToImmutableEquatableDictionary(),
            reversedSingletons.ToImmutableEquatableArray(),
            reversedScoped.ToImmutableEquatableArray());
    }

    /// <summary>
    /// Creates a CachedRegistration with pre-computed field and method names.
    /// Computes both names in a single pass to avoid redundant string operations.
    /// </summary>
    private static CachedRegistration CreateCachedRegistration(
        ServiceRegistrationModel reg,
        EagerResolveOptions eagerResolveOptions,
        HashSet<string> reservedNames)
    {
        var (fieldName, methodName) = ComputeServiceNames(reg);

        // Avoid naming conflicts with user-declared partial accessor methods
        if(reservedNames.Contains(methodName))
        {
            methodName = $"{methodName}_Resolve";
        }

        // Determine if this registration should be eagerly resolved
        // Instance registrations are inherently eager (no field caching needed)
        // Transient services are not supported for eager resolution
        var isEager = reg.Instance is null && reg.Lifetime switch
        {
            ServiceLifetime.Singleton => (eagerResolveOptions & EagerResolveOptions.Singleton) != 0,
            ServiceLifetime.Scoped => (eagerResolveOptions & EagerResolveOptions.Scoped) != 0,
            _ => false // Transient is never eager
        };

        return new CachedRegistration(reg, fieldName, methodName, isEager);
    }

    /// <summary>
    /// Computes both field name and resolver method name for a service in a single pass.
    /// This avoids redundant GetSafeIdentifier calls and string operations.
    /// </summary>
    /// <returns>A tuple containing (FieldName, ResolverMethodName).</returns>
    private static (string FieldName, string ResolverMethodName) ComputeServiceNames(ServiceRegistrationModel reg)
    {
        var implType = reg.ImplementationType;
        var typeName = implType switch
        {
            GenericTypeData { IsOpenGeneric: false } genericTypeData when genericTypeData.Name != genericTypeData.NameWithoutGeneric => genericTypeData.Name,
            GenericTypeData genericTypeData => genericTypeData.NameWithoutGeneric,
            _ => implType.Name,
        };
        var baseName = GetSafeIdentifier(typeName);
        var lowerFirstChar = char.ToLowerInvariant(baseName[0]);
        var restOfName = baseName[1..];

        // Handle keyed services
        if(reg.Key is not null)
        {
            var safeKey = GetSafeIdentifier(reg.Key);
            return (
                $"_{lowerFirstChar}{restOfName}_{safeKey}",
                $"Get{baseName}_{safeKey}"
            );
        }

        // Handle instance registrations - include instance name in the method name
        if(reg.Instance is not null)
        {
            var safeInstance = GetSafeIdentifier(reg.Instance);
            return (
                $"_{lowerFirstChar}{restOfName}_{safeInstance}",
                $"Get{baseName}_{safeInstance}"
            );
        }

        // Handle factory registrations - include factory path in the method name
        if(reg.Factory is not null)
        {
            var safeFactory = GetSafeIdentifier(reg.Factory.Path);
            return (
                $"_{lowerFirstChar}{restOfName}_{safeFactory}",
                $"Get{baseName}_{safeFactory}"
            );
        }

        return (
            $"_{lowerFirstChar}{restOfName}",
            $"Get{baseName}"
        );
    }
}
