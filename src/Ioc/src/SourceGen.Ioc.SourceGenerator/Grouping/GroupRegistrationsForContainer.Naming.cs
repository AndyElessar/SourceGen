namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Member names that the generated service fields and resolvers must not use: the members of the generated
    /// container infrastructure, inherited <see cref="object"/> members, imported module fields, and the members
    /// the user declares on the container class (including partial accessors).
    /// </summary>
    private static readonly string[] GeneratedMemberNames =
    [
        "GetService", "GetRequiredService", "GetKeyedService", "GetRequiredKeyedService", "GetServices", "GetKeyedServices",
        "GetFallbackServiceProvider", "GetType", "GetHashCode",
        "CreateScope", "CreateAsyncScope", "CreateFallbackScope", "ServiceProviderFactory",
        "CreateControllerFactory", "CreateComponentFactory", "CreateComponentPropertyInjector",
        "_root", "_disposed", "_isRootScope", "_fallbackProvider", "_ownedFallbackProvider", "_fallbackScope",
        "_localResolvers", "_serviceResolvers", "_controllerFactoryCache", "_componentFactoryCache", "_propertyActivatorCache",
    ];

    private static ReservedMemberNames GetReservedNames(ContainerModel container)
    {
        var reservedNames = new ReservedMemberNames();
        foreach(var name in GeneratedMemberNames)
        {
            reservedNames.Add(name);
        }

        foreach(var name in container.MemberNames)
        {
            reservedNames.Add(name);
        }

        foreach(var module in container.ImportedModules)
        {
            reservedNames.Add(GetModuleFieldName(module.Name));
        }

        return reservedNames;
    }

    /// <summary>
    /// Reserved member names, checked without building candidate names: each reserved name also reserves the
    /// resolver and field names that would derive it, so a check is a single set lookup.
    /// </summary>
    private sealed class ReservedMemberNames
    {
        private static readonly string[] SynchronizationFieldSuffixes = ["Lock", "Semaphore", "SpinLock"];

        private readonly HashSet<string> resolverNames = new(StringComparer.Ordinal);
        private readonly HashSet<string> fieldNames = new(StringComparer.Ordinal);

        public void Add(string name)
        {
            // A resolver method M is taken by M, its Create method, and their Async variants
            AddResolverName(name);
            if(TryRemoveSuffix(name, "Async", out var syncName))
            {
                AddResolverName(syncName);
            }

            // A field F is taken by F and its synchronization fields
            fieldNames.Add(name);
            foreach(var suffix in SynchronizationFieldSuffixes)
            {
                if(TryRemoveSuffix(name, suffix, out var fieldName))
                {
                    fieldNames.Add(fieldName);
                }
            }
        }

        /// <summary>
        /// Gets whether a resolver method name, or a method derived from it (<c>Create…</c>, <c>…Async</c>), is reserved.
        /// </summary>
        public bool IsReservedResolverName(string methodName) => resolverNames.Contains(methodName);

        /// <summary>
        /// Gets whether a service field name, or a synchronization field derived from it, is reserved.
        /// </summary>
        public bool IsReservedFieldName(string fieldName) => fieldNames.Contains(fieldName);

        /// <summary>
        /// Adds <paramref name="name"/> and the resolver method names whose Create method (see <see cref="GetCreateMethodName"/>) it is.
        /// </summary>
        private void AddResolverName(string name)
        {
            resolverNames.Add(name);

            // "Get{X}" creates with "Create{X}"
            if(name.Length > 6 && name.StartsWith("Create", StringComparison.Ordinal))
            {
                resolverNames.Add("Get" + name[6..]);
            }

            // Any other method M creates with "{M}_Create"
            if(TryRemoveSuffix(name, "_Create", out var methodName)
                && !(methodName.Length > 3 && methodName.StartsWith("Get", StringComparison.Ordinal)))
            {
                resolverNames.Add(methodName);
            }
        }

        private static bool TryRemoveSuffix(string name, string suffix, out string result)
        {
            if(name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
            {
                result = name[..^suffix.Length];
                return true;
            }

            result = name;
            return false;
        }
    }

    /// <summary>
    /// Gets the type name that member names of an implementation type are derived from.
    /// </summary>
    private static string GetNamingTypeName(TypeData implType) => implType switch
    {
        GenericTypeData { IsOpenGeneric: false } genericTypeData when genericTypeData.Name != genericTypeData.NameWithoutGeneric => genericTypeData.Name,
        GenericTypeData genericTypeData => genericTypeData.NameWithoutGeneric,
        _ => implType.Name,
    };

    /// <summary>
    /// Computes both field name and resolver method name for a service in a single pass.
    /// This avoids redundant GetSafeIdentifier calls and string operations.
    /// </summary>
    /// <returns>A tuple containing (FieldName, ResolverMethodName).</returns>
    private static (string FieldName, string ResolverMethodName) ComputeServiceNames(ServiceRegistrationModel reg, ContainerNames names)
    {
        var baseName = names.Get(GetNamingTypeName(reg.ImplementationType));
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
