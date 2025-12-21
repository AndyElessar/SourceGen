namespace SourceGen.Ioc.SourceGenerator.Register;

partial class RegisterSourceGenerator
{
    private static RegistrationData? TransformRegister(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        if(ctx.TargetSymbol is not INamedTypeSymbol typeSymbol)
            return null;

        var attributeData = ctx.Attributes.FirstOrDefault();
        if(attributeData == null)
            return null;

        return ExtractRegistrationData(typeSymbol, attributeData);
    }

    private static IEnumerable<RegistrationData> TransformRegisterFor(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        foreach(var attr in ctx.Attributes)
        {
            if(attr.ConstructorArguments.Length == 0)
                continue;
            if(attr.ConstructorArguments[0].Value is not INamedTypeSymbol targetType)
                continue;

            var data = ExtractRegistrationData(targetType, attr);

            yield return data;
        }
    }

    private static RegistrationData ExtractRegistrationData(INamedTypeSymbol typeSymbol, AttributeData attributeData)
    {
        var implementationType = typeSymbol.GetTypeData();
        var lifetime = attributeData.GetNamedArgument<int>("Lifetime", 0);
        var registerAllInterfaces = attributeData.GetNamedArgument<bool>("RegisterAllInterfaces", false);
        var registerAllBaseClasses = attributeData.GetNamedArgument<bool>("RegisterAllBaseClasses", false);
        var serviceTypes = attributeData.GetTypeArrayArgument("ServiceTypes");

        var keyType = attributeData.GetNamedArgument<int>("KeyType", 0);
        string? key = null;
        foreach(var namedArg in attributeData.NamedArguments)
        {
            if(namedArg.Key == "Key")
            {
                if(namedArg.Value.IsNull)
                {
                    key = null;
                }
                else
                {
                    if(keyType == 1) // KeyType.Csharp
                    {
                        key = namedArg.Value.Value?.ToString();
                    }
                    else
                    {
                        key = namedArg.Value.GetPrimitiveConstantString();
                        keyType = 1; // Treat as CSharp code
                    }
                }
                break;
            }
        }

        var hasExplicitLifetime = attributeData.HasNamedArgument("Lifetime");
        var hasExplicitRegisterAllInterfaces = attributeData.HasNamedArgument("RegisterAllInterfaces");
        var hasExplicitRegisterAllBaseClasses = attributeData.HasNamedArgument("RegisterAllBaseClasses");

        var allInterfaces = typeSymbol.GetAllInterfaces();
        var allBaseClasses = typeSymbol.GetAllBaseClasses();

        return new RegistrationData(
            implementationType,
            lifetime,
            registerAllInterfaces,
            registerAllBaseClasses,
            serviceTypes,
            key,
            keyType,
            allInterfaces,
            allBaseClasses,
            hasExplicitLifetime,
            hasExplicitRegisterAllInterfaces,
            hasExplicitRegisterAllBaseClasses);
    }
}
