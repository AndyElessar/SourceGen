using static SourceGen.Ioc.SourceGenerator.Models.Constants;

namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Represents a Func standalone service registration entry to be generated.
    /// </summary>
    /// <param name="FuncServiceTypeName">The full Func service type name (e.g. Func&lt;string, IService&gt;).</param>
    /// <param name="InnerServiceTypeName">The fully-qualified return service type name.</param>
    /// <param name="ImplementationTypeName">The fully-qualified implementation type name.</param>
    /// <param name="Lifetime">The service lifetime matching the inner service.</param>
    /// <param name="ImplementationTypeConstructorParams">The implementation constructor parameters.</param>
    /// <param name="ImplementationTypeInjectionMembers">The implementation injection members.</param>
    /// <param name="InputTypes">The Func input type parameters.</param>
    /// <param name="Tags">The tags inherited from the source registration.</param>
    private readonly record struct FuncRegistrationEntry(
        string FuncServiceTypeName,
        string InnerServiceTypeName,
        string ImplementationTypeName,
        ServiceLifetime Lifetime,
        ImmutableEquatableArray<ParameterData>? ImplementationTypeConstructorParams,
        ImmutableEquatableArray<InjectionMemberData> ImplementationTypeInjectionMembers,
        ImmutableEquatableArray<TypeParameter> InputTypes,
        ImmutableEquatableArray<string> Tags);

    /// <summary>
    /// Collects Func standalone registration entries needed by consumer dependencies.
    /// </summary>
    private static List<FuncRegistrationEntry> CollectFuncEntries(
        ImmutableEquatableArray<ServiceRegistrationWithTags> registrations)
    {
        var neededFuncTypes = new Dictionary<string, FuncTypeData>(StringComparer.Ordinal);
        foreach(var regWithTags in registrations)
        {
            var reg = regWithTags.Registration;
            ScanParamsForFuncNeeds(reg.ImplementationType.ConstructorParameters, neededFuncTypes);
            ScanInjectionMembersForFuncNeeds(reg.InjectionMembers, neededFuncTypes);
        }

        if(neededFuncTypes.Count == 0)
            return [];

        var entries = new List<FuncRegistrationEntry>();
        var addedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach(var regWithTags in registrations)
        {
            var reg = regWithTags.Registration;
            if(reg.IsOpenGeneric)
                continue;

            foreach(var needed in neededFuncTypes.Values)
            {
                if(!string.Equals(reg.ServiceType.Name, needed.ReturnType.Name, StringComparison.Ordinal))
                    continue;

                var entryKey = $"{needed.Name}|{reg.ImplementationType.Name}|{reg.Key}";
                if(!addedKeys.Add(entryKey))
                    continue;

                entries.Add(new FuncRegistrationEntry(
                    needed.Name,
                    needed.ReturnType.Name,
                    reg.ImplementationType.Name,
                    reg.Lifetime,
                    reg.ImplementationType.ConstructorParameters,
                    reg.InjectionMembers,
                    needed.InputTypes,
                    regWithTags.Tags));
            }
        }

        return entries;
    }

    /// <summary>
    /// Scans constructor parameters for Func dependencies.
    /// </summary>
    private static void ScanParamsForFuncNeeds(
        ImmutableEquatableArray<ParameterData>? parameters,
        Dictionary<string, FuncTypeData> neededFuncTypes)
    {
        if(parameters is null)
            return;

        foreach(var param in parameters)
        {
            ScanTypeForFuncNeeds(param.Type, neededFuncTypes);
        }
    }

    /// <summary>
    /// Scans injection members for Func dependencies.
    /// </summary>
    private static void ScanInjectionMembersForFuncNeeds(
        ImmutableEquatableArray<InjectionMemberData>? members,
        Dictionary<string, FuncTypeData> neededFuncTypes)
    {
        if(members is null or { Length: 0 })
            return;

        foreach(var member in members)
        {
            if(member.Type is not null)
                ScanTypeForFuncNeeds(member.Type, neededFuncTypes);

            if(member.Parameters is not null)
            {
                foreach(var param in member.Parameters)
                    ScanTypeForFuncNeeds(param.Type, neededFuncTypes);
            }
        }
    }

    /// <summary>
    /// Checks if a type is or contains a direct Func dependency and tracks the Func type.
    /// </summary>
    private static void ScanTypeForFuncNeeds(
        TypeData type,
        Dictionary<string, FuncTypeData> neededFuncTypes)
    {
        switch(type)
        {
            case FuncTypeData func when func.ReturnType is not WrapperTypeData:
                if(!neededFuncTypes.ContainsKey(func.Name))
                    neededFuncTypes[func.Name] = func;
                break;

            case CollectionWrapperTypeData { ElementType: FuncTypeData func } when func.ReturnType is not WrapperTypeData:
                if(!neededFuncTypes.ContainsKey(func.Name))
                    neededFuncTypes[func.Name] = func;
                break;
        }
    }

    /// <summary>
    /// Writes standalone Func registrations.
    /// </summary>
    private static void WriteFuncRegistrations(
        SourceWriter writer,
        List<FuncRegistrationEntry>? entries)
    {
        if(entries is null or { Count: 0 })
            return;

        writer.WriteLine();
        writer.WriteLine("// Func wrapper registrations");

        foreach(var entry in entries)
        {
            var lifetime = entry.Lifetime.Name;

            if(entry.InputTypes.Length == 0)
            {
                var wrapperTypeName = $"global::System.Func<{entry.InnerServiceTypeName}>";
                var resolveCall = $"sp.{GetRequiredService}<{entry.ImplementationTypeName}>()";
                writer.WriteLine(
                    $"services.Add{lifetime}<{wrapperTypeName}>(({IServiceProviderGlobalTypeName} sp) => " +
                    $"new {wrapperTypeName}(() => {resolveCall}));");
                continue;
            }

            writer.WriteLine($"services.Add{lifetime}<{entry.FuncServiceTypeName}>(({IServiceProviderGlobalTypeName} sp) =>");
            writer.Indentation++;
            writer.WriteLine($"new {entry.FuncServiceTypeName}(({BuildFuncLambdaParameters(entry.InputTypes)}) =>");
            writer.WriteLine("{");
            writer.Indentation++;

            WriteFuncFactoryBody(writer, entry);

            writer.Indentation--;
            writer.WriteLine("}));");
            writer.Indentation--;
        }
    }

    /// <summary>
    /// Writes the body of a multi-parameter Func factory lambda using first-unused type matching.
    /// </summary>
    private static void WriteFuncFactoryBody(SourceWriter writer, FuncRegistrationEntry entry)
    {
        var constructorParams = entry.ImplementationTypeConstructorParams ?? [];
        var inputArgNames = new string[entry.InputTypes.Length];
        var inputArgTypeNames = new string[entry.InputTypes.Length];
        var inputArgUsed = new bool[entry.InputTypes.Length];

        for(var i = 0; i < entry.InputTypes.Length; i++)
        {
            inputArgNames[i] = $"arg{i}";
            inputArgTypeNames[i] = entry.InputTypes[i].Type.Name;
            inputArgUsed[i] = false;
        }

        var constructorParamEntries = new List<(string Name, string? Value, bool NeedsConditional)>(constructorParams.Length);
        var resolvedParamIndex = 0;
        foreach(var param in constructorParams)
        {
            var matchedArg = TryConsumeMatchingFuncInputArg(param.Type.Name, inputArgNames, inputArgTypeNames, inputArgUsed);
            if(matchedArg is not null)
            {
                constructorParamEntries.Add((param.Name, matchedArg, false));
                continue;
            }

            var paramVar = $"p{resolvedParamIndex}";
            var resolvedVar = ResolveParamAndEmitVar(writer, param, paramVar, isKeyedRegistration: false, registrationKey: null);
            constructorParamEntries.Add((param.Name, resolvedVar, false));
            resolvedParamIndex++;
        }

        var propertyInits = new List<string>();
        var propertyFieldIndex = 0;
        foreach(var member in entry.ImplementationTypeInjectionMembers)
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

            var memberVar = $"s0_p{propertyFieldIndex}";
            var hasNonNullDefault = member.HasDefaultValue && !member.DefaultValueIsNull;
            ResolveMemberValue(writer, memberType, memberType.Name, memberVar, member.Key, member.IsNullable, hasNonNullDefault, member.DefaultValue);
            propertyInits.Add($"{member.Name} = {memberVar}");
            propertyFieldIndex++;
        }

        var constructorArgs = BuildArgumentListFromEntries([.. constructorParamEntries]);
        var initializerPart = propertyInits.Count > 0 ? $" {{ {string.Join(", ", propertyInits)} }}" : string.Empty;
        var constructorInvocation = BuildConstructorInvocation(entry.ImplementationTypeName, constructorArgs, initializerPart);
        writer.WriteLine($"var s0 = {constructorInvocation};");

        var methodParamIndex = 0;
        foreach(var method in entry.ImplementationTypeInjectionMembers)
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

                var paramVar = $"s0_m{methodParamIndex}";
                string? resolvedVar;
                if(method.Key is not null)
                {
                    resolvedVar = ResolveMethodParameterWithKey(
                        writer,
                        param,
                        paramVar,
                        method.Key,
                        isKeyedRegistration: false,
                        registrationKey: null,
                        typeNameResolver: null);
                }
                else
                {
                    resolvedVar = ResolveParamAndEmitVar(writer, param, paramVar, isKeyedRegistration: false, registrationKey: null);
                }

                methodEntries.Add((param.Name, resolvedVar, false));
                methodParamIndex++;
            }

            var methodArgs = BuildArgumentListFromEntries([.. methodEntries]);
            writer.WriteLine($"s0.{method.Name}({methodArgs});");
        }

        writer.WriteLine("return s0;");
    }

    /// <summary>
    /// Builds the Func lambda parameter list, e.g. "string arg0, int arg1".
    /// </summary>
    private static string BuildFuncLambdaParameters(ImmutableEquatableArray<TypeParameter> inputTypes)
    {
        if(inputTypes.Length == 0)
            return string.Empty;

        var parts = new string[inputTypes.Length];
        for(var i = 0; i < inputTypes.Length; i++)
        {
            parts[i] = $"{inputTypes[i].Type.Name} arg{i}";
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Finds and consumes the first unused Func input argument whose type matches exactly.
    /// </summary>
    private static string? TryConsumeMatchingFuncInputArg(
        string requestedTypeName,
        string[] inputArgNames,
        string[] inputArgTypeNames,
        bool[] inputArgUsed)
    {
        for(var i = 0; i < inputArgTypeNames.Length; i++)
        {
            if(inputArgUsed[i])
                continue;

            if(!string.Equals(inputArgTypeNames[i], requestedTypeName, StringComparison.Ordinal))
                continue;

            inputArgUsed[i] = true;
            return inputArgNames[i];
        }

        return null;
    }

    /// <summary>
    /// Collects Func resolver entries for container code generation.
    /// Only single-parameter Func&lt;T&gt; wrappers are tracked as field-backed wrapper resolvers.
    /// </summary>
    private static ImmutableEquatableArray<ContainerFuncEntry> CollectContainerFuncEntries(
        ImmutableEquatableArray<CachedRegistration> singletons,
        ImmutableEquatableArray<CachedRegistration> scoped,
        ImmutableEquatableArray<CachedRegistration> transients,
        ImmutableEquatableDictionary<(string ServiceType, string? Key), ImmutableEquatableArray<CachedRegistration>> byServiceTypeAndKey)
    {
        var neededTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach(var lifetime in new[] { singletons, scoped, transients })
        {
            foreach(var cached in lifetime)
            {
                var reg = cached.Registration;
                ScanParamsForContainerFuncNeeds(reg.ImplementationType.ConstructorParameters, neededTypes);
                ScanInjectionMembersForContainerFuncNeeds(reg.InjectionMembers, neededTypes);
            }
        }

        if(neededTypes.Count == 0)
            return [];

        var entries = new List<ContainerFuncEntry>();
        var addedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach(var kvp in byServiceTypeAndKey)
        {
            var serviceType = kvp.Key.ServiceType;
            if(!neededTypes.Contains(serviceType))
                continue;

            foreach(var cached in kvp.Value)
            {
                var reg = cached.Registration;
                if(reg.IsOpenGeneric)
                    continue;

                // Async-init services cannot be resolved synchronously — exclude from Func<T> entries
                if(HasAsyncInitMembers(reg))
                    continue;

                var entryKey = $"{serviceType}|{reg.ImplementationType.Name}|{reg.Key}";
                if(!addedKeys.Add(entryKey))
                    continue;

                var safeInnerType = GetSafeIdentifier(serviceType);
                var safeImplType = GetSafeIdentifier(reg.ImplementationType.Name);
                var fieldName = $"_func_{safeInnerType}_{safeImplType}";

                entries.Add(new ContainerFuncEntry(serviceType, cached.ResolverMethodName, fieldName));
            }
        }

        return entries.ToImmutableEquatableArray();
    }

    /// <summary>
    /// Scans constructor parameters for single-parameter Func dependencies.
    /// </summary>
    private static void ScanParamsForContainerFuncNeeds(
        ImmutableEquatableArray<ParameterData>? parameters,
        HashSet<string> neededTypes)
    {
        if(parameters is null)
            return;

        foreach(var param in parameters)
        {
            ScanTypeForContainerFuncNeeds(param.Type, neededTypes);
        }
    }

    /// <summary>
    /// Scans injection members for single-parameter Func dependencies.
    /// </summary>
    private static void ScanInjectionMembersForContainerFuncNeeds(
        ImmutableEquatableArray<InjectionMemberData>? members,
        HashSet<string> neededTypes)
    {
        if(members is null or { Length: 0 })
            return;

        foreach(var member in members)
        {
            if(member.Type is not null)
                ScanTypeForContainerFuncNeeds(member.Type, neededTypes);

            if(member.Parameters is not null)
            {
                foreach(var param in member.Parameters)
                    ScanTypeForContainerFuncNeeds(param.Type, neededTypes);
            }
        }
    }

    /// <summary>
    /// Tracks Func&lt;T&gt; wrapper needs for container field-backed wrapper resolvers.
    /// </summary>
    private static void ScanTypeForContainerFuncNeeds(TypeData type, HashSet<string> neededTypes)
    {
        switch(type)
        {
            case FuncTypeData func when !func.HasInputParameters && func.ReturnType is not WrapperTypeData:
                neededTypes.Add(func.ReturnType.Name);
                break;

            case CollectionWrapperTypeData { ElementType: FuncTypeData func } when !func.HasInputParameters && func.ReturnType is not WrapperTypeData:
                neededTypes.Add(func.ReturnType.Name);
                break;
        }
    }

    /// <summary>
    /// Writes Func field declarations and array resolvers for container generation.
    /// </summary>
    private static void WriteContainerFuncFields(
        SourceWriter writer,
        ImmutableEquatableArray<ContainerFuncEntry> entries)
    {
        if(entries.Length == 0)
            return;

        writer.WriteLine("// Func wrapper fields");
        writer.WriteLine();

        foreach(var entry in entries)
        {
            var wrapperTypeName = $"global::System.Func<{entry.InnerServiceTypeName}>";
            writer.WriteLine($"private readonly {wrapperTypeName} {entry.FieldName};");
        }
        writer.WriteLine();

        var grouped = entries
            .GroupBy(static e => e.InnerServiceTypeName)
            .ToList();

        foreach(var group in grouped)
        {
            var innerServiceTypeName = group.Key;
            var wrapperTypeName = $"global::System.Func<{innerServiceTypeName}>";
            var arrayMethodName = GetFuncArrayResolverMethodName(innerServiceTypeName);

            writer.WriteLine($"private {wrapperTypeName}[] {arrayMethodName}() =>");
            writer.Indentation++;
            writer.WriteLine("[");
            writer.Indentation++;

            foreach(var entry in group)
            {
                writer.WriteLine($"{entry.FieldName},");
            }

            writer.Indentation--;
            writer.WriteLine("];");
            writer.Indentation--;
            writer.WriteLine();
        }
    }

    /// <summary>
    /// Writes field initialization statements for Func wrapper fields.
    /// </summary>
    private static void WriteContainerFuncFieldInitializations(
        SourceWriter writer,
        ImmutableEquatableArray<ContainerFuncEntry> entries)
    {
        if(entries.Length == 0)
            return;

        writer.WriteLine();
        writer.WriteLine("// Initialize Func wrapper fields");
        foreach(var entry in entries)
        {
            var wrapperTypeName = $"global::System.Func<{entry.InnerServiceTypeName}>";
            writer.WriteLine($"{entry.FieldName} = new {wrapperTypeName}(() => {entry.ResolverMethodName}());");
        }
    }

    /// <summary>
    /// Writes _localResolvers entries for Func wrapper services.
    /// </summary>
    private static void WriteContainerFuncLocalResolverEntries(
        SourceWriter writer,
        string containerTypeName,
        ImmutableEquatableArray<ContainerFuncEntry> entries)
    {
        if(entries.Length == 0)
            return;

        writer.WriteLine();
        writer.WriteLine("// Func wrapper resolvers");

        var grouped = entries
            .GroupBy(static e => e.InnerServiceTypeName)
            .ToList();

        foreach(var group in grouped)
        {
            var innerServiceTypeName = group.Key;
            var wrapperTypeName = $"global::System.Func<{innerServiceTypeName}>";

            var lastEntry = group.Last();
            writer.WriteLine($"new(new ServiceIdentifier(typeof({wrapperTypeName}), {KeyedServiceAnyKey}), static c => c.{lastEntry.FieldName}),");

            var arrayMethodName = GetFuncArrayResolverMethodName(innerServiceTypeName);
            writer.WriteLine($"new(new ServiceIdentifier(typeof(global::System.Collections.Generic.IEnumerable<{wrapperTypeName}>), {KeyedServiceAnyKey}), static c => c.{arrayMethodName}()),");
            writer.WriteLine($"new(new ServiceIdentifier(typeof(global::System.Collections.Generic.IReadOnlyCollection<{wrapperTypeName}>), {KeyedServiceAnyKey}), static c => c.{arrayMethodName}()),");
            writer.WriteLine($"new(new ServiceIdentifier(typeof(global::System.Collections.Generic.ICollection<{wrapperTypeName}>), {KeyedServiceAnyKey}), static c => c.{arrayMethodName}()),");
            writer.WriteLine($"new(new ServiceIdentifier(typeof(global::System.Collections.Generic.IReadOnlyList<{wrapperTypeName}>), {KeyedServiceAnyKey}), static c => c.{arrayMethodName}()),");
            writer.WriteLine($"new(new ServiceIdentifier(typeof(global::System.Collections.Generic.IList<{wrapperTypeName}>), {KeyedServiceAnyKey}), static c => c.{arrayMethodName}()),");
            writer.WriteLine($"new(new ServiceIdentifier(typeof({wrapperTypeName}[]), {KeyedServiceAnyKey}), static c => c.{arrayMethodName}()),");
        }
    }

    /// <summary>
    /// Gets the array resolver method name for a Func wrapper type.
    /// </summary>
    private static string GetFuncArrayResolverMethodName(string innerServiceTypeName)
    {
        var safeInnerType = GetSafeIdentifier(innerServiceTypeName);
        return $"GetAllFunc_{safeInnerType}_Array";
    }
}
