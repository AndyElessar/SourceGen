using static SourceGen.Ioc.SourceGenerator.Models.Constants;

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
        var serviceTypeName = registration.ServiceType.Name;

        // A non-keyed chain is registered under a hidden key, so Lazy/Func wrappers can resolve this registration's
        // decorated instance; the service type forwards to it, sharing the instance. The body still resolves non-keyed.
        var decoratedServiceKey = registration.Key is null ? GetDecoratedServiceKey(registration) : null;
        writer.WriteServiceLambdaOpen(lifetime, serviceTypeName, decoratedServiceKey ?? registration.Key);

        writer.WriteLine("{");
        writer.Indentation++;

        var methodName = GetServiceResolutionMethod(registration.Key, isOptional: false);
        writer.WriteLine($"var s0 = sp.{methodName}<{registration.ImplementationType.Name}>({(registration.Key is not null ? "key" : "")});");

        var outermostVar = WriteDecoratorChain(writer, registration, asyncInitServiceTypeNames);
        writer.WriteLine($"return {outermostVar};");

        writer.Indentation--;
        writer.WriteLine("});");

        if(decoratedServiceKey is not null)
        {
            writer.WriteLine($"services.Add{lifetime}<{serviceTypeName}>(({IServiceProviderGlobalTypeName} sp) => sp.{GetRequiredKeyedService}<{serviceTypeName}>({decoratedServiceKey}));");
        }
    }

    /// <summary>
    /// Writes the statements wrapping the instance in variable <c>s0</c> with <paramref name="registration"/>'s decorators,
    /// the last decorator outermost, and returns the variable holding the outermost instance (<c>s{n}</c>).
    /// Decorator dependencies are resolved from <c>sp</c> (and <c>key</c> for keyed registrations).
    /// </summary>
    private static string WriteDecoratorChain(SourceWriter writer, ServiceRegistrationModel registration, ImmutableEquatableSet<string>? asyncInitServiceTypeNames)
    {
        var decorators = registration.Decorators;
        var decoratorCount = decorators.Length;

        var serviceTypeParams = registration.ServiceType is GenericTypeData genericServiceType
            ? genericServiceType.TypeParameters
            : null;

        var serviceTypeNames = BuildServiceTypeNames(registration);

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

        return $"s{decoratorCount}";
    }

    /// <summary>
    /// Gets the hidden service key (a C# string literal) of a non-keyed decorated registration's decorator chain.
    /// </summary>
    private static string GetDecoratedServiceKey(ServiceRegistrationModel registration)
    {
        var key = $"SourceGen.Ioc.Decorated:{registration.ServiceType.Name}|{registration.ImplementationType.Name}".Replace("global::", "");
        return $"\"{key.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }

    /// <summary>
    /// Gets the expression (using <c>sp</c>, and <c>key</c> for keyed registrations) that a standalone
    /// <c>Lazy&lt;T&gt;</c> / <c>Func&lt;T&gt;</c> registration uses to resolve <paramref name="registration"/>'s instance.
    /// </summary>
    /// <remarks>
    /// The implementation type identifies the registration among several of one service type, but it is undecorated;
    /// a decorated registration is resolved through its hidden key, or its own key when keyed.
    /// </remarks>
    private static string GetWrapperResolveCall(ServiceRegistrationModel registration)
    {
        var hasDecorators = registration.Decorators.Length > 0;
        return (hasDecorators, registration.Key) switch
        {
            (true, null) => $"sp.{GetRequiredKeyedService}<{registration.ServiceType.Name}>({GetDecoratedServiceKey(registration)})",
            (true, _) => $"sp.{GetRequiredKeyedService}<{registration.ServiceType.Name}>(key)",
            (false, null) => $"sp.{GetRequiredService}<{registration.ImplementationType.Name}>()",
            (false, _) => $"sp.{GetRequiredKeyedService}<{registration.ImplementationType.Name}>(key)",
        };
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

        var typeArguments = new Dictionary<string, string>(decoratorTypeParams.Length, StringComparer.Ordinal);
        for(int i = 0; i < decoratorTypeParams.Length; i++)
        {
            typeArguments[decoratorTypeParams[i].ParameterName] = serviceTypeParams[i].Type.Name;
        }

        return SubstituteTypeArguments(paramType.Name, typeArguments);
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
