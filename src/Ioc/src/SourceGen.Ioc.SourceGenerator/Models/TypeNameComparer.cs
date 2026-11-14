namespace SourceGen.Ioc;

/// <summary>
/// Compares fully qualified type names as runtime types: tuple element names are ignored, so
/// <c>IHandler&lt;(Entity X, string Y)&gt;</c> equals <c>IHandler&lt;(Entity, string)&gt;</c>. Type names keep their tuple
/// element names, which the generated code is written with.
/// </summary>
internal sealed class TypeNameComparer : IEqualityComparer<string>
{
    public static TypeNameComparer Instance { get; } = new();

    private TypeNameComparer()
    {
    }

    public bool Equals(string? x, string? y)
    {
        if(string.Equals(x, y, StringComparison.Ordinal))
            return true;

        // Only names with tuples can differ in tuple element names alone
        return x is not null && y is not null
            && x.IndexOf('(') >= 0 && y.IndexOf('(') >= 0
            && string.Equals(WithoutTupleElementNames(x), WithoutTupleElementNames(y), StringComparison.Ordinal);
    }

    public int GetHashCode(string obj) => StringComparer.Ordinal.GetHashCode(WithoutTupleElementNames(obj));

    /// <summary>Compares pairs of type names, such as the key and value types of a <c>KeyValuePair</c>, like <see cref="Instance"/>.</summary>
    public static IEqualityComparer<(string, string)> PairComparer { get; } = new TypeNamePairComparer();

    private sealed class TypeNamePairComparer : IEqualityComparer<(string, string)>
    {
        public bool Equals((string, string) x, (string, string) y)
            => Instance.Equals(x.Item1, y.Item1) && Instance.Equals(x.Item2, y.Item2);

        public int GetHashCode((string, string) obj)
            => unchecked((Instance.GetHashCode(obj.Item1) * 397) ^ Instance.GetHashCode(obj.Item2));
    }

    /// <summary>
    /// Removes the element names of the tuples in a fully qualified type name, such as <c>(int, string)</c> for
    /// <c>(int Id, string Name)</c>.
    /// </summary>
    public static string WithoutTupleElementNames(string typeName)
    {
        if(typeName.IndexOf('(') < 0)
            return typeName;

        // An element name follows its type after a space, directly in the tuple's parentheses; spaces elsewhere follow a
        // comma, or are inside a nested generic type, array or tuple
        var builder = new StringBuilder(typeName.Length);
        var brackets = new Stack<char>();
        var skipping = false;
        for(var i = 0; i < typeName.Length; i++)
        {
            var c = typeName[i];
            if(skipping)
            {
                if(c is not (',' or ')'))
                    continue;

                skipping = false;
            }

            switch(c)
            {
                case '<' or '(' or '[':
                    brackets.Push(c);
                    break;
                case '>' or ')' or ']':
                    brackets.Pop();
                    break;
                case ' ' when brackets.Count > 0 && brackets.Peek() == '(' && typeName[i - 1] != ',':
                    skipping = true;
                    continue;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
