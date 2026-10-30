using static SourceGen.Ioc.Constants;

namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Represents a Func standalone service registration entry to be generated.
    /// </summary>
    /// <param name="FuncServiceTypeName">The full Func service type name (e.g. Func&lt;string, IService&gt;).</param>
    /// <param name="InnerServiceTypeName">The fully-qualified return service type name.</param>
    /// <param name="InputTypes">The Func input type parameters.</param>
    /// <param name="Registration">
    /// The source registration: its lifetime and key (the Func is registered under the same ones), and the
    /// implementation type, injection members and decorators that a Func with input parameters constructs and applies.
    /// </param>
    /// <param name="Tags">The tags inherited from the source registration.</param>
    private readonly record struct FuncRegistrationEntry(
        string FuncServiceTypeName,
        string InnerServiceTypeName,
        ImmutableEquatableArray<TypeParameter> InputTypes,
        ServiceRegistrationModel Registration,
        ImmutableEquatableArray<string> Tags)
    {
        public void WriteRegistration(SourceWriter writer)
        {
            var lifetime = Registration.Lifetime.Name;
            var key = Registration.Key;

            if(InputTypes.Length == 0)
            {
                var resolveCall = GetWrapperResolveCall(Registration);
                if(key is not null)
                {
                    writer.WriteLine($"services.AddKeyed{lifetime}<global::System.Func<{InnerServiceTypeName}>>({key}, ({IServiceProviderGlobalTypeName} sp, object? key) => new global::System.Func<{InnerServiceTypeName}>(() => {resolveCall}));");
                }
                else
                {
                    writer.WriteLine($"services.Add{lifetime}<global::System.Func<{InnerServiceTypeName}>>(({IServiceProviderGlobalTypeName} sp) => new global::System.Func<{InnerServiceTypeName}>(() => {resolveCall}));");
                }

                return;
            }

            writer.WriteServiceLambdaOpen(lifetime, FuncServiceTypeName, key);
            writer.Indentation++;
            writer.WriteLine($"new {FuncServiceTypeName}(({BuildFuncLambdaParameters(InputTypes)}) =>");
            writer.WriteLine("{");
            writer.Indentation++;

            WriteFuncFactoryBody(writer, this);

            writer.Indentation--;
            writer.WriteLine("}));");
            writer.Indentation--;
        }
    }

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
        var addedKeys = new HashSet<(string FuncServiceType, string ImplementationType, string? Key)>();
        var neededByReturnType = neededFuncTypes.Values.ToLookup(static needed => needed.ReturnType.Name, StringComparer.Ordinal);

        foreach(var regWithTags in registrations)
        {
            var reg = regWithTags.Registration;
            if(reg.IsOpenGeneric)
                continue;

            foreach(var needed in neededByReturnType[reg.ServiceType.Name])
            {
                if(!addedKeys.Add((needed.Name, reg.ImplementationType.Name, reg.Key)))
                    continue;

                entries.Add(new FuncRegistrationEntry(
                    needed.Name,
                    needed.ReturnType.Name,
                    needed.InputTypes,
                    reg,
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
            switch(member)
            {
                case PropertyOrFieldInjectionData propertyOrField:
                    ScanTypeForFuncNeeds(propertyOrField.Type, neededFuncTypes);
                    break;

                case MethodInjectionData method:
                    foreach(var param in method.Parameters)
                        ScanTypeForFuncNeeds(param.Type, neededFuncTypes);
                    break;
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
            // Nested wrappers are intentionally excluded from standalone field generation. This is a
            // deliberate pragmatic choice: direct container resolution of nested wrappers (for example,
            // container.GetService<Lazy<Func<T>>>()) is extremely rare, and constructor injection is
            // already fully covered by inline construction. Keeping nested wrappers inline avoids
            // expanding field scanning, naming conventions, and scoped-container infrastructure for all
            // nested shapes. Nested wrappers also usually do not require cross-consumer instance
            // sharing, so per-consumer inline instances are semantically correct.
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
        ImmutableEquatableArray<FuncRegistrationEntry> entries)
    {
        if(entries.Length == 0)
            return;

        writer.WriteLine();
        writer.WriteLine("// Func wrapper registrations");

        foreach(var entry in entries)
        {
            entry.WriteRegistration(writer);
        }
    }

    /// <summary>
    /// Writes the body of a multi-parameter Func factory lambda using first-unused type matching.
    /// </summary>
    private static void WriteFuncFactoryBody(SourceWriter writer, FuncRegistrationEntry entry)
    {
        var constructorParams = entry.Registration.ImplementationType.ConstructorParameters ?? [];
        var inputArgNames = new string[entry.InputTypes.Length];
        var inputArgTypeNames = new string[entry.InputTypes.Length];
        var inputArgUsed = new bool[entry.InputTypes.Length];

        for(var i = 0; i < entry.InputTypes.Length; i++)
        {
            inputArgNames[i] = $"arg{i}";
            inputArgTypeNames[i] = entry.InputTypes[i].Type.Name;
            inputArgUsed[i] = false;
        }

        var constructorParamEntries = new List<(string Name, string? Value)>(constructorParams.Length);
        var resolvedParamIndex = 0;
        foreach(var param in constructorParams)
        {
            var matchedArg = TryConsumeMatchingFuncInputArg(param.Type.Name, inputArgNames, inputArgTypeNames, inputArgUsed);
            if(matchedArg is not null)
            {
                constructorParamEntries.Add((param.Name, matchedArg));
                continue;
            }

            var paramVar = $"p{resolvedParamIndex}";
            var resolvedVar = ResolveParamAndEmitVar(writer, param, paramVar, isKeyedRegistration: entry.Registration.Key is not null, registrationKey: entry.Registration.Key);
            constructorParamEntries.Add((param.Name, resolvedVar));
            resolvedParamIndex++;
        }

        var propertyInits = new List<string>();
        var propertyFieldIndex = 0;
        foreach(var injectionMember in entry.Registration.InjectionMembers)
        {
            if(injectionMember is not PropertyOrFieldInjectionData member)
                continue;

            var memberType = member.Type;
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

        var constructorArgs = BuildArgumentListFromEntries(constructorParamEntries);
        var initializerPart = propertyInits.Count > 0 ? $" {{ {string.Join(", ", propertyInits)} }}" : string.Empty;
        var constructorInvocation = BuildConstructorInvocation(entry.Registration.ImplementationType.Name, constructorArgs, initializerPart);
        writer.WriteLine($"var s0 = {constructorInvocation};");

        var methodParamIndex = 0;
        foreach(var member in entry.Registration.InjectionMembers)
        {
            if(member is not MethodInjectionData { IsAsync: false } method)
                continue;

            var methodParams = method.Parameters;
            var methodEntries = new List<(string Name, string? Value)>(methodParams.Length);
            foreach(var param in methodParams)
            {
                var matchedArg = TryConsumeMatchingFuncInputArg(param.Type.Name, inputArgNames, inputArgTypeNames, inputArgUsed);
                if(matchedArg is not null)
                {
                    methodEntries.Add((param.Name, matchedArg));
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
                        isKeyedRegistration: entry.Registration.Key is not null,
                        registrationKey: entry.Registration.Key,
                        typeNameResolver: null);
                }
                else
                {
                    resolvedVar = ResolveParamAndEmitVar(writer, param, paramVar, isKeyedRegistration: entry.Registration.Key is not null, registrationKey: entry.Registration.Key);
                }

                methodEntries.Add((param.Name, resolvedVar));
                methodParamIndex++;
            }

            var methodArgs = BuildArgumentListFromEntries(methodEntries);
            writer.WriteLine($"s0.{method.Name}({methodArgs});");
        }

        // The input arguments are for the implementation only; decorator dependencies are resolved from DI
        var outermostVar = WriteDecoratorChain(writer, entry.Registration, asyncInitServiceTypeNames: null);
        writer.WriteLine($"return {outermostVar};");
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
}
