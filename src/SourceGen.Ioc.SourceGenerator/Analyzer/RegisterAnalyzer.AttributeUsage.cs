using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceGen.Ioc;

/// <summary>
/// Partial class for attribute usage analysis (SGIOC001, SGIOC006-010, SGIOC016-017).
/// </summary>
public sealed partial class RegisterAnalyzer
{
    /// <summary>
    /// SGIOC006: Analyzes parameters for duplicated keyed service attributes.
    /// Reports warning when both [FromKeyedServices] and [Inject] attributes are marked on the same parameter.
    /// </summary>
    private static void AnalyzeDuplicatedKeyedServiceAttributes(SymbolAnalysisContext context)
    {
        if (context.Symbol is not IMethodSymbol method)
            return;

        foreach (var parameter in method.Parameters)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var hasFromKeyedServices = false;
            var hasInject = false;
            AttributeData? injectAttribute = null;

            foreach (var attr in parameter.GetAttributes())
            {
                var attrClass = attr.AttributeClass;
                if (attrClass is null)
                    continue;

                if (attrClass.Name == "FromKeyedServicesAttribute"
                    && attrClass.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection")
                {
                    hasFromKeyedServices = true;
                }
                else if (attrClass.IsInject)
                {
                    hasInject = true;
                    injectAttribute = attr;
                }
            }

            if (hasFromKeyedServices && hasInject)
            {
                var location = injectAttribute?.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                    ?? parameter.Locations.FirstOrDefault();

                context.ReportDiagnostic(Diagnostic.Create(
                    DuplicatedKeyedServiceAttribute,
                    location,
                    parameter.Name));
            }
        }
    }

