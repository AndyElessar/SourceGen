using SourceGen.Ioc.Diagnostics;
using SourceGen.Ioc.Model;

namespace SourceGen.Ioc.Parse;

/// <summary>The attribute that opts in to <c>Add{Name}</c>, as it is written.</summary>
/// <param name="Name">The <c>Name</c> property, or <see langword="null"/> when it is not set.</param>
/// <param name="FilePath">The path of the syntax tree of the attribute.</param>
/// <param name="Span">The span of the attribute.</param>
internal sealed record AddMethodAttribute(string? Name, string FilePath, Microsoft.CodeAnalysis.Text.TextSpan Span);

/// <summary>Reads the settings of <c>Add{Name}</c> (Register.Output.spec, "Conditions for generation" and "Method").</summary>
internal static class AddMethodReader
{
    /// <summary>The metadata name of <c>IocServiceCollectionExtensionsAttribute</c>.</summary>
    public const string Attribute = "SourceGen.Ioc.IocServiceCollectionExtensionsAttribute";

    /// <summary>Reads the attribute.</summary>
    /// <param name="context">The context of the attribute.</param>
    /// <returns>The attribute model.</returns>
    public static AddMethodAttribute ReadAttribute(GeneratorAttributeSyntaxContext context)
    {
        AttributeData attribute = context.Attributes[0];
        string? name = attribute.NamedArguments.FirstOrDefault(static a => a.Key == "Name").Value.Value as string;
        SyntaxReference? syntax = attribute.ApplicationSyntaxReference;
        return new(name, syntax?.SyntaxTree.FilePath ?? string.Empty, syntax?.Span ?? default);
    }

    /// <summary>Decides the settings, or the diagnostic when the name is not valid.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="assemblyName">The name of this assembly.</param>
    /// <param name="rootNamespace">The MSBuild property <c>RootNamespace</c>, or <see langword="null"/> when it is not set.</param>
    /// <returns>The settings, or <see langword="null"/> and a diagnostic.</returns>
    public static (AddMethodSettings? Settings, DiagnosticInfo? Diagnostic) Decide(AddMethodAttribute attribute, string assemblyName, string? rootNamespace)
    {
        string source = attribute.Name ?? assemblyName;
        StringBuilder name = new(source.Length);
        foreach(char c in source)
        {
            if(SyntaxFacts.IsIdentifierPartCharacter(c))
            {
                name.Append(c);
            }
        }

        string? reason = name.Length == 0 ? "no identifier characters remain"
            : !SyntaxFacts.IsIdentifierStartCharacter(name[0]) ? "the name cannot start an identifier"
            : null;
        if(reason is not null)
        {
            return (null, new DiagnosticInfo(Descriptors.InvalidAddMethodName.Id, [source, reason], attribute.FilePath, attribute.Span));
        }

        string ns = string.IsNullOrEmpty(rootNamespace) ? assemblyName : rootNamespace!;
        return (new AddMethodSettings(name.ToString(), ns, assemblyName), null);
    }
}
