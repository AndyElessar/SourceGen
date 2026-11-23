using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SourceGen.Ioc.Test.Helpers;

/// <summary>
/// Cuts the parts under test out of a large generated file, so that a focused snapshot shows only them
/// (constitution, section 9.1).
/// </summary>
public static class SnapshotHelper
{
    /// <summary>
    /// Returns the text of each member or type declaration of <paramref name="source"/> whose name is in
    /// <paramref name="names"/>, in source order, separated by an empty line.
    /// </summary>
    public static string Members(string source, params IReadOnlyList<string> names)
    {
        HashSet<string> wanted = [.. names];
        SyntaxNode root = CSharpSyntaxTree.ParseText(source, SourceGeneratorTestHelper.ParseOptions).GetRoot();
        string[] parts = [.. root.DescendantNodes()
            .OfType<MemberDeclarationSyntax>()
            .Where(m => Name(m) is { } name && wanted.Contains(name))
            .Select(static m => m.ToFullString().Trim('\r', '\n'))];

        HashSet<string> found = [.. root.DescendantNodes().OfType<MemberDeclarationSyntax>().Select(Name).OfType<string>()];
        string[] missing = [.. names.Where(n => !found.Contains(n))];
        if(missing.Length > 0)
        {
            throw new ArgumentException($"The source has no member named {string.Join(", ", missing)}.", nameof(names));
        }

        return string.Join($"{Environment.NewLine}{Environment.NewLine}", parts);
    }

    private static string? Name(MemberDeclarationSyntax member) => member switch
    {
        BaseTypeDeclarationSyntax t => t.Identifier.Text,
        MethodDeclarationSyntax m => m.Identifier.Text,
        PropertyDeclarationSyntax p => p.Identifier.Text,
        ConstructorDeclarationSyntax c => c.Identifier.Text,
        FieldDeclarationSyntax f => f.Declaration.Variables.FirstOrDefault()?.Identifier.Text,
        EventDeclarationSyntax e => e.Identifier.Text,
        EventFieldDeclarationSyntax e => e.Declaration.Variables.FirstOrDefault()?.Identifier.Text,
        DelegateDeclarationSyntax d => d.Identifier.Text,
        IndexerDeclarationSyntax => "this[]",
        _ => null,
    };
}
