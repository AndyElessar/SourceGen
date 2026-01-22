using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceGen.Ioc;

/// <summary>
/// Partial class for unresolvable members analysis (SGIOC013-015).
/// </summary>
public sealed partial class RegisterAnalyzer
{
    /// <summary>
    /// SGIOC013 and SGIOC014: Analyzes [ServiceKey] parameter usage.
    /// SGIOC013: Reports error when the parameter type does not match the registered key type.
    /// SGIOC014: Reports error when [ServiceKey] is used but no Key is registered.
    /// </summary>
    private static void AnalyzeServiceKeyTypeMismatch(
        Action<Diagnostic> reportDiagnostic,
        ServiceInfo serviceInfo,
        CancellationToken cancellationToken)
    {
        var keyTypeSymbol = serviceInfo.KeyTypeSymbol;
        var hasKey = serviceInfo.HasKey;

        // Check constructor parameters (using cached constructor)
        var constructor = serviceInfo.Constructor;
        if (constructor is not null)
        {
            AnalyzeServiceKeyParametersInMethod(reportDiagnostic, constructor.Parameters, keyTypeSymbol, hasKey, cancellationToken);
        }

        // Check [Inject] method parameters (using cached injected members)
        foreach (var (member, _) in serviceInfo.InjectedMembers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (member is not IMethodSymbol method)
                continue;

            AnalyzeServiceKeyParametersInMethod(reportDiagnostic, method.Parameters, keyTypeSymbol, hasKey, cancellationToken);
        }
    }

    /// <summary>
    /// SGIOC015: Analyzes constructor parameters and injected properties/fields/methods for unresolvable built-in types.
    /// Reports error when:
    /// - A constructor parameter is a built-in type (int, string, byte[], etc.) that cannot be resolved from dependency injection
    ///   and does not have [IocInject] with key, [ServiceKey], [FromKeyedServices], or an explicit default value.
    /// - A property or field with [IocInject] or [Inject] attribute is a built-in type and does not have a service key specified.
    /// - A method with [IocInject] or [Inject] attribute has a parameter of built-in type and does not have a service key or default value.
    /// </summary>
    private static void AnalyzeUnresolvableMembers(
        Action<Diagnostic> reportDiagnostic,
        ServiceInfo serviceInfo,
        CancellationToken cancellationToken)
    {
        // Skip constructor analysis if this service has a factory method or instance - no constructor resolution needed
        // But still check injected properties/fields/methods
        if (!serviceInfo.HasFactory && !serviceInfo.HasInstance)
        {
            // Analyze constructor parameters (using cached constructor)
            var constructor = serviceInfo.Constructor;
            if (constructor is not null)
            {
                AnalyzeUnresolvableConstructorParameters(reportDiagnostic, constructor.Parameters, serviceInfo.Location, cancellationToken);
            }
        }

        // Analyze injected properties, fields, and methods (using cached injected members)
        AnalyzeUnresolvableInjectedMembers(reportDiagnostic, serviceInfo, cancellationToken);
    }

