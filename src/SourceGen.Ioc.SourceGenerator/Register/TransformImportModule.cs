namespace SourceGen.Ioc.SourceGenerator.Register;

partial class RegisterSourceGenerator
{
    /// <summary>
    /// Transforms ImportModuleAttribute to extract default settings from the referenced module's assembly.
    /// </summary>
    private static IEnumerable<DefaultSettingsModel> TransformImportModule(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
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

            // Find all IoCRegisterDefaultsAttribute on the module type and its assembly
            foreach(var defaultSettings in ExtractDefaultSettingsFromModule(moduleType, moduleAssembly, ct))
            {
                yield return defaultSettings;
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
        // First, check for IoCRegisterDefaultsAttribute on the module type itself
        foreach(var attr in moduleType.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            if(IsIoCRegisterDefaultsAttribute(attr))
            {
                var data = attr.ExtractDefaultSettings();
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
                var data = attr.ExtractDefaultSettings();
                if(data is not null)
                    yield return data;
            }
        }
    }

    /// <summary>
    /// Checks if the attribute is IoCRegisterDefaultsAttribute by its full name.
    /// </summary>
    private static bool IsIoCRegisterDefaultsAttribute(AttributeData attr)
    {
        var attrClass = attr.AttributeClass;
        if(attrClass is null)
            return false;

        // Build the full name and compare
        var fullName = attrClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // Remove "global::" prefix if present
        if(fullName.StartsWith("global::", StringComparison.Ordinal))
            fullName = fullName[8..];

        return fullName == Constants.IoCRegisterDefaultsAttributeFullName;
    }
}
