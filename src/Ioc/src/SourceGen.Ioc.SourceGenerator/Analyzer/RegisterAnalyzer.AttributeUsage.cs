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
    private static void AnalyzeInjectAttribute(SymbolAnalysisContext context, AnalyzerContext analyzerContext)
    {
        var member = context.Symbol;

        // Check if the member has IocInjectAttribute/InjectAttribute (by name only, matching TransformRegister behavior)
        var injectAttribute = member.GetAttributes()
            .FirstOrDefault(static attr => attr.AttributeClass?.IsInject == true);

        if (injectAttribute is null)
            return;

        var location = injectAttribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
            ?? member.Locations.FirstOrDefault();

        // SGIOC028: async void methods cannot be awaited - report before any other method check
        if (member is IMethodSymbol { IsAsync: true, ReturnsVoid: true })
        {
            context.ReportDiagnostic(Diagnostic.Create(
                AsyncVoidInjectMethod,
                location,
                member.Name));
            return;
        }

        var asyncMethodInjectEnabled = (analyzerContext.Features & IocFeatures.AsyncMethodInject) != 0;
        var isTaskReturningMethod = member is IMethodSymbol taskMethod && IsNonGenericTaskType(taskMethod.ReturnType);

        var (requiredFeature, featureName) = member switch
        {
            IPropertySymbol => (IocFeatures.PropertyInject, nameof(IocFeatures.PropertyInject)),
            IFieldSymbol => (IocFeatures.FieldInject, nameof(IocFeatures.FieldInject)),
            IMethodSymbol when isTaskReturningMethod => (IocFeatures.AsyncMethodInject, nameof(IocFeatures.AsyncMethodInject)),
            IMethodSymbol => (IocFeatures.MethodInject, nameof(IocFeatures.MethodInject)),
            _ => (IocFeatures.None, string.Empty)
        };

        if(requiredFeature != IocFeatures.None && (analyzerContext.Features & requiredFeature) == 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InjectFeatureDisabled,
                location,
                member.Name,
                featureName));
            // For Task-returning methods: SGIOC022 already fired; do NOT also report SGIOC007 return-type error
            if (isTaskReturningMethod)
                return;
        }

        // Container-class partial definitions are valid injection targets (keyed service accessors)
        if (member is IPropertySymbol { IsPartialDefinition: true } && IsInContainerClass(member.ContainingType))
            return;
        if (member is IMethodSymbol { IsPartialDefinition: true } && IsInContainerClass(member.ContainingType))
            return;

        var reason = GetMemberInjectabilityIssue(member, asyncMethodInjectEnabled);
        if (reason is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidInjectAttributeUsage,
                location,
                member.Name,
                reason));
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

        // SGIOC023 + SGIOC024: Validate InjectMembers elements - only for IoCRegisterFor attributes
        if (!isDefaultsAttribute)
        {
            AnalyzeInjectMembersOnAttribute(context, argumentList, (analyzerContext.Features & IocFeatures.AsyncMethodInject) != 0);
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

                // Suppress SGIOC016 if the registration attribute provides GenericFactoryTypeMapping
                var hasGenericFactoryTypeMappingOnAttr = false;
                // Always check GenericFactoryTypeMapping for duplicate placeholders (SGIOC017), regardless of [IocGenericFactory]
                foreach (var arg in argumentList.Arguments)
                {
                    if (arg.NameEquals?.Name.Identifier.Text == "GenericFactoryTypeMapping")
                    {
                        ExpressionSyntax[] mappingElements = arg.Expression switch
                        {
                            CollectionExpressionSyntax coll => [.. coll.Elements.OfType<ExpressionElementSyntax>().Select(static e => e.Expression)],
                            ArrayCreationExpressionSyntax { Initializer: not null } arr => [.. arr.Initializer.Expressions],
                            ImplicitArrayCreationExpressionSyntax implicitArr => [.. implicitArr.Initializer.Expressions],
                            _ => []
                        };

                        if (mappingElements.Length >= 2)
                        {
                            // Check for duplicate placeholder types (index 1+)
                            var seenTypes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
                            ITypeSymbol? duplicateType = null;
                            for (int i = 1; i < mappingElements.Length; i++)
                            {
                                if (mappingElements[i] is TypeOfExpressionSyntax typeofExpr)
                                {
                                    var typeInfo = context.SemanticModel.GetTypeInfo(typeofExpr.Type, context.CancellationToken);
                                    if (typeInfo.Type is { } placeholderType)
                                    {
                                        if (!seenTypes.Add(placeholderType))
                                        {
                                            duplicateType = placeholderType;
                                            break;
                                        }
                                    }
                                }
                            }

                            if (duplicateType is not null)
                            {
                                // Duplicate placeholders: report SGIOC017
                                context.ReportDiagnostic(Diagnostic.Create(
                                    DuplicatedGenericFactoryPlaceholders,
                                    arg.Expression.GetLocation(),
                                    duplicateType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                            }
                            else if (!hasIocGenericFactory && mappingElements.Length - 1 == methodSymbol.TypeParameters.Length)
                            {
                                // Only suppress SGIOC016 when [IocGenericFactory] is NOT present
                                // AND the mapping provides exactly one placeholder per factory type parameter
                                hasGenericFactoryTypeMappingOnAttr = true;
                            }
                        }
                        break;
                    }
                }

                if (!hasIocGenericFactory && !hasGenericFactoryTypeMappingOnAttr)
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
        var seenTypes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        for (int i = 1; i < typeArray.Length; i++)
        {
            if (typeArray[i].Value is ITypeSymbol placeholderType)
            {
                if (!seenTypes.Add(placeholderType))
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
        var (_, lifetime) = attribute.TryGetLifetime();

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

    /// <summary>
    /// SGIOC023 + SGIOC024: Validates InjectMembers array elements on a registration attribute.
    /// SGIOC023: Fires when an element is not nameof(...) or a valid array literal { nameof(...), key [, KeyType] }.
    /// SGIOC024: Fires when the resolved member is not injectable.
    /// </summary>
    private static void AnalyzeInjectMembersOnAttribute(
        SyntaxNodeAnalysisContext context,
        AttributeArgumentListSyntax argumentList,
        bool asyncMethodInjectEnabled = false)
    {
        AttributeArgumentSyntax? injectMembersArg = null;
        foreach (var arg in argumentList.Arguments)
        {
            if (arg.NameEquals?.Name.Identifier.Text == "InjectMembers")
            {
                injectMembersArg = arg;
                break;
            }
        }

        if (injectMembersArg is null)
            return;

        var elements = GetInjectMembersElements(injectMembersArg.Expression);
        if (elements is null || elements.Length == 0)
            return;

        for (int i = 0; i < elements.Length; i++)
        {
            var element = elements[i];
            ExpressionSyntax? nameofExpr = null;

            if (IsNameofExpression(element))
            {
                // Format: nameof(Member)
                nameofExpr = element;
            }
            else
            {
                // Format: { nameof(Member), key [, KeyType] }
                var nested = GetInjectMembersElements(element);
                if (nested is null || nested.Length < 2 || !IsNameofExpression(nested[0]))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InjectMembersInvalidFormat,
                        element.GetLocation(),
                        i));
                    continue;
                }

                // Reject arrays with more than 3 elements
                if (nested.Length > 3)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InjectMembersInvalidFormat,
                        element.GetLocation(),
                        i));
                    continue;
                }

                // If 3 elements, validate 3rd is a valid KeyType constant (Value=0 or Csharp=1)
                if (nested.Length == 3)
                {
                    var ktConst = context.SemanticModel.GetConstantValue(nested[2], context.CancellationToken);
                    if (!ktConst.HasValue || ktConst.Value is not int ktVal || ktVal is not (0 or 1))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            InjectMembersInvalidFormat,
                            element.GetLocation(),
                            i));
                        continue;
                    }
                }

                nameofExpr = nested[0];
            }

            // Resolve the member symbol from nameof(...)
            if (nameofExpr is not InvocationExpressionSyntax nameofInvocation)
                continue;

            var inner = nameofInvocation.ArgumentList.Arguments[0].Expression;
            var symbolInfo = context.SemanticModel.GetSymbolInfo(inner, context.CancellationToken);
            var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

            if (symbol is null)
                continue; // unresolvable — a compile error will already be reported

            // SGIOC024: Check if member is injectable
            var (isInjectable, reason) = ValidateInjectableMember(symbol, asyncMethodInjectEnabled);
            if (!isInjectable)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InjectMembersNonInjectableMember,
                    nameofExpr.GetLocation(),
                    symbol.Name,
                    reason));
            }
        }
    }

    /// <summary>
    /// Returns the reason a member is not injectable, or <see langword="null"/> if it is valid.
    /// Shared by SGIOC007 (<see cref="AnalyzeInjectAttribute"/>) and SGIOC024 (<see cref="ValidateInjectableMember"/>).
    /// </summary>
    /// <param name="asyncMethodInjectEnabled">
    /// When <see langword="true"/>, methods returning non-generic <see cref="System.Threading.Tasks.Task"/> are
    /// considered valid injection targets (allowed by AsyncMethodInject feature).
    /// </param>
    private static string? GetMemberInjectabilityIssue(ISymbol symbol, bool asyncMethodInjectEnabled = false)
    {
        if (symbol.IsStatic)
            return "it is static";

        return symbol switch
        {
            IPropertySymbol p when p.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal)
                => "property is not accessible",
            IPropertySymbol { SetMethod: null } => "property has no setter",
            IPropertySymbol p when p.SetMethod!.DeclaredAccessibility is Accessibility.Private
                => "property setter is private",
            IPropertySymbol p when p.SetMethod!.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal)
                => "property setter is not accessible",
            IFieldSymbol { IsReadOnly: true } => "field is readonly",
            IFieldSymbol f when f.DeclaredAccessibility is Accessibility.Private
                => "field is private",
            IFieldSymbol f when f.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal)
                => "field is not accessible",
            IMethodSymbol { MethodKind: MethodKind.Constructor } => null,
            IMethodSymbol m when m.DeclaredAccessibility is Accessibility.Private
                => "method is private",
            IMethodSymbol m when m.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal)
                => "method is not accessible",
            IMethodSymbol m when m.MethodKind != MethodKind.Ordinary
                => "method is not an ordinary method",
            // Allow non-generic Task return type only when AsyncMethodInject feature is enabled
            IMethodSymbol { ReturnsVoid: false } m when !(asyncMethodInjectEnabled && IsNonGenericTaskType(m.ReturnType))
                => asyncMethodInjectEnabled ? "method does not return void or non-generic Task" : "method does not return void",
            IMethodSymbol { IsGenericMethod: true } => "method is generic",
            IPropertySymbol or IFieldSymbol or IMethodSymbol => null,
            _ => "member is not a property, field, or method"
        };
    }

    private static (bool IsInjectable, string Reason) ValidateInjectableMember(ISymbol symbol, bool asyncMethodInjectEnabled = false)
    {
        var reason = GetMemberInjectabilityIssue(symbol, asyncMethodInjectEnabled);
        return reason is null ? (true, string.Empty) : (false, reason);
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="type"/> is the non-generic
    /// <see cref="System.Threading.Tasks.Task"/> class (i.e., <c>Task</c> with arity 0).
    /// </summary>
    private static bool IsNonGenericTaskType(ITypeSymbol? type)
        => type is INamedTypeSymbol { Arity: 0, Name: "Task" } named
            && named.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks";

    private static ExpressionSyntax[]? GetInjectMembersElements(ExpressionSyntax expression)
        => expression switch
        {
            ArrayCreationExpressionSyntax { Initializer: not null } arr
                => [.. arr.Initializer.Expressions],
            ImplicitArrayCreationExpressionSyntax implicitArr
                => [.. implicitArr.Initializer.Expressions],
            CollectionExpressionSyntax coll
                => [.. coll.Elements.OfType<ExpressionElementSyntax>().Select(static e => e.Expression)],
            _ => null
        };

    private static bool IsNameofExpression(ExpressionSyntax expression)
        => expression is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: "nameof" } } inv
            && inv.ArgumentList.Arguments.Count == 1;
}
