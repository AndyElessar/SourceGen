namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Shared helper to construct an instance and apply property/field/method injection.
    /// Supports decorator scenarios (the decorated instance is passed to the constructor parameter at
    /// <paramref name="decoratedParameterIndex"/>) and generic type substitution.
    /// </summary>
    private static void WriteConstructInstanceWithInjection(
        SourceWriter writer,
        string instanceVarName,
        string implTypeName,
        ImmutableEquatableArray<ParameterData>? constructorParams,
        ImmutableEquatableArray<InjectionMemberData> injectionMembers,
        bool isKeyedRegistration,
        string? registrationKey,
        int decoratedParameterIndex,
        Func<TypeData, string>? memberTypeNameResolver,
        string? decoratedPrevVar,
        RegisterWriteContext context,
        bool isAsyncMode = false)
    {
        var ctorParams = constructorParams ?? [];
        var constructorParamEntries = new List<(string Name, string? Value)>(ctorParams.Length);
        int paramIndex = 0;

        for(var i = 0; i < ctorParams.Length; i++)
        {
            var param = ctorParams[i];
            if(decoratedPrevVar is not null && i == decoratedParameterIndex)
            {
                constructorParamEntries.Add((param.Name, decoratedPrevVar));
            }
            else
            {
                var varName = decoratedPrevVar is not null ? $"{instanceVarName}_p{paramIndex}" : $"p{paramIndex}";
                var resolvedVar = ResolveParamAndEmitVar(writer, param, varName, isKeyedRegistration, registrationKey, context);
                constructorParamEntries.Add((param.Name, resolvedVar));
                paramIndex++;
            }
        }

        EmitConstruction(
            writer,
            instanceVarName,
            implTypeName,
            constructorParamEntries,
            injectionMembers,
            isKeyedRegistration,
            registrationKey,
            memberTypeNameResolver,
            context,
            isAsyncMode);
    }

    private static void EmitConstruction(
        SourceWriter writer,
        string instanceVarName,
        string implTypeName,
        List<(string Name, string? Value)> constructorParamEntries,
        ImmutableEquatableArray<InjectionMemberData> injectionMembers,
        bool isKeyedRegistration,
        string? registrationKey,
        Func<TypeData, string>? memberTypeNameResolver,
        RegisterWriteContext context,
        bool isAsyncMode = false)
    {
        List<string> preProps = [];
        int pfIdxCounter = 0;
        foreach(var member in injectionMembers)
        {
            if(member is not PropertyOrFieldInjectionData m)
                continue;

            var varN = $"{instanceVarName}_p{pfIdxCounter}";
            var mt = m.Type;
            var mtName = memberTypeNameResolver is not null ? memberTypeNameResolver(mt) : mt.Name;
            bool hasNonNullDefault = m.HasDefaultValue && !m.DefaultValueIsNull;
            ResolveMemberValue(writer, mt, mtName, varN, m.Key, m.IsNullable, hasNonNullDefault, m.DefaultValue, context);
            preProps.Add($"{m.Name} = {varN}");
            pfIdxCounter++;
        }

        int memberParamIndex = pfIdxCounter;
        var methodParamResolutions = ResolveMethodParamResolutions(
            writer,
            injectionMembers,
            InjectionMemberType.Method,
            instanceVarName,
            ref memberParamIndex,
            isKeyedRegistration,
            registrationKey,
            memberTypeNameResolver,
            context);

        var asyncMethodParamResolutions = isAsyncMode
            ? ResolveMethodParamResolutions(
                writer,
                injectionMembers,
                InjectionMemberType.AsyncMethod,
                instanceVarName,
                ref memberParamIndex,
                isKeyedRegistration,
                registrationKey,
                memberTypeNameResolver,
                context)
            : [];

        var constructorArgs = BuildArgumentListFromEntries(constructorParamEntries);
        var initializerPart = preProps.Count > 0 ? $" {{ {string.Join(", ", preProps)} }}" : "";
        var constructorInvocation = BuildConstructorInvocation(implTypeName, constructorArgs, initializerPart);
        writer.WriteLine($"var {instanceVarName} = {constructorInvocation};");

        EmitMethodInvocations(writer, methodParamResolutions, useAwait: false);
        EmitMethodInvocations(writer, asyncMethodParamResolutions, useAwait: true);
    }

    /// <summary>
    /// Resolves method parameters of a given <paramref name="targetType"/> and emits their variable declarations.
    /// Shared by sync (<see cref="InjectionMemberType.Method"/>) and async (<see cref="InjectionMemberType.AsyncMethod"/>) resolution loops.
    /// </summary>
    private static List<(string InvocationTarget, string?[] ParamVars, string[] ParamNames)> ResolveMethodParamResolutions(
        SourceWriter writer,
        ImmutableEquatableArray<InjectionMemberData> injectionMembers,
        InjectionMemberType targetType,
        string instanceVarName,
        ref int memberParamIndex,
        bool isKeyedRegistration,
        string? registrationKey,
        Func<TypeData, string>? memberTypeNameResolver,
        RegisterWriteContext context)
    {
        var resolutions = new List<(string InvocationTarget, string?[] ParamVars, string[] ParamNames)>();
        foreach(var member in injectionMembers)
        {
            if(member is not MethodInjectionData method || method.MemberType != targetType)
                continue;

            var mParams = method.Parameters;
            var mVars = new string?[mParams.Length];
            var mNames = new string[mParams.Length];
            int mi = 0;
            foreach(var p in mParams)
            {
                var pVar = $"{instanceVarName}_m{memberParamIndex}";
                mNames[mi] = p.Name;
                if(method.Key is not null)
                {
                    mVars[mi] = ResolveMethodParameterWithKey(
                        writer,
                        p,
                        pVar,
                        method.Key,
                        isKeyedRegistration,
                        registrationKey,
                        memberTypeNameResolver,
                        context);
                }
                else
                {
                    mVars[mi] = ResolveParamAndEmitVar(writer, p, pVar, isKeyedRegistration, registrationKey, context);
                }

                mi++;
                memberParamIndex++;
            }

            resolutions.Add((method.GetInvocationTarget(instanceVarName), mVars, mNames));
        }

        return resolutions;
    }

    /// <summary>
    /// Emits method invocation statements for the given <paramref name="resolutions"/>.
    /// When <paramref name="useAwait"/> is <see langword="true"/>, each call is prefixed with <c>await</c>.
    /// </summary>
    private static void EmitMethodInvocations(
        SourceWriter writer,
        List<(string InvocationTarget, string?[] ParamVars, string[] ParamNames)> resolutions,
        bool useAwait)
    {
        foreach(var (invocationTarget, mVars, mNames) in resolutions)
        {
            var entries = new (string Name, string? Value)[mVars.Length];
            for(int i = 0; i < mVars.Length; i++)
            {
                entries[i] = (mNames[i], mVars[i]);
            }

            var args = BuildArgumentListFromEntries(entries);
            writer.WriteLine($"{(useAwait ? "await " : "")}{invocationTarget}({args});");
        }
    }
}
