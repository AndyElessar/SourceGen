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
        var threadSafeStrategy = ThreadSafeStrategy.Lock;
        var eagerResolveOptions = EagerResolveOptions.Singleton;
        ImmutableEquatableArray<string>? includeTags = null;

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

                case "IncludeTags":
                    if(!namedArg.Value.IsNull && namedArg.Value.Values.Length > 0)
                    {
                        includeTags = namedArg.Value.Values
                            .Where(static v => v.Value is string)
                            .Select(static v => (string)v.Value!)
                            .ToImmutableEquatableArray();
                    }
                    break;

                case "UseSwitchStatement":
                    if(namedArg.Value.Value is bool switchValue)
                        useSwitchStatement = switchValue;
                    break;

                case "ThreadSafeStrategy":
                    if(namedArg.Value.Value is int strategyValue)
                        threadSafeStrategy = (ThreadSafeStrategy)strategyValue;
                    break;

                case "EagerResolveOptions":
                    if(namedArg.Value.Value is int eagerValue)
                        eagerResolveOptions = (EagerResolveOptions)eagerValue;
                    break;
            }
        }

        // Extract imported modules from IocImportModuleAttribute on the container class
        var importedModules = ExtractImportedModules(typeSymbol);

        // Extract explicit registrations if ExplicitOnly mode is enabled
        var explicitRegistrations = explicitOnly
            ? ExtractExplicitRegistrations(typeSymbol, ctx.SemanticModel)
            : [];

        // Extract user-declared partial methods/properties for direct service resolution
        var partialAccessors = ExtractPartialAccessors(typeSymbol, ctx.SemanticModel);

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
            includeTags ?? [],
            useSwitchStatement,
            threadSafeStrategy,
            eagerResolveOptions,
            importedModules,
            explicitRegistrations,
            partialAccessors);
    }

    /// <summary>
    /// Extracts user-declared partial methods and partial properties from the container class.
    /// These serve as fast-path accessors for direct service resolution.
    /// </summary>
    /// <remarks>
    /// Partial methods must: be partial definitions, return non-void, have no parameters, and not be generic.
    /// Partial properties must: be partial definitions with a getter.
    /// The [IocInject]/[Inject] attribute is optional and only used to specify a keyed service key.
    /// </remarks>
    private static ImmutableEquatableArray<PartialAccessorData> ExtractPartialAccessors(
        INamedTypeSymbol containerSymbol,
        SemanticModel semanticModel)
    {
        var accessors = new List<PartialAccessorData>();

        foreach(var member in containerSymbol.GetMembers())
        {
            // Skip static members
            if(member.IsStatic)
                continue;

            switch(member)
            {
                case IMethodSymbol method
                    when method.IsPartialDefinition
                         && !method.ReturnsVoid
                         && method.Parameters.Length == 0
                         && !method.IsGenericMethod
                         && method.MethodKind == MethodKind.Ordinary:
                {
                    var returnType = method.ReturnType;
                    var returnTypeName = returnType.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                        .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var isNullable = returnType.NullableAnnotation == NullableAnnotation.Annotated;
                    var key = ExtractKeyFromInjectAttribute(method, semanticModel);

                    accessors.Add(new PartialAccessorData(
                        PartialAccessorKind.Method,
                        method.Name,
                        returnTypeName,
                        isNullable,
                        key));
                    break;
                }

                case IPropertySymbol property
                    when property.IsPartialDefinition
                         && property.GetMethod is not null:
                {
                    var returnType = property.Type;
                    var returnTypeName = returnType.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                        .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var isNullable = returnType.NullableAnnotation == NullableAnnotation.Annotated;
                    var key = ExtractKeyFromInjectAttribute(property, semanticModel);

                    accessors.Add(new PartialAccessorData(
                        PartialAccessorKind.Property,
                        property.Name,
                        returnTypeName,
                        isNullable,
                        key));
                    break;
                }
            }
        }

        return accessors.ToImmutableEquatableArray();
    }

    /// <summary>
    /// Extracts the service key from [IocInject]/[Inject] attribute on a member, if present.
    /// </summary>
    private static string? ExtractKeyFromInjectAttribute(ISymbol member, SemanticModel semanticModel)
    {
        foreach(var attr in member.GetAttributes())
        {
            if(attr.AttributeClass?.IsInject == true)
            {
                var (key, _) = attr.GetKey(semanticModel);
                return key;
            }
        }

        return null;
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

            // For generic types, use metadata name format for comparison
            var metadataName = attrClass.IsGenericType
                ? attrClass.OriginalDefinition.MetadataName
                : attrClass.MetadataName;
            var metadataNamespace = attrClass.ContainingNamespace?.ToDisplayString() ?? "";
            var originalFullName = string.IsNullOrEmpty(metadataNamespace)
                ? metadataName
                : $"{metadataNamespace}.{metadataName}";

            if(fullName == Constants.IocImportModuleAttributeFullName)
            {
                // Non-generic: ModuleType is in constructor argument
                if(attr.ConstructorArguments.Length > 0 &&
                   attr.ConstructorArguments[0].Value is INamedTypeSymbol moduleType)
                {
                    modules.Add(moduleType.GetTypeData());
                }
            }
            else if(originalFullName == Constants.IocImportModuleAttributeFullName_T1)
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

            // For generic types, use metadata name format (e.g., IocRegisterForAttribute`1) for comparison
            var metadataName = attrClass.IsGenericType
                ? attrClass.OriginalDefinition.MetadataName
                : attrClass.MetadataName;
            var metadataNamespace = attrClass.ContainingNamespace?.ToDisplayString() ?? "";
            var originalFullName = string.IsNullOrEmpty(metadataNamespace)
                ? metadataName
                : $"{metadataNamespace}.{metadataName}";

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
