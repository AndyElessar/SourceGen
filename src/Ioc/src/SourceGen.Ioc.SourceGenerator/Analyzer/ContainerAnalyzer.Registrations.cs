using System.Collections.Concurrent;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceGen.Ioc;

partial class ContainerAnalyzer
{
    #region Collection

    /// <summary>
    /// Collects the [IocRegister] / [IocRegisterFor] registrations, type-level [IocRegisterDefaults] and
    /// [IocImportModule] attributes of a named type.
    /// </summary>
    private static void CollectRegistrationAttributes(SymbolAnalysisContext context, ContainerAnalyzerContext analyzerContext)
    {
        if(context.Symbol is not INamedTypeSymbol typeSymbol)
            return;

        var attributeSymbols = analyzerContext.AttributeSymbols;
        foreach(var attribute in typeSymbol.GetAttributes())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var attrClass = attribute.AttributeClass;
            if(attrClass is null)
                continue;

            if(AnalyzerHelpers.IsIoCRegisterAttribute(attrClass, attributeSymbols))
            {
                // Like the generator, open types nested in a generic type are not registered (SGIOC038)
                if(!typeSymbol.IsOpenNestedInGenericType)
                    analyzerContext.Registrations.Add(CreateRawRegistration(typeSymbol, attribute, explicitContainer: null));
            }
            else if(AnalyzerHelpers.IsIoCRegisterForAttribute(attrClass, attributeSymbols))
            {
                // [IocRegisterFor] on a container class is also an explicit registration of that container (ExplicitOnly)
                var targetType = attribute.GetTargetTypeFromRegisterForAttribute();
                if(targetType is { IsOpenNestedInGenericType: false })
                    analyzerContext.Registrations.Add(CreateRawRegistration(targetType, attribute, explicitContainer: typeSymbol));
            }
            else if(AnalyzerHelpers.IsIoCRegisterDefaultsAttribute(attrClass, attributeSymbols))
            {
                if(CreateDefaultSettings(attribute) is { } defaults)
                    analyzerContext.DefaultSettings.Add(defaults);
            }
            else if(AnalyzerHelpers.IsIocImportModuleAttribute(attrClass, attributeSymbols))
            {
                if(attribute.GetImportedModuleType() is { } moduleType)
                    analyzerContext.ModuleImports.Add(new ModuleImport(moduleType, attribute));
            }
        }
    }

    /// <summary>
    /// Collects assembly-level [IocRegisterFor] registrations and [IocRegisterDefaults] settings.
    /// </summary>
    private static void CollectAssemblyLevelAttributes(
        Compilation compilation,
        ContainerAnalyzerContext analyzerContext,
        CancellationToken cancellationToken)
    {
        var attributeSymbols = analyzerContext.AttributeSymbols;
        foreach(var attribute in compilation.Assembly.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var attrClass = attribute.AttributeClass;
            if(attrClass is null)
                continue;

            if(AnalyzerHelpers.IsIoCRegisterForAttribute(attrClass, attributeSymbols))
            {
                var targetType = attribute.GetTargetTypeFromRegisterForAttribute();
                if(targetType is { IsOpenNestedInGenericType: false })
                    analyzerContext.Registrations.Add(CreateRawRegistration(targetType, attribute, explicitContainer: null));
            }
            else if(AnalyzerHelpers.IsIoCRegisterDefaultsAttribute(attrClass, attributeSymbols))
            {
                if(CreateDefaultSettings(attribute) is { } defaults)
                    analyzerContext.DefaultSettings.Add(defaults);
            }
        }
    }

    private static RawRegistration CreateRawRegistration(
        INamedTypeSymbol implementationType,
        AttributeData attribute,
        INamedTypeSymbol? explicitContainer)
    {
        var (key, _, _) = attribute.GetKeyInfo();
        var (hasRegisterAllInterfaces, registerAllInterfaces) = attribute.TryGetRegisterAllInterfaces();
        var (hasRegisterAllBaseClasses, registerAllBaseClasses) = attribute.TryGetRegisterAllBaseClasses();
        var (hasFactory, hasInstance) = attribute.HasFactoryOrInstance();
        var hasInjectMembers = attribute.NamedArguments.Any(static a => a.Key == "InjectMembers" && !a.Value.IsNull);

        return new RawRegistration(
            implementationType,
            key,
            GetAttributeServiceKey(attribute),
            [.. AnalyzerHelpers.GetExplicitServiceTypes(attribute)],
            hasRegisterAllInterfaces,
            registerAllInterfaces,
            hasRegisterAllBaseClasses,
            registerAllBaseClasses,
            [.. attribute.GetTags()],
            explicitContainer,
            hasFactory,
            hasInstance,
            hasInjectMembers,
            AnalyzerHelpers.GetDecoratorSymbols(attribute),
            attribute.ApplicationSyntaxReference);
    }

    /// <summary>
    /// Resolves the <c>Factory</c> method and the <c>InjectMembers</c> members of attributes, which are given with
    /// <c>nameof()</c> and need a semantic model. Mirrors the generator's <c>GetFactoryMethodData</c> and
    /// <c>ExtractInjectMembersFromAttribute</c>.
    /// </summary>
    /// <remarks>
    /// Few attributes use these arguments, so the semantic models are created at compilation end for those attributes
    /// only, instead of registering a syntax node action that visits every attribute of the compilation.
    /// </remarks>
    private sealed class AttributeSemanticsResolver(Compilation compilation, CancellationToken cancellationToken)
    {
        private readonly Dictionary<SyntaxTree, SemanticModel> _semanticModels = [];

        /// <summary>Gets the semantics of an attribute in source; <see langword="null"/> for an attribute in metadata.</summary>
        public AttributeSemantics? Get(SyntaxReference? syntax)
        {
            if(syntax?.GetSyntax(cancellationToken) is not AttributeSyntax { ArgumentList: { } argumentList })
                return null;

            IMethodSymbol? factory = null;
            ImmutableArray<InjectMemberInfo> injectMembers = [];
            foreach(var argument in argumentList.Arguments)
            {
                switch(argument.NameEquals?.Name.Identifier.Text)
                {
                    case "Factory" when GetNameofArgument(argument.Expression) is { } factoryExpression:
                        factory = RoslynExtensions.ResolveMethodSymbol(factoryExpression, GetSemanticModel(syntax.SyntaxTree));
                        break;

                    case "InjectMembers":
                        injectMembers = GetInjectMembers(argument.Expression, GetSemanticModel(syntax.SyntaxTree), cancellationToken);
                        break;
                }
            }

            return new AttributeSemantics(factory, injectMembers);
        }

        // RS1030: semantic models are only created for the few attributes with Factory or InjectMembers
#pragma warning disable RS1030
        private SemanticModel GetSemanticModel(SyntaxTree tree)
        {
            if(!_semanticModels.TryGetValue(tree, out var semanticModel))
            {
                semanticModel = compilation.GetSemanticModel(tree);
                _semanticModels[tree] = semanticModel;
            }

            return semanticModel;
        }
#pragma warning restore RS1030
    }

    /// <summary>
    /// Reads the <c>InjectMembers</c> elements: <c>nameof(member)</c>, or <c>new object[] { nameof(member), key [, KeyType] }</c>.
    /// Invalid elements (SGIOC023/024) are skipped, like the generator's <c>ParseInjectMemberElement</c>.
    /// </summary>
    private static ImmutableArray<InjectMemberInfo> GetInjectMembers(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if(GetArrayElements(expression) is not { } elements)
            return [];

        var result = ImmutableArray.CreateBuilder<InjectMemberInfo>();
        foreach(var element in elements)
        {
            if(GetNameofArgument(element) is { } memberExpression)
            {
                if(GetMember(memberExpression) is { } member)
                    result.Add(new(member, Key: null, element.GetLocation()));

                continue;
            }

            if(GetArrayElements(element) is not { Length: 2 or 3 } nested
                || GetNameofArgument(nested[0]) is not { } keyedMemberExpression
                || GetMember(keyedMemberExpression) is not { } keyedMember)
            {
                continue;
            }

            var keyType = 0;
            if(nested.Length == 3)
            {
                if(semanticModel.GetConstantValue(nested[2], cancellationToken) is not { HasValue: true, Value: int value and (0 or 1) })
                    continue;

                keyType = value;
            }

            ServiceKeyValue key;
            if(keyType == 1)
            {
                key = ServiceKeyValue.Unknown(nested[1].ToString());
            }
            else
            {
                if(semanticModel.GetConstantValue(nested[1], cancellationToken) is not { HasValue: true, Value: { } keyValue })
                    continue;

                key = new ServiceKeyValue(
                    semanticModel.GetTypeInfo(nested[1], cancellationToken).Type,
                    keyValue,
                    IsUnknown: false,
                    nested[1].ToString());
            }

            result.Add(new(keyedMember, key, element.GetLocation()));
        }

        return result.ToImmutable();

        // Whether the member is injectable is checked on the member of the implementation type it resolves to (a getter-only
        // interface property can be implemented with a setter), see DependencyCache.GetInjectMembers
        ISymbol? GetMember(ExpressionSyntax memberExpression)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(memberExpression, cancellationToken);
            return symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
        }

        static ExpressionSyntax[]? GetArrayElements(ExpressionSyntax expression) => expression switch
        {
            ArrayCreationExpressionSyntax { Initializer: { } initializer } => [.. initializer.Expressions],
            ImplicitArrayCreationExpressionSyntax implicitArray => [.. implicitArray.Initializer.Expressions],
            CollectionExpressionSyntax collection => [.. collection.Elements.OfType<ExpressionElementSyntax>().Select(static e => e.Expression)],
            _ => null,
        };
    }

    /// <summary>Gets the argument of a <c>nameof(...)</c> expression.</summary>
    private static ExpressionSyntax? GetNameofArgument(ExpressionSyntax expression)
        => expression is InvocationExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.Text: "nameof" },
            ArgumentList.Arguments: [{ Expression: var argument }],
        }
            ? argument
            : null;

    /// <summary>
    /// Reads an [IocRegisterDefaults] / [IocRegisterDefaults&lt;T&gt;] attribute.
    /// Mirrors <c>ExtractDefaultSettings</c> / <c>ExtractDefaultSettingsFromGenericAttribute</c>.
    /// </summary>
    private static DefaultSettings? CreateDefaultSettings(AttributeData attribute)
    {
        var attrClass = attribute.AttributeClass;
        if(attrClass is null)
            return null;

        INamedTypeSymbol? targetType;
        if(attrClass.IsGenericType)
        {
            if(attrClass.TypeArguments.Length == 0 || attribute.ConstructorArguments.Length < 1)
                return null;

            targetType = attrClass.TypeArguments[0] as INamedTypeSymbol;
        }
        else
        {
            if(attribute.ConstructorArguments.Length < 2)
                return null;

            targetType = attribute.ConstructorArguments[0].Value as INamedTypeSymbol;
        }

        // Settings for an open type nested in a generic type do not match the generator's types (SGIOC038)
        if(targetType is null or { IsOpenNestedInGenericType: true })
            return null;

        var (_, registerAllInterfaces) = attribute.TryGetRegisterAllInterfaces();
        var (_, registerAllBaseClasses) = attribute.TryGetRegisterAllBaseClasses();

        return new DefaultSettings(
            targetType,
            [.. attribute.GetServiceTypeSymbols()],
            registerAllInterfaces,
            registerAllBaseClasses,
            [.. attribute.GetTags()],
            [.. attribute.GetImplementationTypeSymbols()],
            attribute.NamedArguments.Any(static a => a.Key == "Factory" && !a.Value.IsNull),
            attrClass.IsGenericType,
            AnalyzerHelpers.GetDecoratorSymbols(attribute),
            attribute.ApplicationSyntaxReference);
    }

    #endregion

    #region Registration resolution

    /// <summary>
    /// Applies default settings to every collected registration, in the same way as the generator's
    /// <c>ProcessSingleRegistration</c>: matching defaults add service types, tags, and RegisterAllInterfaces /
    /// RegisterAllBaseClasses values the registration does not set.
    /// </summary>
    private static List<ResolvedRegistration> ResolveRegistrations(
        Compilation compilation,
        ContainerAnalyzerContext analyzerContext,
        CancellationToken cancellationToken)
    {
        var defaults = BuildDefaultSettingsLookup(compilation, analyzerContext, cancellationToken);
        var semantics = new AttributeSemanticsResolver(compilation, cancellationToken);

        var resolved = new List<ResolvedRegistration>();
        var matched = new List<DefaultSettings>();
        foreach(var registration in analyzerContext.Registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var implementationType = registration.ImplementationType;
            var serviceTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default) { implementationType };
            AddExplicitServiceTypes(serviceTypes, implementationType, registration.ExplicitServiceTypes);

            // Base classes are matched before interfaces; the first match is the best default
            matched.Clear();
            DefaultSettings? best = null;
            if(!defaults.IsEmpty)
            {
                foreach(var candidate in EnumerateBaseClasses(implementationType))
                    MatchDefaults(candidate);

                foreach(var candidate in implementationType.AllInterfaces)
                    MatchDefaults(candidate);
            }

            if(best is not null)
                AddExplicitServiceTypes(serviceTypes, implementationType, best.ServiceTypes);

            var registerAllInterfaces = registration.HasExplicitRegisterAllInterfaces
                ? registration.RegisterAllInterfaces
                : best?.RegisterAllInterfaces ?? registration.RegisterAllInterfaces;
            if(registerAllInterfaces)
                serviceTypes.UnionWith(implementationType.AllInterfaces);

            var registerAllBaseClasses = registration.HasExplicitRegisterAllBaseClasses
                ? registration.RegisterAllBaseClasses
                : best?.RegisterAllBaseClasses ?? registration.RegisterAllBaseClasses;
            if(registerAllBaseClasses)
                serviceTypes.UnionWith(EnumerateBaseClasses(implementationType));

            var tags = registration.Tags.Length > 0
                ? registration.Tags
                : best?.Tags ?? registration.Tags;

            // Instance takes precedence (SGIOC010); a registration's Factory takes precedence over the default Factory
            var activation = registration switch
            {
                { HasInstance: true } => RegistrationActivation.None,
                { HasFactory: true } => GetFactoryActivation(registration.Syntax),
                _ when best is { HasFactory: true } => GetFactoryActivation(best.Syntax),
                _ => RegistrationActivation.Constructor,
            };

            resolved.Add(new ResolvedRegistration(
                implementationType,
                registration.Key,
                registration.KeyValue,
                [.. serviceTypes],
                tags,
                registration.ExplicitContainer,
                activation,
                activation.Kind is ActivationKind.Constructor && registration.HasInjectMembers
                    ? semantics.Get(registration.Syntax)?.InjectMembers ?? []
                    : [],
                // Like the generator, a registration's own decorators replace the default ones; an Instance is not decorated
                registration.HasInstance ? []
                    : registration.Decorators.Length > 0 ? registration.Decorators
                    : best?.Decorators ?? []));

            void MatchDefaults(INamedTypeSymbol candidate)
            {
                if(defaults.TryGetExact(candidate, out var exact) && !matched.Contains(exact))
                {
                    matched.Add(exact);
                    serviceTypes.Add(candidate);
                    best ??= exact;
                }

                if(candidate.IsGenericType
                    && defaults.TryGetGeneric(candidate, out var generic)
                    && !matched.Contains(generic))
                {
                    matched.Add(generic);
                    serviceTypes.Add(candidate);
                    best ??= generic;
                }
            }
        }

        AddDefaultSettingsImplementationTypes(resolved, analyzerContext, semantics, cancellationToken);
        AddFactoryOpenGenericRegistrations(resolved, analyzerContext);
        AddImportedOpenGenericRegistrations(resolved, analyzerContext, cancellationToken);

        return resolved;

        // A factory given as a string or declared in metadata cannot be resolved; its dependencies are unknown
        RegistrationActivation GetFactoryActivation(SyntaxReference? syntax)
            => semantics.Get(syntax)?.Factory is { } factory
                ? RegistrationActivation.FromFactory(factory)
                : RegistrationActivation.None;
    }

    /// <summary>
    /// Adds the open generic registrations of current-assembly <c>[IocRegisterDefaults]</c> with a <c>Factory</c> and an
    /// open generic target type, like the generator's <c>CreateOpenGenericEntriesFromDefaultSettings</c>.
    /// </summary>
    private static void AddFactoryOpenGenericRegistrations(
        List<ResolvedRegistration> resolved,
        ContainerAnalyzerContext analyzerContext)
    {
        foreach(var defaults in analyzerContext.DefaultSettings)
        {
            if(!defaults.HasFactory || !IsOpenGeneric(defaults.TargetType))
                continue;

            // The factory creates the closed forms the generator discovers; the target type is not constructed
            var targetType = defaults.TargetType.OriginalDefinition;
            resolved.Add(new ResolvedRegistration(
                targetType,
                Key: null,
                KeyValue: null,
                [targetType],
                defaults.Tags,
                ExplicitContainer: null,
                RegistrationActivation.None));
        }
    }

    /// <summary>
    /// Adds the open generic <c>[IocRegisterFor]</c> registrations of every imported module and, recursively, of the
    /// modules it imports. Like the generator's <c>ExtractOpenGenericEntriesFromModuleRecursive</c>, they are added to
    /// the registrations of the current assembly for every [IocImportModule] of the compilation; they expose the open
    /// generic <c>ServiceTypes</c> and interfaces of the implementation.
    /// </summary>
    private static void AddImportedOpenGenericRegistrations(
        List<ResolvedRegistration> resolved,
        ContainerAnalyzerContext analyzerContext,
        CancellationToken cancellationToken)
    {
        var attributeSymbols = analyzerContext.AttributeSymbols;
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach(var import in analyzerContext.ModuleImports)
            AddModule(import.ModuleType);

        void AddModule(INamedTypeSymbol moduleType)
        {
            if(!visited.Add(moduleType))
                return;

            foreach(var attribute in moduleType.GetAttributes())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if(attribute.AttributeClass is not { } attrClass)
                    continue;

                if(AnalyzerHelpers.IsIocImportModuleAttribute(attrClass, attributeSymbols))
                {
                    if(attribute.GetImportedModuleType() is { } importedModule)
                        AddModule(importedModule);

                    continue;
                }

                if(!AnalyzerHelpers.IsIoCRegisterForAttribute(attrClass, attributeSymbols)
                    || attribute.GetTargetTypeFromRegisterForAttribute() is not { IsOpenNestedInGenericType: false } targetType
                    || !IsOpenGeneric(targetType))
                {
                    continue;
                }

                var implementationType = targetType.OriginalDefinition;
                var explicitServiceTypes = AnalyzerHelpers.GetExplicitServiceTypes(attribute);
                var serviceTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
                foreach(var serviceType in explicitServiceTypes.Count > 0
                             ? explicitServiceTypes.Concat(implementationType.AllInterfaces)
                             : implementationType.AllInterfaces)
                {
                    if(IsOpenGeneric(serviceType))
                        serviceTypes.Add(serviceType.OriginalDefinition);
                }

                if(serviceTypes.Count == 0)
                    continue;

                var (key, _, _) = attribute.GetKeyInfo();
                resolved.Add(new ResolvedRegistration(
                    implementationType,
                    key,
                    GetAttributeServiceKey(attribute),
                    [.. serviceTypes],
                    [.. attribute.GetTags()],
                    ExplicitContainer: null));
            }
        }
    }

    /// <summary>
    /// Adds the registrations of <c>[IocRegisterDefaults(ImplementationTypes = ...)]</c> in the current assembly.
    /// Mirrors the generator's <c>CreateRegistrationDataFromDefaultSettings</c>: the settings of the attribute apply
    /// directly, without a default settings lookup.
    /// </summary>
    private static void AddDefaultSettingsImplementationTypes(
        List<ResolvedRegistration> resolved,
        ContainerAnalyzerContext analyzerContext,
        AttributeSemanticsResolver semantics,
        CancellationToken cancellationToken)
    {
        foreach(var defaults in analyzerContext.DefaultSettings)
        {
            foreach(var implementationType in defaults.ImplementationTypes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // A closed implementation is registered as the types of its hierarchy named like the target or the
                // service types; when none matches (e.g. Razor components), as the target and service types themselves.
                var declaredServiceTypes = IsOpenGeneric(implementationType)
                    ? []
                    : FindServiceTypesByName(implementationType, defaults);
                if(declaredServiceTypes.Count == 0)
                    declaredServiceTypes = [.. defaults.ServiceTypes, defaults.TargetType];

                var serviceTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default) { implementationType };
                AddExplicitServiceTypes(serviceTypes, implementationType, declaredServiceTypes);

                if(defaults.RegisterAllInterfaces)
                    serviceTypes.UnionWith(implementationType.AllInterfaces);

                if(defaults.RegisterAllBaseClasses)
                    serviceTypes.UnionWith(EnumerateBaseClasses(implementationType));

                // Only an open generic implementation uses the Factory of the settings
                var activation = RegistrationActivation.Constructor;
                if(defaults.HasFactory && IsOpenGeneric(implementationType))
                {
                    activation = semantics.Get(defaults.Syntax)?.Factory is { } factory
                        ? RegistrationActivation.FromFactory(factory)
                        : RegistrationActivation.None;
                }

                resolved.Add(new ResolvedRegistration(
                    implementationType,
                    Key: null,
                    KeyValue: null,
                    [.. serviceTypes],
                    defaults.Tags,
                    ExplicitContainer: null,
                    activation,
                    Decorators: defaults.Decorators));
            }
        }

        // Mirrors FindClosedServiceTypesFromImplementation, which compares names without generic arguments
        static List<INamedTypeSymbol> FindServiceTypesByName(INamedTypeSymbol implementationType, DefaultSettings defaults)
        {
            var result = new List<INamedTypeSymbol>();
            foreach(var target in defaults.ServiceTypes.Prepend(defaults.TargetType))
            {
                foreach(var candidate in implementationType.AllInterfaces.Concat(EnumerateBaseClasses(implementationType)))
                {
                    if(candidate.Name == target.Name
                        && SymbolEqualityComparer.Default.Equals(candidate.ContainingSymbol, target.ContainingSymbol))
                    {
                        result.Add(candidate);
                    }
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Adds explicit service types like the generator's <c>AddExplicitServiceType</c>: a closed implementation is
    /// registered as each closed form of an open generic service type that it implements or inherits
    /// (e.g. <c>typeof(IHandler&lt;&gt;)</c> for <c>Handler : IHandler&lt;string&gt;</c>).
    /// Without such a form, the open type is kept.
    /// </summary>
    private static void AddExplicitServiceTypes(
        HashSet<INamedTypeSymbol> serviceTypes,
        INamedTypeSymbol implementationType,
        IEnumerable<INamedTypeSymbol> explicitServiceTypes)
    {
        var isClosedImplementation = !IsOpenGeneric(implementationType);
        foreach(var serviceType in explicitServiceTypes)
        {
            if(isClosedImplementation && serviceType.IsUnboundGenericType)
            {
                var closed = false;
                foreach(var candidate in implementationType.AllInterfaces.Concat(EnumerateBaseClasses(implementationType)))
                {
                    if(candidate.IsGenericType
                        && SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, serviceType.OriginalDefinition))
                    {
                        serviceTypes.Add(candidate);
                        closed = true;
                    }
                }

                if(closed)
                    continue;
            }

            serviceTypes.Add(serviceType);
        }
    }

    private static bool IsOpenGeneric(INamedTypeSymbol type)
        => type.IsUnboundGenericType
            || (type.IsGenericType && type.TypeArguments.Any(static arg => arg.TypeKind == TypeKind.TypeParameter));

    /// <summary>
    /// Builds the default settings lookup with the generator's precedence: settings of the current assembly
    /// (non-generic attributes, then generic attributes, each in source order), then settings of imported module
    /// assemblies. The first settings for a target type win.
    /// </summary>
    private static DefaultSettingsLookup BuildDefaultSettingsLookup(
        Compilation compilation,
        ContainerAnalyzerContext analyzerContext,
        CancellationToken cancellationToken)
    {
        var lookup = new DefaultSettingsLookup();
        var treeOrder = new Dictionary<SyntaxTree, int>();
        var treeIndex = 0;
        foreach(var tree in compilation.SyntaxTrees)
            treeOrder[tree] = treeIndex++;

        foreach(var defaults in analyzerContext.DefaultSettings
                     .OrderBy(static d => d.IsGeneric)
                     .ThenBy(d => GetSourceOrder(d.Syntax, treeOrder)))
        {
            lookup.Add(defaults);
        }

        // Imported module assemblies: settings on the module type, then assembly-level settings
        foreach(var import in analyzerContext.ModuleImports
                     .OrderBy(static i => i.Attribute.AttributeClass?.IsGenericType == true)
                     .ThenBy(i => GetSourceOrder(i.Attribute.ApplicationSyntaxReference, treeOrder)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var moduleType = import.ModuleType.IsGenericType ? import.ModuleType.OriginalDefinition : import.ModuleType;
            var moduleAssembly = moduleType.ContainingAssembly;
            if(moduleAssembly is null || SymbolEqualityComparer.Default.Equals(moduleAssembly, compilation.Assembly))
                continue;

            AddImportedDefaults(moduleType.GetAttributes());
            AddImportedDefaults(moduleAssembly.GetAttributes());
        }

        return lookup;

        void AddImportedDefaults(ImmutableArray<AttributeData> attributes)
        {
            foreach(var attribute in attributes)
            {
                if(attribute.AttributeClass is { } attrClass
                    && AnalyzerHelpers.IsIoCRegisterDefaultsAttribute(attrClass, analyzerContext.AttributeSymbols)
                    && CreateDefaultSettings(attribute) is { } defaults)
                {
                    lookup.Add(defaults);
                }
            }
        }
    }

    private static (int Tree, int Position) GetSourceOrder(SyntaxReference? syntax, Dictionary<SyntaxTree, int> treeOrder)
    {
        if(syntax is null)
            return (int.MaxValue, int.MaxValue);

        return (treeOrder.TryGetValue(syntax.SyntaxTree, out var index) ? index : int.MaxValue, syntax.Span.Start);
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateBaseClasses(INamedTypeSymbol type)
    {
        var baseType = type.BaseType;
        while(baseType is not null && baseType.SpecialType is not SpecialType.System_Object)
        {
            yield return baseType;
            baseType = baseType.BaseType;
        }
    }

    #endregion

    #region Container scopes

    /// <summary>
    /// Computes the registrations of each container the way the generator selects them
    /// (ExplicitOnly → [IocRegisterFor] on the container, otherwise all registrations filtered by IncludeTags),
    /// plus the services of its imported modules.
    /// </summary>
    private sealed class ContainerScopeBuilder(
        List<ResolvedRegistration> registrations,
        Dictionary<INamedTypeSymbol, ContainerInfo> containers,
        IAssemblySymbol assembly)
    {
        private readonly Dictionary<INamedTypeSymbol, ContainerScope> _scopes = new(SymbolEqualityComparer.Default);
        private readonly HashSet<INamedTypeSymbol> _building = new(SymbolEqualityComparer.Default);
        private RegistrationSet? _allRegistrations;

        public ContainerScope GetScope(ContainerInfo container)
        {
            if(_scopes.TryGetValue(container.Symbol, out var scope))
                return scope;

            _building.Add(container.Symbol);

            var own = GetOwnRegistrations(container);
            var services = own.Services;
            var serviceKeys = own.ServiceKeys;
            var hasExternalModule = false;

            if(container.ImportedModules.Length > 0)
            {
                services = new ServiceIndex();
                services.UnionWith(own.Services);
                serviceKeys = [.. own.ServiceKeys];
                foreach(var module in container.ImportedModules)
                {
                    if(!SymbolEqualityComparer.Default.Equals(module.ContainingAssembly, assembly))
                    {
                        // Registrations of another assembly cannot be enumerated
                        hasExternalModule = true;
                        continue;
                    }

                    // Circular imports are reported by SGIOC025
                    if(_building.Contains(module) || !containers.TryGetValue(module, out var moduleContainer))
                        continue;

                    var moduleScope = GetScope(moduleContainer);
                    services.UnionWith(moduleScope.Services);
                    serviceKeys.UnionWith(moduleScope.ServiceKeys);
                    hasExternalModule |= moduleScope.HasExternalModule;
                }
            }

            _building.Remove(container.Symbol);

            scope = new ContainerScope(own, services, serviceKeys, hasExternalModule);
            _scopes[container.Symbol] = scope;
            return scope;
        }

        /// <summary>
        /// Gets whether the container uses every registration of the compilation, so it can share analysis results.
        /// </summary>
        public static bool UsesAllRegistrations(ContainerInfo container)
            => !container.ExplicitOnly && container.IncludeTags.IsEmpty && container.ImportedModules.IsEmpty;

        private RegistrationSet GetOwnRegistrations(ContainerInfo container)
        {
            if(container.ExplicitOnly)
            {
                return new RegistrationSet(registrations
                    .Where(r => SymbolEqualityComparer.Default.Equals(r.ExplicitContainer, container.Symbol))
                    .ToList());
            }

            if(container.IncludeTags.IsEmpty)
                return _allRegistrations ??= new RegistrationSet(registrations);

            var includeTags = new HashSet<string>(container.IncludeTags, StringComparer.Ordinal);
            return new RegistrationSet(registrations
                .Where(r => r.Tags.Any(includeTags.Contains))
                .ToList());
        }
    }

    /// <summary>
    /// The registrations of a container (without imported modules) and the service types they expose.
    /// </summary>
    private sealed class RegistrationSet
    {
        public RegistrationSet(List<ResolvedRegistration> registrations)
        {
            Registrations = registrations;
            foreach(var registration in registrations)
            {
                foreach(var serviceType in registration.ServiceTypes)
                {
                    Services.Add(serviceType, registration.KeyValue, registration.ImplementationType);
                    ServiceKeys.Add(new(serviceType, registration.Key));
                }
            }
        }

        public List<ResolvedRegistration> Registrations { get; }

        /// <summary>The services by key value (SGIOC018 and collection checks).</summary>
        public ServiceIndex Services { get; } = new();

        /// <summary>The services by key expression (SGIOC021, which compares keys like the generator's accessor lookup).</summary>
        public HashSet<ServiceSymbolKey> ServiceKeys { get; } = [];
    }

    /// <param name="Own">The registrations of the container itself.</param>
    /// <param name="Services">The services of the container, including the services of imported modules.</param>
    /// <param name="ServiceKeys">The services by key expression, including the services of imported modules (SGIOC021).</param>
    /// <param name="HasExternalModule">Whether the container imports a module of another assembly (directly or transitively).</param>
    private sealed record class ContainerScope(
        RegistrationSet Own,
        ServiceIndex Services,
        HashSet<ServiceSymbolKey> ServiceKeys,
        bool HasExternalModule);

    /// <summary>
    /// Registered services by type and key value. Generated containers look up keyed services by key value at runtime
    /// (<c>ServiceIdentifier</c>), so keys are compared by constant value; a key given as C# code
    /// (<c>KeyType = KeyType.Csharp</c>) cannot be evaluated and matches any key.
    /// </summary>
    private sealed class ServiceIndex
    {
        private readonly HashSet<INamedTypeSymbol> _nonKeyed = new(SymbolEqualityComparer.Default);
        private readonly HashSet<INamedTypeSymbol> _keyed = new(SymbolEqualityComparer.Default);
        private readonly HashSet<INamedTypeSymbol> _unknownKeyed = new(SymbolEqualityComparer.Default);
        private readonly HashSet<KeyedService> _keyedValues = [];

        // The implementations of open generic service types, by generic type definition
        private readonly Dictionary<INamedTypeSymbol, List<OpenGenericImplementation>> _openGenerics = new(SymbolEqualityComparer.Default);

        /// <summary>
        /// Adds a service. An open generic service type is added as its generic type definition, which resolves the
        /// closed forms its implementation can be closed for (the generator closes open generic registrations for the
        /// dependencies it discovers).
        /// </summary>
        public void Add(INamedTypeSymbol serviceType, ServiceKeyValue? key, INamedTypeSymbol implementationType)
        {
            // Like the generator, an open type nested in a generic type, such as Outer<T>.INode<T> of Node<T> registered
            // with RegisterAllInterfaces, is not registered (SGIOC038)
            if(serviceType.IsOpenNestedInGenericType)
                return;

            if(IsOpenGeneric(serviceType))
            {
                serviceType = serviceType.OriginalDefinition;
                if(!_openGenerics.TryGetValue(serviceType, out var implementations))
                {
                    implementations = [];
                    _openGenerics.Add(serviceType, implementations);
                }

                implementations.Add(new(implementationType.OriginalDefinition, key));
            }

            if(key is not { } keyValue)
            {
                _nonKeyed.Add(serviceType);
                return;
            }

            _keyed.Add(serviceType);
            if(keyValue.IsUnknown)
                _unknownKeyed.Add(serviceType);
            else
                _keyedValues.Add(new(serviceType, keyValue));
        }

        public void UnionWith(ServiceIndex other)
        {
            _nonKeyed.UnionWith(other._nonKeyed);
            _keyed.UnionWith(other._keyed);
            _unknownKeyed.UnionWith(other._unknownKeyed);
            _keyedValues.UnionWith(other._keyedValues);

            foreach(var entry in other._openGenerics)
            {
                if(_openGenerics.TryGetValue(entry.Key, out var implementations))
                    implementations.AddRange(entry.Value);
                else
                    _openGenerics.Add(entry.Key, [.. entry.Value]);
            }
        }

        /// <summary>
        /// Gets whether a service is registered. A closed generic type is also resolved by an open generic registration
        /// of its generic type definition when <paramref name="matchOpenGenerics"/> is <see langword="true"/> and the
        /// registration's implementation can be closed for it, like the generator's closed generic registrations.
        /// </summary>
        public bool Contains(INamedTypeSymbol serviceType, ServiceKeyValue? key, bool matchOpenGenerics = true)
        {
            if(ContainsExact(serviceType, key))
                return true;

            if(!matchOpenGenerics
                || !serviceType.IsGenericType
                || SymbolEqualityComparer.Default.Equals(serviceType, serviceType.OriginalDefinition)
                || !_openGenerics.TryGetValue(serviceType.OriginalDefinition, out var implementations))
            {
                return false;
            }

            foreach(var implementation in implementations)
            {
                if(KeyMatches(implementation.Key, key) && CanCloseOpenGeneric(implementation.Type, serviceType))
                    return true;
            }

            return false;
        }

        // Mirrors ContainsExact: a key given as C# code matches any key
        private static bool KeyMatches(ServiceKeyValue? registered, ServiceKeyValue? requested)
            => requested is not { } requestedKey
                ? registered is null
                : requestedKey.IsUnknown || registered is { } registeredKey && (registeredKey.IsUnknown || registeredKey.Equals(requestedKey));

        /// <summary>Gets whether a service has a keyed registration (dictionary and key value pair collection checks).</summary>
        public bool ContainsKeyed(INamedTypeSymbol serviceType)
            => _keyed.Contains(serviceType)
                || (serviceType.IsGenericType && _keyed.Contains(serviceType.OriginalDefinition));

        private bool ContainsExact(INamedTypeSymbol serviceType, ServiceKeyValue? key)
        {
            if(key is not { } keyValue)
                return _nonKeyed.Contains(serviceType);

            // A key given as C# code is matched by any registration of the type
            if(keyValue.IsUnknown)
                return _keyed.Contains(serviceType) || _nonKeyed.Contains(serviceType);

            return _keyedValues.Contains(new(serviceType, keyValue)) || _unknownKeyed.Contains(serviceType);
        }
    }

    /// <summary>The generic type definition of an open generic implementation, and the key it is registered with.</summary>
    private readonly record struct OpenGenericImplementation(INamedTypeSymbol Type, ServiceKeyValue? Key);

    private readonly record struct KeyedService(INamedTypeSymbol ServiceType, ServiceKeyValue Key)
    {
        public bool Equals(KeyedService other) =>
            SymbolEqualityComparer.Default.Equals(ServiceType, other.ServiceType) && Key.Equals(other.Key);

        public override int GetHashCode() =>
            unchecked((SymbolEqualityComparer.Default.GetHashCode(ServiceType) * 397) ^ Key.GetHashCode());
    }

    /// <summary>
    /// A service key: the constant value and its type, or an unknown value for a key given as C# code.
    /// </summary>
    /// <param name="Display">The key as C# code, for diagnostic messages.</param>
    private readonly record struct ServiceKeyValue(ITypeSymbol? Type, object? Value, bool IsUnknown, string Display)
    {
        public static ServiceKeyValue FromConstant(TypedConstant constant)
            => new(constant.Type, constant.Value, IsUnknown: false, constant.ToCSharpString());

        public static ServiceKeyValue Unknown(string display)
            => new(Type: null, Value: null, IsUnknown: true, display);

        public bool Equals(ServiceKeyValue other) =>
            IsUnknown == other.IsUnknown
            && SymbolEqualityComparer.Default.Equals(Type, other.Type)
            && object.Equals(Value, other.Value);

        public override int GetHashCode() =>
            unchecked((SymbolEqualityComparer.Default.GetHashCode(Type) * 397) ^ (Value?.GetHashCode() ?? 0) ^ (IsUnknown ? 1 : 0));
    }

    /// <summary>
    /// Gets the key of a registration or [IocInject] attribute, with the same argument rules as <c>GetKeyInfo</c>:
    /// the first constructor argument (unless it is a lifetime, type or array), otherwise the <c>Key</c> named argument.
    /// </summary>
    private static ServiceKeyValue? GetAttributeServiceKey(AttributeData attribute)
    {
        TypedConstant? key = null;
        if(attribute.ConstructorArguments.Length > 0)
        {
            var ctorArg = attribute.ConstructorArguments[0];
            if(ctorArg.Type?.Name != nameof(ServiceLifetime)
                && ctorArg.Kind is not (TypedConstantKind.Type or TypedConstantKind.Array)
                && !ctorArg.IsNull)
            {
                key = ctorArg;
            }
        }

        if(key is null)
        {
            foreach(var namedArg in attribute.NamedArguments)
            {
                if(namedArg.Key == "Key" && !namedArg.Value.IsNull)
                {
                    key = namedArg.Value;
                    break;
                }
            }
        }

        if(key is not { } keyConstant)
            return null;

        var (_, keyType) = attribute.TryGetNamedArgument<int>("KeyType", 0);
        return keyType == 1
            ? ServiceKeyValue.Unknown(keyConstant.Value?.ToString() ?? "null")
            : ServiceKeyValue.FromConstant(keyConstant);
    }

    #endregion

    #region SGIOC018

    /// <summary>
    /// SGIOC018: Collects the unregistered dependencies of the registrations of a container, according to how the
    /// container creates each instance: the constructor, injected members and <c>InjectMembers</c> of the implementation
    /// type, or the parameters of the factory method. <c>Instance</c> registrations and factories that cannot be resolved
    /// are not analyzed. Service types that are not implementation types (interfaces, base classes) are not analyzed,
    /// because the generator only constructs and injects the implementation type.
    /// Implementations of imported modules are analyzed as part of their own container. Implementations and factories
    /// declared in another assembly are not analyzed, except their <c>InjectMembers</c>, which are reported on the
    /// attribute. Dependencies that use type parameters (open generic implementations, generic factory methods) are not
    /// analyzed, because they depend on the closed forms the generator creates.
    /// </summary>
    private static List<(Location? Location, string ServiceType)> CollectUnresolvedDependencies(
        ContainerScope scope,
        DependencyCache dependencyCache,
        CancellationToken cancellationToken)
    {
        var unresolvedDependencies = new List<(Location? Location, string ServiceType)>();
        var analyzedSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        foreach(var registration in scope.Own.Registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch(registration.Activation.Kind)
            {
                case ActivationKind.Constructor:
                    var implementationType = registration.ImplementationType;
                    if(IsInSource(implementationType) && analyzedSymbols.Add(implementationType))
                        AddUnresolved(dependencyCache.GetConstructorAndMembers(implementationType, cancellationToken));

                    if(!registration.InjectMembers.IsDefaultOrEmpty)
                        AddUnresolved(DependencyCache.GetInjectMembers(implementationType, registration.InjectMembers));

                    break;

                case ActivationKind.Factory when registration.Activation.Factory is { } factory:
                    if(IsInSource(factory) && analyzedSymbols.Add(factory))
                        AddUnresolved(dependencyCache.GetFactory(factory));

                    break;
            }
        }

        return unresolvedDependencies;

        void AddUnresolved(List<Dependency> dependencies)
        {
            foreach(var dependency in dependencies)
            {
                if(ContainsTypeParameter(dependency.Type) || IsServiceRegistered(dependency.Type, dependency.Key, scope.Services))
                    continue;

                var serviceType = dependency.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                unresolvedDependencies.Add((
                    dependency.Location,
                    dependency.Key is { } key ? $"{serviceType} (key {key.Display})" : serviceType));
            }
        }

        static bool IsInSource(ISymbol symbol) => symbol.Locations.Any(static l => l.IsInSource);
    }

    /// <summary>
    /// Gets whether the container resolves a dependency. Like the generator's
    /// <c>ResolveServiceDependencyForContainerEntryModel</c>, a supported wrapper resolves its inner service with the same
    /// key: collections, <c>Lazy&lt;T&gt;</c>, <c>Func&lt;T&gt;</c> and <c>KeyValuePair&lt;TKey, T&gt;</c> unwrap
    /// recursively; <c>Task&lt;T&gt;</c> and <c>Func&lt;..., T&gt;</c> with input parameters need a registration of
    /// <c>T</c> itself; dictionaries and collections of key value pairs need a non-keyed dependency and a keyed registration
    /// of the value type.
    /// </summary>
    private static bool IsServiceRegistered(INamedTypeSymbol serviceType, ServiceKeyValue? key, ServiceIndex services)
    {
        if(services.Contains(serviceType, key))
            return true;

        if(!TryGetSupportedWrapper(serviceType, out var kind, out var elementType))
            return false;

        return kind switch
        {
            WrapperKind.Task => services.Contains(elementType, key),
            WrapperKind.Func when serviceType.Arity > 1 => services.Contains(elementType, key),
            WrapperKind.Lazy or WrapperKind.Func or WrapperKind.KeyValuePair => IsServiceRegistered(elementType, key, services),
            WrapperKind.Dictionary => key is null && services.ContainsKeyed(elementType),

            // Collections
            _ when elementType.TryGetWrapperInfo(out var element) && element.Kind is WrapperKind.KeyValuePair
                => key is null && element.ElementType is { } valueType && services.ContainsKeyed(valueType),
            _ => IsServiceRegistered(elementType, key, services),
        };
    }

    /// <summary>
    /// Gets the wrapper kind and element type of a wrapper the generator supports. Like the generator's
    /// <c>GetTypeData</c>, <c>ValueTask&lt;T&gt;</c> and unsupported nesting (e.g. <c>Task&lt;Lazy&lt;T&gt;&gt;</c>) are
    /// plain service types.
    /// </summary>
    private static bool TryGetSupportedWrapper(INamedTypeSymbol type, out WrapperKind kind, out INamedTypeSymbol elementType)
    {
        kind = WrapperKind.None;
        elementType = null!;

        if(!type.TryGetWrapperInfo(out var wrapper) || wrapper.Kind is WrapperKind.ValueTask || wrapper.ElementType is null)
            return false;

        foreach(var typeArgument in type.TypeArguments)
        {
            if(typeArgument is INamedTypeSymbol namedArgument
                && namedArgument.TryGetWrapperInfo(out var child)
                && child.Kind is not WrapperKind.ValueTask
                && IsUnsupportedWrapperNesting(wrapper.Kind, child.Kind, isAfterCollection: false))
            {
                return false;
            }
        }

        kind = wrapper.Kind;
        elementType = wrapper.ElementType;
        return true;
    }

    private static bool ContainsTypeParameter(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol => true,
        IArrayTypeSymbol array => ContainsTypeParameter(array.ElementType),
        INamedTypeSymbol named => named.TypeArguments.Any(ContainsTypeParameter),
        _ => false,
    };

    /// <param name="Key">The service key of a keyed dependency; <see langword="null"/> for a non-keyed dependency.</param>
    private readonly record struct Dependency(Location? Location, INamedTypeSymbol Type, ServiceKeyValue? Key);

    /// <summary>
    /// Caches the dependencies that need a registration (dependencies that are always resolvable are excluded) of
    /// implementation types and factory methods, so each is inspected once per compilation.
    /// The key of a dependency follows the generator: <c>[FromKeyedServices(key)]</c> takes precedence over the key of
    /// <c>[IocInject]</c> / <c>[Inject]</c> on the parameter, which takes precedence over the key of an injected method.
    /// </summary>
    private sealed class DependencyCache
    {
        private readonly Dictionary<ISymbol, List<Dependency>> _cache = new(SymbolEqualityComparer.Default);

        /// <summary>Gets the dependencies of the constructor and the <c>[IocInject]</c> members of an implementation type.</summary>
        public List<Dependency> GetConstructorAndMembers(INamedTypeSymbol implementationType, CancellationToken cancellationToken)
        {
            if(_cache.TryGetValue(implementationType, out var dependencies))
                return dependencies;

            dependencies = [];

            var constructor = implementationType.SpecifiedOrPrimaryOrMostParametersConstructor;
            if(constructor is not null)
                AddParameters(dependencies, constructor.Parameters, methodKey: null, location: null);

            foreach(var (member, injectAttribute) in implementationType.GetInjectedMembers())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var location = injectAttribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation()
                    ?? member.Locations.FirstOrDefault();
                AddMember(dependencies, member, GetAttributeServiceKey(injectAttribute), location, useLocationForParameters: false);
            }

            _cache[implementationType] = dependencies;
            return dependencies;
        }

        /// <summary>
        /// Gets the dependencies of a factory method: its parameters except <c>IServiceProvider</c> and <c>[ServiceKey]</c>,
        /// like the generator's <c>CreateFactoryMethodData</c>.
        /// </summary>
        public List<Dependency> GetFactory(IMethodSymbol factory)
        {
            if(_cache.TryGetValue(factory, out var dependencies))
                return dependencies;

            dependencies = [];
            AddParameters(dependencies, factory.Parameters, methodKey: null, location: null);

            _cache[factory] = dependencies;
            return dependencies;
        }

        /// <summary>
        /// Gets the dependencies of the <c>InjectMembers</c> of a registration, reported on their attribute elements.
        /// <c>[IocInject]</c> on a member takes precedence, like the generator's <c>MergeInjectionMembers</c>.
        /// </summary>
        public static List<Dependency> GetInjectMembers(
            INamedTypeSymbol implementationType,
            ImmutableArray<InjectMemberInfo> injectMembers)
        {
            var injectedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach(var (member, _) in implementationType.GetInjectedMembers())
                injectedNames.Add(member.Name);

            var dependencies = new List<Dependency>();
            foreach(var injectMember in injectMembers)
            {
                // Like the generator, the member is injected as a member of the implementation type, such as Node<T>.Repo
                // for nameof(Node<int>.Repo) or nameof(INode<int>.Repo), so an open generic implementation's member uses its
                // type parameters. A member the type does not have, or implements explicitly, is not injected (SGIOC024).
                if(!injectedNames.Contains(injectMember.Member.Name)
                    && injectMember.Member.AsMemberOf(implementationType) is { } member
                    && IocSourceGenerator.IsInjectableMember(member))
                {
                    AddMember(dependencies, member, injectMember.Key, injectMember.Location, useLocationForParameters: true);
                }
            }

            return dependencies;
        }

        private static void AddMember(
            List<Dependency> dependencies,
            ISymbol member,
            ServiceKeyValue? key,
            Location? location,
            bool useLocationForParameters)
        {
            switch(member)
            {
                // Injected IEnumerable<T> properties are resolved even without a registration
                case IPropertySymbol { Type: INamedTypeSymbol propertyType } when !AnalyzerHelpers.IsIEnumerableOfT(propertyType):
                    if(!AnalyzerHelpers.IsAlwaysResolvable(propertyType))
                        dependencies.Add(new(location, propertyType, key));
                    break;

                case IFieldSymbol { Type: INamedTypeSymbol fieldType }:
                    if(!AnalyzerHelpers.IsAlwaysResolvable(fieldType))
                        dependencies.Add(new(location, fieldType, key));
                    break;

                case IMethodSymbol method:
                    AddParameters(dependencies, method.Parameters, key, useLocationForParameters ? location : null);
                    break;
            }
        }

        /// <param name="methodKey">The key of an injected method, which applies to parameters without their own key.</param>
        /// <param name="location">The location to report; <see langword="null"/> for the location of each parameter.</param>
        private static void AddParameters(
            List<Dependency> dependencies,
            ImmutableArray<IParameterSymbol> parameters,
            ServiceKeyValue? methodKey,
            Location? location)
        {
            foreach(var parameter in parameters)
            {
                if(parameter.HasExplicitDefaultValue
                    || parameter.Type is not INamedTypeSymbol parameterType
                    || AnalyzerHelpers.IsAlwaysResolvable(parameterType)
                    || !TryGetParameterKey(parameter, out var key))
                {
                    continue;
                }

                dependencies.Add(new(location ?? parameter.Locations.FirstOrDefault(), parameterType, key ?? methodKey));
            }
        }

        /// <summary>
        /// Gets the key of a parameter dependency. Returns <see langword="false"/> for a parameter that is not resolved
        /// as a service: <c>[ServiceKey]</c>, and <c>[FromKeyedServices]</c> without a key (inherited key).
        /// </summary>
        private static bool TryGetParameterKey(IParameterSymbol parameter, out ServiceKeyValue? key)
        {
            key = null;
            foreach(var attribute in parameter.GetAttributes())
            {
                var attrClass = attribute.AttributeClass;
                if(attrClass is null)
                    continue;

                if(AnalyzerHelpers.IsServiceKeyAttribute(attrClass))
                    return false;

                if(AnalyzerHelpers.IsFromKeyedServicesAttribute(attrClass))
                {
                    if(attribute.ConstructorArguments is not [{ IsNull: false, Value: not null } keyArg])
                        return false;

                    key = ServiceKeyValue.FromConstant(keyArg);
                    return true;
                }

                if(attrClass.IsInject && key is null)
                    key = GetAttributeServiceKey(attribute);
            }

            return true;
        }
    }

    #endregion

    #region Models

    /// <param name="ExplicitContainer">
    /// The type an [IocRegisterFor] attribute is applied to; it is an explicit registration when that type is an
    /// ExplicitOnly container. <see langword="null"/> for [IocRegister] and assembly-level registrations.
    /// </param>
    private sealed record class RawRegistration(
        INamedTypeSymbol ImplementationType,
        string? Key,
        ServiceKeyValue? KeyValue,
        ImmutableArray<INamedTypeSymbol> ExplicitServiceTypes,
        bool HasExplicitRegisterAllInterfaces,
        bool RegisterAllInterfaces,
        bool HasExplicitRegisterAllBaseClasses,
        bool RegisterAllBaseClasses,
        ImmutableArray<string> Tags,
        INamedTypeSymbol? ExplicitContainer,
        bool HasFactory,
        bool HasInstance,
        bool HasInjectMembers,
        ImmutableArray<INamedTypeSymbol> Decorators,
        SyntaxReference? Syntax);

    /// <param name="Key">The key as the generator's code expression (SGIOC021/027/029).</param>
    /// <param name="KeyValue">The key value (SGIOC018).</param>
    /// <param name="Activation">How the container creates the instance; the default is the constructor.</param>
    /// <param name="InjectMembers">The members of the <c>InjectMembers</c> attribute property (constructor activation only).</param>
    /// <param name="Decorators">The decorator types of the registration, or of its best default settings (SGIOC027/029).</param>
    private sealed record class ResolvedRegistration(
        INamedTypeSymbol ImplementationType,
        string? Key,
        ServiceKeyValue? KeyValue,
        ImmutableArray<INamedTypeSymbol> ServiceTypes,
        ImmutableArray<string> Tags,
        INamedTypeSymbol? ExplicitContainer,
        RegistrationActivation Activation = default,
        ImmutableArray<InjectMemberInfo> InjectMembers = default,
        ImmutableArray<INamedTypeSymbol> Decorators = default);

    private enum ActivationKind
    {
        /// <summary>The constructor, injected members and <c>InjectMembers</c> are resolved.</summary>
        Constructor,

        /// <summary>Only the parameters of the factory method are resolved.</summary>
        Factory,

        /// <summary>The dependencies are unknown or there are none (<c>Instance</c>, or a factory that cannot be resolved).</summary>
        None,
    }

    private readonly struct RegistrationActivation
    {
        private RegistrationActivation(ActivationKind kind, IMethodSymbol? factory)
        {
            Kind = kind;
            Factory = factory;
        }

        public ActivationKind Kind { get; }
        public IMethodSymbol? Factory { get; }

        public static RegistrationActivation Constructor => new(ActivationKind.Constructor, null);
        public static RegistrationActivation None => new(ActivationKind.None, null);
        public static RegistrationActivation FromFactory(IMethodSymbol factory) => new(ActivationKind.Factory, factory);
    }

    /// <summary>A member listed in <c>InjectMembers</c>, with its key and the location of its element.</summary>
    private readonly record struct InjectMemberInfo(ISymbol Member, ServiceKeyValue? Key, Location Location);

    /// <param name="Factory">The resolved <c>Factory</c> method, or <see langword="null"/> when it cannot be resolved.</param>
    private sealed record class AttributeSemantics(IMethodSymbol? Factory, ImmutableArray<InjectMemberInfo> InjectMembers);


    /// <summary>
    /// Settings of an [IocRegisterDefaults] attribute. Compared by reference, like the generator's default settings indices.
    /// </summary>
    private sealed class DefaultSettings(
        INamedTypeSymbol targetType,
        ImmutableArray<INamedTypeSymbol> serviceTypes,
        bool registerAllInterfaces,
        bool registerAllBaseClasses,
        ImmutableArray<string> tags,
        ImmutableArray<INamedTypeSymbol> implementationTypes,
        bool hasFactory,
        bool isGeneric,
        ImmutableArray<INamedTypeSymbol> decorators,
        SyntaxReference? syntax)
    {
        public INamedTypeSymbol TargetType { get; } = targetType;
        public ImmutableArray<INamedTypeSymbol> ServiceTypes { get; } = serviceTypes;
        public bool RegisterAllInterfaces { get; } = registerAllInterfaces;
        public bool RegisterAllBaseClasses { get; } = registerAllBaseClasses;
        public ImmutableArray<string> Tags { get; } = tags;

        /// <summary>
        /// Implementation types registered with these settings. Only registrations of the current assembly are
        /// created; the generator ignores them for settings imported from a module assembly.
        /// </summary>
        public ImmutableArray<INamedTypeSymbol> ImplementationTypes { get; } = implementationTypes;

        /// <summary>Whether the settings specify a <c>Factory</c>, which registers an open generic target type.</summary>
        public bool HasFactory { get; } = hasFactory;
        public bool IsGeneric { get; } = isGeneric;
        public ImmutableArray<INamedTypeSymbol> Decorators { get; } = decorators;
        public SyntaxReference? Syntax { get; } = syntax;
    }

    /// <summary>
    /// Mirrors the generator's <c>DefaultSettingsMap</c>: exact matches by type, and generic matches by generic
    /// type definition. The first settings added for a key win.
    /// </summary>
    private sealed class DefaultSettingsLookup
    {
        private readonly Dictionary<INamedTypeSymbol, DefaultSettings> _exact = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<INamedTypeSymbol, DefaultSettings> _generic = new(SymbolEqualityComparer.Default);

        public bool IsEmpty => _exact.Count == 0;

        public void Add(DefaultSettings defaults)
        {
            var targetType = defaults.TargetType;
            if(!_exact.ContainsKey(targetType))
                _exact[targetType] = defaults;

            if(targetType.IsGenericType && !_generic.ContainsKey(targetType.OriginalDefinition))
                _generic[targetType.OriginalDefinition] = defaults;
        }

        public bool TryGetExact(INamedTypeSymbol type, out DefaultSettings defaults)
            => _exact.TryGetValue(type, out defaults!);

        public bool TryGetGeneric(INamedTypeSymbol type, out DefaultSettings defaults)
            => _generic.TryGetValue(type.OriginalDefinition, out defaults!);
    }

    private readonly record struct ModuleImport(INamedTypeSymbol ModuleType, AttributeData Attribute);

    private sealed record class ContainerInfo(
        INamedTypeSymbol Symbol,
        bool IntegrateServiceProvider,
        bool ExplicitOnly,
        ImmutableArray<string> IncludeTags,
        ImmutableArray<INamedTypeSymbol> ImportedModules);

    private sealed class ContainerAnalyzerContext(IocAttributeSymbols attributeSymbols, IocFeatures features)
    {
        public IocAttributeSymbols AttributeSymbols { get; } = attributeSymbols;
        public IocFeatures Features { get; } = features;
        public ConcurrentBag<RawRegistration> Registrations { get; } = [];
        public ConcurrentBag<DefaultSettings> DefaultSettings { get; } = [];
        public ConcurrentBag<ModuleImport> ModuleImports { get; } = [];
        public ConcurrentBag<ContainerInfo> Containers { get; } = [];
        public ConcurrentBag<(INamedTypeSymbol Container, INamedTypeSymbol Module)> ImportEdges { get; } = [];
    }

    #endregion
}
