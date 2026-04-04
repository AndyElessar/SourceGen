namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Shared helper to construct an instance and apply property/field/method injection with conditional handling.
    /// Supports decorator scenarios via service-parameter detection and generic type substitution.
    /// </summary>
    private static void WriteConstructInstanceWithInjection(
        SourceWriter writer,
        string instanceVarName,
        string implTypeName,
        ImmutableEquatableArray<ParameterData>? constructorParams,
        ImmutableEquatableArray<InjectionMemberData> injectionMembers,
        bool isKeyedRegistration,
        string? registrationKey,
        HashSet<string>? serviceTypeNames,
        Func<TypeData, string>? ctorTypeNameResolver,
        Func<TypeData, string>? memberTypeNameResolver,
        string? decoratedPrevVar,
        ImmutableEquatableSet<string>? asyncInitServiceTypeNames = null,
        bool isAsyncMode = false)
    {
        var ctorParams = constructorParams ?? [];
        var constructorParamEntries = new List<(string Name, string? Value, bool NeedsConditional)>(ctorParams.Length);
        int paramIndex = 0;

        foreach(var param in ctorParams)
        {
            var resolvedTypeName = ctorTypeNameResolver is not null ? ctorTypeNameResolver(param.Type) : param.Type.Name;
            if(decoratedPrevVar is not null && serviceTypeNames is not null && IsServiceTypeParameter(param.Type, resolvedTypeName, serviceTypeNames))
            {
                constructorParamEntries.Add((param.Name, decoratedPrevVar, false));
            }
            else
            {
                var varName = decoratedPrevVar is not null ? $"{instanceVarName}_p{paramIndex}" : $"p{paramIndex}";
                var resolvedVar = ResolveParamAndEmitVar(writer, param, varName, isKeyedRegistration, registrationKey, asyncInitServiceTypeNames);
                constructorParamEntries.Add((param.Name, resolvedVar, false));
                paramIndex++;
            }
        }

        if(constructorParamEntries.Any(e => e.NeedsConditional))
        {
            var conditionalParams = constructorParamEntries.Where(e => e.NeedsConditional && e.Value is not null).ToArray();
            var initializerPart = "";
            if(conditionalParams.Length == 1)
            {
                var condParam = conditionalParams[0];
                var withArgs = BuildArgumentListFromEntries([.. constructorParamEntries.Select(e => (e.Name, e.NeedsConditional ? e.Value : e.Value, false))]);
                var withoutArgs = BuildArgumentListFromEntries([.. constructorParamEntries.Select(e => (e.Name, e.NeedsConditional ? (string?)null : e.Value, false))]);
                var withInvocation = BuildConstructorInvocation(implTypeName, withArgs, initializerPart);
                var withoutInvocation = BuildConstructorInvocation(implTypeName, withoutArgs, initializerPart);
                writer.WriteLine($"var {instanceVarName} = {condParam.Value} is not null ? {withInvocation} : {withoutInvocation};");
            }
            else
            {
                var conditions = conditionalParams.Select(p => $"{p.Value} is not null").ToArray();
                var allCondition = string.Join(" && ", conditions);
                writer.WriteLine($"{implTypeName} {instanceVarName};");
                writer.WriteLine($"if ({allCondition})");
                writer.WriteLine("{");
                writer.Indentation++;
                var allArgs = BuildArgumentListFromEntries([.. constructorParamEntries.Select(e => (e.Name, e.Value, false))]);
                var allInvocation = BuildConstructorInvocation(implTypeName, allArgs, initializerPart);
                writer.WriteLine($"{instanceVarName} = {allInvocation};");
                writer.Indentation--;
                writer.WriteLine("}");
                writer.WriteLine("else");
                writer.WriteLine("{");
                writer.Indentation++;
                var fallbackArgs = BuildArgumentListFromEntries([.. constructorParamEntries.Select(e => (e.Name, e.NeedsConditional ? (string?)null : e.Value, false))]);
                var fallbackInvocation = BuildConstructorInvocation(implTypeName, fallbackArgs, initializerPart);
                writer.WriteLine($"{instanceVarName} = {fallbackInvocation};");
                writer.Indentation--;
                writer.WriteLine("}");
            }
        }
        else
        {
            EmitConstruction(
                writer,
                instanceVarName,
                implTypeName,
                constructorParamEntries,
                injectionMembers,
                isKeyedRegistration,
                registrationKey,
                memberTypeNameResolver,
                asyncInitServiceTypeNames,
                isAsyncMode);
        }
    }

    private static void EmitConstruction(
        SourceWriter writer,
        string instanceVarName,
        string implTypeName,
        List<(string Name, string? Value, bool NeedsConditional)> constructorParamEntries,
        ImmutableEquatableArray<InjectionMemberData> injectionMembers,
        bool isKeyedRegistration,
        string? registrationKey,
        Func<TypeData, string>? memberTypeNameResolver,
        ImmutableEquatableSet<string>? asyncInitServiceTypeNames = null,
        bool isAsyncMode = false)
    {
        int pfCount = injectionMembers.Count(m => m.MemberType is InjectionMemberType.Property or InjectionMemberType.Field);
        var preProps = pfCount > 0 ? new (string Name, string ParamVar)[pfCount] : [];
        int idx = 0;
        int pfIdxCounter = 0;
        foreach(var m in injectionMembers)
        {
            if(m.MemberType is not (InjectionMemberType.Property or InjectionMemberType.Field))
                continue;

            var varN = $"{instanceVarName}_p{pfIdxCounter}";
            var mt = m.Type;
            var mtName = mt is null ? "object" : (memberTypeNameResolver is not null ? memberTypeNameResolver(mt) : mt.Name);
            bool hasNonNullDefault = m.HasDefaultValue && !m.DefaultValueIsNull;
            ResolveMemberValue(writer, mt, mtName, varN, m.Key, m.IsNullable, hasNonNullDefault, m.DefaultValue, asyncInitServiceTypeNames);
            preProps[idx++] = (m.Name, varN);
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
            asyncInitServiceTypeNames);

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
                asyncInitServiceTypeNames)
            : [];

        var propertyInits = preProps.Select(p => $"{p.Name} = {p.ParamVar}").ToArray();
        var constructorArgs = BuildArgumentListFromEntries([.. constructorParamEntries]);
        var initializerPart = propertyInits.Length > 0 ? $" {{ {string.Join(", ", propertyInits)} }}" : "";
        var constructorInvocation = BuildConstructorInvocation(implTypeName, constructorArgs, initializerPart);
        writer.WriteLine($"var {instanceVarName} = {constructorInvocation};");

        EmitMethodInvocations(writer, instanceVarName, methodParamResolutions, useAwait: false);
        EmitMethodInvocations(writer, instanceVarName, asyncMethodParamResolutions, useAwait: true);
    }

    /// <summary>
    /// Resolves method parameters of a given <paramref name="targetType"/> and emits their variable declarations.
    /// Shared by sync (<see cref="InjectionMemberType.Method"/>) and async (<see cref="InjectionMemberType.AsyncMethod"/>) resolution loops.
    /// </summary>
    private static List<(string MethodName, string?[] ParamVars, string[] ParamNames)> ResolveMethodParamResolutions(
        SourceWriter writer,
        ImmutableEquatableArray<InjectionMemberData> injectionMembers,
        InjectionMemberType targetType,
        string instanceVarName,
        ref int memberParamIndex,
        bool isKeyedRegistration,
        string? registrationKey,
        Func<TypeData, string>? memberTypeNameResolver,
        ImmutableEquatableSet<string>? asyncInitServiceTypeNames)
    {
        var resolutions = new List<(string MethodName, string?[] ParamVars, string[] ParamNames)>();
        foreach(var method in injectionMembers)
        {
            if(method.MemberType != targetType)
                continue;

            var mParams = method.Parameters ?? [];
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
                        asyncInitServiceTypeNames);
                }
                else
                {
                    mVars[mi] = ResolveParamAndEmitVar(writer, p, pVar, isKeyedRegistration, registrationKey, asyncInitServiceTypeNames);
                }

                mi++;
                memberParamIndex++;
            }

            resolutions.Add((method.Name, mVars, mNames));
        }

        return resolutions;
    }

    /// <summary>
    /// Emits method invocation statements for the given <paramref name="resolutions"/>.
    /// When <paramref name="useAwait"/> is <see langword="true"/>, each call is prefixed with <c>await</c>.
    /// </summary>
    private static void EmitMethodInvocations(
        SourceWriter writer,
        string instanceVarName,
        List<(string MethodName, string?[] ParamVars, string[] ParamNames)> resolutions,
        bool useAwait)
    {
        foreach(var (mName, mVars, mNames) in resolutions)
        {
            var entries = new List<(string Name, string? Value, bool NeedsConditional)>(mVars.Length);
            for(int i = 0; i < mVars.Length; i++)
            {
                entries.Add((mNames[i], mVars[i], false));
            }

            var args = BuildArgumentListFromEntries([.. entries]);
            writer.WriteLine(useAwait
                ? $"await {instanceVarName}.{mName}({args});"
                : $"{instanceVarName}.{mName}({args});");
        }
    }
}
