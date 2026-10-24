namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    private static RegistrationData? TransformRegister(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if(ctx.TargetSymbol is not INamedTypeSymbol typeSymbol)
            return null;

        if(ctx.Attributes.Length == 0)
            return null;

        return ExtractRegistrationData(typeSymbol, ctx.Attributes[0], ctx.SemanticModel);
    }

    /// <summary>
    /// Transforms generic IocRegisterAttribute (e.g., IocRegisterAttribute&lt;T&gt;) to extract registration data.
    /// The service types are specified via type parameters instead of constructor arguments.
    /// </summary>
    private static RegistrationData? TransformRegisterGeneric(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if(ctx.TargetSymbol is not INamedTypeSymbol typeSymbol)
            return null;

        if(ctx.Attributes.Length == 0)
            return null;

        return ExtractRegistrationDataFromGenericAttribute(typeSymbol, ctx.Attributes[0], ctx.SemanticModel);
    }

    private static IEnumerable<RegistrationData> TransformRegisterFor(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        foreach(var attr in ctx.Attributes)
        {
            ct.ThrowIfCancellationRequested();

            if(attr.ConstructorArguments.Length == 0)
                continue;
            if(attr.ConstructorArguments[0].Value is not INamedTypeSymbol targetType)
                continue;

            var data = ExtractRegistrationData(targetType, attr, ctx.SemanticModel);

            yield return data;
        }
    }

    /// <summary>
    /// Transforms generic IoCRegisterForAttribute (IoCRegisterForAttribute&lt;T&gt;) to extract registration data.
    /// The target type is specified via type parameter instead of constructor argument.
    /// </summary>
    private static IEnumerable<RegistrationData> TransformRegisterForGeneric(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        foreach(var attr in ctx.Attributes)
        {
            ct.ThrowIfCancellationRequested();

            var attrClass = attr.AttributeClass;
            if(attrClass?.IsGenericType != true || attrClass.TypeArguments.Length == 0)
                continue;

            if(attrClass.TypeArguments[0] is not INamedTypeSymbol targetType)
                continue;

            // Use ExtractRegistrationData because IoCRegisterForAttribute<T> uses ServiceTypes named argument,
            // not the generic type parameter, for specifying service types.
            var data = ExtractRegistrationData(targetType, attr, ctx.SemanticModel);

            yield return data;
        }
    }
}
