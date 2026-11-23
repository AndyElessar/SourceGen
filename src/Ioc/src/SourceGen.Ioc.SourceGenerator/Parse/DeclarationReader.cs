using SourceGen.Ioc.Model;

namespace SourceGen.Ioc.Parse;

/// <summary>Reads registration declarations from the attributes (Annotations.spec, "Registration declarations").</summary>
internal static class DeclarationReader
{
    /// <summary>The metadata name of <c>IocRegisterAttribute</c>.</summary>
    public const string RegisterAttribute = "SourceGen.Ioc.IocRegisterAttribute";

    /// <summary>The metadata name of <c>IocRegisterForAttribute</c>.</summary>
    public const string RegisterForAttribute = "SourceGen.Ioc.IocRegisterForAttribute";

    private const string InjectAttribute = "SourceGen.Ioc.IocInjectAttribute";
    private const string ActivatorUtilitiesConstructorAttribute = "Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructorAttribute";

    /// <summary>Reads each <c>IocRegisterAttribute</c> on a class.</summary>
    /// <param name="context">The context of the attribute.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The valid declarations. An invalid one is skipped.</returns>
    public static ImmutableEquatableArray<RegistrationDeclaration> ReadRegister(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        List<RegistrationDeclaration> declarations = [];
        foreach(AttributeData attribute in context.Attributes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(attribute.ConstructorArguments is [{ Value: int lifetime }]
                && Read((INamedTypeSymbol)context.TargetSymbol, lifetime, attribute, context.SemanticModel.Compilation) is { } declaration)
            {
                declarations.Add(declaration);
            }
        }

        return declarations.ToImmutableEquatableArray();
    }

    /// <summary>Reads each <c>IocRegisterForAttribute</c> on the assembly.</summary>
    /// <param name="context">The context of the attribute.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The valid declarations. An invalid one is skipped.</returns>
    public static ImmutableEquatableArray<RegistrationDeclaration> ReadRegisterFor(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        List<RegistrationDeclaration> declarations = [];
        foreach(AttributeData attribute in context.Attributes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(attribute.ConstructorArguments is [{ Value: INamedTypeSymbol implementation }, { Value: int lifetime }]
                && Read(implementation, lifetime, attribute, context.SemanticModel.Compilation) is { } declaration)
            {
                declarations.Add(declaration);
            }
        }

        return declarations.ToImmutableEquatableArray();
    }

    private static RegistrationDeclaration? Read(INamedTypeSymbol implementation, int lifetime, AttributeData attribute, Compilation compilation)
    {
        // The checks with diagnostics come in a later slice. Until then an unsupported declaration is skipped.
        if(implementation is not { TypeKind: TypeKind.Class, IsAbstract: false, IsStatic: false, IsGenericType: false }
            || lifetime is < 0 or > 2
            || TypeRefFactory.Create(implementation, compilation) is not NamedTypeRef implementationRef
            || SelectConstructor(implementation, compilation) is not { } constructor)
        {
            return null;
        }

        List<TypeRef> serviceTypes = [];
        foreach(KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            if(argument.Key != "ServiceTypes" || argument.Value.IsNull)
            {
                continue;
            }

            foreach(TypedConstant element in argument.Value.Values)
            {
                // A value type is SGIOC004, which a later slice reports. Until then the declaration is skipped
                if(element.Value is not INamedTypeSymbol { IsUnboundGenericType: false, IsValueType: false } serviceType
                    || TypeRefFactory.Create(serviceType, compilation) is not { } serviceRef)
                {
                    return null;
                }

                serviceTypes.Add(serviceRef);
            }
        }

        List<TypeRef> parameters = [];
        foreach(IParameterSymbol parameter in constructor.Parameters)
        {
            // The generated argument is a value: a ref, out, or ref readonly parameter cannot take it. An in parameter can
            if(parameter.RefKind is RefKind.Ref or RefKind.Out or RefKind.RefReadOnlyParameter
                || TypeRefFactory.Create(parameter.Type, compilation) is not { } parameterRef)
            {
                return null;
            }

            parameters.Add(parameterRef);
        }

        return new RegistrationDeclaration(implementationRef, (Lifetime)lifetime, serviceTypes.ToImmutableEquatableArray(), parameters.ToImmutableEquatableArray());
    }

    /// <summary>Selects the constructor (Annotations.spec, "Constructor selection").</summary>
    /// <returns>The constructor, or <see langword="null"/> when no single constructor can be selected.</returns>
    private static IMethodSymbol? SelectConstructor(INamedTypeSymbol type, Compilation compilation)
    {
        IMethodSymbol? marked = null;
        foreach(IMethodSymbol constructor in type.InstanceConstructors)
        {
            if(constructor.GetAttributes().Any(static a => a.AttributeClass is { Name: "IocInjectAttribute" or "ActivatorUtilitiesConstructorAttribute" } attributeClass
                && attributeClass.ToDisplayString() is InjectAttribute or ActivatorUtilitiesConstructorAttribute))
            {
                if(marked is not null)
                {
                    return null;
                }

                marked = constructor;
            }
        }

        if(marked is not null)
        {
            return marked;
        }

        // A primary constructor is found from syntax, so only for a type of this compilation. The test is the assembly,
        // not the availability of syntax: a project reference in the IDE also gives syntax. An implicitly declared
        // constructor (the default constructor, the copy constructor of a record) has no syntax, so it never matches.
        if(SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly))
        {
            foreach(IMethodSymbol constructor in type.InstanceConstructors)
            {
                if(constructor.DeclaringSyntaxReferences.Any(static r => r.GetSyntax() is TypeDeclarationSyntax))
                {
                    return constructor;
                }
            }
        }

        IMethodSymbol[] publicConstructors = [.. type.InstanceConstructors.Where(static c => c.DeclaredAccessibility == Accessibility.Public)];
        return publicConstructors.Length == 1 ? publicConstructors[0] : null;
    }
}
