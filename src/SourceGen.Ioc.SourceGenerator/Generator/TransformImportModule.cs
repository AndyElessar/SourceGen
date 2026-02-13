namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Transforms ImportModuleAttribute to extract both default settings and open generic entries
    /// from the referenced module's assembly in a single pass.
    /// </summary>
    private static IEnumerable<ImportModuleResult> TransformImportModule(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        foreach(var attr in ctx.Attributes)
        {
            ct.ThrowIfCancellationRequested();

            // Get the ModuleType from the attribute
            if(attr.ConstructorArguments.Length == 0)
                continue;

            if(attr.ConstructorArguments[0].Value is not INamedTypeSymbol moduleType)
                continue;

            // Get the assembly containing the module type
            var moduleAssembly = moduleType.ContainingAssembly;
            if(moduleAssembly is null)
                continue;

            // Extract both default settings and open generic entries in a single pass
            var defaultSettings = ExtractDefaultSettingsFromModule(moduleType, moduleAssembly, ct)
                .ToImmutableEquatableArray();
            var openGenericEntries = ExtractOpenGenericEntriesFromModuleRecursive(moduleType, [], ct)
                .ToImmutableEquatableArray();

            yield return new ImportModuleResult(defaultSettings, openGenericEntries);
        }
    }

    /// <summary>
    /// Transforms generic ImportModuleAttribute (ImportModuleAttribute&lt;T&gt;) to extract both default settings
    /// and open generic entries from the referenced module's assembly in a single pass.
    /// </summary>
    private static IEnumerable<ImportModuleResult> TransformImportModuleGeneric(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        foreach(var attr in ctx.Attributes)
        {
            ct.ThrowIfCancellationRequested();

            var attrClass = attr.AttributeClass;
            if(attrClass?.IsGenericType != true || attrClass.TypeArguments.Length == 0)
                continue;

            if(attrClass.TypeArguments[0] is not INamedTypeSymbol moduleType)
                continue;

            // Get the assembly containing the module type
            var moduleAssembly = moduleType.ContainingAssembly;
            if(moduleAssembly is null)
                continue;

            // Extract both default settings and open generic entries in a single pass
            var defaultSettings = ExtractDefaultSettingsFromModule(moduleType, moduleAssembly, ct)
                .ToImmutableEquatableArray();
            var openGenericEntries = ExtractOpenGenericEntriesFromModuleRecursive(moduleType, [], ct)
                .ToImmutableEquatableArray();

            yield return new ImportModuleResult(defaultSettings, openGenericEntries);
        }
    }

    /// <summary>
    /// Recursively extracts open generic entries from a module and its imported modules.
    /// </summary>
    private static IEnumerable<OpenGenericEntry> ExtractOpenGenericEntriesFromModuleRecursive(
        INamedTypeSymbol moduleType,
        HashSet<string> visitedModules,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Avoid infinite recursion with circular imports
        var moduleKey = moduleType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if(!visitedModules.Add(moduleKey))
            yield break;

        // Extract from this module's IocRegisterFor attributes
        foreach(var entry in ExtractOpenGenericEntriesFromModule(moduleType))
        {
            yield return entry;
        }

        // Recursively extract from imported modules
        foreach(var attr in moduleType.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            var attrClass = attr.AttributeClass;
            if(attrClass is null)
                continue;

            INamedTypeSymbol? importedModuleType = null;

            // Check for IocImportModuleAttribute (non-generic)
            var fullName = attrClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if(fullName.StartsWith("global::", StringComparison.Ordinal))
                fullName = fullName[8..];

            if(fullName == Constants.IocImportModuleAttributeFullName)
            {
                if(attr.ConstructorArguments.Length > 0 &&
                   attr.ConstructorArguments[0].Value is INamedTypeSymbol modType)
                {
                    importedModuleType = modType;
                }
            }
            else if(attrClass.IsGenericType)
            {
                // Check for IocImportModuleAttribute<T>
                var metadataName = attrClass.OriginalDefinition.MetadataName;
                var metadataNamespace = attrClass.ContainingNamespace?.ToDisplayString() ?? "";
                var originalFullName = string.IsNullOrEmpty(metadataNamespace)
                    ? metadataName
                    : $"{metadataNamespace}.{metadataName}";

                if(originalFullName == Constants.IocImportModuleAttributeFullName_T1 &&
                   attrClass.TypeArguments.Length > 0 &&
                   attrClass.TypeArguments[0] is INamedTypeSymbol genericModType)
                {
                    importedModuleType = genericModType;
                }
            }

            if(importedModuleType is not null)
            {
                foreach(var entry in ExtractOpenGenericEntriesFromModuleRecursive(importedModuleType, visitedModules, ct))
                {
                    yield return entry;
                }
            }
        }
    }

    /// <summary>
    /// Extracts open generic entries from IocRegisterFor attributes on a module type.
    /// </summary>
    private static IEnumerable<OpenGenericEntry> ExtractOpenGenericEntriesFromModule(INamedTypeSymbol moduleType)
    {
        foreach(var attr in moduleType.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if(attrClass is null)
                continue;

            INamedTypeSymbol? targetType = null;

            // Check for IocRegisterForAttribute
            var fullName = attrClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if(fullName.StartsWith("global::", StringComparison.Ordinal))
                fullName = fullName[8..];

            if(fullName == Constants.IocRegisterForAttributeFullName)
            {
                if(attr.ConstructorArguments.Length > 0 &&
                   attr.ConstructorArguments[0].Value is INamedTypeSymbol implType)
                {
                    targetType = implType;
                }
            }
            else if(attrClass.IsGenericType)
            {
                // Check for IocRegisterForAttribute<T>
                var metadataName = attrClass.OriginalDefinition.MetadataName;
                var metadataNamespace = attrClass.ContainingNamespace?.ToDisplayString() ?? "";
                var originalFullName = string.IsNullOrEmpty(metadataNamespace)
                    ? metadataName
                    : $"{metadataNamespace}.{metadataName}";

                if(originalFullName == Constants.IocRegisterForAttributeFullName_T1 &&
                   attrClass.TypeArguments.Length > 0 &&
                   attrClass.TypeArguments[0] is INamedTypeSymbol genericImplType)
                {
                    targetType = genericImplType;
                }
            }

            if(targetType is null)
                continue;

            // Only process open generic types (not nested open generics)
            if(!targetType.IsUnboundGenericType && !targetType.IsGenericType)
                continue;

            // Check if this is an open generic
            var originalDef = targetType.IsUnboundGenericType ? targetType : targetType.OriginalDefinition;
            if(!originalDef.IsGenericType || !originalDef.TypeParameters.Any())
                continue;

            // Use original definition for unbound generics
            var typeToProcess = originalDef;

            // Extract the open generic entry
            var implementationType = typeToProcess.GetTypeData(extractConstructorParams: true, extractHierarchy: true);

            // Skip nested open generics
            if(implementationType is not GenericTypeData implementationGenericType)
                continue;

            if(implementationGenericType.IsNestedOpenGeneric)
                continue;

            // Only process if this is truly an open generic
            if(!implementationGenericType.IsOpenGeneric)
                continue;

            // Extract service types from the attribute
            var serviceTypes = attr.GetServiceTypes();
            var serviceTypesToUse = serviceTypes.Length > 0
                ? serviceTypes
                : implementationType.AllInterfaces ?? [];

            // Build the set of open generic service types
            HashSet<TypeData> openGenericServiceTypes = [];
            foreach(var serviceType in serviceTypesToUse)
            {
                if(serviceType is GenericTypeData { IsOpenGeneric: true, IsNestedOpenGeneric: false })
                {
                    openGenericServiceTypes.Add(serviceType);
                }
            }

            // Also include all open generic interfaces from the implementation
            if(implementationType.AllInterfaces is not null)
            {
                foreach(var iface in implementationType.AllInterfaces)
                {
                    if(iface is GenericTypeData { IsOpenGeneric: true, IsNestedOpenGeneric: false })
                    {
                        openGenericServiceTypes.Add(iface);
                    }
                }
            }

            if(openGenericServiceTypes.Count == 0)
                continue;

            // Extract other registration details
            var (hasExplicitLifetime, lifetime) = attr.TryGetLifetime();
            var decorators = attr.GetDecorators();
            var tags = attr.GetTags();
            var (key, keyType) = attr.GetKey(null);

            // Create the OpenGenericRegistrationInfo
            // Note: We don't extract InjectionMembers for cross-assembly imports since we can't access method bodies
            var info = new OpenGenericRegistrationInfo(
                implementationType,
                openGenericServiceTypes.ToImmutableEquatableArray(),
                implementationType.AllInterfaces ?? [],
                hasExplicitLifetime ? lifetime : ServiceLifetime.Transient,
                key,
                keyType,
                decorators,
                tags,
                InjectionMembers: [],
                Factory: null,
                Instance: null);

            // Create entries for each open generic service type
            var addedKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach(var serviceType in openGenericServiceTypes)
            {
                if(serviceType is not GenericTypeData genericServiceType)
                {
                    continue;
                }

                var serviceKey = genericServiceType.NameWithoutGeneric;
                if(addedKeys.Add(serviceKey))
                {
                    yield return new OpenGenericEntry(serviceKey, info);
                }
            }
        }
    }

    /// <summary>
    /// Extracts default settings from the module type and its containing assembly.
    /// </summary>
    private static IEnumerable<DefaultSettingsModel> ExtractDefaultSettingsFromModule(
        INamedTypeSymbol moduleType,
        IAssemblySymbol moduleAssembly,
        CancellationToken ct)
    {
        // Use OriginalDefinition for generic types to ensure we get the attributes
        // from the original type definition, not the constructed type.
        // This is important for unbound generic types like IRequestHandler<,> where
        // attributes are defined on the original definition.
        var typeToCheck = moduleType.IsGenericType ? moduleType.OriginalDefinition : moduleType;

        // First, check for IoCRegisterDefaultsAttribute on the module type itself
        foreach(var attr in typeToCheck.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            if(IsIoCRegisterDefaultsAttribute(attr))
            {
                var data = ExtractDefaultSettingsFromAttributeData(attr);
                if(data is not null)
                    yield return data;
            }
        }

        // Then, check for assembly-level IoCRegisterDefaultsAttribute
        foreach(var attr in moduleAssembly.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            if(IsIoCRegisterDefaultsAttribute(attr))
            {
                var data = ExtractDefaultSettingsFromAttributeData(attr);
                if(data is not null)
                    yield return data;
            }
        }
    }

    /// <summary>
    /// Extracts default settings from an attribute, handling both generic and non-generic variants.
    /// </summary>
    private static DefaultSettingsModel? ExtractDefaultSettingsFromAttributeData(AttributeData attr)
    {
        var attrClass = attr.AttributeClass;
        if(attrClass is null)
            return null;

        // Check if this is a generic version (IoCRegisterDefaultsAttribute<T>)
        if(attrClass.IsGenericType && attrClass.TypeArguments.Length > 0)
        {
            return attr.ExtractDefaultSettingsFromGenericAttribute();
        }

        // Non-generic version
        return attr.ExtractDefaultSettings();
    }

    /// <summary>
    /// Checks if the attribute is IoCRegisterDefaultsAttribute (or its generic variant) by its full name.
    /// </summary>
    private static bool IsIoCRegisterDefaultsAttribute(AttributeData attr)
    {
        var attrClass = attr.AttributeClass;
        if(attrClass is null)
            return false;

        // For generic types, use the original definition
        var typeToCheck = attrClass.IsGenericType ? attrClass.OriginalDefinition : attrClass;

        // Build the full name and compare
        var fullName = typeToCheck.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // Remove "global::" prefix if present
        if(fullName.StartsWith("global::", StringComparison.Ordinal))
            fullName = fullName[8..];

        // Check for both generic and non-generic variants
        return fullName == Constants.IocRegisterDefaultsAttributeFullName
            || fullName == Constants.IocRegisterDefaultsAttributeFullName_T1;
    }
}
