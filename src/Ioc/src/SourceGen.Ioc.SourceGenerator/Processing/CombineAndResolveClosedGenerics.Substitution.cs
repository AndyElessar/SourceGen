namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Builds closed implementation TypeData with substituted constructor parameters.
    /// </summary>
    private static GenericTypeData BuildClosedImplTypeData(
        string closedImplTypeName,
        GenericTypeData openImplType,
        Dictionary<string, string> typeArgMap)
    {
        // Build closed type parameters
        var openTypeParams = openImplType.TypeParameters;
        ImmutableEquatableArray<TypeParameter>? closedTypeParams = null;
        if(openTypeParams is not null && openTypeParams.Length > 0)
        {
            var newParams = new List<TypeParameter>(openTypeParams.Length);
            foreach(var param in openTypeParams)
            {
                if(typeArgMap.TryGetValue(param.ParameterName, out var closedTypeName))
                {
                    var closedType = TypeData.CreateSimple(closedTypeName);
                    newParams.Add(new TypeParameter(param.ParameterName, closedType));
                }
            }
            closedTypeParams = newParams.ToImmutableEquatableArray();
        }

        // Build closed constructor parameters
        var openConstructorParams = openImplType.ConstructorParameters;
        ImmutableEquatableArray<ParameterData>? closedConstructorParams = null;
        if(openConstructorParams is not null && openConstructorParams.Length > 0)
        {
            var newParams = new List<ParameterData>(openConstructorParams.Length);
            foreach(var param in openConstructorParams)
            {
                var newParamTypeName = SubstituteTypeArguments(param.Type.Name, typeArgMap);
                var newParamType = RehydrateTypeData(param.Type, Name: newParamTypeName, IsOpenGeneric: false);
                newParams.Add(param with { Type = newParamType });
            }
            closedConstructorParams = newParams.ToImmutableEquatableArray();
        }

        return TypeData.CreateGeneric(
            closedImplTypeName,
            openImplType.NameWithoutGeneric,
            IsOpenGeneric: false,
            openImplType.GenericArity,
            IsNestedOpenGeneric: false,
            TypeParameters: closedTypeParams,
            ConstructorParameters: closedConstructorParams,
            HasInjectConstructor: openImplType.HasInjectConstructor);
    }

    /// <summary>
    /// Builds all closed service types from open service types using the service type argument map.
    /// </summary>
    private static List<TypeData> BuildClosedServiceTypesFromServiceTypeMap(
        ImmutableEquatableArray<TypeData> openServiceTypes,
        GenericTypeData openImplType,
        ImmutableEquatableArray<TypeData> implInterfaces,
        Dictionary<string, string> serviceTypeArgMap,
        Dictionary<string, string> implTypeArgMap)
    {
        var result = new List<TypeData>(openServiceTypes.Length);

        foreach(var openServiceType in openServiceTypes)
        {
            if(openServiceType is not GenericTypeData openGenericServiceType)
            {
                result.Add(openServiceType);
                continue;
            }

            // Skip non-generic service types
            if(openGenericServiceType.GenericArity == 0)
            {
                result.Add(openServiceType);
                continue;
            }

            // A form the implementation declares is written with its own type parameters, which the impl type arg map
            // closes even when the service type is a definition with other parameter names, such as typeof(IHandler<,>)
            if(FindImplementedForm(openImplType, implInterfaces, openGenericServiceType) is { } implementedForm)
            {
                result.Add(TypeData.CreateGeneric(
                    SubstituteTypeArguments(implementedForm.Name, implTypeArgMap),
                    implementedForm.NameWithoutGeneric,
                    IsOpenGeneric: false,
                    implementedForm.GenericArity,
                    IsNestedOpenGeneric: false,
                    TypeParameters: SubstituteTypeParameters(implementedForm.TypeParameters, implTypeArgMap)));
                continue;
            }

            // Use service type arg map for service types, impl type arg map for impl types
            var typeArgMapToUse = serviceTypeArgMap;

            // Build closed service type name
            var closedServiceTypeName = SubstituteTypeArguments(openServiceType.Name, typeArgMapToUse);

            // Also try with impl type arg map if service type arg map doesn't fully substitute
            if(closedServiceTypeName.Contains('>') || openGenericServiceType.IsOpenGeneric)
            {
                closedServiceTypeName = SubstituteTypeArguments(closedServiceTypeName, implTypeArgMap);
            }

            // Skip if the substitution didn't fully resolve the type (still contains type parameters)
            // This happens when ServiceTypes contains pure open generics like IRequestHandler<TRequest, TResponse>
            // and the type parameter names don't match the implementation's type parameters
            if(StillContainsUnresolvedTypeParameters(closedServiceTypeName, openServiceType))
            {
                continue;
            }

            var closedServiceType = TypeData.CreateGeneric(
                closedServiceTypeName,
                openGenericServiceType.NameWithoutGeneric,
                IsOpenGeneric: false,
                openGenericServiceType.GenericArity,
                IsNestedOpenGeneric: false,
                TypeParameters: SubstituteTypeParameters(openGenericServiceType.TypeParameters, serviceTypeArgMap));

            result.Add(closedServiceType);
        }

        return result;
    }

    /// <summary>
    /// Checks if the substituted type name still contains unresolved type parameters.
    /// This happens when the original type has type parameters that weren't in the substitution map.
    /// </summary>
    private static bool StillContainsUnresolvedTypeParameters(string closedTypeName, TypeData openServiceType)
    {
        // If the type parameters are null or empty, it's not a generic type to begin with
        var typeParams = openServiceType is GenericTypeData genericServiceType
            ? genericServiceType.TypeParameters
            : null;
        if(typeParams is null || typeParams.Length == 0)
        {
            return false;
        }

        // Check if any of the original type parameter names still appear in the closed type name
        // This indicates incomplete substitution
        foreach(var typeParam in typeParams)
        {
            var paramTypeName = typeParam.Type.Name;
            // Check if this type parameter is a simple type parameter (not a generic type)
            // and still appears in the closed type name
            if(typeParam.Type is TypeParameterTypeData
                or GenericTypeData { IsOpenGeneric: true, GenericArity: 0 })
            {
                // Check if the parameter name still appears between < and > or < and ,
                if(closedTypeName.Contains($"<{paramTypeName}>") ||
                   closedTypeName.Contains($"<{paramTypeName},") ||
                   closedTypeName.Contains($", {paramTypeName}>") ||
                   closedTypeName.Contains($", {paramTypeName},"))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Substitutes type parameters in TypeParameter array with actual types.
    /// Recursively processes nested type parameters for generic types like Task&lt;T&gt;.
    /// </summary>
    private static ImmutableEquatableArray<TypeParameter>? SubstituteTypeParameters(
        ImmutableEquatableArray<TypeParameter>? typeParams,
        Dictionary<string, string> typeArgMap)
    {
        if(typeParams is null || typeParams.Length == 0)
        {
            return null;
        }

        var result = new List<TypeParameter>(typeParams.Length);
        foreach(var param in typeParams)
        {
            var substitutedType = SubstituteTypeData(param.Type, typeArgMap);
            result.Add(param with { Type = substitutedType });
        }

        return result.ToImmutableEquatableArray();
    }

    /// <summary>
    /// Recursively substitutes type arguments in a TypeData, including nested type parameters.
    /// For example, Task&lt;T&gt; with T -> Entity becomes Task&lt;Entity&gt; with proper TypeParameters.
    /// </summary>
    private static TypeData SubstituteTypeData(TypeData typeData, Dictionary<string, string> typeArgMap)
    {
        var newTypeName = SubstituteTypeArguments(typeData.Name, typeArgMap);
        var newTypeParams = SubstituteTypeParameters(
            typeData is GenericTypeData genericTypeData ? genericTypeData.TypeParameters : null,
            typeArgMap);

        var substituted = RehydrateTypeData(typeData, Name: newTypeName, IsOpenGeneric: false, TypeParameters: newTypeParams, OverrideTypeParameters: true);

        // Interfaces and base classes are compared with closed decorator constraints, so they are closed too
        return typeData.AllInterfaces is null && typeData.AllBaseClasses is null
            ? substituted
            : substituted with
            {
                AllInterfaces = SubstituteTypeNames(typeData.AllInterfaces, typeArgMap),
                AllBaseClasses = SubstituteTypeNames(typeData.AllBaseClasses, typeArgMap),
            };

        static ImmutableEquatableArray<TypeData>? SubstituteTypeNames(ImmutableEquatableArray<TypeData>? types, Dictionary<string, string> typeArgMap)
        {
            if(types is null)
            {
                return null;
            }

            var result = new List<TypeData>(types.Length);
            foreach(var type in types)
            {
                result.Add(RehydrateTypeData(type, Name: SubstituteTypeArguments(type.Name, typeArgMap), IsOpenGeneric: false));
            }

            return result.ToImmutableEquatableArray();
        }
    }

    /// <summary>
    /// Rehydrates a <see cref="TypeData"/> instance while preserving subtype semantics.
    /// </summary>
    private static TypeData RehydrateTypeData(
        TypeData source,
        string? Name = null,
        bool? IsOpenGeneric = null,
        bool? IsNestedOpenGeneric = null,
        ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
        bool OverrideTypeParameters = false)
    {
        var newName = Name ?? source.Name;
        var sourceGenericType = source as GenericTypeData;
        var sourceWrapperType = source as WrapperTypeData;
        var newIsOpenGeneric = IsOpenGeneric ?? sourceGenericType?.IsOpenGeneric ?? false;
        var newIsNestedOpenGeneric = IsNestedOpenGeneric ?? sourceGenericType?.IsNestedOpenGeneric ?? false;
        var newTypeParameters = OverrideTypeParameters ? TypeParameters : sourceGenericType?.TypeParameters;

        if(sourceWrapperType is not null)
        {
            return TypeData.CreateWrapper(
                newName,
                sourceWrapperType.NameWithoutGeneric,
                newIsOpenGeneric,
                sourceWrapperType.GenericArity,
                sourceWrapperType.WrapperKind,
                newIsNestedOpenGeneric,
                newTypeParameters,
                source.ConstructorParameters,
                source.HasInjectConstructor,
                source.InjectionMembers,
                source.AllInterfaces,
                source.AllBaseClasses);
        }

        if(sourceGenericType is not null && (newIsOpenGeneric || sourceGenericType.GenericArity > 0 || newTypeParameters is { Length: > 0 } || source is TypeParameterTypeData))
        {
            return TypeData.CreateGeneric(
                newName,
                sourceGenericType.NameWithoutGeneric,
                newIsOpenGeneric,
                sourceGenericType.GenericArity,
                newIsNestedOpenGeneric,
                newTypeParameters,
                source.ConstructorParameters,
                source.HasInjectConstructor,
                source.InjectionMembers,
                source.AllInterfaces,
                source.AllBaseClasses);
        }

        return TypeData.CreateSimple(
            newName,
            source.ConstructorParameters,
            source.HasInjectConstructor,
            source.InjectionMembers,
            source.AllInterfaces,
            source.AllBaseClasses);
    }

    /// <summary>
    /// Replaces the type arguments of a closed service type with the matching type arguments of the dependency it was
    /// built for.
    /// </summary>
    /// <remarks>
    /// Closed service types are built by substituting type argument names, so their type arguments lack the interfaces
    /// that decorator constraint checks need. The dependency's type arguments carry them.
    /// </remarks>
    private static TypeData WithTypeArgumentsOf(TypeData closedServiceType, GenericTypeData dependencyType)
    {
        if(closedServiceType is not GenericTypeData { TypeParameters: { Length: > 0 } typeParams } genericServiceType
            || dependencyType.TypeParameters is not { Length: > 0 } dependencyTypeParams)
        {
            return closedServiceType;
        }

        var result = new List<TypeParameter>(typeParams.Length);
        foreach(var param in typeParams)
        {
            var dependencyArg = dependencyTypeParams.FirstOrDefault(p => p.Type.Name == param.Type.Name);
            result.Add(dependencyArg is null ? param : param with { Type = dependencyArg.Type });
        }

        return genericServiceType with { TypeParameters = result.ToImmutableEquatableArray() };
    }

    /// <summary>
    /// Processes decorators for a specific service type, using the service type's type parameters for substitution.
    /// This is used for closed generic registrations where the decorator types need to be closed based on
    /// the service type (interface) rather than the implementation type.
    /// </summary>
    private static ImmutableEquatableArray<TypeData> ProcessDecoratorsForServiceType(
        ImmutableEquatableArray<TypeData> decorators,
        TypeData serviceType)
    {
        if(decorators.Length == 0)
        {
            return decorators;
        }

        var serviceTypeParams = serviceType is GenericTypeData genericServiceType
            ? genericServiceType.TypeParameters
            : null;
        if(serviceTypeParams is null || serviceTypeParams.Length == 0)
        {
            // No type parameters, return decorators as-is
            return decorators;
        }

        return CloseDecoratorsForServiceType(decorators, serviceType);
    }

    /// <summary>
    /// Substitutes type parameters in a decorator and processes its constructor parameters and injection members.
    /// </summary>
    private static TypeData SubstituteDecoratorTypeParams(
        GenericTypeData genericDecorator,
        Dictionary<string, string> typeArgMap)
    {
        var decorator = genericDecorator;
        var decoratorTypeParams = genericDecorator.TypeParameters;

        // Substitute in decorator type name
        var closedDecoratorName = SubstituteTypeArguments(decorator.Name, typeArgMap);
        var closedDecoratorNameWithoutGeneric = genericDecorator.NameWithoutGeneric;

        // Substitute in constructor parameters
        var constructorParams = decorator.ConstructorParameters;
        ImmutableEquatableArray<ParameterData>? closedConstructorParams = null;
        if(constructorParams is not null && constructorParams.Length > 0)
        {
            var newParams = new List<ParameterData>(constructorParams.Length);
            foreach(var param in constructorParams)
            {
                // Use SubstituteTypeData to recursively substitute type parameters
                // This ensures nested generics like ILogger<HandlerDecorator1<TRequest, TResponse>>
                // get their TypeParameters correctly substituted
                var substitutedType = SubstituteTypeData(param.Type, typeArgMap);
                var newParamType = RehydrateTypeData(substitutedType, IsNestedOpenGeneric: false);
                newParams.Add(param with { Type = newParamType });
            }
            closedConstructorParams = newParams.ToImmutableEquatableArray();
        }

        // Substitute in injection members (properties, fields, methods with [IocInject] attributes)
        var injectionMembers = decorator.InjectionMembers;
        ImmutableEquatableArray<InjectionMemberData>? closedInjectionMembers = null;
        if(injectionMembers is not null && injectionMembers.Length > 0)
        {
            var newMembers = new List<InjectionMemberData>(injectionMembers.Length);
            foreach(var member in injectionMembers)
            {
                var newMember = SubstituteInjectionMember(member, typeArgMap);
                newMembers.Add(newMember);
            }
            closedInjectionMembers = newMembers.ToImmutableEquatableArray();
        }

        // Substitute in type parameters
        var newTypeParams = SubstituteTypeParameters(decoratorTypeParams, typeArgMap);

        return TypeData.CreateGeneric(
            closedDecoratorName,
            closedDecoratorNameWithoutGeneric,
            IsOpenGeneric: false,
            genericDecorator.GenericArity,
            IsNestedOpenGeneric: false,
            TypeParameters: newTypeParams,
            ConstructorParameters: closedConstructorParams,
            HasInjectConstructor: false,
            InjectionMembers: closedInjectionMembers);
    }

    /// <summary>
    /// Substitutes type parameters in an injection member.
    /// </summary>
    private static InjectionMemberData SubstituteInjectionMember(
        InjectionMemberData member,
        Dictionary<string, string> typeArgMap)
    {
        // For properties and fields, substitute the member type
        TypeData? newType = null;
        if(member.Type is not null)
        {
            var substitutedType = SubstituteTypeData(member.Type, typeArgMap);
            newType = RehydrateTypeData(substitutedType, IsNestedOpenGeneric: false);
        }

        // For methods, substitute each parameter type
        ImmutableEquatableArray<ParameterData>? newParams = null;
        if(member.Parameters is not null && member.Parameters.Length > 0)
        {
            var paramList = new List<ParameterData>(member.Parameters.Length);
            foreach(var param in member.Parameters)
            {
                var substitutedType = SubstituteTypeData(param.Type, typeArgMap);
                var newParamType = RehydrateTypeData(substitutedType, IsNestedOpenGeneric: false);
                paramList.Add(param with { Type = newParamType });
            }
            newParams = paramList.ToImmutableEquatableArray();
        }

        return member with { Type = newType, Parameters = newParams };
    }
}