    /// <summary>
    /// SGIOC007: Analyzes IocInjectAttribute/InjectAttribute usage on members.
    /// Reports error when IocInjectAttribute/InjectAttribute is marked on static member, inaccessible member, or method that does not return void.
    /// </summary>
    private static void AnalyzeInjectAttribute(SymbolAnalysisContext context)
    {
        var member = context.Symbol;

        // Check if the member has IocInjectAttribute/InjectAttribute (by name only, matching TransformRegister behavior)
        var injectAttribute = member.GetAttributes()
            .FirstOrDefault(static attr => attr.AttributeClass?.IsInject == true);

        if (injectAttribute is null)
            return;

        var location = injectAttribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
            ?? member.Locations.FirstOrDefault();

        // Check if member is static
        if (member.IsStatic)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidInjectAttributeUsage,
                location,
                member.Name,
                "it is static"));
            return;
        }

        switch (member)
        {
            case IPropertySymbol property:
                // Allow [IocInject] on partial properties in [IocContainer] classes (for keyed service accessor)
                if (property.IsPartialDefinition && IsInContainerClass(member.ContainingType))
                    return;

                // Check if property has no setter or setter is private
                if (property.SetMethod is null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidInjectAttributeUsage,
                        location,
                        member.Name,
                        "property has no setter"));
                }
                else if (property.SetMethod.DeclaredAccessibility is Accessibility.Private)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidInjectAttributeUsage,
                        location,
                        member.Name,
                        "property setter is private"));
                }

                break;

            case IFieldSymbol field:
                // Check if field is readonly
                if (field.IsReadOnly)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidInjectAttributeUsage,
                        location,
                        member.Name,
                        "field is readonly"));
                }
                // Check if field is private
                else if (field.DeclaredAccessibility is Accessibility.Private)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidInjectAttributeUsage,
                        location,
                        member.Name,
                        "field is private"));
                }

                break;

            case IMethodSymbol method:
                // Allow [IocInject] on partial methods in [IocContainer] classes (for keyed service accessor)
                if (method.IsPartialDefinition && IsInContainerClass(member.ContainingType))
                    return;

                // Check if method is private
                if (method.DeclaredAccessibility is Accessibility.Private)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidInjectAttributeUsage,
                        location,
                        member.Name,
                        "method is private"));
                }
                // Check if method does not return void
                else if (!method.ReturnsVoid)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidInjectAttributeUsage,
                        location,
                        member.Name,
                        "method must return void"));
                }

                break;
        }
    }

    /// <summary>
    /// SGIOC008: Analyzes Factory and Instance members specified via nameof() on AttributeSyntax.
    /// This uses RegisterSyntaxNodeAction to get the SemanticModel directly, avoiding RS1030.
    /// </summary>
    private static void AnalyzeFactoryAndInstanceOnAttribute(
        SyntaxNodeAnalysisContext context,
        AnalyzerContext analyzerContext)
    {
        if (context.Node is not AttributeSyntax attributeSyntax)
            return;

        // Get the attribute symbol to check if it's an IoC registration attribute
        var attributeSymbol = context.SemanticModel.GetSymbolInfo(attributeSyntax, context.CancellationToken).Symbol;
        if (attributeSymbol is not IMethodSymbol attributeConstructor)
            return;

        var attributeClass = attributeConstructor.ContainingType;
        if (attributeClass is null)
            return;

        // Check if this is an IoC registration attribute (including generic variants)
        if (!AnalyzerHelpers.IsAnyIoCAttribute(attributeClass, analyzerContext.AttributeSymbols))
            return;

        var argumentList = attributeSyntax.ArgumentList;
        if (argumentList is null)
            return;

        var location = attributeSyntax.GetLocation();

        // Check if this is IoCRegisterDefaultsAttribute (which only has Factory, not Instance)
        var isDefaultsAttribute = AnalyzerHelpers.IsIoCRegisterDefaultsAttribute(attributeClass, analyzerContext.AttributeSymbols);

        // SGIOC010: Check if both Factory and Instance are specified (only for IoCRegister/IoCRegisterFor)
        if (!isDefaultsAttribute)
        {
            AnalyzeFactoryAndInstanceConflict(context, argumentList, location);
        }

        // Check Factory member (SGIOC008)
        AnalyzeNameofMemberOnSyntax(context, argumentList, location, "Factory");

        // Check Instance member (SGIOC008) - only for IoCRegister/IoCRegisterFor
        if (!isDefaultsAttribute)
        {
            AnalyzeNameofMemberOnSyntax(context, argumentList, location, "Instance");
        }
    }

    /// <summary>
    /// SGIOC010: Analyzes if both Factory and Instance are specified on the same attribute.
    /// Reports error when both are present, as Factory takes precedence and Instance will be ignored.
    /// </summary>
    private static void AnalyzeFactoryAndInstanceConflict(
        SyntaxNodeAnalysisContext context,
        AttributeArgumentListSyntax argumentList,
        Location location)
    {
        var hasFactory = false;
        var hasInstance = false;

        foreach (var argument in argumentList.Arguments)
        {
            var name = argument.NameEquals?.Name.Identifier.Text;
            if (name == "Factory")
                hasFactory = true;
            else if (name == "Instance")
                hasInstance = true;

            // Early exit if both found
            if (hasFactory && hasInstance)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    FactoryAndInstanceConflict,
                    location));
                return;
            }
        }
    }

    /// <summary>
    /// Analyzes a specific member (Factory or Instance) specified via nameof() using SyntaxNodeAnalysisContext.
    /// </summary>
    private static void AnalyzeNameofMemberOnSyntax(
        SyntaxNodeAnalysisContext context,
        AttributeArgumentListSyntax argumentList,
        Location location,
        string memberKind)
    {
        foreach (var argument in argumentList.Arguments)
        {
            // Check if this is the named argument we're looking for
            if (argument.NameEquals?.Name.Identifier.Text != memberKind)
                continue;

            // Check if the expression is a nameof() invocation
            if (argument.Expression is not InvocationExpressionSyntax invocation ||
               invocation.Expression is not IdentifierNameSyntax identifierName ||
               identifierName.Identifier.Text != "nameof")
            {
                // Not a nameof() expression, skip validation
                return;
            }

            // Extract the argument inside nameof()
            if (invocation.ArgumentList.Arguments.Count != 1)
                return;

            var nameofArgument = invocation.ArgumentList.Arguments[0].Expression;

            // Resolve the symbol using the SemanticModel from context
            var symbolInfo = context.SemanticModel.GetSymbolInfo(nameofArgument, context.CancellationToken);
            var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

            if (symbol is null)
                return;

            // Validate the symbol
            var (isValid, errorReason) = AnalyzerHelpers.ValidateFactoryOrInstanceSymbol(symbol);
            if (!isValid)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidFactoryOrInstanceMember,
                    location,
                    memberKind,
                    symbol.Name,
                    errorReason));
            }

            // SGIOC016: Check if Factory method is generic but missing [IocGenericFactory] attribute
            if (memberKind == "Factory" && symbol is IMethodSymbol methodSymbol && methodSymbol.TypeParameters.Length > 0)
            {
                // Check if the method has [IocGenericFactory] attribute
                var hasIocGenericFactory = false;
                foreach (var attr in methodSymbol.GetAttributes())
                {
                    if (attr.AttributeClass?.ToDisplayString() == Constants.IocGenericFactoryAttributeFullName)
                    {
                        hasIocGenericFactory = true;
                        break;
                    }
                }

                if (!hasIocGenericFactory)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        GenericFactoryMissingAttribute,
                        location,
                        methodSymbol.Name));
                }
            }

            return;
        }
    }

    /// <summary>
    /// SGIOC017: Analyzes [IocGenericFactory] attribute for duplicated placeholder types.
    /// Reports error when placeholder types (from second to last in the type array) are duplicated.
    /// </summary>
    private static void AnalyzeIocGenericFactoryAttribute(SymbolAnalysisContext context)
    {
        if (context.Symbol is not IMethodSymbol methodSymbol)
            return;

        // Find [IocGenericFactory] attribute
        AttributeData? genericFactoryAttr = null;
        foreach (var attr in methodSymbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == Constants.IocGenericFactoryAttributeFullName)
            {
                genericFactoryAttr = attr;
                break;
            }
        }

        if (genericFactoryAttr is null)
            return;

        // Extract type array from constructor argument
        // [IocGenericFactory(typeof(IRequestHandler<Task<int>>), typeof(int), typeof(decimal))]
        if (genericFactoryAttr.ConstructorArguments.Length == 0)
            return;

        var firstArg = genericFactoryAttr.ConstructorArguments[0];
        if (firstArg.Kind != TypedConstantKind.Array || firstArg.Values.IsDefaultOrEmpty)
            return;

        var typeArray = firstArg.Values;
        if (typeArray.Length < 2)
            return; // Need at least service type template and one placeholder

        // Check for duplicates in placeholder types (from index 1 to end)
        var seenTypes = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 1; i < typeArray.Length; i++)
        {
            if (typeArray[i].Value is ITypeSymbol placeholderType)
            {
                var typeName = placeholderType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                if (!seenTypes.Add(typeName))
                {
                    // Duplicate found
                    var location = genericFactoryAttr.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation();
                    context.ReportDiagnostic(Diagnostic.Create(
                        DuplicatedGenericFactoryPlaceholders,
                        location,
                        placeholderType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                    return; // Report once per attribute
                }
            }
        }
    }

    /// <summary>
    /// SGIOC009: Analyzes Instance registration to ensure Lifetime is Singleton.
    /// Reports error when Instance is specified but Lifetime is not Singleton.
    /// </summary>
    /// <param name="reportDiagnostic">The action to report diagnostics.</param>
    /// <param name="attribute">The IoC registration attribute.</param>
    /// <param name="location">The location for the diagnostic.</param>
    private static void AnalyzeInstanceLifetime(
        Action<Diagnostic> reportDiagnostic,
        AttributeData attribute,
        Location? location)
    {
        // Check if Instance is specified
        string? instance = null;
        foreach (var namedArg in attribute.NamedArguments)
        {
            if (namedArg.Key == "Instance" && !namedArg.Value.IsNull)
            {
                instance = namedArg.Value.Value?.ToString();
                break;
            }
        }

        if (instance is null)
            return;

        // Get the lifetime
        var (hasLifetime, lifetime) = attribute.TryGetLifetime();

        // If lifetime is not explicitly set, it defaults to Singleton (0), which is valid
        if (!hasLifetime)
            return;

        // If lifetime is Singleton, it's valid
        if (lifetime is ServiceLifetime.Singleton)
            return;

        // Report error: Instance requires Singleton lifetime
        reportDiagnostic(Diagnostic.Create(
            InstanceRequiresSingleton,
            location,
            instance,
            lifetime.Name));
    }

    private static void AnalyzeInvalidAttributeUsage(SymbolAnalysisContext context, INamedTypeSymbol targetType, Location? location)
        => AnalyzeInvalidAttributeUsage(context.ReportDiagnostic, targetType, location);

    private static void AnalyzeInvalidAttributeUsage(SemanticModelAnalysisContext context, INamedTypeSymbol targetType, Location? location)
        => AnalyzeInvalidAttributeUsage(context.ReportDiagnostic, targetType, location);

    private static void AnalyzeInvalidAttributeUsage(Action<Diagnostic> reportDiagnostic, INamedTypeSymbol targetType, Location? location)
    {
        var (isInvalid, reason) = AnalyzerHelpers.GetRegistrationInvalidReason(targetType);
        if (isInvalid)
        {
            var diagnostic = Diagnostic.Create(
                InvalidAttributeUsage,
                location,
                targetType.Name,
                reason);
            reportDiagnostic(diagnostic);
        }
    }

    /// <summary>
    /// Checks whether the containing type is marked with [IocContainer]/[Container] attribute.
    /// Used to allow [IocInject] on partial methods/properties for keyed service accessor specification.
    /// </summary>
    private static bool IsInContainerClass(INamedTypeSymbol? containingType)
    {
        if(containingType is null)
            return false;

        foreach(var attr in containingType.GetAttributes())
        {
            if(attr.AttributeClass?.Name is "IocContainerAttribute" or "ContainerAttribute")
                return true;
        }

        return false;
    }
}
