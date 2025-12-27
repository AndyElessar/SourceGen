namespace SourceGen.Ioc.SourceGenerator.Register;

partial class RegisterSourceGenerator
{
    private static ImmutableEquatableArray<ServiceRegistrationModel> GenerateServiceRegistrations(
        in ImmutableArray<RegistrationData> registrations,
        DefaultSettingsMap defaultSettings,
        CancellationToken ct)
    {
        var result = new List<ServiceRegistrationModel>((int)(registrations.Length * 1.5));

        // Reusable buffers to reduce allocations
        var matchedDefaultIndices = new HashSet<int>();
        var matchedServiceTypes = new List<TypeData>();
        var serviceTypesToRegister = new HashSet<TypeData>();

        foreach(var registration in registrations)
        {
            ct.ThrowIfCancellationRequested();

            matchedDefaultIndices.Clear();
            matchedServiceTypes.Clear();
            serviceTypesToRegister.Clear();
            int bestDefaultIndex = -1;

            // Helper to process a candidate type (interface or base class)
            void ProcessCandidateType(TypeData candidateTypeData)
            {
                string candidateType = candidateTypeData.Name;
                // Check exact matches
                if(defaultSettings.TryGetExactMatches(candidateType, out var index))
                {
                    if(matchedDefaultIndices.Add(index))
                    {
                        matchedServiceTypes.Add(candidateTypeData);
                        if(bestDefaultIndex == -1)
                        {
                            bestDefaultIndex = index;
                        }
                    }
                }

                // Check generic matches
                if(candidateTypeData.IsOpenGeneric || candidateTypeData.Name != candidateTypeData.NameWithoutGeneric)
                {
                    if(defaultSettings.TryGetGenericMatches(candidateTypeData.NameWithoutGeneric, candidateTypeData.GenericArity, out var gIndex))
                    {
                        if(matchedDefaultIndices.Add(gIndex))
                        {
                            matchedServiceTypes.Add(candidateTypeData);
                            if(bestDefaultIndex == -1)
                            {
                                bestDefaultIndex = gIndex;
                            }
                        }
                    }
                }
            }

            // Scan base classes first, then interfaces
            foreach(var baseClass in registration.AllBaseClasses)
            {
                ProcessCandidateType(baseClass);
            }

            foreach(var iface in registration.AllInterfaces)
            {
                ProcessCandidateType(iface);
            }

            DefaultSettingsModel? matchingDefault = bestDefaultIndex != -1 ? defaultSettings[bestDefaultIndex] : null;

            // Merge settings
            var lifetime = registration.HasExplicitLifetime
                ? registration.Lifetime
                : (matchingDefault?.Lifetime ?? registration.Lifetime);

            var registerAllInterfaces = registration.HasExplicitRegisterAllInterfaces
                ? registration.RegisterAllInterfaces
                : (matchingDefault?.RegisterAllInterfaces ?? registration.RegisterAllInterfaces);

            var registerAllBaseClasses = registration.HasExplicitRegisterAllBaseClasses
                ? registration.RegisterAllBaseClasses
                : (matchingDefault?.RegisterAllBaseClasses ?? registration.RegisterAllBaseClasses);

            // Collect service types
            serviceTypesToRegister.Add(registration.ImplementationType);

            // Add explicit service types
            foreach(var st in registration.ServiceTypes)
            {
                serviceTypesToRegister.Add(st);
            }

            // Add default service types if any
            if(matchingDefault is not null)
            {
                foreach(var st in matchingDefault.ServiceTypes)
                {
                    serviceTypesToRegister.Add(st);
                }
            }

            // Add matched interfaces/base classes from default settings
            foreach(var matchedType in matchedServiceTypes)
            {
                serviceTypesToRegister.Add(matchedType);
            }

            // Add all interfaces if requested
            if(registerAllInterfaces)
            {
                foreach(var iface in registration.AllInterfaces)
                {
                    serviceTypesToRegister.Add(iface);
                }
            }

            // Add all base classes if requested
            if(registerAllBaseClasses)
            {
                foreach(var baseClass in registration.AllBaseClasses)
                {
                    serviceTypesToRegister.Add(baseClass);
                }
            }

            // Merge decorators - use registration's decorators if present, otherwise use default settings'
            var decorators = registration.Decorators.Length > 0
                ? registration.Decorators
                : (matchingDefault?.Decorators ?? registration.Decorators);

            // Check if implementation is open generic
            var isOpenGenericImplementation = registration.ImplementationType.IsOpenGeneric;

            // Build all service types that the implementation can be assigned to
            var allServiceTypeNames = BuildAllServiceTypeNames(registration);

            // Create registrations for each service type
            foreach(var serviceTypeData in serviceTypesToRegister)
            {
                var isOpenGenericService = serviceTypeData.IsOpenGeneric;
                var serviceType = serviceTypeData.Name;

                // For open generic implementation, only register with open generic service types
                // For closed implementation, skip open generic service types
                if(isOpenGenericImplementation != isOpenGenericService)
                {
                    continue;
                }

                // Skip nested open generic types (e.g., IGeneric<IGeneric2<T>>)
                // as they cannot be properly registered with DI container
                if(serviceTypeData.IsNestedOpenGeneric || registration.ImplementationType.IsNestedOpenGeneric)
                {
                    continue;
                }

                // For open generic service types (excluding the implementation type itself),
                // verify the implementation actually implements it correctly.
                // Skip if the implementation only implements a nested variant (e.g., IRepository<IGeneric<T>> instead of IRepository<T>)
                if(isOpenGenericService && serviceTypeData.Name != registration.ImplementationType.Name)
                {
                    var serviceTypeKey = $"{serviceTypeData.NameWithoutGeneric}`{serviceTypeData.GenericArity}";
                    if(!registration.ValidOpenGenericServiceTypes.Contains(serviceTypeKey))
                    {
                        continue;
                    }
                }

                result.Add(new ServiceRegistrationModel(
                    serviceType,
                    serviceTypeData.GenericArity,
                    registration.ImplementationType.Name,
                    registration.ImplementationType.GenericArity,
                    lifetime,
                    registration.Key,
                    registration.KeyType,
                    isOpenGenericImplementation,
                    decorators,
                    allServiceTypeNames));
            }
        }
        return result.ToImmutableEquatableArray();
    }

    /// <summary>
    /// Builds an array of all service type names that the implementation can be assigned to.
    /// This includes the implementation type itself, all interfaces, and all base classes.
    /// </summary>
    private static ImmutableEquatableArray<string> BuildAllServiceTypeNames(RegistrationData registration)
    {
        var result = new List<string>(1 + registration.AllInterfaces.Length + registration.AllBaseClasses.Length);

        // Add implementation type
        result.Add(registration.ImplementationType.Name);

        // Add all interfaces
        foreach(var iface in registration.AllInterfaces)
        {
            result.Add(iface.Name);
        }

        // Add all base classes
        foreach(var baseClass in registration.AllBaseClasses)
        {
            result.Add(baseClass.Name);
        }

        return result.ToImmutableEquatableArray();
    }
}
