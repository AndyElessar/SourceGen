using SourceGen.Ioc.Model;

namespace SourceGen.Ioc.Emit;

/// <summary>Writes a <see cref="TypeRef"/> as C# source, qualified with <c>global::</c>.</summary>
internal static class TypeNames
{
    /// <summary>Gets the C# name of <paramref name="type"/>, for example <c>global::Ns.Outer&lt;int&gt;.Inner&lt;string&gt;[]</c>.</summary>
    /// <param name="type">A closed type.</param>
    /// <returns>The C# name.</returns>
    public static string CSharp(TypeRef type)
    {
        StringBuilder builder = new();
        Append(builder, type);
        return builder.ToString();
    }

    /// <summary>Gets the name of <paramref name="type"/> for a message: the C# display format without <c>global::</c>.</summary>
    /// <param name="type">A closed type.</param>
    /// <returns>The name, for example <c>App.IRepo</c>.</returns>
    public static string Display(TypeRef type) => CSharp(type).Replace("global::", string.Empty);

    private static void Append(StringBuilder builder, TypeRef type)
    {
        switch(type)
        {
            case NamedTypeRef named:
                AppendNamed(builder, named);
                break;
            case ArrayTypeRef array:
                // C# writes the ranks from the outermost array to the innermost: int[][,] is an array of int[,].
                // The canonical string writes them the other way, so the element cannot be written first.
                TypeRef element = array;
                List<int> ranks = [];
                while(element is ArrayTypeRef level)
                {
                    ranks.Add(level.Rank);
                    element = level.Element;
                }

                Append(builder, element);
                foreach(int rank in ranks)
                {
                    builder.Append('[').Append(',', rank - 1).Append(']');
                }

                break;
            case TypeParameterRef:
                throw new InvalidOperationException("A type parameter has no C# name without its declaration.");
        }
    }

    private static void AppendNamed(StringBuilder builder, NamedTypeRef named)
    {
        string[] levels = named.MetadataName.Split('+');
        int ns = levels[0].LastIndexOf('.');
        builder.Append("global::");
        if(ns >= 0)
        {
            foreach(string part in levels[0][..ns].Split('.'))
            {
                AppendIdentifier(builder, part).Append('.');
            }

            levels[0] = levels[0][(ns + 1)..];
        }

        int argument = 0;
        for(int i = 0; i < levels.Length; i++)
        {
            if(i > 0)
            {
                builder.Append('.');
            }

            string level = levels[i];
            int tick = level.IndexOf('`');
            int arity = tick >= 0 ? int.Parse(level[(tick + 1)..], System.Globalization.CultureInfo.InvariantCulture) : 0;
            AppendIdentifier(builder, tick >= 0 ? level[..tick] : level);
            if(arity == 0 || named.Arguments is null)
            {
                continue;
            }

            builder.Append('<');
            for(int a = 0; a < arity; a++)
            {
                if(a > 0)
                {
                    builder.Append(", ");
                }

                Append(builder, named.Arguments[argument++]);
            }

            builder.Append('>');
        }
    }

    private static StringBuilder AppendIdentifier(StringBuilder builder, string identifier)
        => SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None ? builder.Append('@').Append(identifier) : builder.Append(identifier);
}
