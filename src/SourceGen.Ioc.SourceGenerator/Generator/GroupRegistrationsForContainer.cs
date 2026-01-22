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

        // Group registrations for code generation
        var groups = BuildContainerRegistrationGroups(registrations);

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
            data.ImplementationType.IsOpenGeneric,
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
        ImmutableEquatableArray<ServiceRegistrationModel> registrations)
    {
        // Group by (ServiceType.Name, Key) for efficient lookup
        var byServiceTypeAndKey = new Dictionary<(string ServiceType, string? Key), List<CachedRegistration>>();

        // Track all unique service types for IsService checks
        var allServiceTypes = new HashSet<string>();

        // Track unique implementations per lifetime using Dictionary instead of List + index tracking.
        // Key: (ImplementationName, ServiceKey), Value: (CachedRegistration, HasDecorators)
        var singletonMap = new Dictionary<(string ImplName, string? Key), (CachedRegistration Cached, bool HasDecorators)>();
        var scopedMap = new Dictionary<(string ImplName, string? Key), (CachedRegistration Cached, bool HasDecorators)>();
        var transientMap = new Dictionary<(string ImplName, string? Key), (CachedRegistration Cached, bool HasDecorators)>();

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

            // Pre-compute field and method names once
            var cached = CreateCachedRegistration(reg);

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
            var lifetimeKey = (reg.ImplementationType.Name, reg.Key);
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

        // Collect service types with multiple implementations for IEnumerable<T> resolution
        var collectionServiceTypes = new List<string>();
        var collectionRegistrations = new Dictionary<string, ImmutableEquatableArray<CachedRegistration>>();

        foreach(var kvp in byServiceTypeAndKey)
        {
            // Only include non-keyed service types with multiple distinct implementations
            if(kvp.Key.Key is null && kvp.Value.Count > 1)
            {
                // Count distinct implementation types using HashSet to avoid LINQ
                var distinctImplementations = new HashSet<string>();
                var hasSelfRegistrationOnly = true;

                foreach(var cached in kvp.Value)
                {
                    distinctImplementations.Add(cached.Registration.ImplementationType.Name);
                    if(cached.Registration.ServiceType.Name != cached.Registration.ImplementationType.Name)
                    {
                        hasSelfRegistrationOnly = false;
                    }
                }

                // Only generate collection if there are multiple distinct implementations
                // AND not all are self-registrations
                if(distinctImplementations.Count > 1 && !hasSelfRegistrationOnly)
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

        return new ContainerRegistrationGroups(
            immutableByServiceTypeAndKey,
            allServiceTypes.ToImmutableEquatableSet(),
            singletons.ToImmutableEquatableArray(),
            scoped.ToImmutableEquatableArray(),
            transients.ToImmutableEquatableArray(),
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
    private static CachedRegistration CreateCachedRegistration(ServiceRegistrationModel reg)
    {
        var (fieldName, methodName) = ComputeServiceNames(reg);
        return new CachedRegistration(reg, fieldName, methodName);
    }

    /// <summary>
    /// Computes both field name and resolver method name for a service in a single pass.
    /// This avoids redundant GetSafeIdentifier calls and string operations.
    /// </summary>
    /// <returns>A tuple containing (FieldName, ResolverMethodName).</returns>
    private static (string FieldName, string ResolverMethodName) ComputeServiceNames(ServiceRegistrationModel reg)
    {
        var implType = reg.ImplementationType;
        var typeName = implType.IsClosedGeneric ? implType.Name : implType.NameWithoutGeneric;
        var baseName = GetSafeIdentifier(typeName);
        var lowerFirstChar = char.ToLowerInvariant(baseName[0]);
        var restOfName = baseName[1..];

        if(reg.Key is not null)
        {
            var safeKey = GetSafeIdentifier(reg.Key);
            return (
                $"_{lowerFirstChar}{restOfName}_{safeKey}",
                $"Get{baseName}_{safeKey}"
            );
        }

        return (
            $"_{lowerFirstChar}{restOfName}",
            $"Get{baseName}"
        );
    }
}
