namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Writes decorator pattern registration code.
    /// </summary>
    /// <remarks>
    /// Generates code like:
    /// <code>
    /// services.AddSingleton&lt;IMyService&gt;((IServiceProvider sp) =>
    /// {
    ///     var s0 = sp.GetRequiredService&lt;MyService&gt;();
    ///     var s1_p0 = sp.GetRequiredService&lt;ILogger&lt;MyServiceDecorator2&gt;&gt;();
    ///     var s1 = new MyServiceDecorator2(s1_p0, s0);
    ///     var s2_p0 = sp.GetRequiredService&lt;ILogger&lt;MyServiceDecorator&gt;&gt;();
    ///     var s2 = new MyServiceDecorator(s2_p0, s1);
    ///     return s2;
    /// });
    /// </code>
    /// For open generic decorators, falls back to ActivatorUtilities.CreateInstance.
    /// </remarks>
    private static void WriteDecoratorRegistration(SourceWriter writer, ServiceRegistrationModel registration, string lifetime, ImmutableEquatableSet<string>? asyncInitServiceTypeNames = null)
    {
        var decorators = registration.Decorators;
        var decoratorCount = decorators.Length;

        var serviceTypeParams = registration.ServiceType is GenericTypeData genericServiceType
            ? genericServiceType.TypeParameters
            : null;
        var serviceTypeName = registration.ServiceType.Name;

        var serviceTypeNames = BuildServiceTypeNames(registration);

        writer.WriteServiceLambdaOpen(lifetime, serviceTypeName, registration.Key);

        writer.WriteLine("{");
        writer.Indentation++;

        var methodName = GetServiceResolutionMethod(registration.Key, isOptional: false);
        var resolveCall = registration.Key is not null
            ? $"sp.{methodName}<{registration.ImplementationType.Name}>(key)"
            : $"sp.{methodName}<{registration.ImplementationType.Name}>()";
        writer.WriteLine($"var s0 = {resolveCall};");

        for(int i = 0; i < decoratorCount; i++)
        {
            var decorator = decorators[decoratorCount - 1 - i];
            var prevVar = $"s{i}";
            var currentVar = $"s{i + 1}";

            var decoratorTypeName = GetClosedDecoratorTypeName(decorator, serviceTypeParams);
            var ctorParams = decorator.ConstructorParameters;
            if(ctorParams is null || ctorParams.Length == 0)
            {
                writer.WriteLine($"var {currentVar} = global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<{decoratorTypeName}>(sp, {prevVar});");
                continue;
            }

            bool isKeyedRegistration = registration.Key is not null;
            WriteConstructInstanceWithInjection(
                writer,
                instanceVarName: currentVar,
                implTypeName: decoratorTypeName,
                constructorParams: ctorParams,
                injectionMembers: decorator.InjectionMembers ?? [],
                isKeyedRegistration: isKeyedRegistration,
                registrationKey: registration.Key,
                serviceTypeNames: serviceTypeNames,
                ctorTypeNameResolver: t => decorator is GenericTypeData { IsOpenGeneric: true } && serviceTypeParams is not null ? SubstituteGenericArguments(t, decorator, serviceTypeParams) : t.Name,
                memberTypeNameResolver: t => decorator is GenericTypeData { IsOpenGeneric: true } && serviceTypeParams is not null ? SubstituteGenericArguments(t, decorator, serviceTypeParams) : t.Name,
                decoratedPrevVar: prevVar,
                asyncInitServiceTypeNames: asyncInitServiceTypeNames);
        }

        writer.WriteLine($"return s{decoratorCount};");

        writer.Indentation--;
        writer.WriteLine("});");
    }

    /// <summary>
    /// Gets the closed decorator type name by substituting generic arguments if the decorator is an open generic.
    /// </summary>
    private static string GetClosedDecoratorTypeName(TypeData decorator, ImmutableEquatableArray<TypeParameter>? serviceTypeParams)
    {
        if(decorator is not GenericTypeData { IsOpenGeneric: true } genericDecorator)
        {
            return decorator.Name;
        }

        if(serviceTypeParams is null || serviceTypeParams.Length == 0)
        {
            return decorator.Name;
        }

        return $"{genericDecorator.NameWithoutGeneric}<{string.Join(", ", serviceTypeParams.Select(a => a.Type.Name))}>";
    }

    /// <summary>
    /// Substitutes generic type parameters in a parameter type with actual generic arguments.
    /// </summary>
    private static string SubstituteGenericArguments(TypeData paramType, TypeData decorator, ImmutableEquatableArray<TypeParameter> serviceTypeParams)
    {
        var decoratorTypeParams = decorator is GenericTypeData genericDecorator
            ? genericDecorator.TypeParameters
            : null;
        if(decoratorTypeParams is null || decoratorTypeParams.Length == 0)
        {
            return paramType.Name;
        }

        if(serviceTypeParams.Length != decoratorTypeParams.Length)
        {
            return paramType.Name;
        }

        var result = paramType.Name;
        for(int i = 0; i < decoratorTypeParams.Length; i++)
        {
            result = ReplaceTypeParameter(result, decoratorTypeParams[i].ParameterName, serviceTypeParams[i].Type.Name);
        }

        return result;
    }

    /// <summary>
    /// Builds a set of service type names for IsServiceParameter check.
    /// </summary>
    private static HashSet<string> BuildServiceTypeNames(ServiceRegistrationModel registration)
    {
        var serviceTypeNames = new HashSet<string>(StringComparer.Ordinal);

        AddTypeNameVariants(serviceTypeNames, registration.ServiceType);
        AddTypeNameVariants(serviceTypeNames, registration.ImplementationType);

        if(registration.ImplementationType.AllBaseClasses is not null)
        {
            foreach(var baseClass in registration.ImplementationType.AllBaseClasses)
            {
                AddTypeNameVariants(serviceTypeNames, baseClass);
            }
        }

        if(registration.ImplementationType.AllInterfaces is not null)
        {
            foreach(var iface in registration.ImplementationType.AllInterfaces)
            {
                AddTypeNameVariants(serviceTypeNames, iface);
            }
        }

        return serviceTypeNames;
    }

    /// <summary>
    /// Adds both the full name and non-generic name variants to the set.
    /// </summary>
    private static void AddTypeNameVariants(HashSet<string> set, TypeData type)
    {
        set.Add(type.Name);
        if(type is GenericTypeData genericType && type.Name != genericType.NameWithoutGeneric)
        {
            set.Add(genericType.NameWithoutGeneric);
        }
    }

    /// <summary>
    /// Checks if a parameter type matches any of the service types.
    /// </summary>
    private static bool IsServiceTypeParameter(TypeData paramType, string substitutedTypeName, HashSet<string> serviceTypeNames)
    {
        if(serviceTypeNames.Contains(substitutedTypeName))
        {
            return true;
        }

        if(serviceTypeNames.Contains(paramType.Name))
        {
            return true;
        }

        return paramType is GenericTypeData genericParamType
            && serviceTypeNames.Contains(genericParamType.NameWithoutGeneric);
    }
}
