namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Transforms IocContainerAttribute to extract container configuration data.
    /// </summary>
    private static ContainerModel? TransformContainer(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if(ctx.TargetSymbol is not INamedTypeSymbol typeSymbol)
            return null;

        // Verify the class is partial
        var syntaxNode = ctx.TargetNode;
        if(syntaxNode is not ClassDeclarationSyntax classDeclaration)
            return null;

        // Check if the class is declared as partial
        if(!classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            return null;
        }

        var attributeData = ctx.Attributes.FirstOrDefault();
        if(attributeData is null)
            return null;

        // Extract container options from attribute
        var resolveIServiceCollection = true;
        var explicitOnly = false;
        var useSwitchStatement = false;

        foreach(var namedArg in attributeData.NamedArguments)
        {
            switch(namedArg.Key)
            {
                case "ResolveIServiceCollection":
                    if(namedArg.Value.Value is bool resolveValue)
                        resolveIServiceCollection = resolveValue;
                    break;

                case "ExplicitOnly":
                    if(namedArg.Value.Value is bool explicitValue)
                        explicitOnly = explicitValue;
                    break;

                case "UseSwitchStatement":
                    if(namedArg.Value.Value is bool switchValue)
                        useSwitchStatement = switchValue;
                    break;
            }
        }

        // Extract imported modules from IocImportModuleAttribute on the container class
        var importedModules = ExtractImportedModules(typeSymbol);

        // Extract explicit registrations if ExplicitOnly mode is enabled
        var explicitRegistrations = explicitOnly
            ? ExtractExplicitRegistrations(typeSymbol, ctx.SemanticModel)
            : [];

        // Get container type information
        var containerTypeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var containerNamespace = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : typeSymbol.ContainingNamespace.ToDisplayString();
        var className = typeSymbol.Name;

        return new ContainerModel(
            containerTypeName,
            containerNamespace,
            className,
            resolveIServiceCollection,
            explicitOnly,
            useSwitchStatement,
            importedModules,
            explicitRegistrations);
    }

    /// <summary>
    /// Extracts imported module types from IocImportModuleAttribute on the container class.
    /// </summary>
    private static ImmutableEquatableArray<TypeData> ExtractImportedModules(INamedTypeSymbol containerSymbol)
    {
        var modules = new List<TypeData>();

        foreach(var attr in containerSymbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if(attrClass is null)
                continue;

            // Check for IocImportModuleAttribute (non-generic)
            var fullName = attrClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if(fullName.StartsWith("global::", StringComparison.Ordinal))
                fullName = fullName[8..];

            if(fullName == Constants.IocImportModuleAttributeFullName)
            {
                // Non-generic: ModuleType is in constructor argument
                if(attr.ConstructorArguments.Length > 0 &&
                   attr.ConstructorArguments[0].Value is INamedTypeSymbol moduleType)
                {
                    modules.Add(moduleType.GetTypeData());
                }
            }
            else if(fullName == Constants.IocImportModuleAttributeFullName_T1 ||
                    (attrClass.IsGenericType && attrClass.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Contains("IocImportModuleAttribute")))
            {
                // Generic: ModuleType is the type argument
                if(attrClass.IsGenericType && attrClass.TypeArguments.Length > 0 &&
                   attrClass.TypeArguments[0] is INamedTypeSymbol genericModuleType)
                {
                    modules.Add(genericModuleType.GetTypeData());
                }
            }
        }

        return modules.ToImmutableEquatableArray();
    }

    /// <summary>
    /// Extracts explicit registrations from attributes on the container class.
    /// This includes IocRegisterForAttribute and IocRegisterDefaultsAttribute.
    /// </summary>
    private static ImmutableEquatableArray<RegistrationData> ExtractExplicitRegistrations(
        INamedTypeSymbol containerSymbol,
        SemanticModel semanticModel)
    {
        var registrations = new List<RegistrationData>();

        foreach(var attr in containerSymbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if(attrClass is null)
                continue;

            var fullName = attrClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if(fullName.StartsWith("global::", StringComparison.Ordinal))
                fullName = fullName[8..];

            var originalFullName = attrClass.IsGenericType
                ? attrClass.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                : fullName;
            if(originalFullName.StartsWith("global::", StringComparison.Ordinal))
                originalFullName = originalFullName[8..];

            // Handle IocRegisterForAttribute (non-generic)
            if(fullName == Constants.IocRegisterForAttributeFullName)
            {
                if(attr.ConstructorArguments.Length > 0 &&
                   attr.ConstructorArguments[0].Value is INamedTypeSymbol targetType)
                {
                    var data = ExtractRegistrationData(targetType, attr, semanticModel);
                    registrations.Add(data);
                }
            }
            // Handle IocRegisterForAttribute<T> (generic)
            else if(originalFullName == Constants.IocRegisterForAttributeFullName_T1)
            {
                if(attrClass.IsGenericType && attrClass.TypeArguments.Length > 0 &&
                   attrClass.TypeArguments[0] is INamedTypeSymbol targetType)
                {
                    var data = ExtractRegistrationData(targetType, attr, semanticModel);
                    registrations.Add(data);
                }
            }
        }

        return registrations.ToImmutableEquatableArray();
    }
}