    /// <summary>
    /// Analyzes constructor parameters for unresolvable built-in types.
    /// </summary>
    private static void AnalyzeUnresolvableConstructorParameters(
        Action<Diagnostic> reportDiagnostic,
        ImmutableArray<IParameterSymbol> parameters,
        Location? serviceLocation,
        CancellationToken cancellationToken)
    {
        foreach (var param in parameters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Use helper method to check if parameter is unresolvable
            if (!AnalyzerHelpers.IsBuiltInUnresolvableParameter(param))
                continue;

            // Report SGIOC015: Unresolvable constructor parameter
            var location = param.Locations.FirstOrDefault() ?? serviceLocation;
            reportDiagnostic(Diagnostic.Create(
                UnresolvableMember,
                location,
                "Constructor parameter",
                param.Name,
                param.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }

    /// <summary>
    /// Analyzes properties, fields, and methods with [IocInject] or [Inject] attribute for unresolvable built-in types.
    /// Uses cached InjectedMembers from ServiceInfo to avoid repeated GetMembers() and attribute lookups.
    /// </summary>
    private static void AnalyzeUnresolvableInjectedMembers(
        Action<Diagnostic> reportDiagnostic,
        ServiceInfo serviceInfo,
        CancellationToken cancellationToken)
    {
        var serviceLocation = serviceInfo.Location;

        // Use cached injected members
        foreach (var (member, injectAttribute) in serviceInfo.InjectedMembers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (member)
            {
                case IPropertySymbol property:
                    AnalyzeUnresolvableInjectedProperty(reportDiagnostic, property, injectAttribute, serviceLocation, cancellationToken);
                    break;

                case IFieldSymbol field:
                    AnalyzeUnresolvableInjectedField(reportDiagnostic, field, injectAttribute, serviceLocation, cancellationToken);
                    break;

                case IMethodSymbol method:
                    AnalyzeUnresolvableInjectedMethodParameters(reportDiagnostic, method, injectAttribute, serviceLocation, cancellationToken);
                    break;
            }
        }
    }

    /// <summary>
    /// Analyzes a property with [IocInject] or [Inject] attribute for unresolvable built-in types.
    /// </summary>
    private static void AnalyzeUnresolvableInjectedProperty(
        Action<Diagnostic> reportDiagnostic,
        IPropertySymbol property,
        AttributeData injectAttribute,
        Location? serviceLocation,
        CancellationToken cancellationToken)
    {
        // Use helper method to check if property is unresolvable
        if (!AnalyzerHelpers.IsBuiltInUnresolvableProperty(property, injectAttribute))
            return;

        // Report SGIOC015: Unresolvable injected property
        var location = injectAttribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation()
            ?? property.Locations.FirstOrDefault()
            ?? serviceLocation;

        reportDiagnostic(Diagnostic.Create(
            UnresolvableMember,
            location,
            "Property",
            property.Name,
            property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }

    /// <summary>
    /// Analyzes a field with [IocInject] or [Inject] attribute for unresolvable built-in types.
    /// </summary>
    private static void AnalyzeUnresolvableInjectedField(
        Action<Diagnostic> reportDiagnostic,
        IFieldSymbol field,
        AttributeData injectAttribute,
        Location? serviceLocation,
        CancellationToken cancellationToken)
    {
        // Use helper method to check if field is unresolvable
        if (!AnalyzerHelpers.IsBuiltInUnresolvableField(field, injectAttribute))
            return;

        // Report SGIOC015: Unresolvable injected field
        var location = injectAttribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation()
            ?? field.Locations.FirstOrDefault()
            ?? serviceLocation;

        reportDiagnostic(Diagnostic.Create(
            UnresolvableMember,
            location,
            "Field",
            field.Name,
            field.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }

    /// <summary>
    /// Analyzes method parameters with [IocInject] or [Inject] attribute for unresolvable built-in types.
    /// </summary>
    private static void AnalyzeUnresolvableInjectedMethodParameters(
        Action<Diagnostic> reportDiagnostic,
        IMethodSymbol method,
        AttributeData methodInjectAttribute,
        Location? serviceLocation,
        CancellationToken cancellationToken)
    {
        foreach (var param in method.Parameters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Use helper method to check if parameter is unresolvable
            if (!AnalyzerHelpers.IsBuiltInUnresolvableMethodParameter(param))
                continue;

            // Report SGIOC015: Unresolvable method parameter
            var location = param.Locations.FirstOrDefault()
                ?? methodInjectAttribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation()
                ?? method.Locations.FirstOrDefault()
                ?? serviceLocation;

            reportDiagnostic(Diagnostic.Create(
                UnresolvableMember,
                location,
                "Method parameter",
                param.Name,
                param.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }

    /// <summary>
    /// Analyzes parameters for [ServiceKey] attribute and reports diagnostics.
    /// SGIOC013: Reports when parameter type does not match registered key type.
    /// SGIOC014: Reports when [ServiceKey] is used but no Key is registered.
    /// </summary>
    private static void AnalyzeServiceKeyParametersInMethod(
        Action<Diagnostic> reportDiagnostic,
        ImmutableArray<IParameterSymbol> parameters,
        ITypeSymbol? keyTypeSymbol,
        bool hasKey,
        CancellationToken cancellationToken)
    {
        foreach (var param in parameters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Check if parameter has [ServiceKey] attribute
            var serviceKeyAttribute = param.GetAttributes()
                .FirstOrDefault(attr =>
                    attr.AttributeClass?.Name == "ServiceKeyAttribute"
                    && attr.AttributeClass.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.DependencyInjection");

            if (serviceKeyAttribute is null)
                continue;

            var location = serviceKeyAttribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation()
                ?? param.Locations.FirstOrDefault();

            // SGIOC014: No key is registered but [ServiceKey] is used
            if (!hasKey)
            {
                reportDiagnostic(Diagnostic.Create(
                    ServiceKeyNotRegistered,
                    location,
                    param.Name));
                continue;
            }

            // Skip type checking if KeyType is Csharp (keyTypeSymbol will be null)
            if (keyTypeSymbol is null)
                continue;

            var paramType = param.Type;

            // SGIOC013: Check if the parameter type is compatible with the key type
            // The parameter type should be the same as or assignable from the key type
            if (!IsAssignable(paramType, keyTypeSymbol))
            {
                reportDiagnostic(Diagnostic.Create(
                    ServiceKeyTypeMismatch,
                    location,
                    param.Name,
                    paramType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    keyTypeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            }
        }
    }
}
