namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Gets additional service types from default settings and matched types.
    /// </summary>
    private static IEnumerable<TypeData> GetAdditionalServiceTypesFromDefaults(
        DefaultSettingsModel? matchingDefault,
        List<TypeData> matchedServiceTypes)
    {
        if(matchingDefault is not null)
        {
            foreach(var st in matchingDefault.ServiceTypes)
            {
                yield return st;
            }
        }

        foreach(var matchedType in matchedServiceTypes)
        {
            yield return matchedType;
        }
    }

    /// <summary>
    /// Finds matching default settings from base classes and interfaces.
    /// </summary>
    /// <returns>The best matching default index, or -1 if none found.</returns>
    private static int FindMatchingDefaults(
        ImmutableEquatableArray<TypeData> baseClasses,
        ImmutableEquatableArray<TypeData> interfaces,
        DefaultSettingsMap defaultSettings,
        List<int> matchedDefaultIndices,
        List<TypeData> matchedServiceTypes)
    {
        int bestDefaultIndex = -1;

        foreach(var candidate in baseClasses)
        {
            TryMatchDefaultSettings(candidate, defaultSettings, matchedDefaultIndices, matchedServiceTypes, ref bestDefaultIndex);
        }

        foreach(var candidate in interfaces)
        {
            TryMatchDefaultSettings(candidate, defaultSettings, matchedDefaultIndices, matchedServiceTypes, ref bestDefaultIndex);
        }

        return bestDefaultIndex;
    }

    /// <summary>
    /// Attempts to match a candidate type against default settings.
    /// </summary>
    private static void TryMatchDefaultSettings(
        TypeData candidate,
        DefaultSettingsMap defaultSettings,
        List<int> matchedDefaultIndices,
        List<TypeData> matchedServiceTypes,
        ref int bestDefaultIndex)
    {
        // Check exact matches
        if(defaultSettings.TryGetExactMatches(candidate.Name, out var index) && !matchedDefaultIndices.Contains(index))
        {
            matchedDefaultIndices.Add(index);
            matchedServiceTypes.Add(candidate);
            if(bestDefaultIndex < 0) bestDefaultIndex = index;
        }

        // Check generic matches (only if type has generic parameters)
        if(candidate is GenericTypeData candidateGeneric
            && (candidateGeneric.IsOpenGeneric || candidate.Name != candidateGeneric.NameWithoutGeneric))
        {
            if(defaultSettings.TryGetGenericMatches(candidateGeneric.NameWithoutGeneric, candidateGeneric.GenericArity, out var gIndex)
                && !matchedDefaultIndices.Contains(gIndex))
            {
                matchedDefaultIndices.Add(gIndex);
                matchedServiceTypes.Add(candidate);
                if(bestDefaultIndex < 0) bestDefaultIndex = gIndex;
            }
        }
    }

    /// <summary>
    /// Merges registration settings with default settings. The lifetime is the explicit one, otherwise Singleton for an
    /// <c>Instance</c> registration (its default settings and fallback lifetime do not apply), otherwise that of the default
    /// settings, otherwise the fallback lifetime.
    /// </summary>
    private static (ServiceLifetime Lifetime, bool RegisterAllInterfaces, bool RegisterAllBaseClasses) MergeSettings(
        RegistrationData registration,
        DefaultSettingsModel? matchingDefault,
        ServiceLifetime fallbackLifetime)
    {
        var lifetime = registration.HasExplicitLifetime
            ? registration.Lifetime
            : registration.Instance is not null
                ? ServiceLifetime.Singleton
                : (matchingDefault?.Lifetime ?? fallbackLifetime);

        var registerAllInterfaces = registration.HasExplicitRegisterAllInterfaces
            ? registration.RegisterAllInterfaces
            : (matchingDefault?.RegisterAllInterfaces ?? registration.RegisterAllInterfaces);

        var registerAllBaseClasses = registration.HasExplicitRegisterAllBaseClasses
            ? registration.RegisterAllBaseClasses
            : (matchingDefault?.RegisterAllBaseClasses ?? registration.RegisterAllBaseClasses);

        return (lifetime, registerAllInterfaces, registerAllBaseClasses);
    }
}