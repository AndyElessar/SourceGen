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
        var integrateServiceProvider = true;
        var explicitOnly = false;
        var useSwitchStatement = false;
        var threadSafeStrategy = ThreadSafeStrategy.Lock;
        var eagerResolveOptions = EagerResolveOptions.Singleton;
        ImmutableEquatableArray<string>? includeTags = null;

        foreach(var namedArg in attributeData.NamedArguments)
        {
            switch(namedArg.Key)
            {
                case "IntegrateServiceProvider":
                    if(namedArg.Value.Value is bool integrateValue)
                        integrateServiceProvider = integrateValue;
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

        // [IocDiscover] on an ExplicitOnly container closes its open generic registrations
        var explicitDiscoveredDependencies = explicitOnly
            ? ExtractDiscoveredDependencies(typeSymbol)
            : [];

        // Extract user-declared partial methods/properties for direct service resolution
        var partialAccessors = ExtractPartialAccessors(typeSymbol, ctx.SemanticModel);

        // Get container type information
        var containerTypeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var containerNamespace = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : typeSymbol.ContainingNamespace.ToDisplayString();
        var className = typeSymbol.Name;

        var controllerActivatorType = ctx.SemanticModel.Compilation
            .GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.Controllers.IControllerActivator");
        var implementControllerActivator = controllerActivatorType is not null
            && typeSymbol.AllInterfaces.Contains(controllerActivatorType, SymbolEqualityComparer.Default);

        var componentActivatorType = ctx.SemanticModel.Compilation
            .GetTypeByMetadataName("Microsoft.AspNetCore.Components.IComponentActivator");
        var implementComponentActivator = componentActivatorType is not null
            && typeSymbol.AllInterfaces.Contains(componentActivatorType, SymbolEqualityComparer.Default);

        var componentPropertyActivatorType = ctx.SemanticModel.Compilation
            .GetTypeByMetadataName("Microsoft.AspNetCore.Components.IComponentPropertyActivator");
        var implementComponentPropertyActivator = componentPropertyActivatorType is not null
            && typeSymbol.AllInterfaces.Contains(componentPropertyActivatorType, SymbolEqualityComparer.Default);

        return new ContainerModel(
            containerTypeName,
            containerNamespace,
            className,
            integrateServiceProvider,
            explicitOnly,
            includeTags ?? [],
            useSwitchStatement,
            threadSafeStrategy,
            eagerResolveOptions,
            importedModules,
            explicitRegistrations,
            explicitDiscoveredDependencies,
            partialAccessors,
            implementControllerActivator,
            implementComponentActivator,
            implementComponentPropertyActivator,
            GetDeclaredMemberNames(typeSymbol));
    }

    /// <summary>
    /// Gets the names of the members declared on the container class and its base classes (except <see cref="object"/>),
    /// sorted so the model stays equatable regardless of declaration order.
    /// </summary>
    private static ImmutableEquatableArray<string> GetDeclaredMemberNames(INamedTypeSymbol containerSymbol)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        for(var type = containerSymbol; type is not null && type.SpecialType != SpecialType.System_Object; type = type.BaseType)
        {
            names.UnionWith(type.MemberNames);
        }

        return names.OrderBy(static name => name, StringComparer.Ordinal).ToImmutableEquatableArray();
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
                    accessors.Add(CreatePartialAccessor(PartialAccessorKind.Method, method, method.ReturnType, semanticModel));
                    break;

                case IPropertySymbol property
                    when property.IsPartialDefinition
                         && property.GetMethod is not null:
                    accessors.Add(CreatePartialAccessor(PartialAccessorKind.Property, property, property.Type, semanticModel));
                    break;
            }
        }

        return accessors.ToImmutableEquatableArray();
    }

    private static PartialAccessorData CreatePartialAccessor(
        PartialAccessorKind kind,
        ISymbol member,
        ITypeSymbol returnType,
        SemanticModel semanticModel)
    {
        var notAnnotatedType = returnType.WithNullableAnnotation(NullableAnnotation.NotAnnotated);

        return new PartialAccessorData(
            kind,
            member.Name,
            notAnnotatedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            notAnnotatedType.GetTypeData(),
            returnType.NullableAnnotation == NullableAnnotation.Annotated,
            ExtractKeyFromInjectAttribute(member, semanticModel));
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
                var (key, _, _) = attr.GetKeyInfo(semanticModel);
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
                var moduleType = attr.GetImportedModuleType();
                if(moduleType is not null)
                    modules.Add(moduleType.GetTypeData());
            }
            else if(originalFullName == Constants.IocImportModuleAttributeFullName_T1)
            {
                var genericModuleType = attr.GetImportedModuleType();
                if(genericModuleType is not null)
                    modules.Add(genericModuleType.GetTypeData());
            }
        }

        return modules.ToImmutableEquatableArray();
    }

    /// <summary>
    /// Extracts the closed generic types of [IocDiscover] / [IocDiscover&lt;T&gt;] on the container class.
    /// </summary>
    private static ImmutableEquatableArray<ClosedGenericDependency> ExtractDiscoveredDependencies(INamedTypeSymbol containerSymbol)
    {
        var dependencies = new List<ClosedGenericDependency>();

        foreach(var attr in containerSymbol.GetAttributes())
        {
            if(attr.AttributeClass is not { } attrClass)
                continue;

            var metadataName = attrClass.IsGenericType ? attrClass.OriginalDefinition.MetadataName : attrClass.MetadataName;
            var metadataNamespace = attrClass.ContainingNamespace?.ToDisplayString() ?? "";
            var fullName = string.IsNullOrEmpty(metadataNamespace) ? metadataName : $"{metadataNamespace}.{metadataName}";

            var closedType = fullName switch
            {
                Constants.IocDiscoverAttributeFullName when attr.ConstructorArguments is [{ Value: INamedTypeSymbol type }] => type,
                Constants.IocDiscoverAttributeFullName_T1 when attrClass.TypeArguments is [INamedTypeSymbol type] => type,
                _ => null,
            };

            if(closedType is not null && CreateClosedGenericDependency(closedType) is { } dependency)
                dependencies.Add(dependency);
        }

        return dependencies.ToImmutableEquatableArray();
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

